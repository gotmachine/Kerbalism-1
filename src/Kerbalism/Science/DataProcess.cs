using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace KERBALISM
{
	/// <summary>
	/// Loaded/unloaded state independant object persisted in DB.processes, meant to be referenced by a PartModule.
	/// It is basically the always-active version of the stock PartModule and should contain all the data and game logic, .
	/// <para/>Ideally, all its methods must be able to run without ever using the PM / ProtoPM or Part/ProtoPart.
	/// The game-logic methods are called from Science.Update() for every existing DataProcess in DB.processes.
	/// <para/>When loaded, the PM should query/update it for UI stuff and if loaded-only information (physics...) is required.
	/// The PM OnLoad() should use the GetProcessOnPartModuleLoad() method for instantiation and reacquiring the reference from DB.
	/// <para/>The editor/prefab object is not persisted, so any state set in the editor should use PM persistant fields
	/// that are passed to equivalent fields on the process in the OnPartModuleLoad() method.
	/// </summary>
	public abstract class DataProcess
	{
		// persistence
		public string processId;
		public bool enabled; // must be synchronized with the PM enabled / moduleIsEnabled state, used for malfunctions
		public uint privateHdId = 0; // id of the only drive the process must use for its results, set to 0 to allow any drive
		public bool isConverter = false;
		public bool running = false;

		public Result result; // the current result this process is generating data for
		public Result convertedResult; // if this is a converter, the result being converted
		public FileType type; // the type of data that should be generated

		protected long dataPending;
		public long existingData;
		public List<string> issues;

		// TODO : the subject should be a field implemented in here in the base class
		/// <summary> the Subject that this process is generating </summary>
		public Subject Subject { get; private set; }


		/// <summary>
		/// this method must be called from the PartModule OnLoad() to instantiate/reacquire the reference
		/// of the process
		/// </summary>
		public static T GetProcessOnPartModuleLoad<T>(PartModule partModule, string processId, bool isConverter = false) where T : DataProcess
		{
			T process = null;
			Type processType = typeof(T);

			// the prefab don't need a process
			if (HighLogic.LoadedScene == GameScenes.LOADING)
			{
				return null;
			}

			// Create a dummy process in the editor
			if (Lib.IsEditor())
			{
				process = Activator.CreateInstance(processType) as T;
				process.processId = processId;
				process.isConverter = isConverter;
			}

			// in flight get/add the process from the DB
			if (Lib.IsFlight())
			{
				// get the process from the DB
				process = DB.GetDataProcess(partModule.part.flightID, processId) as T;

				// if not present, this is a newly created part, create the process and add it to the DB
				if (process == null)
				{
					// instantiate the process and set the base fields
					process = Activator.CreateInstance(processType) as T;
					process.processId = processId;
					process.enabled = partModule.enabled && partModule.moduleIsEnabled;
					process.isConverter = isConverter;
					process.issues = new List<string>();

					// find the private hd
					HardDrive hd = partModule.part.FindModuleImplementing<HardDrive>();
					if (hd != null && hd.experiment_id == processId)
					{
						process.privateHdId = partModule.part.flightID;
						hd.SetPrivate(true);
					}
					else
					{
						process.privateHdId = 0u;
						hd.SetPrivate(false);
					}
						

					// call process specific implementation
					process.OnPartModuleLoad(partModule);

					// add the new process to the DB
					DB.AddDataProcess(partModule.part.flightID, process);
				}
			}

			return process;
		}

		public static DataProcess Load(ConfigNode node)
		{
			// get Type
			string typeStr = Lib.ConfigValue(node, "processType", "invalid");
			Type processType = Type.GetType(typeStr);
			if (processType == null)
			{
				Lib.Log("DB LOAD ERROR : dataProcess of type '" + typeStr + "' doesn't exist");
				return null;
			}

			// create the process using the default ctor
			DataProcess process = Activator.CreateInstance(processType) as DataProcess;

			// deserialization
			process.processId = Lib.ConfigValue(node, "processId", "invalid");
			process.enabled = Lib.ConfigValue(node, "enabled", true);
			process.privateHdId = Lib.ConfigValue(node, "privateHdId", 0u);
			process.running = Lib.ConfigValue(node, "running", false);
			switch (Lib.ConfigValue(node, "type", "invalid"))
			{
				case nameof(FileType.File): process.type = FileType.File; break;
				case nameof(FileType.Sample): process.type = FileType.Sample; break;
				default: process.type = FileType.File; break;
			}

			return process;
		}

		public void Save(ConfigNode node)
		{
			node.AddValue("type", GetType().FullName);
			node.AddValue("processId", processId);
			node.AddValue("enabled", enabled);
			node.AddValue("privateHdId", privateHdId);
			node.AddValue("running", running);

			OnSave(node);
		}

		/// <summary> vessel dataprocess caching </summary>
		public static List<DataProcess> GetProcesses(Vessel vessel)
		{
			List<DataProcess> result = Cache.VesselObjectsCache<List<DataProcess>>(vessel, "processes");
			if (result != null)
				return result;

			result = new List<DataProcess>();

			if (vessel.loaded)
			{
				for (int i = 0; i < vessel.parts.Count; i++)
				{
					if (DB.processes.ContainsKey(vessel.parts[i].flightID))
						result.AddRange(DB.processes[vessel.parts[i].flightID]);
				}
			}
			else
			{
				for (int i = 0; i < vessel.protoVessel.protoPartSnapshots.Count; i++)
				{
					if (DB.processes.ContainsKey(vessel.protoVessel.protoPartSnapshots[i].flightID))
						result.AddRange(DB.processes[vessel.protoVessel.protoPartSnapshots[i].flightID]);
				}
			}

			Cache.SetVesselObjectsCache(vessel, "processes", result);
			return result;
		}

		/// <summary>
		/// this is a pseudo ctor called when the in-flight part is first created (not from the editor),
		/// it must setup everything specific to the DataProcess implementation
		/// and can use the partmodule (by casting) for passing persistant fields that were set in the editor
		/// </summary>
		public abstract void OnPartModuleLoad(PartModule partModule);

		/// <summary> process specific serialization </summary>
		public abstract void OnSave(ConfigNode node);

		/// <summary> process specific deserialization </summary>
		public abstract void OnLoad(ConfigNode node);

		/// <summary> converter process must return null  </summary>
		public abstract ExperimentInfo GetExperimentInfo();

		/// <summary> process specific deserialization </summary>
		public bool PreUpdate(Vessel vessel, double elapsed_s)
		{
			// clear deleted results reference
			if (result != null && result.IsDeleted()) result = null;
			if (convertedResult != null && convertedResult.IsDeleted()) convertedResult = null;

			// get subject and search for existing result
			bool subjectHasChanged = false;

			// converters don't require a subject from a converted result because the result
			// may not exist yet and a non-converter may be creating some data that is fully converted (nothing stored).
			// Result/Subject may eventually be created on the fly in Science.Update()
			if (isConverter)
			{
				if (convertedResult == null)
				{
					convertedResult = Drive.GetFirstResult(vessel, p => p.process);
					if (convertedResult != null)
					{
						Subject = convertedResult.Subject;
						subjectHasChanged = true;
					}
					else
					{
						Subject = null;
					}
				}

				return CanRun(vessel, elapsed_s, subjectHasChanged);
			}

			// non-converters must always have a subject
			if (Subject == null || Subject.HasChanged(vessel))
			{
				Subject = Subject.GetCurrentSubject(GetExperimentInfo(), vessel);
				result = null;
				existingData = 0;
			}
			// TODO : partial subject finding should go in Science.Update() so we only do it if needed
			if (result == null && Subject != null && Subject.isValid)
			{
				result = Drive.FindPartialResult(vessel, Subject.SubjectId, type, privateHdId, out existingData);
			}

			// non-converters only get processed if their subject is valid
			if (!Subject.isValid)
				return false;

			issues.Clear();
			return CanRun(vessel, elapsed_s, subjectHasChanged);
		}

		/// <summary>
		/// must check running conditions
		/// </summary>
		public abstract bool CanRun(Vessel vessel, double elapsed_s, bool subjectHasChanged);

		/// <summary>
		/// must return the potential data generated (ignoring potential drive capacity bottleneck)
		/// based on the process internal rules and elapsed_s
		/// </summary>
		/// <param name="dataToConvert">if this is a converter, return value must account for dataToConvert,
		/// the (potential) data amount to be converted that other processes are generating</param>
		public abstract long GetDataPending(Vessel vessel, double elapsed_s, long dataToConvert = 0);

		/// <summary>
		/// using dataProcessed, consume resources and do process-specific things like removing sample mass
		/// </summary>
		public abstract void PostUpdate(Vessel vessel, double elapsed_s, long dataProcessed);

		/// <summary>
		/// If no result exists, create a new (empty) result for the process.
		/// Using minDriveCapacity = 0 will allow the creation of the result on a full drive.
		/// </summary>
		/// <returns>true if a new result was created</returns>
		public virtual bool CreateResult(Vessel vessel, long minDriveCapacity = 1)
		{
			if (result == null)
			{
				Drive drive = Drive.GetDriveBestCapacity(vessel, type, minDriveCapacity, privateHdId);
				if (drive != null)
				{
					result = new Result(drive, type, Subject);
					return true;
				}
			}
			return false;
		}
	}
}
