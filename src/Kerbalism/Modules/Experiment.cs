using KSP.Localization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace KERBALISM
{
	public sealed class Experiment : PartModule, ISpecifics, IPartMassModifier
	{
		// config

		// id of the "EXPERIMENT_VARIANT"
		[KSPField] public string variantId;

		// don't show UI when the experiment is unavailable
		[KSPField] public bool hide_when_unavailable = false;

		// animations
		[KSPField] public string anim_deploy = string.Empty; // deploy animation
		[KSPField] public bool anim_deploy_reverse = false;

		[KSPField] public string anim_loop = string.Empty; // running animation
		[KSPField] public bool anim_loop_reverse = false;

		// PAW UI
		[KSPField(guiActive = true, guiActiveEditor = false, guiName = "_")]
		[UI_Toggle(enabledText = "", disabledText = "", scene = UI_Scene.Flight)]
		private bool extendedPAW = false;

		[KSPField(guiActive = true, guiActiveEditor = true, guiName = "_", isPersistant = true)]
		[UI_Toggle(enabledText = "running", disabledText = "stopped", scene = UI_Scene.Flight)]
		public bool runningToggle = false;

		[KSPField(guiName = "-", guiActiveEditor = false, guiActive = true)]
		public string stateLabel = string.Empty;

		[KSPField(guiActive = true, guiActiveEditor = false, guiName = "Experiment info")]
		[UI_Cycle(scene = UI_Scene.Flight, stateNames = new[] { "mode", "mode" })]
		public int forcedButton = 0;

		[KSPField(guiName = "Issue", guiActiveEditor = false, guiActive = false)]
		public string issueLabel0 = string.Empty;

		[KSPField(guiName = "Issue", guiActiveEditor = false, guiActive = false)]
		public string issueLabel1 = string.Empty;

		[KSPField(guiName = "Issue", guiActiveEditor = false, guiActive = false)]
		public string issueLabel2 = string.Empty;

		// animations
		internal Animator deployAnimator;
		internal Animator loopAnimator;

		// object actually holding the usefull data
		// should never be accessed trough the PM, but from DB.vessels ?
		// TODO : set to null OnDestroy() and check (how?) that there is no memory leak when unloading the vessel
		private ExperimentProcess process;

		//private State state = State.STOPPED;
		private CrewSpecs operator_cs;
		private CrewSpecs reset_cs;
		private CrewSpecs prepare_cs;

		public override void OnStart(StartState state)
		{

			process = DataProcess.GetProcessOnPartModuleLoad<ExperimentProcess>(this, variantId);

			// sanity checks :
			if (process != null && (process.expVar == null || process.expVar.expInfo == null))
			{
				Lib.Log("ERROR : failed loading process for experiment module '" + variantId + "' on part '" + part.name);
				DB.RemoveDataProcess(part.flightID, process);
				// maybe we should also clear the VesselObjectCache
				process = null;
			}

			if (process == null) return;

			// don't break tutorial scenarios
			if (Lib.DisableScenario(this)) return;

			// create animators
			deployAnimator = new Animator(part, anim_deploy);
			deployAnimator.reversed = anim_deploy_reverse;

			loopAnimator = new Animator(part, anim_loop);
			loopAnimator.reversed = anim_loop_reverse;

			// set initial animation states
			deployAnimator.Still(runningToggle ? 1.0 : 0.0);
			loopAnimator.Still(runningToggle ? 1.0 : 0.0);
			if (runningToggle) loopAnimator.Play(false, true);

			// PAW ui init
			//Events["Toggle"].guiActiveUncommand = true;
			//Events["Toggle"].externalToEVAOnly = true;
			//Events["Toggle"].requireFullControl = false;

			//Events["Prepare"].guiActiveUncommand = true;
			//Events["Prepare"].externalToEVAOnly = true;
			//Events["Prepare"].requireFullControl = false;

			//Events["Reset"].guiActiveUncommand = true;
			//Events["Reset"].externalToEVAOnly = true;
			//Events["Reset"].requireFullControl = false;

			Fields[nameof(runningToggle)].uiControlFlight.onFieldChanged += RunningToggle;
			Fields[nameof(forcedButton)].uiControlFlight.onFieldChanged += ForcedRunToggle;


		}


		public void Update()
		{
			if (process == null) return;

			// in flight
			if (Lib.IsFlight())
			{
				// TODO : What is this for ???
				Vessel v = FlightGlobals.ActiveVessel;
				if (v == null || EVA.IsDead(v)) return;

				// get info from cache
				Vessel_info vi = Cache.VesselInfo(vessel);

				// do nothing if vessel is invalid
				if (!vi.is_valid) return;

				// update ui

				string statusString = string.Empty;
				switch (process.GetState())
				{
					case ExperimentProcess.State.STOPPED:
						statusString = "Stopped";
						break;
					case ExperimentProcess.State.ISSUE:
						statusString = "<color=red>Unavailable</color>";
						break;
					case ExperimentProcess.State.SMART_WAIT:
						statusString = "<color=yellow>Smart : waiting</color>";
						break;
					case ExperimentProcess.State.SMART_RUN:
						statusString = Lib.BuildString("<color=green>[S]</color> ", Lib.HumanReadableDuration(process.GetETA()));
						break;
					case ExperimentProcess.State.FORCED_RUN:
						statusString = Lib.BuildString("<color=green>[M]</color> ", Lib.HumanReadableDuration(process.GetETA()));
						break;
				}

				string expName = Lib.Ellipsis(process.expVar.expInfo.title, Styles.ScaleStringLength(24));

				Lib.SetUnsupportedFieldGuiName(Fields[nameof(extendedPAW)], expName);

				if (extendedPAW)
				{
					((UI_Toggle)Fields[nameof(extendedPAW)].uiControlFlight).enabledText = statusString;

					Fields[nameof(stateLabel)].guiActive = true;
					Fields[nameof(stateLabel)].guiName = Lib.HumanReadableDataUsage(process.GetDataDone(), process.expVar.expInfo.fullSize);
					stateLabel = process.Subject.ScienceValueInfo();

					Fields[nameof(runningToggle)].guiActive = true;
					runningToggle = process.running;
					Lib.SetUnsupportedFieldGuiName(Fields[nameof(runningToggle)], process.Subject.SubjectTitle(true));

					Fields[nameof(forcedButton)].guiActive = true;

					if (Fields[nameof(forcedButton)].uiControlFlight.partActionItem != null)
					{
						((UIPartActionCycle)Fields[nameof(forcedButton)].uiControlFlight.partActionItem).fieldStatus.SetText(process.forcedRun ? "manual mode" : "smart mode");
					}

					if (process.issues.Count > 0)
					{
						Fields[nameof(issueLabel0)].guiActive = true;
						Fields[nameof(issueLabel0)].guiName = process.issues[0];
					}
					else
					{
						Fields[nameof(issueLabel0)].guiActive = false;
					}

					if (process.issues.Count > 1)
					{
						Fields[nameof(issueLabel1)].guiActive = true;
						Fields[nameof(issueLabel1)].guiName = process.issues[1];
					}
					else
					{
						Fields[nameof(issueLabel1)].guiActive = false;
					}

					if (process.issues.Count > 2)
					{
						Fields[nameof(issueLabel2)].guiActive = true;
						Fields[nameof(issueLabel2)].guiName = process.issues[2];
					}
					else
					{
						Fields[nameof(issueLabel2)].guiActive = false;
					}

				}
				else
				{
					((UI_Toggle)Fields[nameof(extendedPAW)].uiControlFlight).disabledText = statusString;
					Fields[nameof(stateLabel)].guiActive = false;
					Fields[nameof(runningToggle)].guiActive = false;
					Fields[nameof(forcedButton)].guiActive = false;
					
				}



				// TODO : UI : one toggle that show/hide all other PAW elements, label = status + % done
				// - toggle : start/stop -> label = ETA
				// - toggle : smart/manual -> label = science left -> "valueDoneOnVessel (ValueDoneEverywhere) / totalValue"
				// - label : current situation (if invalid, do it in red + don't show biomes)
				// - if issue(s), label(s) for issue(s) (max 3 ?)
				// + other buttons (reset, prepare, etc...)
			}
			// in the editor
			else if (Lib.IsEditor())
			{
				// update ui
				//Events["Toggle"].guiName = Lib.StatusToggle(exp_variant.title, recording ? "recording" : "stopped");
				//Events["Reset"].active = false;
				//Events["Prepare"].active = false;
			}
		}

		public void FixedUpdate()
		{
			// basic sanity checks
			if (process == null) return;
			if (Lib.IsEditor()) return;
			if (!Cache.VesselInfo(vessel).is_valid) return;

			process.shrouded = part.ShieldedFromAirstream;
		}

		private void RunningToggle(BaseField bf, object o)
		{
			if (!Lib.IsEditor())
				process.running = !process.running;
		}

		public void ForcedRunToggle(BaseField bf, object o)
		{
			((UI_Cycle)Fields[nameof(forcedButton)].uiControlFlight).stateNames[forcedButton] = process.forcedRun ? "manual mode" : "smart mode"; ;
			Lib.Popup(process.expVar.expInfo.title,
				Lib.BuildString(Specs().Info(), "\nSelect experiment mode or exit :"),
				new DialogGUIButton("Smart mode", () => ToggleForcedRun(false)),
				new DialogGUIButton("Manual mode", () => ToggleForcedRun(true)),
				new DialogGUIButton("Exit", () => { }));
		}

		private void ToggleForcedRun(bool forcedRun)
		{
			if (!process.forcedRun) process.running = false;
			process.forcedRun = forcedRun;
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
			if (process == null)
			{
				specs.Add(Localizer.Format("#KERBALISM_ExperimentInfo_Unknown"));
				return specs;
			}

			specs.Add(Lib.BuildString("<b>", process.expVar.expInfo.title, "</b>"));
			if (!string.IsNullOrEmpty(process.expVar.experimentDesc))
			{
				specs.Add(Lib.BuildString("<i>", process.expVar.experimentDesc, "</i>"));
			}

			//specs.Add(string.Empty);

			if (process.type == FileType.File)
			{
				specs.Add("Data size", Lib.HumanReadableDataSize(process.expVar.expInfo.fullSize));
				specs.Add("Data rate", Lib.HumanReadableDataRate(process.expVar.dataRate));
			}
			else
			{
				specs.Add("Sample size", Lib.HumanReadableSampleSize(process.expVar.expInfo.fullSize));
				specs.Add("Sample mass", Lib.HumanReadableMass(process.expVar.expInfo.sampleMass));
				if (!process.expVar.sampleCollecting)
					specs.Add("Sample amount", process.expVar.sampleAmount.ToString());
			}
			specs.Add("Duration", Lib.HumanReadableDuration((double)process.expVar.expInfo.fullSize / process.expVar.dataRate));

			// To reduce partModule info clutter, don't add every single info to the prefab
			if (part == part.partInfo.partPrefab || Lib.IsEditor())
			{
				List<string> situations = process.expVar.expInfo.AvailableSituations();
				if (situations.Count > 0)
				{
					specs.Add(string.Empty);
					specs.Add("<color=#00ffff>Situations:</color>", string.Empty);
					foreach (string s in situations) specs.Add(Lib.BuildString("• <b>", s, "</b>"));
				}

				specs.Add(string.Empty);
				specs.Add("<color=#00ffff>Needs:</color>");

				specs.Add("EC", Lib.HumanReadableRate(process.expVar.ecRate));
				foreach (var p in process.expVar.res_parsed)
					specs.Add(p.key, Lib.HumanReadableRate(p.value));

				//if (exp_variant.crew_prepare.Length > 0)
				//{
				//	var cs = new CrewSpecs(exp_variant.crew_prepare);
				//	specs.Add("Preparation", cs ? cs.Info() : "none");
				//}
				//if (exp_variant.crew_operate.Length > 0)
				//{
				//	var cs = new CrewSpecs(exp_variant.crew_operate);
				//	specs.Add("Operation", cs ? cs.Info() : "unmanned");
				//}
				//if (exp_variant.crew_reset.Length > 0)
				//{
				//	var cs = new CrewSpecs(exp_variant.crew_reset);
				//	specs.Add("Reset", cs ? cs.Info() : "none");
				//}

				if (!string.IsNullOrEmpty(process.expVar.requires))
				{
					specs.Add(string.Empty);
					specs.Add("<color=#00ffff>Requires:</color>", string.Empty);
					var tokens = Lib.Tokenize(process.expVar.requires, ',');
					foreach (string s in tokens) specs.Add(Lib.BuildString("• <b>", Science.RequirementText(s), "</b>"));
				}
			}
			else if (Lib.IsFlight())
			{
				specs.Add(string.Empty);
				specs.Add("<color=#00ffff>Situations:</color>", string.Empty);

				string currentSitString = ExperimentInfo.SituationString(process.Subject.situation);

				foreach (string expSitStr in process.expVar.expInfo.AvailableSituations())
				{
					if (process.Subject.isValid && expSitStr.Contains(currentSitString))
						specs.Add(Lib.BuildString("• <b><color=#00ff00>", currentSitString, " (", process.Subject.biome, ")</color></b>"));
					else
						specs.Add(Lib.BuildString("• <b>", expSitStr, "</b>"));
				}

				// TODO : find a way to map issues to requires !
				if (process.issues.Count > 0)
				{
					specs.Add(string.Empty);
					specs.Add("<color=#ff0000>Issues:</color>", string.Empty);

					foreach (string issue in process.issues)
					{
						specs.Add(issue);
					}
				}
			}

			return specs;
		}

		// module mass support
		public float GetModuleMass(float defaultMass, ModifierStagingSituation sit)
		{
			return process != null ? (float)process.GetSampleMass() : 0f;
		}
		public ModifierChangeWhen GetModuleMassChangeWhen() { return ModifierChangeWhen.CONSTANTLY; }
	}
}
