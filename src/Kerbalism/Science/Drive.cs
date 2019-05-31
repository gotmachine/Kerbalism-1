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

	// TODO : limitaton on amount of results

	/// <summary>
	/// drive is a list of Result with specific handling when calling the usual List methods
	/// </summary>
	public class Drive : IList<Result>
	{
		// caching to avoid costly queries over DB.drives
		public uint partId;

		// persistance
		/// <summary>file capacity in bits, -1 -> infinite</summary>
		public long fileCapacity;
		/// <summary>sample capacity in bits, -1 -> infinite</summary>
		public long sampleCapacity;
		public bool isPrivate = false;
		public string partName = string.Empty;

		private List<Result> results = new List<Result>();

		#region ctor and serialization

		public Drive(uint partId, string partName, long fileCapacity, long sampleCapacity, bool isPrivate)
		{
			this.fileCapacity = fileCapacity;
			this.sampleCapacity = sampleCapacity;
			this.partName = partName;
			this.partId = partId;
			this.isPrivate = isPrivate;
		}

		public Drive(ConfigNode node, string version, uint partId)
		{
			partName = Lib.ConfigValue(node, "partName", string.Empty);
			isPrivate = Lib.ConfigValue(node, "is_private", false);
			sampleCapacity = Lib.ConfigValue(node, "sampleCapacity", -1L);
			fileCapacity = Lib.ConfigValue(node, "fileCapacity", -1L);
			this.partId = partId;

			// load results
			foreach (var resNode in node.GetNodes("result"))
			{
				new Result(this, resNode);
			}

			// migration of pre-2.3 saves
			if (string.CompareOrdinal(version, "2.3.0.0") < 0) // meh... remember never to use a version number > 9
			{
				// dataCapacity was a double in MB, it is now a long in bit
				fileCapacity = Lib.MBToBit(Lib.ConfigValue(node, "dataCapacity", -1.0));

				// parse files to ExperimentResult
				if (node.HasNode("files"))
				{
					foreach (var file_node in node.GetNode("files").GetNodes())
					{
						string subject_id = DB.From_safe_key(file_node.name);
						if (Science.GetSubjectFromCache(subject_id) == null)
							continue;
						long size = Lib.MBToBit(Lib.ConfigValue(node, "size", 0d));
						if (size < 0)
							continue;
						new Result(this, FileType.File, subject_id, size);
					}
				}

				// parse samples to ExperimentResult
				if (node.HasNode("samples"))
				{
					foreach (var file_node in node.GetNode("samples").GetNodes())
					{
						string subject_id = DB.From_safe_key(file_node.name);
						if (Science.GetSubjectFromCache(subject_id) == null)
							continue;
						long size = Lib.MBToBit(Lib.ConfigValue(node, "size", 0.0));
						if (size < 0)
							continue;
						new Result(this, FileType.Sample, subject_id, size);
					}
				}
			}
		}

		public void Save(ConfigNode node)
		{
			node.AddValue("partName", partName);
			node.AddValue("is_private", isPrivate);
			node.AddValue("sampleCapacity", sampleCapacity);
			node.AddValue("fileCapacity", fileCapacity);

			// save results
			foreach (var result in results)
			{
				result.Save(node.AddNode("result"));
			}
		}

		#endregion

		#region info methods
		/// <summary>
		/// get available drive space in bit
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
				default:
					return 0;
			}
		}

		/// <summary>
		/// get used drive space in bit
		/// </summary>
		public long CapacityUsed(FileType type)
		{
			long amount = 0;

			for (int i = 0; i < Count; i++)
			{
				if (this[i].type == type)
					amount += this[i].Size;
			}
			return amount;
		}

		/// <summary>return true if the drive is empty</summary>
		/// <param name="ignoreEmptyResults">if true, a drive that only contains result(s) with zero size will considered empty</param>
		public bool Empty(bool ignoreEmptyResults = false)
		{
			if (results.Count == 0)
				return true;

			if (ignoreEmptyResults)
			{
				for (int i = 0; i < results.Count; i++)
				{
					if (results[i].Size > 0)
						return false;
				}
				return true;
			}
			return false;
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
					capacity -= results[i].Size;
			}

			if (capacity < 0)
			{
				Lib.DebugLog("WARNING : a drive is storing more data than its capacity.");
				capacity = 0;
			}

			return capacity;
		}

		public double GetMass()
		{
			double sampleMass = 0f;
			for (int i = 0; i < Count; i++)
			{
				sampleMass += this[i].GetMass();
			}
			return sampleMass;
		}


		// TODO : better info, use the static stringbuilder
		public string GetStorageInfo()
		{
			int filesCount;
			long filesSize;
			int samplesCount;
			long samplesSize;
			double samplesMass;

			GetStorageInfo(out filesCount, out filesSize, out samplesCount, out samplesSize, out samplesMass);

			StringBuilder sb = new StringBuilder();
			if (fileCapacity > 0)
			{
				sb.Append(Lib.HumanReadableDataUsage(filesSize, fileCapacity));
			}
			else
			{
				sb.Append(Lib.HumanReadableDataSize(filesSize));
				sb.Append(" used");
			}

			if (sampleCapacity != 0)
			{
				if (fileCapacity != 0)
					sb.Append(", ");

				sb.Append(Lib.HumanReadableDataSize(samplesSize, true));
				if (sampleCapacity > 0)
				{
					sb.Append("/");
					sb.Append(Lib.HumanReadableDataSize(sampleCapacity));
					sb.Append(" slot");
				}
				else
				{
					sb.Append(" slot used");
				}

				if (samplesMass > 0)
				{
					sb.Append(" (");
					sb.Append(Lib.HumanReadableMass(samplesMass));
					sb.Append(")");
				}
			}
			return sb.ToString();
		}

		/// <summary>optimized single-loop method for getting all drive stats</summary>
		public void GetStorageInfo(out int fileCount, out long filesSize, out int sampleCount, out long samplesSize, out double samplesMass)
		{
			fileCount = sampleCount = 0;
			filesSize = samplesSize = 0L;
			samplesMass = 0.0;

			for (int i = 0; i < results.Count; i++)
			{
				switch (results[i].type)
				{
					case FileType.File:
						filesSize += results[i].Size;
						fileCount++;
						break;
					case FileType.Sample:
						samplesSize += results[i].Size;
						samplesMass += results[i].GetMass();
						sampleCount++;
						break;
					default:
						break;
				}
			}
		}

		/// <summary>return % of file capacity available on all the vessel drives, private drives not included</summary>
		public static double GetAvailableFileSpace(Vessel vessel, out long freeCapacity)
		{
			freeCapacity = 0;
			long totalCapacity = 0;

			foreach (Drive drive in GetDrives(vessel))
			{
				if (drive.isPrivate)
					continue;

				if (drive.fileCapacity < 0)
					return 1.0;

				totalCapacity += drive.fileCapacity;
				freeCapacity += drive.fileCapacity;
				for (int i = 0; i < drive.Count; i++)
				{
					if (drive[i].type == FileType.File)
						freeCapacity -= drive[i].Size;
				}

			}

			if (totalCapacity == 0)
				return 0.0;

			return (double)freeCapacity / (double)totalCapacity;
		}



		#endregion

		#region result related methods

		public List<Result> GetResultsById(string subject_id)
		{
			return results.FindAll(p => p.subjectId == subject_id);
		}

		/// <summary>From this drive, get all samples flagged for analysis</summary>
		public List<Result> GetResultsToAnalyze(Vessel vessel)
		{
			return results.FindAll(p => p.process && p.type == FileType.Sample);
		}

		/// <summary>From this drive, get all files flagged for transmission</summary>
		public List<Result> GetResultsToTransmit(Vessel vessel)
		{
			return results.FindAll(p => p.process && p.type == FileType.File);
		}

		/// <summary>From this drive, get the results marked for transfer and that can be transferred</summary>
		public List<Result> GetResultsToTransfer(Vessel vessel)
		{
			int crewCount = Lib.CrewCount(vessel);
			return results.FindAll(p => p.transfer && p.CanTransfer(crewCount));
		}

		/// <summary>From this drive, get the size of the results marked for transfer and that can be transferred</summary>
		public void GetTransferrableResultsSize(Vessel vessel, ref long transferFileSize, ref long transferSampleSize)
		{
			int crewCount = Lib.CrewCount(vessel);

			for (int i = 0; i < results.Count; i++)
			{
				if (results[i].transfer && results[i].CanTransfer(crewCount))
				{
					switch (results[i].type)
					{
						case FileType.File: transferFileSize += results[i].Size; break;
						case FileType.Sample: transferSampleSize += results[i].Size; break;
						default: break;
					}
				}
			}
		}

		#endregion

		#region static methods
		public static Drive GetDrive(uint HdId)
		{
			if (DB.drives.ContainsKey(HdId)) return DB.drives[HdId];
			return null;
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

		public static List<Drive> GetDrives(ProtoVessel protoVessel)
		{
			List<Drive> result = new List<Drive>();

			foreach (ProtoPartSnapshot pp in protoVessel.protoPartSnapshots)
			{
				if (DB.drives.ContainsKey(pp.flightID))
					result.Add(DB.drives[pp.flightID]);
			}

			return result;
		}

		/// <summary>
		/// Return the drive with the most available space for the given "type"
		/// or null if no drive with at least "minCapacity" is found. Private drives are ignored.
		/// </summary>
		/// <param name="minCapacity">in bit</param>
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
					if (drive.isPrivate)
						continue;

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

			if (bestDriveCapacity < minCapacity)
				return null;
			return bestDrive;
		}

		/// <summary>From the vessel drives, return the first result according to the predicate or null if none found</summary>
		public static Result GetFirstResult(Vessel vessel, Func<Result, bool> predicate)
		{
			Result result = null;
			foreach (var drive in GetDrives(vessel))
			{
				result = drive.FirstOrDefault(predicate);
				if (result != null)
					return result;
			}
			return result;
		}

		/// <summary>From the vessel drives, return all results</summary>
		public static List<Result> GetResults(Vessel vessel)
		{
			List<Result> results = new List<Result>();
			foreach (var drive in GetDrives(vessel))
			{
				for (int i = 0; i < drive.Count; i++)
				{
					results.Add(drive[i]);
				}
			}
			return results;
		}

		/// <summary>From the vessel drives, return all results according to the predicate</summary>
		public static List<Result> GetResults(Vessel vessel, Func<Result, bool> predicate)
		{
			List<Result> results = new List<Result>();
			foreach (var drive in GetDrives(vessel))
			{
				results.AddRange(drive.Where(predicate));
			}
			return results;
		}

		/// <summary>From the vessel drives, return all results marked for transfer and that can be transferred</summary>
		public static List<Result> GetAllResultsToTransfer(Vessel vessel)
		{
			return GetResults(vessel, p => p.transfer && p.CanTransfer(vessel));
		}

		// Note : there is not garanty that the result order will stay the same between calls to this method.
		/// <summary>
		/// for the given subject_id, return the partial results if they can be completed (drive not full and maxSize not reached)
		/// <para/> "totalData" always return the sum of all data stored on the vessel for this subject_id
		/// </summary>
		public static List<Result> FindPartialResults(Vessel vessel, string subject_id, FileType type, uint HdId, out long totalData)
		{
			totalData = 0;
			List<Result> results = new List<Result>();
			foreach (Drive drive in GetDrives(vessel))
			{
				for (int i = 0; i < drive.Count; i++)
				{
					if (drive[i].subjectId != subject_id)
						continue;
					if (drive[i].type != type)
						continue;

					totalData += drive[i].Size;

					if (drive[i].Size < drive[i].MaxSize)
					{
						// if a private drive is specified, get the result only if it is on the private drive
						if (HdId != 0 && drive != GetDrive(HdId))
							continue;

						if (drive.CapacityAvailable(type) > 0)
							results.Add(drive[i]);
					}
				}
			}
			return results;
		}

		/// <summary>
		/// for the given subject_id, return the first found partial result that can be completed (drive not full and maxSize not reached),
		/// return null if no result was found
		/// <para/> "totalData" always return the sum of all data stored on the vessel for this subject_id
		/// </summary>
		public static Result FindPartialResult(Vessel vessel, string subject_id, FileType type, uint HdId, out long totalData)
		{
			return FindPartialResults(vessel, subject_id, type, HdId, out totalData).FirstOrDefault();
		}


		public static void TransferResultsToVessel(List<Result> results, Vessel toVessel)
		{
			bool allTransfered = results.Count == 0;
			foreach (Result result in results)
			{
				GetResults(toVessel, p => p.subjectId == result.subjectId && p.type == result.type)
					.Where(p => p.DriveCapacityAvailable() > 0 && !p.GetDrive.isPrivate)
					.ToList()
					.ForEach(p => p.GetDrive.Add(p));

				while (!result.IsDeleted())
				{
					Drive drive = GetDriveBestCapacity(toVessel, result.type);
					if (drive == null) break;
					drive.Add(result);
				}
			}
		}

		#endregion

		#region IList implementation

		// interface methods directy wrapped to the private list
		public bool IsReadOnly => ((IList<Result>)results).IsReadOnly;
		public IEnumerator<Result> GetEnumerator() => results.GetEnumerator();
		IEnumerator IEnumerable.GetEnumerator() => results.GetEnumerator();
		public int Count => results.Count;
		public Result this[int index] { get => results[index]; set => results[index] = value; }
		public int IndexOf(Result item) => results.IndexOf(item);
		public bool Contains(Result item) => results.Contains(item);



		/// <summary>if "result" is on another drive, it will be transfered to this drive.
		/// <para/>if there is not enough available space, the result will be partially transferred and the old result with the remaining size will remain on its drive.
		/// <para/>results that have the same "subject_id" will be merged according to their max size
		/// <para/>Be aware that this will create a new ExperimentResult object, not "change the drive" of the provided ExperimentResult
		/// </summary>
		public void Add(Result result)
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
		public void Insert(int index, Result result)
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

		/// <summary>
		/// utility method for transferring results between drives in Add() or Insert() IList methods
		/// </summary>
		/// <param name="index">if >= 0, the new result will be insered at index instead of added</param>
		private void TransferResult(Result result, int index = -1)
		{
			// get available space in this drive
			long spaceAvailable = result.DriveCapacityAvailable();
			if (spaceAvailable == 0) return;

			// clamp transferred amount to available space
			long sizeTransfered = Math.Min(spaceAvailable, result.Size);

			// remove data from the old result
			if (!result.RemoveData(ref sizeTransfered))
				result.Delete();

			// get results of the same type (full or not)
			foreach (Result pr in results.FindAll(
				p => p.subjectId == result.subjectId
				&& p.type == result.type))
			{
				// try to add some data and keep track of what was added
				sizeTransfered -= pr.AddData(sizeTransfered);
			}

			// if that wasn't enough, create a new result
			if (sizeTransfered > 0)
				new Result(this, result, sizeTransfered, index);
		}

		public bool Remove(Result result)
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
		public void CopyTo(Result[] array, int arrayIndex)
		{
			results.CopyTo(array, arrayIndex);
		}
		#endregion
	}
}
