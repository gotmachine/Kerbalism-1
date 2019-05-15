using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace KERBALISM
{


	/// <summary>
	/// Object tied to an Experiment PartModule, holds all the data needed to execute the experiment logic.
	/// <para/>It is first created by the PartModule, referenced in the VesselData object, accessed and persisted trough DB.vessels.
	/// <para/>It stays available for both loaded and unloaded vessels and is processed in Science.Update().
	/// <para/>The PartModule always have a reference to it when the vessel is loaded, and can query it for UI stuff.
	/// <para/>It is also available in the editor but not persisted so anything set in the editor should be persisted on the PM
	/// </summary>
	public sealed class ExperimentProcess
	{
		// persistence
		public uint partId;
		public bool enabled; // synchronized with the PM enabled / moduleIsEnabled state
		public bool recording;
		public bool forcedRun;
		public bool smartMode;
		public bool didPrepare;
		public bool needReset;
		public bool shrouded;
		public bool broken; // TODO : is that needed ? Side note : check synchronization with the PM enabled/moduleIsEnabled state
		public long sampleAmount; // use integers internaly to avoid rounding errors
		public uint privateHdId;

		// state
		private bool hasIssue;
		private string situationIssue;
		private string processIssue;
		private List<string> requireIssues;

		// dataSampled will be set when the subject changes and then increased everytime some data is processed by this process
		// but do not expect it to accuratly reflect the data amount present in drive, it will become incoherent in many situations :
		// - if some data is transmitted or transfered from the drives
		// - if some data is added or removed by another process
		// it's purpose is only to prevent experiments in manual mode from running forever
		private long dataSampled;
		private double scienceValue;

		public ExperimentVariant expVar;
		public FileType type;
		public ExperimentSubject subject;
		public ExperimentResult result;

		public long dataPending;
		public long dataProcessed;

		public enum State
		{
			STOPPED = 0, ISSUE = 1, SMART_WAIT = 2, SMART_RUN = 3, FORCED_RUN = 4
		}

		public State GetState()
		{
			if (hasIssue) return State.ISSUE;
			if (!recording) return State.STOPPED;
			if (forcedRun) return State.FORCED_RUN;
			if (scienceValue < double.Epsilon && smartMode) return State.SMART_WAIT;
			return State.SMART_RUN;
		}

		/// <summary>
		/// Use only in the editor or when the vessel is first launched
		/// </summary>
		/// <param name="experimentModule"></param>
		public ExperimentProcess(Part part, string exp_variant_id, int sample_amount, bool recording, bool forcedRun)
		{
			expVar = Science.GetExperimentInfo(exp_variant_id);
			this.partId = part.flightID;
			this.recording = recording;
			this.forcedRun = forcedRun;
			this.didPrepare = false;
			this.shrouded = part.ShieldedFromAirstream;
			this.broken = false;
			this.sampleAmount = sample_amount * expVar.exp_info.dataSize;
			if (sampleAmount == 0)
				type = FileType.File;
			else
				type = FileType.Sample;
		}

		public bool Prepare(Vessel vessel, double elapsed_s)
		{
			// TODO optimisation : only test all conditions if the module PAW or the device UI need it
			// this can probably be done using the onPartActionUICreate / onPartActionUIDismiss events for PAW UI
			// and we can manage that easily for the devices

			// clear result reference if it was deleted
			if (result != null && result.isDeleted) result = null;

			// test for non-situation dependant issues
			processIssue = TestForIssues(vessel);

			// test for requirements issues
			requireIssues = expVar.TestRequirements(vessel);

			// get subject and result
			if (subject == null)
			{
				subject = new ExperimentSubject(expVar.exp_info, vessel);
				if (subject.isValid)
				{
					result = Drive2.FindPartialResult(vessel, expVar.exp_info, subject.subject_id, type, privateHdId, out dataSampled);
				}
			}
			else if (subject.HasChanged(vessel))
			{
				subject = new ExperimentSubject(expVar.exp_info, vessel);
				forcedRun = false;

				if (subject.isValid)
				{
					result = Drive2.FindPartialResult(vessel, expVar.exp_info, subject.subject_id, type, privateHdId, out dataSampled);
				}
				else
				{
					result = null;
				}
			}

			// note : available space on drives will be checked after we know what has been transmitted
			if (!subject.isValid || requireIssues.Count > 0 || !string.IsNullOrEmpty(processIssue))
				hasIssue = true;

			// check for science value
			// TODO : do not use science value, use data amount
			if (subject.isValid)
				scienceValue = subject.ScienceValueRemainingTotal();
			else
				scienceValue = 0;


			if (GetState() > State.SMART_WAIT)
			{
				dataPending = (long)(expVar.data_rate * elapsed_s);
				// clamp data to what is actually needed
				if (forcedRun)
					dataPending = Math.Min(dataPending, expVar.exp_info.dataSize - (dataSampled % expVar.exp_info.dataSize));
				else
					dataPending = Math.Min(dataPending, subject.DataRemainingTotal());
			}
			else
			{
				dataPending = 0;
			}

			dataProcessed = 0;
			return dataPending > 0;

			// TODO : update dataSampled after a 
		}

		public double GetPercentDone()
		{
			if (smartMode)
				return 1.0 - (subject.ScienceValueRemainingTotal() / subject.ScienceValueGame());
			else
				return (dataSampled % expVar.exp_info.dataSize) / expVar.exp_info.dataSize;
		}

		public double GetSampleMass()
		{
			return sampleAmount * expVar.exp_info.massPerBit;
		}

		private string TestForIssues(Vessel v)
		{
			//var subject_id = Science.Generate_subject_id(experiment.experiment_id, v);

			if (broken)
				return "broken";

			if (shrouded && !expVar.allow_shrouded)
				return "shrouded";

			if (needReset)
				return "reset required";

			Resource_info ec = ResourceCache.Info(v, "ElectricCharge");
			if (ec.amount < double.Epsilon && expVar.ec_rate > 0)
				return "no Electricity";

			for (int i = 0; i < expVar.res_parsed.Length; i++)
			{
				// TODO : this check will be inconsistent when timewarping fast, just check that amount > 0
				Resource_info ri = ResourceCache.Info(v, expVar.res_parsed[i].key);
				if (ri.amount < expVar.res_parsed[i].value * Kerbalism.elapsed_s)
					return "missing " + ri.resource_name;
			}

			if (!string.IsNullOrEmpty(expVar.crew_operate))
			{
				var cs = new CrewSpecs(expVar.crew_operate);
				if (!cs && Lib.CrewCount(v) > 0)
					return "crew on board";
				else if (cs && !cs.Check(v))
					return cs.Warning();
			}

			if (type == FileType.Sample && !expVar.sample_collecting && sampleAmount <= 0)
				return "depleted";

			if (!didPrepare && !string.IsNullOrEmpty(expVar.crew_prepare))
				return "not prepared";

			return string.Empty;
		}

	}

}
