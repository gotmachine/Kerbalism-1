using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace KERBALISM
{

	public enum FileType
	{
		File, Sample
	}


	// drive is now a list. reasons:
	// - to keep it a dictionary and managing duplicates, we would need a dictionary<key, list<value>>, this complicate code a lot
	// - most of the time, drives are empty or containing only a couple of files
	// - we sometimes find by key but not that much
	// - finding by key in dictionary containing less than 3-4 elements is slower than iterating over a list
	// - we very often iterate over the whole drive content, iterating over a dictionary is slow and garbagey

	/// <summary>
	/// drive is a list of ExperimentResult with specific handling when calling the usual List methods
	/// </summary>
	public class Drive : IList<ExperimentResult>
	{
		/// <summary>file capacity in bits</summary>
		public long fileCapacity;
		/// <summary>sample capacity in slots</summary>
		public long sampleCapacity;
		public string name = string.Empty; // TODO : what is this used for ?
		public bool is_private = false;

		private List<ExperimentResult> results;

		public Drive(string title, long dataCapacity, long sampleCapacity);

		public Drive(ConfigNode node, string version)
		{
			is_private = Lib.ConfigValue(node, "is_private", false);
			sampleCapacity = Lib.ConfigValue(node, "sampleCapacity", -1L);
			fileCapacity = Lib.ConfigValue(node, "fileCapacity", -1L);

			// load results
			foreach (var resNode in node.GetNodes())
			{
				new ExperimentResult(this, resNode);
			}

			// migration of pre-2.3 saves
			if (string.CompareOrdinal(version, "2.3.0.0") < 0)
			{
				// dataCapacity was a double in MB, it is now a long in bit
				fileCapacity = Lib.MBToBit(Lib.ConfigValue(node, "dataCapacity", -1.0));

				// parse files to ExperimentResult
				if (node.HasNode("files"))
				{
					foreach (var file_node in node.GetNode("files").GetNodes())
					{
						string subject_id = DB.From_safe_key(file_node.name);
						string title = subject_id;
						long size = Lib.ConfigValue(node, "size", 0);
						if (size < 0) continue;
						long maxSize = 0;
						ExperimentInfo expInfo = Science.GetExperimentInfoFromSubject(subject_id);
						if (expInfo != null)
						{
							maxSize = expInfo.dataSize;
							title = expInfo.experimentTitle;
						}
						new ExperimentResult(this, FileType.File, subject_id, title, size, maxSize);
					}
				}

				// parse samples to ExperimentResult
				if (node.HasNode("samples"))
				{
					foreach (var file_node in node.GetNode("samples").GetNodes())
					{
						string subject_id = DB.From_safe_key(file_node.name);
						string title = subject_id;
						long size = Lib.ConfigValue(node, "size", 0);
						if (size < 0) continue;
						double massPerBit = Lib.ConfigValue(node, "mass", 0.0) / size;

						long maxSize = 0;
						ExperimentInfo expInfo = Science.GetExperimentInfoFromSubject(subject_id);
						if (expInfo != null)
						{
							maxSize = expInfo.dataSize;
							title = expInfo.experimentTitle;
							if (massPerBit < double.Epsilon) massPerBit = expInfo.massPerBit;
						}
						new ExperimentResult(this, FileType.Sample, subject_id, title, size, maxSize, massPerBit);
					}
				}
			}
		}

		public void Save(ConfigNode node)
		{
			node.AddValue("is_private", is_private);
			node.AddValue("sampleCapacity", sampleCapacity);
			node.AddValue("fileCapacity", fileCapacity);

			// save results
			foreach (var result in results)
			{
				result.Save(node.AddNode("result"));
			}
		}



		/// <summary>
		/// get available drive space in bits (file) or slots (sample)
		/// </summary>
		public long CapacityAvailable(FileType type)
		{
			switch (type)
			{
				case FileType.File:
					if (fileCapacity < 0) return long.MaxValue;
					return fileCapacity - CapacityUsed(type);
				case FileType.Sample:
					if (sampleCapacity < 0) return long.MaxValue;
					return sampleCapacity - CapacityUsed(type);
			}
			return 0;
		}


		/// <summary>
		/// get used drive file space in bits (file) or slots (sample)
		/// </summary>
		public long CapacityUsed(FileType type)
		{
			long amount = 0;

			for (int i = 0; i < Count; i++)
			{
				if (this[i].type == type)
				{
					switch (type)
					{

						case FileType.File:
							amount += this[i].size;
							break;
						case FileType.Sample:
							amount += Lib.SampleSizeToFullSlots(this[i].size);
							break;
					}
				}	
			}
			return amount;
		}


		public static Drive GetDrive(uint HdId)
		{
			if (DB.drives.ContainsKey(HdId)) return DB.drives[HdId];
			return null;
		}

		/// <summary>
		/// Return the drive with the most available space for the given "type"
		/// or null if no drive with at least "minCapacity" is found. Private drives are ignored.
		/// </summary>
		/// <param name="minCapacity">in bits for files, in slots for samples</param>
		/// <param name="privateHdId">if != 0, only check for the drive with the specified ID</param>
		public static Drive GetDriveBestCapacity(Vessel vessel, FileType type, long minCapacity = 1, uint privateHdId = 0)
		{
			Drive bestDrive = null;
			long bestDriveCapacity = 0;

			if (privateHdId != 0)
			{
				bestDrive = GetDrive(privateHdId);
				if (bestDrive != null) bestDriveCapacity = bestDrive.CapacityAvailable(type);
			}
			else
			{
				foreach (Drive drive in GetDrives(vessel))
				{
					if (drive.is_private) continue;

					if (bestDrive == null)
					{
						bestDrive = drive;
						bestDriveCapacity = drive.CapacityAvailable(type);
						continue;
					}

					long driveCapacity = drive.CapacityAvailable(type);
					if (driveCapacity > bestDriveCapacity)
					{
						bestDrive = drive;
						bestDriveCapacity = driveCapacity;
					}
				}
			}

			if (bestDriveCapacity < minCapacity) return null;
			return bestDrive;
		}

		public static List<Drive> GetDrives(Vessel vessel)
		{
			List<Drive> result = Cache.VesselObjectsCache<List<Drive>>(vessel, "drives");
			if (result != null)
				return result;

			result = new List<Drive>();

			if (vessel.loaded)
			{
				for (int i = 0; i < vessel.parts.Count; i++)
				{
					if (DB.drives.ContainsKey(vessel.parts[i].flightID))
						result.Add(DB.drives[vessel.parts[i].flightID]);
				}
			}
			else
			{
				for (int i = 0; i < vessel.protoVessel.protoPartSnapshots.Count; i++)
				{
					if (DB.drives.ContainsKey(vessel.protoVessel.protoPartSnapshots[i].flightID))
						result.Add(DB.drives[vessel.protoVessel.protoPartSnapshots[i].flightID]);
				}
			}

			Cache.SetVesselObjectsCache(vessel, "drives", result);
			return result;
		}

		public List<ExperimentResult> GetResultsById(string subject_id)
		{
			List<ExperimentResult> results = new List<ExperimentResult>();
			for (int i = 0; i < Count; i++)
			{
				if (results[i].subject_id == subject_id)
				{
					results.Add(results[i]);
				}
			}
			return results;
		}

		/// <summary>
		/// returns size (in bits) available on the drive for this subject_id, including space available in partially filled slots
		/// </summary>
		public long SizeCapacityAvailable(FileType type, string subject_id)
		{
			long capacity = 0;
			switch (type)
			{
				case FileType.File: capacity = fileCapacity; break;
				case FileType.Sample: capacity = sampleCapacity * Lib.slotSize; break;
			}

			if (capacity < 0) return long.MaxValue;

			for (int i = 0; i < results.Count; i++)
			{
				if (results[i].type == type)
				{
					switch (type)
					{
						case FileType.File:
							capacity -= results[i].size;
							break;
						case FileType.Sample:
							if (results[i].subject_id == subject_id)
								capacity -= results[i].size + Lib.SizeLostBySlotting(results[i].maxSize);
							else
								capacity -= Lib.SampleSizeToFullSlots(this[i].size) * Lib.slotSize;
							break;
					}
				}
			}

			if (capacity < 0)
			{
				Lib.DebugLog("WARNING : a drive is storing more data than its capacity.");
				capacity = 0;
			}

			return capacity;
		}

		public static List<ExperimentResult> GetResults(Vessel vessel)
		{
			List<ExperimentResult> results = new List<ExperimentResult>();
			foreach (var drive in GetDrives(vessel))
			{
				for (int i = 0; i < drive.Count; i++)
				{
					results.Add(drive[i]);
				}
			}
			return results;
		}

		public static List<ExperimentResult> GetResults(Vessel vessel, Func<ExperimentResult, bool> predicate)
		{
			List<ExperimentResult> results = new List<ExperimentResult>();
			foreach (var drive in GetDrives(vessel))
			{
				results.AddRange(drive.Where(predicate));
			}
			return results;
		}

		// Note : we don't sort the returned results by available drive space to avoid experiements having their active result "shifted" as more data is generated.
		// But also note that there is not garanty that the result order will stay the same between calls to this method.
		/// <summary>
		/// for the given subject_id, return the partial results if they can be completed (drive not full and maxSize not reached)
		/// <para/> "totalData" always return the sum of all data stored on the vessel for this subject_id
		/// </summary>
		public static List<ExperimentResult> FindPartialResults(Vessel vessel, string subject_id, FileType type, uint HdId, out long totalData)
		{
			//Drive result = null;
			totalData = 0;
			List<Drive> drives = GetDrives(vessel);
			List<ExperimentResult> results = new List<ExperimentResult>();
			for (int i = 0; i < drives.Count; i++)
			{
				for (int j = 0; j < drives[i].Count; j++)
				{
					if (drives[i][j].subject_id != subject_id)
						continue;
					if (drives[i][j].type != type)
						continue;

					totalData += drives[i][j].size;

					if (drives[i][j].size < drives[i][j].maxSize)
					{
						// if a private drive is specified, get the result only if it is on the private drive
						if (HdId != 0 && drives[i] != GetDrive(HdId))
							continue;

						if (type == FileType.File)
						{
							// for files, just check if there is space available
							if (drives[i].CapacityAvailable(FileType.File) > 0)
								results.Add(drives[i][j]);
						}
						else
						{
							// for samples, check if slots are available or if the last used slot is not full
							if (drives[i].CapacityAvailable(FileType.Sample) > 0 || Lib.SampleSizeFillFullSlots(drives[i][j].size))
								results.Add(drives[i][j]);
						}
						
					}
				}
			}

			return results;

			// we now have the list of results that are incomplete and whose drive isn't full
			// at most this list contains 2 results -> WRONG IN MANY CASES. if it is the case :
			// - one result is part of a complete subject splitted amongst multiple drives
			// - the other is the partial result we seek for
			//switch (results.Count)
			//{
			//	case 0: return null;
			//	case 1: return results[0];
			//	default:
			//		if (totalData - ((totalData / results[0].maxSize) * results[0].maxSize) == results[0].size)
			//			return results[1];
			//		else
			//			return results[0];
			//}
		}

		/// <summary>
		/// for the given subject_id, return the first found partial result that can be completed (drive not full and maxSize not reached),
		/// return null if no result was found
		/// <para/> "totalData" always return the sum of all data stored on the vessel for this subject_id
		/// </summary>
		public static ExperimentResult FindPartialResult(Vessel vessel, string subject_id, FileType type, uint HdId, out long totalData)
		{
			return FindPartialResults(vessel, subject_id, type, HdId, out totalData).FirstOrDefault();
		}

		#region IList implementation

		// interface methods directy wrapped to the private list
		public bool IsReadOnly => ((IList<ExperimentResult>)results).IsReadOnly;
		public IEnumerator<ExperimentResult> GetEnumerator() { return results.GetEnumerator(); }
		IEnumerator IEnumerable.GetEnumerator() { return results.GetEnumerator(); }
		public int Count => results.Count;
		public ExperimentResult this[int index] { get => results[index]; set => results[index] = value; }
		public int IndexOf(ExperimentResult item) { return results.IndexOf(item); }
		public bool Contains(ExperimentResult item) { return results.Contains(item); }

		/// <summary>
		/// utility method for transferring results between drives in Add() or Insert() IList methods
		/// </summary>
		/// <param name="index">if >= 0, the new result will be insered at index instead of added</param>
		private void TransferResult(ExperimentResult result, int index = -1)
		{
			// get exact available space in this drive
			long spaceAvailable = SizeCapacityAvailable(result.type, result.subject_id);
			if (spaceAvailable == 0) return;

			// clamp transferred amount to available space
			long sizeTransfered = Math.Min(spaceAvailable, result.size);

			// update the old result
			result.size -= sizeTransfered;
			if (result.size == 0) result.Delete();

			// get partial results that can be completed
			foreach (ExperimentResult pr in results.FindAll(
				p => p.subject_id == result.subject_id
				&& p.type == result.type
				&& p.size < p.maxSize))
			{
				long toTransfer = Math.Min(sizeTransfered, pr.maxSize - pr.size);
				sizeTransfered -= toTransfer;
				pr.size += toTransfer;
			}

			// if that wasn't enough, create a new result
			if (sizeTransfered > 0)
				new ExperimentResult(this, result, sizeTransfered, index);
		}


		/// <summary>if "result" is on another drive, it will be transfered to this drive.
		/// <para/>if there is not enough available space, the result will be partially transferred and the old result with the remaining size will remain on its drive.
		/// <para/>results that have the same "subject_id" will be merged according to their max size
		/// <para/>Be aware that this will create a new ExperimentResult object, not "change the drive" of the provided ExperimentResult
		/// </summary>
		public void Add(ExperimentResult result)
		{
			// the same result can't be added twice
			if (results.Contains(result)) return;

			// if the result drive is this one, Add() was called from the result ctor and this is a new result for this drive
			if (result.GetDrive == this)
			{
				results.Add(result);
				return;
			}

			// else we are transferring the result from another drive
			TransferResult(result);
		}

		/// <summary>if "result" is on another drive, it will be transfered to this drive.
		/// <para/>if there is not enough available space, the result will be partially transferred and the old result with the remaining size will not be deleted.
		/// <para/>results that have the same "subject_id" will be merged according to their max size
		/// <para/>Be aware that this will create a new ExperimentResult object, not "change the drive" of the provided ExperimentResult
		/// </summary>
		public void Insert(int index, ExperimentResult result)
		{
			// the same result can't be added twice
			if (results.Contains(result)) return;

			// if the result drive is this one, Insert() was called from the result ctor and this is a new result for this drive
			if (result.GetDrive == this)
			{
				results.Insert(index, result);
				return;
			}

			// else we are transferring the result from another drive
			TransferResult(result, index);
		}

		public bool Remove(ExperimentResult result)
		{
			if (results.Remove(result))
			{
				result.DeleteDriveRef();
				return true;
			}
			return false;
		}

		public void RemoveAt(int index)
		{
			if (index < results.Count)
				results[index].DeleteDriveRef();

			results.RemoveAt(index);
		}

		public void Clear()
		{
			for (int i = 0; i < results.Count; i++)
			{
				results[i].DeleteDriveRef();
			}
			results.Clear();
		}

		/// <summary>
		/// drive specific logic not implemented,
		/// do not use for transferring results between drives, use Add() or Insert() instead
		/// </summary>
		public void CopyTo(ExperimentResult[] array, int arrayIndex)
		{
			results.CopyTo(array, arrayIndex);
		}
		#endregion
	}
}
