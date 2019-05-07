using KSP.Localization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace KERBALISM
{
	public sealed class Experiment2 : PartModule, ISpecifics, IPartMassModifier
	{
		// config

		// id of the stock "EXPERIMENT_DEFINITION" or "variant_id" set in the "EXPERIMENT_INFO" node
		[KSPField] public string exp_info_id;                 

		// amount of "blank" samples, if set to 0 the experiment will generate a file
		// sample mass added to the module will be sample_amount * experiment_info.sample_mass
		[KSPField] public double sample_amount = 0;

		// don't show UI when the experiment is unavailable
		[KSPField] public bool hide_when_unavailable = false;	

		// animations
		[KSPField] public string anim_deploy = string.Empty; // deploy animation
		[KSPField] public bool anim_deploy_reverse = false;

		[KSPField] public string anim_loop = string.Empty; // running animation
		[KSPField] public bool anim_loop_reverse = false;

		// for prefab/editor only. Never use in flight, get them from the ExperimentProcess object
		[KSPField(isPersistant = true)] private bool editorRecording = false;
		[KSPField(isPersistant = true)] private bool editorForcedRun = false;
		[KSPField(isPersistant = true)] private bool flightProcessCreated = false;

		// animations
		internal Animator deployAnimator;
		internal Animator loopAnimator;

		// object actually holding the usefull data
		// should never be accessed trough the PM, but from DB.vessels ?
		private ExperimentProcess process;

		private State state = State.STOPPED;
		private CrewSpecs operator_cs;
		private CrewSpecs reset_cs;
		private CrewSpecs prepare_cs;

		public enum State
		{
			STOPPED = 0, WAITING, RUNNING, ISSUE
		}

		public override void OnLoad(ConfigNode node)
		{
			// Get a temporary process for the prefab and in the editor
			if (Lib.IsEditor() || HighLogic.LoadedScene == GameScenes.LOADING)
			{
				process = new ExperimentProcess(part, exp_info_id, sample_amount, editorRecording, editorForcedRun);

				// TODO : is that still necessary ? If the prefab is correctly initialized with it's own ExperimentProcess it shouldn't...
				if (IsSample()) GameEvents.onEditorShipModified.Fire(EditorLogic.fetch.ship);
			}

			// in flight get/add the process from the DB
			if (Lib.IsFlight())
			{
				if (flightProcessCreated)
				{
					process = DB.Vessel(vessel).GetExperimentProcess(part, exp_info_id);
				}
				else
				{
					process = DB.Vessel(vessel).AddExperimentProcess(part, exp_info_id, sample_amount, editorRecording, editorForcedRun);
					if (process != null) flightProcessCreated = true;
				}
			}

			// sanity checks :
			if (process == null || process.exp_info == null)
			{
				Lib.Log("ERROR : failed loading process for experiment module '" + exp_info_id + "' on part '" + part.name);
				process = null;
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
			deployAnimator.Still(editorRecording ? 1.0 : 0.0);
			loopAnimator.Still(editorRecording ? 1.0 : 0.0);
			if (editorRecording) loopAnimator.Play(false, true);

			// PAW ui init
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

		public void Update()
		{
			// in flight
			if (Lib.IsFlight())
			{
				// TODO : What is this ???
				Vessel v = FlightGlobals.ActiveVessel;
				if (v == null || EVA.IsDead(v)) return;

				// get info from cache
				Vessel_info vi = Cache.VesselInfo(vessel);

				// do nothing if vessel is invalid
				if (!vi.is_valid) return;

				//var sampleSize = exp.data_max;
				//var dataSampled = GetDataSampled();
				//var eta = exp_info.data_rate < double.Epsilon || Done(exp, dataSampled) ? " (done)" : " " + Lib.HumanReadableCountdown((exp_info.data_max - dataSampled) / exp_info.data_rate); //TODO  account for remaining science value, not only size 

				// update ui
				var title = Lib.Ellipsis(exp_info.title, Styles.ScaleStringLength(24));

				//var valueTotal = Science.TotalValue(last_subject_id);
				//var valueDone = valueTotal - scienceValue;
				//bool done = scienceValue < double.Epsilon;

				// TODO : UI : one toggle that show/hide all other PAW elements
				// - label : current situation
				// - label : status + science left + ETA -> for science left, do "valueDoneOnVessel (ValueDoneOnAllVessels) / totalValue"
				// - if issue, label for issue
				// - button : start/stop
				// - toggle : smart/manual
				// + other buttons (reset, prepare, etc...)

				//string statusString = Lib.Color(done ? "green" : "#00ffffff", Lib.BuildString("•", valueDone.ToString("F1"), "/", valueTotal.ToString("F1"), " "), true);

				//switch (state)
				//{
				//	case State.ISSUE: statusString += Lib.Color("yellow", issue); break;
				//	case State.RUNNING:
				//		if (done) statusString = Lib.BuildString(Lib.Color("red", "re-running "), Lib.Color("green", eta));
				//		else statusString += Lib.Color("green", eta);
				//		break;
				//	case State.WAITING: statusString += "waiting..."; break;
				//	case State.STOPPED: statusString += "stopped"; break;
				//}

				// temp :
				string statusString = string.Empty ;
				switch (state)
				{
					case State.ISSUE: statusString += Lib.Color("yellow", "issue"); break;
					case State.RUNNING:
						statusString += Lib.Color("green", "running");
						//if (done) statusString = Lib.BuildString(Lib.Color("red", "re-running "), Lib.Color("green", eta));
						//else statusString += Lib.Color("green", eta);
						break;
					case State.WAITING: statusString += "waiting..."; break;
					case State.STOPPED: statusString += "stopped"; break;
				}

				Events["Toggle"].guiName = Lib.StatusToggle(title, statusString);
				//Events["Toggle"].active = (prepare_cs == null || didPrepare);

				// TODO : Experiment UI
				//Events["Prepare"].guiName = Lib.BuildString("Prepare <b>", exp.name, "</b>");
				//Events["Prepare"].active = !didPrepare && prepare_cs != null && string.IsNullOrEmpty(last_subject_id);

				//Events["Reset"].guiName = Lib.BuildString("Reset <b>", exp.name, "</b>");
				//// we need a reset either if we have recorded data or did a setup
				//bool resetActive = (reset_cs != null || prepare_cs != null) && !string.IsNullOrEmpty(last_subject_id);
				//Events["Reset"].active = resetActive;

				//if (issue.Length > 0 && hide_when_unavailable && issue != insufficient_storage)
				//{
				//	Events["Toggle"].active = false;
				//}
			}
			// in the editor
			else if (Lib.IsEditor())
			{
				// update ui
				Events["Toggle"].guiName = Lib.StatusToggle(exp_info.title, recording ? "recording" : "stopped");
				Events["Reset"].active = false;
				Events["Prepare"].active = false;
			}
		}

		public void FixedUpdate()
		{
			// basic sanity checks
			if (Lib.IsEditor()) return;
			if (!Cache.VesselInfo(vessel).is_valid) return;

			process.shrouded = part.ShieldedFromAirstream;








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

		public bool IsSample()
		{
			return sample_amount != 0;
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
			//var exp = Science.Experiment(experiment_id);
			if (exp_info == null)
			{
				specs.Add(Localizer.Format("#KERBALISM_ExperimentInfo_Unknown"));
				return specs;
			}

			specs.Add(Lib.BuildString("<b>", exp_info.title, "</b>"));
			if (!string.IsNullOrEmpty(exp_info.experiment_desc))
			{
				specs.Add(Lib.BuildString("<i>", exp_info.experiment_desc, "</i>"));
			}

			specs.Add(string.Empty);
			//double expSize = exp_info.data_max;
			if (IsSample())
			{
				specs.Add("Data", Lib.HumanReadableDataSize(exp_info.data_max));
				specs.Add("Data rate", Lib.HumanReadableDataRate(exp_info.data_rate));
				specs.Add("Duration", Lib.HumanReadableDuration(exp_info.data_max / exp_info.data_rate));
			}
			else
			{
				specs.Add("Sample size", Lib.HumanReadableSampleSize(exp_info.data_max));
				specs.Add("Sample mass", Lib.HumanReadableMass(exp_info.sample_mass));
				if (!exp_info.sample_collecting && exp_info.sample_mass > 0)
					specs.Add("Experiments", sample_amount.ToString("F0"));
				specs.Add("Duration", Lib.HumanReadableDuration(exp_info.data_max / exp_info.data_rate));
			}

			List<string> situations = exp_info.Situations();
			if (situations.Count > 0)
			{
				specs.Add(string.Empty);
				specs.Add("<color=#00ffff>Situations:</color>", string.Empty);
				foreach (string s in situations) specs.Add(Lib.BuildString("• <b>", s, "</b>"));
			}

			specs.Add(string.Empty);
			specs.Add("<color=#00ffff>Needs:</color>");

			specs.Add("EC", Lib.HumanReadableRate(exp_info.ec_rate));
			foreach (var p in exp_info.ParseResources())
				specs.Add(p.Key, Lib.HumanReadableRate(p.Value));

			if (exp_info.crew_prepare.Length > 0)
			{
				var cs = new CrewSpecs(exp_info.crew_prepare);
				specs.Add("Preparation", cs ? cs.Info() : "none");
			}
			if (exp_info.crew_operate.Length > 0)
			{
				var cs = new CrewSpecs(exp_info.crew_operate);
				specs.Add("Operation", cs ? cs.Info() : "unmanned");
			}
			if (exp_info.crew_reset.Length > 0)
			{
				var cs = new CrewSpecs(exp_info.crew_reset);
				specs.Add("Reset", cs ? cs.Info() : "none");
			}

			if (!string.IsNullOrEmpty(exp_info.requires))
			{
				specs.Add(string.Empty);
				specs.Add("<color=#00ffff>Requires:</color>", string.Empty);
				var tokens = Lib.Tokenize(exp_info.requires, ',');
				foreach (string s in tokens) specs.Add(Lib.BuildString("• <b>", Science.RequirementText(s), "</b>"));
			}

			return specs;
		}

		// module mass support
		public float GetModuleMass(float defaultMass, ModifierStagingSituation sit)
		{
			return process != null ? (float)process.remainingSampleMass : 0f;
		}
		public ModifierChangeWhen GetModuleMassChangeWhen() { return ModifierChangeWhen.CONSTANTLY; }
	}
}
