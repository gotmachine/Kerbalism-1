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
		public bool forcedRun;
		public bool didPrepare;
		public bool needReset;
		public bool shrouded;
		private long sampleAmount = 0;

		// current simstep caching
		private double resourceScale;
		private double scienceValue;

		// related objects
		public ExperimentVariant expVar;

		public override void OnPartModuleLoad(PartModule partModule)
		{
			Experiment expModule = (Experiment)partModule;

			expVar = Science.GetExperimentVariant(expModule.variantId);
			if (expVar == null) return;

			running = false;
			forcedRun = false;
			didPrepare = false;
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
			forcedRun = Lib.ConfigValue(node, "recording", false);
			didPrepare = Lib.ConfigValue(node, "didPrepare", false);
			needReset = Lib.ConfigValue(node, "needReset", false);
			shrouded = Lib.ConfigValue(node, "shrouded", false);
			sampleAmount = Lib.ConfigValue(node, "sampleAmount", 0L);
		}

		public override void OnSave(ConfigNode node)
		{
			node.AddValue("forcedRun", forcedRun);
			node.AddValue("didPrepare", didPrepare);
			node.AddValue("needReset", needReset);
			node.AddValue("shrouded", shrouded);
			node.AddValue("sampleAmount", sampleAmount);
		}

		public State GetState()
		{
			if (issues.Count > 0) return State.ISSUE;
			if (!running) return State.STOPPED;
			if (forcedRun) return State.FORCED_RUN;
			if (scienceValue < double.Epsilon) return State.SMART_WAIT;
			return State.SMART_RUN;
		}

		public override ExperimentInfo GetExperimentInfo()
		{
			return expVar.expInfo;
		}

		// TODO optimisation : only test all conditions if the module PAW or the device UI need it
		// this can probably be done by tracking the onPartActionUICreate / onPartActionUIDismiss events for PAW UI
		// and we can manage that internally for the devices
		public override bool CanRun(Vessel vessel, double elapsed_s, bool subjectHasChanged)
		{

			if (!enabled)
				issues.Add("broken");

			if (shrouded && !expVar.allowShrouded)
				issues.Add("shrouded");

			if (needReset)
				issues.Add("reset required");

			resourceScale = 1.0;

			if (expVar.ecRate > 0)
			{
				Resource_info ec = ResourceCache.Info(vessel, "ElectricCharge");
				if (ec.amount < double.Epsilon)
					issues.Add("no Electricity");

				resourceScale = Math.Min(ec.amount / (expVar.ecRate * elapsed_s), 1.0);
			}

			for (int i = 0; i < expVar.res_parsed.Length; i++)
			{
				Resource_info ri = ResourceCache.Info(vessel, expVar.res_parsed[i].key);
				if (ri.amount < double.Epsilon)
					issues.Add(Lib.BuildString("missing ", ri.resource_name));

				resourceScale = Math.Min(resourceScale, Math.Min(ri.amount / (expVar.res_parsed[i].value * elapsed_s), 1.0));
			}

			if (!string.IsNullOrEmpty(expVar.crewOperate))
			{
				var cs = new CrewSpecs(expVar.crewOperate);
				if (!cs && Lib.CrewCount(vessel) > 0)
					issues.Add("crew on board");
				else if (cs && !cs.Check(vessel))
					issues.Add(cs.Warning());
			}

			if (type == FileType.Sample && !expVar.sampleCollecting && sampleAmount <= 0)
				issues.Add("depleted");

			if (!didPrepare && !string.IsNullOrEmpty(expVar.crewPrepare))
				issues.Add("not prepared");

			// test for requirements issues
			issues.AddRange(expVar.TestRequirements(vessel));

			if (subjectHasChanged)
				forcedRun = false;

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
				{
					long sizeRemaining = expVar.expInfo.fullSize - (existingData % expVar.expInfo.fullSize);
					if (sizeRemaining < dataPending)
					{
						dataPending = sizeRemaining;
						running = false;
					}
				}
				else
				{
					dataPending = Math.Min(dataPending, Subject.DataRemainingTotal());
				}	
			}
			else
			{
				dataPending = 0;
			}

			return dataPending > 0;
		}

		public override long GetDataPending(Vessel vessel, double elapsed_s, long dataToConvert = 0)
		{
			return dataPending;
		}

		public override void PostUpdate(Vessel vessel, double elapsed_s, long dataProcessed)
		{
			if (dataProcessed <= 0)
				return;

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

		public override double GetResultScienceCap()
		{
			if (expVar.scienceCap > 0)
				return expVar.scienceCap;
			else
				return base.GetResultScienceCap();
		}

		public long GetDataDone()
		{
			return existingData % expVar.expInfo.fullSize;
		}

		public double GetPercentDone()
		{
			if (forcedRun)
				return (double)GetDataDone() / expVar.expInfo.fullSize;
			else
				return 1.0 - (Subject.ScienceValueRemainingTotal() / Subject.ScienceValueGame());

		}

		public double GetETA()
		{
				return expVar.duration - (GetPercentDone() * expVar.duration);
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
	}
}
