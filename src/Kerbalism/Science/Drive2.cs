using System;
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
	/// drive data as a dictionnary
	/// key is the "experiment@situation" string (usual var name is "subject_id")
	/// value is a list of ExperimentData representing a single file or sample (allowing for duplicates)
	/// </summary>
	///

	public class Drive2 : List<ExperimentResult>
	{
		/// <summary> file capacity in bits </summary>
		public long dataCapacity;
		/// <summary> sample capacity in slots </summary>
		public long sampleCapacity;
		public string name = String.Empty; // TODO : what is this used for ?
		public bool is_private = false;

		// TODO : migrate from dictionary
		public void AddData(ConfigNode node)
		{
			ExperimentResult expRes = new ExperimentResult(node);
			if (ContainsKey(DB.From_safe_key(node.name)))
				this[DB.From_safe_key(node.name)].Add(expRes);
			else
				Add(DB.From_safe_key(node.name), new List<ExperimentResult> { expRes });
		}

		// TODO : migrate from dictionary
		/// <summary>Add a result, merging with an existing partial result if present</summary>
		public void AddData(ExperimentResult expRes)
		{
			expRes.ClampToMaxSize();

			if (!ContainsKey(expRes.subject_id))
				Add(expRes.subject_id, new List<ExperimentResult> { expRes });
			else
			{
				for (int i = 0; i < this[expRes.subject_id].Count; i++)
				{
					// if there is already an incomplete result, complete it before creating a new result
					if (this[expRes.subject_id][i].type == expRes.type &&
						this[expRes.subject_id][i].size < this[expRes.subject_id][i].MaxSize())
					{
						double addedData = Math.Min(this[expRes.subject_id][i].MaxSize() - this[expRes.subject_id][i].size, expRes.size);
						this[expRes.subject_id][i].size += addedData;
						if (expRes.type == ExperimentResult.DataType.Sample)
						{
							double factor = addedData / expRes.size;
							this[expRes.subject_id][i].mass += factor * expRes.mass;
							// update mass left
							expRes.mass *= 1.0 - factor;
						}
						// update size left
						expRes.size -= addedData;

						if (expRes.size > 0)
						{
							// there is still some data left, create a new result
							this[expRes.subject_id].Add(expRes);
							return;
						}
						else
						{
							expRes = null;
							return;
						}
					}
				}
			}
		}

		public void AddData(string subject_id, ExperimentResult.DataType type, double size, double mass = 0)
		{


		}


		/// <summary>
		/// get available drive space in bits (file) or slots (sample)
		/// </summary>
		public long CapacityAvailable(FileType type)
		{
			switch (type)
			{
				case FileType.File:
					if (dataCapacity < 0) return long.MaxValue;
					return dataCapacity - CapacityUsed(type);
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


		public static Drive2 GetDrive(uint HdId)
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
		public static Drive2 GetDriveBestCapacity(Vessel vessel, FileType type, long minCapacity = 1, uint privateHdId = 0)
		{
			Drive2 bestDrive = null;
			long bestDriveCapacity = 0;

			if (privateHdId != 0)
			{
				bestDrive = GetDrive(privateHdId);
				if (bestDrive != null) bestDriveCapacity = bestDrive.CapacityAvailable(type);
			}
			else
			{
				foreach (Drive2 drive in GetDrives(vessel))
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

		public static List<Drive2> GetDrives(Vessel vessel)
		{
			List<Drive2> result = Cache.VesselObjectsCache<List<Drive2>>(vessel, "drives");
			if (result != null)
				return result;

			result = new List<Drive2>();

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

		public static List<ExperimentResult> GetResults(Vessel vessel, Predicate<ExperimentResult> p)
		{
			List<ExperimentResult> results = new List<ExperimentResult>();
			foreach (var drive in GetDrives(vessel))
			{
				results.AddRange(drive.FindAll(p));
			}
			return results;
		}

		/// <summary>
		/// for the given subject_id, return the partial result if it exists and can be completed (drive not full and subject_max_data not reached)
		/// <para/> "totalData" always return the sum of all data stored on the vessel for this subject_id
		/// </summary>
		public static ExperimentResult FindPartialResult(Vessel vessel, ExperimentInfo exp_info, string subject_id, FileType type, uint HdId, out long totalData)
		{
			//Drive result = null;
			totalData = 0;
			List<Drive2> drives = GetDrives(vessel);
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

					if (drives[i][j].size < exp_info.dataSize)
					{
						// if a private drive is specified, get the result only if it is on the private drive
						if (HdId != 0 && drives[i] != GetDrive(HdId)) continue;

						if (type == FileType.File)
						{
							// for files, just check if there is space available
							if (drives[i].CapacityAvailable(FileType.File) > 0)
								results.Add(drives[i][j]);
						}
						else
						{
							// for samples, check if slots are available or the last used slot is not full
							if (drives[i].CapacityAvailable(FileType.Sample) > 0 || Lib.SampleSizeFillFullSlots(drives[i][j].size))
								results.Add(drives[i][j]);
						}
						
					}
				}
			}
			// we now have the list of results that are incomplete and whose drive isn't full
			// at most this list contains 2 results. if it is the case :
			// - one result is part of a complete subject splitted amongst multiple drives
			// - the other is the partial result we seek for
			switch (results.Count)
			{
				case 0: return null;
				case 1: return results[0];
				default:
					if (totalData - ((totalData / exp_info.dataSize) * exp_info.dataSize) == results[0].size)
						return results[1];
					else
						return results[0];
			}
		}
	}
}
