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

		// for prefab/editor only. Never use in flight, get them from the ExperimentProcess object
		[KSPField(isPersistant = true)] public bool editorRecording = false;
		[KSPField(isPersistant = true)] public bool editorForcedRun = false;
		[KSPField(isPersistant = true)] private bool flightProcessCreated = false;

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

		public override void OnLoad(ConfigNode node)
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
				// TODO : What is this for ???
				Vessel v = FlightGlobals.ActiveVessel;
				if (v == null || EVA.IsDead(v)) return;

				// get info from cache
				Vessel_info vi = Cache.VesselInfo(vessel);

				// do nothing if vessel is invalid
				if (!vi.is_valid) return;

				// update ui
				var title = Lib.Ellipsis(process.expVar.expInfo.title, Styles.ScaleStringLength(24));

				// TODO : UI : one toggle that show/hide all other PAW elements, label = status + % done
				// - toggle : start/stop -> label = ETA
				// - toggle : smart/manual -> label = science left -> "valueDoneOnVessel (ValueDoneEverywhere) / totalValue"
				// - label : current situation (if invalid, do it in red + don't show biomes)
				// - if issue(s), label(s) for issue(s) (max 3 ?)
				// + other buttons (reset, prepare, etc...)

				string statusString;
				switch (process.GetState())
				{
					case ExperimentProcess.State.STOPPED:
						statusString = "stopped";
						break;
					case ExperimentProcess.State.ISSUE:
						statusString = "issue";
						break;
					case ExperimentProcess.State.SMART_WAIT:
						statusString = "waiting";
						break;
					case ExperimentProcess.State.SMART_RUN:
						statusString = "running : " + Lib.HumanReadablePerc(process.GetPercentDone(), "F1");
						break;
					case ExperimentProcess.State.FORCED_RUN:
						statusString = "manual run : " + Lib.HumanReadablePerc(process.GetPercentDone(), "F1"); ;
						break;
					default:
						break;
				}
				
			}
			// in the editor
			else if (Lib.IsEditor())
			{
				// update ui
				//Events["Toggle"].guiName = Lib.StatusToggle(exp_variant.title, recording ? "recording" : "stopped");
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

			specs.Add(string.Empty);
			//double expSize = exp_variant.data_max;
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

			List<string> situations = process.expVar.expInfo.Situations();
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
