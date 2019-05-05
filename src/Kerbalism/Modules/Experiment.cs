using System;
using System.Collections.Generic;
using Experience;
using UnityEngine;
using KSP.Localization;
using System.Collections;

// TODO fix multiple experiements can run at once (and check the whole Toggle() code, things seems to be quirky around there)
// TODO fix the button text stopped/waiting
// TODO see what we can do about the "time remaining" incoherencies (take into account science remaining when smart mode is ON)
// TODO check that experiements are completed up to the last bit, i'm pretty sure this is fucked since I fiddled with drives
// TODO update SampleDrive() like GetbestFileDrive()

// TODO Get ride of dataSampled, it makes no sense to have it since it has to become a duplicate of the file/sample.size.
// Instead keep a direct reference to the size/sample.

	// TODO Move sample_mass, requires,  to the experiement definition
	// 

namespace KERBALISM
{

	public sealed class Experiment : PartModule, ISpecifics, IPartMassModifier
	{
		// config
		[KSPField] public string experiment_id;               // id of associated experiment definition
		[KSPField] public string experiment_desc = string.Empty;  // some nice lines of text
		[KSPField] public double data_rate;                   // sampling rate in Mb/s
		[KSPField] public double ec_rate;                     // EC consumption rate per-second
		[KSPField] public float sample_mass = 0f;             // if set to anything but 0, the experiment is a sample.
		[KSPField] public float sample_reservoir = 0f;        // the amount of sampling mass this unit is shipped with
		[KSPField] public bool sample_collecting = false;     // if set to true, the experiment will generate mass out of nothing
		[KSPField] public bool allow_shrouded = true;         // true if data can be transmitted
		[KSPField] public string requires = string.Empty;     // additional requirements that must be met
		[KSPField] public string crew_operate = string.Empty; // operator crew. if set, crew has to be on vessel while recording
		[KSPField] public string crew_reset = string.Empty;   // reset crew. if set, experiment will stop recording after situation change
		[KSPField] public string crew_prepare = string.Empty; // prepare crew. if set, experiment will require crew to set up before it can start recording 
		[KSPField] public string resources = string.Empty;    // resources consumed by this experiment
		[KSPField] public bool hide_when_unavailable = false; // don't show UI when the experiment is unavailable

		// animations
		[KSPField] public string anim_deploy = string.Empty; // deploy animation
		[KSPField] public bool anim_deploy_reverse = false;

		[KSPField] public string anim_loop = string.Empty; // deploy animation
		[KSPField] public bool anim_loop_reverse = false;

		// persistence
		[KSPField(isPersistant = true)] public bool recording;
		[KSPField(isPersistant = true)] public string issue = string.Empty;
		[KSPField(isPersistant = true)] public string last_subject_id = string.Empty;
		[KSPField(isPersistant = true)] public bool didPrepare = false;
		[KSPField(isPersistant = true)] public double dataSampled2 = 0.0;
		[KSPField(isPersistant = true)] public bool shrouded = false;
		[KSPField(isPersistant = true)] public double remainingSampleMass = 0;
		[KSPField(isPersistant = true)] public bool broken = false;
		[KSPField(isPersistant = true)] public double scienceValue = 0;
		[KSPField(isPersistant = true)] public bool forcedRun = false;
		[KSPField(isPersistant = true)] public uint privateHdId = 0;

		// reference to the current data on the drive, only used when loaded
		// can be a "Sample", a "File" or null if no data exists, be extra carefull with this
		private Sample currentSample = null;
		private File currentFile = null;
		private Drive currentDrive = null;
		//private double lastDataSampled = 0;

		private static readonly string insufficient_storage = "insufficient storage";

		private State state = State.STOPPED;
		// animations
		internal Animator deployAnimator;
		internal Animator loopAnimator;

		private CrewSpecs operator_cs;
		private CrewSpecs reset_cs;
		private CrewSpecs prepare_cs;
		private List<KeyValuePair<string, double>> resourceDefs;
		private double next_check = 0;

		private String situationIssue = String.Empty;

		public enum State
		{
			STOPPED = 0, WAITING, RUNNING, ISSUE
		}

		public static State GetState(Vessel v, double scienceValue, string issue, bool recording, bool forcedRun)
		{
			bool hasValue = scienceValue > double.Epsilon;
			bool smartScience = DB.Vessel(v).cfg_smartscience;

			if (issue.Length > 0) return State.ISSUE;
			if (!recording) return State.STOPPED;
			if (!hasValue && forcedRun) return State.RUNNING;
			if (!hasValue && smartScience) return State.WAITING;
			return State.RUNNING;
		}

		public override void OnLoad(ConfigNode node)
		{
			// build up science sample mass database
			if (HighLogic.LoadedScene == GameScenes.LOADING)
			{
				if (experiment_id == null)
				{
					Lib.Log("ERROR: EXPERIMENT WITHOUT EXPERIMENT_ID IN PART " + part);
				}
				else
				{
					Science.RegisterSampleMass(experiment_id, sample_mass);
				}
			}

			if(Lib.IsEditor())
			{
				remainingSampleMass = sample_mass;
				if (sample_reservoir > float.Epsilon)
					remainingSampleMass = sample_reservoir;
				GameEvents.onEditorShipModified.Fire(EditorLogic.fetch.ship);
			}
		}

		public override void OnStart(StartState state)
		{
			// don't break tutorial scenarios
			if (Lib.DisableScenario(this)) return;

			// create animators
			deployAnimator = new Animator(part, anim_deploy);
			deployAnimator.reversed = anim_deploy_reverse;

			loopAnimator = new Animator(part, anim_loop);
			loopAnimator.reversed = anim_loop_reverse;

			// set initial animation states
			deployAnimator.Still(recording ? 1.0 : 0.0);
			loopAnimator.Still(recording ? 1.0 : 0.0);
			if (recording) loopAnimator.Play(false, true);

			// parse crew specs
			if(!string.IsNullOrEmpty(crew_operate))
				operator_cs = new CrewSpecs(crew_operate);
			if (!string.IsNullOrEmpty(crew_reset))
				reset_cs = new CrewSpecs(crew_reset);
			if (!string.IsNullOrEmpty(crew_prepare))
				prepare_cs = new CrewSpecs(crew_prepare);

			resourceDefs = ParseResources(resources);

			foreach (var hd in part.FindModulesImplementing<HardDrive>())
			{
				if (hd.experiment_id == experiment_id) privateHdId = part.flightID;
			}

			if (Lib.IsFlight())
			{
				currentDrive = GetDriveAndData(this, last_subject_id, vessel, privateHdId, out currentFile, out currentSample);
				lastDataSampled = GetDataSampled();
			}
				
			//if (Lib.IsFlight()) dataSampled = GetDataSampledInDrive(this, last_subject_id, vessel, privateHdId);

			Events["Toggle"].guiActiveUncommand = true;
			Events["Toggle"].externalToEVAOnly = true;
			Events["Toggle"].requireFullControl = false;

			Events["Prepare"].guiActiveUncommand = true;
			Events["Prepare"].externalToEVAOnly = true;
			Events["Prepare"].requireFullControl = false;

			Events["Reset"].guiActiveUncommand = true;
			Events["Reset"].externalToEVAOnly = true;
			Events["Reset"].requireFullControl = false;
		}

		public static bool Done(ExperimentVariantInfo exp, double dataSampled)
		{
			if (exp.data_max < double.Epsilon) return false;
			return dataSampled >= exp.data_max;
		}

		public void Update()
		{
			var exp = Science.Experiment(experiment_id);

			// in flight
			if (Lib.IsFlight())
			{
				Vessel v = FlightGlobals.ActiveVessel;
				if (v == null || EVA.IsDead(v)) return;

				// get info from cache
				Vessel_info vi = Cache.VesselInfo(vessel);

				// do nothing if vessel is invalid
				if (!vi.is_valid) return;

				var sampleSize = exp.data_max;
				var dataSampled = GetDataSampled();
				var eta = data_rate < double.Epsilon || Done(exp, dataSampled) ? " (done)" : " " + Lib.HumanReadableCountdown((sampleSize - dataSampled) / data_rate); //TODO  account for remaining science value, not only size 

				// update ui
				var title = Lib.Ellipsis(exp.name, Styles.ScaleStringLength(24));

				var valueTotal = Science.TotalValue(last_subject_id);
				var valueDone = valueTotal - scienceValue;
				bool done = scienceValue < double.Epsilon;
				string statusString = Lib.Color(done ? "green" : "#00ffffff", Lib.BuildString("•", valueDone.ToString("F1"), "/", valueTotal.ToString("F1"), " "), true);

				switch (state) {
					case State.ISSUE: statusString += Lib.Color("yellow", issue); break;
					case State.RUNNING:
						if (done) statusString = Lib.BuildString(Lib.Color("red", "re-running "), Lib.Color("green", eta));
						else statusString += Lib.Color("green", eta);
						break;
					case State.WAITING: statusString += "waiting..."; break;
					case State.STOPPED: statusString += "stopped"; break;
				}

				Events["Toggle"].guiName = Lib.StatusToggle(title, statusString);
				Events["Toggle"].active = (prepare_cs == null || didPrepare);

				Events["Prepare"].guiName = Lib.BuildString("Prepare <b>", exp.name, "</b>");
				Events["Prepare"].active = !didPrepare && prepare_cs != null && string.IsNullOrEmpty(last_subject_id);

				Events["Reset"].guiName = Lib.BuildString("Reset <b>", exp.name, "</b>");
				// we need a reset either if we have recorded data or did a setup
				bool resetActive = (reset_cs != null || prepare_cs != null) && !string.IsNullOrEmpty(last_subject_id);
				Events["Reset"].active = resetActive;

				if(issue.Length > 0 && hide_when_unavailable && issue != insufficient_storage)
				{
					Events["Toggle"].active = false;
				}
			}
			// in the editor
			else if (Lib.IsEditor())
			{
				// update ui
				Events["Toggle"].guiName = Lib.StatusToggle(exp.name, recording ? "recording" : "stopped");
				Events["Reset"].active = false;
				Events["Prepare"].active = false;
			}
		}

		public void FixedUpdate()
		{
			// basic sanity checks
			if (Lib.IsEditor()) return;
			if (!Cache.VesselInfo(vessel).is_valid) return;
			if (next_check > Planetarium.GetUniversalTime()) return;

			// get ec handler
			Resource_info ec = ResourceCache.Info(vessel, "ElectricCharge");
			shrouded = part.ShieldedFromAirstream;
			issue = TestForIssues(vessel, ec, this, privateHdId, broken,
				remainingSampleMass, didPrepare, shrouded, last_subject_id);

			if (string.IsNullOrEmpty(issue))
				issue = TestForResources(vessel, resourceDefs, Kerbalism.elapsed_s, ResourceCache.Get(vessel));

			scienceValue = Science.Value(last_subject_id, 0, true);
			state = GetState(vessel, scienceValue, issue, recording, forcedRun);

			if (!string.IsNullOrEmpty(issue))
			{
				next_check = Planetarium.GetUniversalTime() + Math.Max(3, Kerbalism.elapsed_s * 3);
				return;
			}

			var subject_id = Science.Generate_subject_id(experiment_id, vessel);
			if (last_subject_id != subject_id)
			{
				currentDrive = GetDriveAndData(this, last_subject_id, vessel, privateHdId, out currentFile, out currentSample);
				lastDataSampled = GetDataSampled();
				//dataSampled = GetDataSampledInDrive(this, subject_id, vessel, privateHdId);
				forcedRun = false;
			}
			last_subject_id = subject_id;

			if (state != State.RUNNING)
				return;

			var exp = Science.Experiment(experiment_id);
			// TODO !!!!IMPORTANT TO FIX!!! This prevent running a second time in manual, and also breaks smart mode
			// Maybe we need to keep track of previous dataSampled to fix ?

			// we have a complete experiement on board
			if (exp.data_max - GetDataSampled() < double.Epsilon)
			{
				if (forcedRun)
				{
					if (lastDataSampled < GetDataSampled())
					{
						// it was just completed, stop it
						recording = false;
						lastDataSampled = GetDataSampled();
						return;
					}
					else
					{
						// get a drive and let a duplicate file/sample be created
						currentDrive = GetDrive(this, last_subject_id, vessel, privateHdId);
						currentFile = null;
						currentSample = null;
					}
				}
			}
			else
			{
				currentDrive = GetDriveAndData(this, last_subject_id, vessel, privateHdId, out currentFile, out currentSample);
			}

			lastDataSampled = GetDataSampled();

			// if experiment is active and there are no issues
			DoRecord(ec, subject_id);
		}

		public double GetDataSampled()
		{
			if (currentFile != null) return currentFile.size;
			if (currentSample != null) return currentSample.size;
			return 0;
		}

		private void DoRecord(Resource_info ec, string subject_id)
		{
			var stored = DoRecord(this, subject_id, currentDrive, GetDataSampled(),
				vessel, ec, ResourceCache.Get(vessel), resourceDefs,
				remainingSampleMass, out remainingSampleMass);

			//var stored = DoRecord(this, subject_id, vessel, ec, privateHdId,
			//	ResourceCache.Get(vessel), resourceDefs,
			//	remainingSampleMass, dataSampled, out dataSampled, out remainingSampleMass);
			if (!stored) issue = insufficient_storage;
		}

		private static Drive GetDrive(Experiment experiment, string subject_id, Vessel vessel, uint privateHdId, out File file, out Sample sample)
		{
			



			if (privateHdId != 0) drive = DB.Drive(privateHdId);
			drive.
			else drive = isFile ? Drive.GetBestFileDrive(vessel, chunkSize, subject_id) : Drive.SampleDrive(vessel, chunkSize, subject_id);
			return drive;
		}

		//private static double GetDataSampledInDrive(Experiment experiment, string subject_id, Vessel vessel, uint hdId)
		//{
		//	var exp = Science.Experiment(subject_id);
		//	double chunkSize = Math.Min(experiment.data_rate * Kerbalism.elapsed_s, exp.max_amount);
		//	Drive drive = GetDrive(experiment, vessel, hdId, chunkSize, subject_id);
		//	return drive.GetExperimentSize(subject_id);
		//}

		/// <summary>
		/// return the drive where a subject_id file/sample exists already, or the drive with the most available space.
		/// dataSampled returns the sum of all data stored on all drives of the vessel (for this subject_id)
		/// </summary>
		private static Drive GetDriveAndData(Experiment experiment, string subject_id, Vessel vessel, out double dataSampled, uint privateHdId = 0)
		{
			Drive drive = null;
			bool isFile = experiment.sample_mass < float.Epsilon;
			drive = isFile ? Drive.GetDriveForFile(vessel, subject_id, out dataSampled) : Drive.SampleDrive(vessel, 0, subject_id);
			// force the private drive to be used
			if (privateHdId != 0) drive = DB.Drive(privateHdId);
			return drive;
		}

		/// <summary>
		/// Get the drive to be used. dataSampled returns the "File" or "Sample" data value if the data exists already, 0 otherwise.
		/// </summary>
		private static Drive GetDriveAndData(Experiment experiment, string subject_id, Vessel vessel, uint hdId, out double dataSampled)
		{
			Sample sample;
			File file;
			Drive drive = GetDriveAndData(experiment, subject_id, vessel, hdId, out file, out sample);
			if (file != null) dataSampled = file.size;
			else if (sample != null) dataSampled = sample.size;
			else dataSampled = 0;
			return drive;
		}

		private static bool DoRecord(Experiment experiment, string subject_id, Drive drive, double dataSampled,
			Vessel vessel, Resource_info ec, Vessel_resources resources, List<KeyValuePair<string, double>> resourceDefs,
			double remainingSampleMass, out double remainingSampleMassOut)
		{
			var exp = Science.Experiment(subject_id);

			if (Done(exp, dataSampled))
			{
				//sampledOut = dataSampled;
				remainingSampleMassOut = remainingSampleMass;
				return true;
			}

			double elapsed = Kerbalism.elapsed_s;
			double chunkSize = Math.Min(experiment.data_rate * elapsed, exp.data_max);
			double massDelta = experiment.sample_mass * chunkSize / exp.data_max;

			//Drive drive = GetDrive(experiment, vessel, hdId, chunkSize, subject_id);

			// restore the file if it already exists
			//if (dataSampled < drive.GetExperimentSize(subject_id))
			//	dataSampled = drive.GetExperimentSize(subject_id);

			// on high time warp this chunk size could be too big for available drive space, but we could store a sizable amount if we process less
			bool isFile = experiment.sample_mass < float.Epsilon;
			double maxCapacity = isFile ? drive.FileCapacityAvailable() : drive.SampleCapacityAvailable(subject_id);
			if (maxCapacity < chunkSize)
			{
				double factor = maxCapacity / chunkSize;
				chunkSize *= factor;
				massDelta *= factor;
				elapsed *= factor;
			}

			// clamp last chunk to reach experiment max amount
			double nextDataSampled = dataSampled + chunkSize;
			if (nextDataSampled > exp.data_max)
			{
				double factor = (exp.data_max - dataSampled) / chunkSize;
				chunkSize *= factor;
				massDelta *= factor;
				elapsed *= factor;
				nextDataSampled = exp.data_max;
			}

			// TODO : if last chunk, amount should be scaled down
			// TODO : this doesn't check if resource is available, see greenhouse for proper way to consume resource from a module
			foreach (var p in resourceDefs)
				resources.Consume(vessel, p.Key, p.Value * elapsed, "experiment");

			bool stored = false;
			if (isFile)
				stored = drive.Record_file(subject_id, chunkSize, true);
			else
				stored = drive.Record_sample(subject_id, chunkSize, massDelta);

			if (stored)
			{
				// TODO : if last chunk, ec_rate should be scaled down
				// TODO : this doesn't check if ec is available, see greenhouse for proper way to consume ec from a module
				// consume ec
				ec.Consume(experiment.ec_rate * elapsed, "experiment");
				//sampledOut = nextDataSampled;
				if (!experiment.sample_collecting)
				{
					remainingSampleMass -= massDelta;
					remainingSampleMass = Math.Max(remainingSampleMass, 0);
				}
				remainingSampleMassOut = remainingSampleMass;
				return true;
			}

			//sampledOut = dataSampled;
			remainingSampleMassOut = remainingSampleMass;
			return false;
		}

		public static void BackgroundUpdate(Vessel v, ProtoPartModuleSnapshot m, Experiment experiment, Resource_info ec, Vessel_resources resources, double elapsed_s)
		{
			bool didPrepare = Lib.Proto.GetBool(m, "didPrepare", false);
			bool shrouded = Lib.Proto.GetBool(m, "shrouded", false);
			string last_subject_id = Lib.Proto.GetString(m, "last_subject_id", "");
			double remainingSampleMass = Lib.Proto.GetDouble(m, "remainingSampleMass", 0);
			bool broken = Lib.Proto.GetBool(m, "broken", false);
			bool forcedRun = Lib.Proto.GetBool(m, "forcedRun", false);
			bool recording = Lib.Proto.GetBool(m, "recording", false);
			uint privateHdId = Lib.Proto.GetUInt(m, "privateHdId", 0);

			string issue = TestForIssues(v, ec, experiment, privateHdId, broken,
				remainingSampleMass, didPrepare, shrouded, last_subject_id);
			if(string.IsNullOrEmpty(issue))
				issue = TestForResources(v, ParseResources(experiment.resources), elapsed_s, resources);

			Lib.Proto.Set(m, "issue", issue);

			if (!string.IsNullOrEmpty(issue))
				return;

			var subject_id = Science.Generate_subject_id(experiment.experiment_id, v);
			Lib.Proto.Set(m, "last_subject_id", subject_id);


			//double dataSampled = Lib.Proto.GetDouble(m, "dataSampled");

			if (last_subject_id != subject_id)
			{
				//dataSampled = GetDataSampledInDrive(experiment, subject_id, v, privateHdId);
				Lib.Proto.Set(m, "forcedRun", false);
			}

			double scienceValue = Science.Value(last_subject_id, 0, true);
			Lib.Proto.Set(m, "scienceValue", scienceValue);

			var state = GetState(v, scienceValue, issue, recording, forcedRun);
			if (state != State.RUNNING)
				return;

			double dataSampled;
			Drive drive = GetDriveAndData(experiment, subject_id, v, privateHdId, out dataSampled);

			if (dataSampled >= Science.Experiment(subject_id).data_max)
			{
				if (forcedRun) Lib.Proto.Set(m, "recording", false); ;
				return;
			}

			var stored = DoRecord(experiment, subject_id, drive, dataSampled,
				v, ec, resources, ParseResources(experiment.resources),
				remainingSampleMass, out remainingSampleMass);

			//var stored = DoRecord(experiment, subject_id, v, ec, privateHdId,
			//	resources, ParseResources(experiment.resources),
			//	remainingSampleMass, dataSampled, out dataSampled, out remainingSampleMass);
			if (!stored) Lib.Proto.Set(m, "issue", insufficient_storage);

			//Lib.Proto.Set(m, "dataSampled", dataSampled);
			Lib.Proto.Set(m, "remainingSampleMass", remainingSampleMass);
		}

		internal static double RestoreSampleMass(double restoredAmount, ProtoPartModuleSnapshot m, string id)
		{
			var broken = Lib.Proto.GetBool(m, "broken", false);
			if (broken) return 0;

			var experiment_id = Lib.Proto.GetString(m, "experiment_id", string.Empty);
			if (experiment_id != id) return 0;

			var sample_collecting = Lib.Proto.GetBool(m, "sample_collecting", false);
			if (sample_collecting) return 0;

			double remainingSampleMass = Lib.Proto.GetDouble(m, "remainingSampleMass", 0);
			double sample_reservoir = Lib.Proto.GetDouble(m, "sample_reservoir", 0);
			if (remainingSampleMass >= sample_reservoir) return 0;

			double delta = Math.Max(restoredAmount, sample_reservoir - remainingSampleMass);
			remainingSampleMass += delta;
			remainingSampleMass = Math.Min(sample_reservoir, remainingSampleMass);
			Lib.Proto.Set(m, "remainingSampleMass", remainingSampleMass);
			return delta;
		}

		internal double RestoreSampleMass(double restoredAmount, string id)
		{
			if (broken) return 0;
			if (sample_collecting || experiment_id != id) return 0;
			if (remainingSampleMass >= sample_reservoir) return 0;
			double delta = Math.Max(restoredAmount, sample_reservoir - remainingSampleMass);
			remainingSampleMass += delta;
			remainingSampleMass = Math.Min(sample_reservoir, remainingSampleMass);
			return delta;
		}

		public void ReliablityEvent(bool breakdown)
		{
			broken = breakdown;
		}

		private static string TestForResources(Vessel v, List<KeyValuePair<string, double>> defs, double elapsed_s, Vessel_resources res)
		{
			if (defs.Count < 1) return string.Empty;

			// test if there are enough resources on the vessel
			foreach(var p in defs)
			{
				var ri = res.Info(v, p.Key);
				if (ri.amount < p.Value * elapsed_s)
					return "missing " + ri.resource_name;
			}

			return string.Empty;
		}

		private static List<KeyValuePair<string, double>> ParseResources(string resources, bool logErros = false)
		{
			var reslib = PartResourceLibrary.Instance.resourceDefinitions;

			List<KeyValuePair<string, double>> defs = new List<KeyValuePair<string, double>>();
			foreach (string s in Lib.Tokenize(resources, ','))
			{
				// definitions are Resource@rate
				var p = Lib.Tokenize(s, '@');
				if (p.Count != 2) continue;				// malformed definition
				string res = p[0];
				if (!reslib.Contains(res)) continue;	// unknown resource
				double rate = double.Parse(p[1]);
				if (res.Length < 1 || rate < double.Epsilon) continue;	// rate <= 0
				defs.Add(new KeyValuePair<string, double>(res, rate));
			}
			return defs;
		}

		private static string TestForIssues(Vessel v, Resource_info ec, Experiment experiment, uint hdId, bool broken,
			double remainingSampleMass, bool didPrepare, bool isShrouded, string last_subject_id)
		{
			var subject_id = Science.Generate_subject_id(experiment.experiment_id, v);

			if (broken)
				return "broken";

			if (isShrouded && !experiment.allow_shrouded)
				return "shrouded";
			
			bool needsReset = experiment.crew_reset.Length > 0
				&& !string.IsNullOrEmpty(last_subject_id) && subject_id != last_subject_id;
			if (needsReset) return "reset required";

			if (ec.amount < double.Epsilon && experiment.ec_rate > double.Epsilon)
				return "no Electricity";
			
			if (!string.IsNullOrEmpty(experiment.crew_operate))
			{
				var cs = new CrewSpecs(experiment.crew_operate);
				if (!cs && Lib.CrewCount(v) > 0) return "crew on board";
				else if (cs && !cs.Check(v)) return cs.Warning();
			}

			if (!experiment.sample_collecting && remainingSampleMass < double.Epsilon && experiment.sample_mass > double.Epsilon)
				return "depleted";

			if (!didPrepare && !string.IsNullOrEmpty(experiment.crew_prepare))
				return "not prepared";

			string situationIssue = Science.TestRequirements(experiment.experiment_id, experiment.requires, v);
			if (situationIssue.Length > 0)
				return Science.RequirementText(situationIssue);

			// TODO : only check if there is some space, no need to check for the chunk size ? is that also true sor samples ?
			var experimentSize = Science.Experiment(subject_id).data_max;
			double chunkSize = Math.Min(experiment.data_rate * Kerbalism.elapsed_s, experimentSize);
			Drive drive = GetDrive(experiment, subject_id, v, hdId, chunkSize);

			var isFile = experiment.sample_mass < double.Epsilon;
			double available = isFile ? drive.FileCapacityAvailable() : drive.SampleCapacityAvailable(subject_id);

			if (Math.Min(experiment.data_rate * Kerbalism.elapsed_s, experimentSize) > available)
				return insufficient_storage;

			return string.Empty;
		}

		[KSPEvent(guiActiveUnfocused = true, guiName = "_", active = false)]
		public void Prepare()
		{
			// disable for dead eva kerbals
			Vessel v = FlightGlobals.ActiveVessel;
			if (v == null || EVA.IsDead(v))
				return;

			if (prepare_cs == null)
				return;

			// check trait
			if (!prepare_cs.Check(v))
			{
				Message.Post(
				  Lib.TextVariant
				  (
					"I'm not qualified for this",
					"I will not even know where to start",
					"I'm afraid I can't do that"
				  ),
				  reset_cs.Warning()
				);
			}

			didPrepare = true;

			Message.Post(
			  "Preparation Complete",
			  Lib.TextVariant
			  (
				"Ready to go",
				"Let's start doing some science!"
			  )
			);
		}

		[KSPEvent(guiActiveUnfocused = true, guiName = "_", active = false)]
		public void Reset()
		{
			Reset(true);
		}

		public bool Reset(bool showMessage)
		{
			// disable for dead eva kerbals
			Vessel v = FlightGlobals.ActiveVessel;
			if (v == null || EVA.IsDead(v))
				return false;

			if (reset_cs == null)
				return false;

			// check trait
			if (!reset_cs.Check(v))
			{
				if(showMessage)
				{
					Message.Post(
					  Lib.TextVariant
					  (
						"I'm not qualified for this",
						"I will not even know where to start",
						"I'm afraid I can't do that"
					  ),
					  reset_cs.Warning()
					);
				}
				return false;
			}

			last_subject_id = string.Empty;
			didPrepare = false;

			if(showMessage)
			{
				Message.Post(
				  "Reset Done",
				  Lib.TextVariant
				  (
					"It's good to go again",
					"Ready for the next bit of science"
				  )
				);
			}
			return true; 
		}

		private bool IsExperimentRunningOnVessel()
		{
			foreach(var e in vessel.FindPartModulesImplementing<Experiment>())
			{
				if (e.enabled && e.experiment_id == experiment_id && e.recording) return true;
			}
			return false;
		}

		public static void PostMultipleRunsMessage(string title)
		{
			Message.Post(Lib.Color("red", "ALREADY RUNNING", true), "Can't start " + title + " a second time on the same vessel");
		}

		[KSPEvent(guiActiveUnfocused = true, guiActive = true, guiActiveEditor = true, guiName = "_", active = true)]
		public void Toggle()
		{
			if(Lib.IsEditor())
			{
				if(!recording)
				{
					recording = EditorTracker.Instance.AllowStart(this);
					if (!recording) PostMultipleRunsMessage(Science.Experiment(experiment_id).name);
				}
				else
					recording = !recording;
				
				deployAnimator.Play(!recording, false);
				GameEvents.onEditorShipModified.Fire(EditorLogic.fetch.ship);
				return;
			}

			//dataSampled = GetDataSampledInDrive(this, Science.Generate_subject_id(experiment_id, vessel), vessel, privateHdId);

			if (Lib.IsFlight() && !vessel.IsControllable)
				return;

			if (deployAnimator.Playing())
				return; // nervous clicker? wait for it, goddamnit.

			var previous_recording = recording;

			if (!recording)
			{
				if (IsExperimentRunningOnVessel())
				{
					// The same experiment must run only once on a vessel
					// TODO : prevent experiment from running twice after docking
					PostMultipleRunsMessage(Science.Experiment(experiment_id).name);
				}
				else
				{
					forcedRun = !DB.Vessel(vessel).cfg_smartscience;
					recording = true;
				}
			}
			else
			{
				recording = false;
				forcedRun = false;
			}

			var new_recording = recording;
			recording = previous_recording;

			if(previous_recording != new_recording)
			{
				
				if(!new_recording)
				{
					// stop experiment

					// plays the deploy animation in reverse
					Action stop = delegate () { recording = false; deployAnimator.Play(true, false); };

					// wait for loop animation to stop before deploy animation
					if (loopAnimator.Playing())
						loopAnimator.Stop(stop);
					else
						stop.Invoke();
				}
				else
				{
					// start experiment

					// play the deploy animation, when it's done start the loop animation
					deployAnimator.Play(false, false, delegate () { recording = true; loopAnimator.Play(false, true); });
				}
			}
		}

		// action groups
		[KSPAction("Start")] public void Start(KSPActionParam param)
		{
			switch (GetState(vessel, scienceValue, issue, recording, forcedRun)) {
				case State.STOPPED:
				case State.WAITING:
					Toggle();
					break;
			}
		}
		[KSPAction("Stop")] public void Stop(KSPActionParam param) {
			if(recording) Toggle();
		}

		// part tooltip
		public override string GetInfo()
		{
			return Specs().Info();
		}

		// specifics support
		public Specifics Specs()
		{
			var specs = new Specifics();
			var exp = Science.Experiment(experiment_id);
			if (exp == null)
			{
				specs.Add(Localizer.Format("#KERBALISM_ExperimentInfo_Unknown"));
				return specs;
			}

			specs.Add(Lib.BuildString("<b>", exp.name, "</b>"));
			if(!string.IsNullOrEmpty(experiment_desc))
			{
				specs.Add(Lib.BuildString("<i>", experiment_desc, "</i>"));
			}
			
			specs.Add(string.Empty);
			double expSize = exp.data_max;
			if (sample_mass < float.Epsilon)
			{
				specs.Add("Data", Lib.HumanReadableDataSize(expSize));
				specs.Add("Data rate", Lib.HumanReadableDataRate(data_rate));
				specs.Add("Duration", Lib.HumanReadableDuration(expSize / data_rate));
			}
			else
			{
				specs.Add("Sample size", Lib.HumanReadableSampleSize(expSize));
				specs.Add("Sample mass", Lib.HumanReadableMass(sample_mass));
				if(!sample_collecting && Math.Abs(sample_reservoir - sample_mass) > double.Epsilon && sample_mass > double.Epsilon)
					specs.Add("Experiments", "" + Math.Round(sample_reservoir / sample_mass, 0));
				specs.Add("Duration", Lib.HumanReadableDuration(expSize / data_rate));
			}

			List<string> situations = exp.Situations();
			if (situations.Count > 0)
			{
				specs.Add(string.Empty);
				specs.Add("<color=#00ffff>Situations:</color>", string.Empty);
				foreach (string s in situations) specs.Add(Lib.BuildString("• <b>", s, "</b>"));
			}

			specs.Add(string.Empty);
			specs.Add("<color=#00ffff>Needs:</color>");

			specs.Add("EC", Lib.HumanReadableRate(ec_rate));
			foreach(var p in ParseResources(resources))
				specs.Add(p.Key, Lib.HumanReadableRate(p.Value));

			if (crew_prepare.Length > 0)
			{
				var cs = new CrewSpecs(crew_prepare);
				specs.Add("Preparation", cs ? cs.Info() : "none");
			}
			if (crew_operate.Length > 0)
			{
				var cs = new CrewSpecs(crew_operate);
				specs.Add("Operation", cs ? cs.Info() : "unmanned");
			}
			if (crew_reset.Length > 0)
			{
				var cs = new CrewSpecs(crew_reset);
				specs.Add("Reset", cs ? cs.Info() : "none");
			}

			if(!string.IsNullOrEmpty(requires))
			{
				specs.Add(string.Empty);
				specs.Add("<color=#00ffff>Requires:</color>", string.Empty);
				var tokens = Lib.Tokenize(requires, ',');
				foreach (string s in tokens) specs.Add(Lib.BuildString("• <b>", Science.RequirementText(s), "</b>"));
			}

			return specs;
		}

		// module mass support
		public float GetModuleMass(float defaultMass, ModifierStagingSituation sit) { return (float)remainingSampleMass; }
		public ModifierChangeWhen GetModuleMassChangeWhen() { return ModifierChangeWhen.CONSTANTLY; }
	}

	internal class EditorTracker
	{
		private static EditorTracker instance;
		private readonly List<Experiment> experiments = new List<Experiment>();

		static EditorTracker()
		{
			if (instance == null)
				instance = new EditorTracker();
		}

		private EditorTracker()
		{
			if(instance == null) {
				instance = this;
				GameEvents.onEditorShipModified.Add(instance.ShipModified);
			}
		}

		internal void ShipModified(ShipConstruct construct)
		{
			experiments.Clear();
			foreach(var part in construct.Parts)
			{
				foreach (var experiment in part.FindModulesImplementing<Experiment>())
				{
					if (!experiment.enabled) experiment.recording = false;
					if (experiment.recording && !AllowStart(experiment))
					{
						// An experiment was added in recording state? Cheeky bugger!
						experiment.recording = false;
						experiment.deployAnimator.Still(0);
					}
					experiments.Add(experiment);
				}
			}
		}

		internal bool AllowStart(Experiment experiment)
		{
			foreach (var e in experiments)
				if (e.recording && e.experiment_id == experiment.experiment_id)
					return false;
			return true;
		}

		internal static EditorTracker Instance
		{
			get
			{
				if (instance == null)
					instance = new EditorTracker();
				return instance;
			}
		}
	}
} // KERBALISM
