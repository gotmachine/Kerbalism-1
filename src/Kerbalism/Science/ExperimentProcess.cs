using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace KERBALISM
{


	/// <summary>
	/// Loaded/unloaded state independant object containing the PartModule data and game logic, persisted in DB.
	/// Ideally, all its methods must be able to run without ever using the PM / ProtoPM or Part/ProtoPart.
	/// The game logic methods are called from Science.Update(), regardless of the state of the vessel.
	/// <para/>When loaded, the PM should query/update it for UI stuff and if loaded-only information (physics...) is required.
	/// The PM OnLoad() is responsible for first time instantiation and for reacquiring the reference from DB.
	/// <para/> It is NOT available in the editor, so any state set in the editor should use PM persistant fields that are passed to equivalent fields on the process object trough the process ctor.
	/// </summary>
	public sealed class ExperimentProcess : DataProcess
	{
		public enum State
		{
			STOPPED = 0, ISSUE = 1, SMART_WAIT = 2, SMART_RUN = 3, FORCED_RUN = 4
		}

		// persistence
		public bool recording;
		public bool forcedRun;
		public bool smartMode;
		public bool didPrepare;
		public bool needReset;
		public bool shrouded;
		private long sampleAmount;
		private long dataSampled; // TODO : dataSampled should go in the base class

		// UI
		private bool hasIssue;
		private string situationIssue;
		private string processIssue;
		private List<string> requireIssues;

		// current simstep caching
		private double resourceScale;
		private double scienceValue;

		// related objects
		public ExperimentVariant expVar;

		public override void OnPartModuleLoad(PartModule partModule)
		{
			Experiment expModule = (Experiment)partModule;

			expVar = Science.GetExperimentVariant(expModule.variantId);
			recording = expModule.editorRecording;
			forcedRun = expModule.editorForcedRun;
			didPrepare = false;
			smartMode = true;
			needReset = false;
			shrouded = expModule.part.ShieldedFromAirstream;
			sampleAmount = expVar.sampleAmount * expVar.expInfo.fullSize;
			if (sampleAmount == 0)
				type = FileType.File;
			else
				type = FileType.Sample;
		}

		public override void OnLoad(ConfigNode node)
		{
			recording = Lib.ConfigValue(node, "recording", false);
			forcedRun = Lib.ConfigValue(node, "recording", false);
			smartMode = Lib.ConfigValue(node, "smartMode", true);
			didPrepare = Lib.ConfigValue(node, "didPrepare", false);
			needReset = Lib.ConfigValue(node, "needReset", false);
			shrouded = Lib.ConfigValue(node, "shrouded", false);
			sampleAmount = Lib.ConfigValue(node, "sampleAmount", 0L);
			dataSampled = Lib.ConfigValue(node, "dataSampled", 0L);
		}

		public override void OnSave(ConfigNode node)
		{
			node.AddValue("recording", recording);
			node.AddValue("forcedRun", forcedRun);
			node.AddValue("smartMode", smartMode);
			node.AddValue("didPrepare", didPrepare);
			node.AddValue("needReset", needReset);
			node.AddValue("shrouded", shrouded);
			node.AddValue("sampleAmount", sampleAmount);
			node.AddValue("dataSampled", dataSampled);
		}

		public State GetState()
		{
			if (hasIssue) return State.ISSUE;
			if (!recording) return State.STOPPED;
			if (forcedRun) return State.FORCED_RUN;
			if (scienceValue < double.Epsilon && smartMode) return State.SMART_WAIT;
			return State.SMART_RUN;
		}

		public override ExperimentInfo GetExperimentInfo()
		{
			return expVar.expInfo;
		}

		public override bool CanRun(Vessel vessel, double elapsed_s, bool subjectHasChanged)
		{
			if (subjectHasChanged)
				forcedRun = false;


			// TODO optimisation : only test all conditions if the module PAW or the device UI need it
			// this can probably be done using the onPartActionUICreate / onPartActionUIDismiss events for PAW UI
			// and we can manage that easily for the devices

			// test for non-situation dependant issues
			processIssue = TestForIssues(vessel, elapsed_s);

			// test for requirements issues
			requireIssues = expVar.TestRequirements(vessel);

			// get subject and result
			// TODO : this should go in the abstract base class
			// but to do it wo need to put subject.isValid and subject.HasChanged in the base abstract Subject class


			// note : available space on drives will be checked after we know what has been transmitted
			if (!Subject.isValid || requireIssues.Count > 0 || !string.IsNullOrEmpty(processIssue))
				hasIssue = true;

			// check for science value
			// TODO : do not use science value, use data amount
			if (Subject.isValid)
				scienceValue = Subject.ScienceValueRemainingTotal();
			else
				scienceValue = 0;

			State state = GetState();

			if (state > State.SMART_WAIT)
			{
				dataPending = (long)(expVar.dataRate * elapsed_s * resourceScale);

				// clamp to available sample material
				if (type == FileType.Sample && !expVar.sampleCollecting)
					dataPending = Math.Min(dataPending, sampleAmount);

				// clamp to what is actually needed
				if (forcedRun)
					dataPending = Math.Min(dataPending, expVar.expInfo.fullSize - (dataSampled % expVar.expInfo.fullSize));
				else
					dataPending = Math.Min(dataPending, Subject.DataRemainingTotal());
			}
			else
			{
				dataPending = 0;
			}

			if (dataPending == 0)
			{
				if (state == State.FORCED_RUN)
				{
					forcedRun = false;
					recording = false;
				}
				return false;
			}

			return true;
		}

		public override long GetDataPending(Vessel vessel, double elapsed_s, long dataToConvert = 0)
		{
			return dataPending;
		}

		public override void Process(Vessel vessel, double elapsed_s, long dataProcessed)
		{
			// if we are here but no data has been processed, it can only be because drives are full
			if (dataProcessed == 0)
			{
				processIssue = "storage full";
				return;
			}

			// keep track of how much data this experiment has produced
			dataSampled += dataProcessed;

			// remove stored sample amount
			if (type == FileType.Sample)
			{
				// note : removing more than what is stored should never happen but better safe than sorry
				sampleAmount = Math.Max(sampleAmount - dataProcessed, 0);
			}

			// get final production ratio
			resourceScale = dataProcessed / (expVar.dataRate * elapsed_s);

			// consume EC
			if (expVar.ecRate > 0)
			{
				Resource_info ec = ResourceCache.Info(vessel, "ElectricCharge");
				ec.Consume(expVar.ecRate * elapsed_s * resourceScale, "experiment");
			}

			// consume other resources
			for (int i = 0; i < expVar.res_parsed.Length; i++)
			{
				Resource_info ri = ResourceCache.Info(vessel, expVar.res_parsed[i].key);
				ri.Consume(expVar.res_parsed[i].value * elapsed_s * resourceScale, "experiment");
			}
		}

		public double GetPercentDone()
		{
			if (smartMode)
				return 1.0 - (Subject.ScienceValueRemainingTotal() / Subject.ScienceValueGame());
			else
				return (dataSampled % expVar.expInfo.fullSize) / expVar.expInfo.fullSize;
		}

		public double GetSampleMass()
		{
			return sampleAmount * expVar.expInfo.massPerBit;
		}

		// TODO : resource evaluation is... problematic
		// specifically there is a problem when vessel-wide resource production rate < experiment nominal consumption rate
		// the right course of action in this case is to scale down the experiment output by the production/consumption ratio
		// CURRENT SITUATION :
		// 1. for EC, we only run when "resource.amount > 0", this will cause the experiment to run at 100% at first step, then 0% next, then 100% next, etc
		// leading to a wrong data output amount because the rate should have been something between 1-99% (not 100%) every 2 steps.
		// 2. for other resources, we only run if "resource.amount > exp_rate * elapsed_s", so the experiment will wait for the resource amount to be adequate,
		// leading (example) to a 0%-0%-0%-0%-100%-0%-0%-0%-0%-100%... cycle, making the output correctly evaluated.
		// WHAT THIS MEAN :
		// - due to 1. experiment data generation rate is greater than what it should have been when there is not enough EC for it to run at 100%
		// - due to 2. we say welcome to the dreaded "output is clamped to capacity at high time warp speeds bug",
		//   and in this case it's really bad because the experiment won't run at all if capapcity is inadequate
		// - due to 1. and 2., experiment evaluation will done every 1+n sim step instead of each step,
		//   exacerbating the (already present) problem that we may miss / not accuratly evaluate the running conditions
		// CURRENT NOT-SATISFACTORY SOLUTION :
		// - if for any resource, "resource.amount == 0", don't run
		// - for each resource, evaluate the resource.amount / (exp_rate * elapsed_s) ratio, keep the worst one
		// - consume resource and produce output according to the worst ratio
		// that should lead to a 0% <> [X%] <> 0% <> [X%] cycle where X is the worst ratio, resulting in :
		// - GOOD : correctly scaled resource consumption / data output in all cases
		// - BAD : data output rate clamping at high timewarp speeds, but the experiement will still run, albeit at a reduced rate
		// - MEH : evaluation will be done every 1 or 2 steps, never more.

		private string TestForIssues(Vessel v, double elapsed_s)
		{
			if (!enabled)
				return "broken";

			if (shrouded && !expVar.allowShrouded)
				return "shrouded";

			if (needReset)
				return "reset required";

			resourceScale = 1.0;

			if (expVar.ecRate > 0)
			{
				Resource_info ec = ResourceCache.Info(v, "ElectricCharge");
				if (ec.amount < double.Epsilon)
					return "no Electricity";

				resourceScale = Math.Min(ec.amount / (expVar.ecRate * elapsed_s), 1.0);
			}

			for (int i = 0; i < expVar.res_parsed.Length; i++)
			{	
				Resource_info ri = ResourceCache.Info(v, expVar.res_parsed[i].key);
				if (ri.amount < double.Epsilon)
					return "missing " + ri.resource_name;

				resourceScale = Math.Min(resourceScale, Math.Min(ri.amount / (expVar.res_parsed[i].value * elapsed_s), 1.0));
			}

			if (!string.IsNullOrEmpty(expVar.crewOperate))
			{
				var cs = new CrewSpecs(expVar.crewOperate);
				if (!cs && Lib.CrewCount(v) > 0)
					return "crew on board";
				else if (cs && !cs.Check(v))
					return cs.Warning();
			}

			if (type == FileType.Sample && !expVar.sampleCollecting && sampleAmount <= 0)
				return "depleted";

			if (!didPrepare && !string.IsNullOrEmpty(expVar.crewPrepare))
				return "not prepared";

			return string.Empty;
		}
	}
}
