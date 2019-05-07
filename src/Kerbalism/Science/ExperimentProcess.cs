using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace KERBALISM
{


	/// <summary>
	/// Object tied to an Experiment PartModule, holds all the data needed to execute the experiment logic.
	/// <para/>It is first created by the PartModule, stored in the VesselData object, accessed and persisted trough DB.vessels.
	/// <para/>It stays available for both loaded and unloaded vessels and is processed in Science.Update().
	/// <para/>The PartModule always have a reference to it when the vessel is loaded, and can query it for UI stuff.
	/// <para/>It is also available in the editor but not persisted so anything set in the editor should be persisted on the PM
	/// </summary>
	public sealed class ExperimentProcess
	{
		// persistence
		public uint part_id;
		public bool enabled; // synchronized with the PM enabled / moduleIsEnabled state
		public bool recording;
		public bool forcedRun;
		public bool didPrepare;
		public bool shrouded;
		public bool broken; // TODO : is that needed ? Side note : check synchronization with the PM enabled/moduleIsEnabled state
		public double remainingSampleMass;
		public uint privateHdId;
		public string issue;

		//
		public ExperimentInfo exp_info;
		public ExperimentResult result;

		private string last_subject_id;

		double data_pending;
		double data_consumed;

		/// <summary>
		/// Use only in the editor or when the vessel is first launched
		/// </summary>
		/// <param name="experimentModule"></param>
		public ExperimentProcess(Part part, string exp_info_id, double sample_amount, bool recording, bool forcedRun)
		{
			exp_info = Science.GetExperimentInfo(exp_info_id);
			this.part_id = part.flightID;
			this.recording = recording;
			this.forcedRun = forcedRun;
			this.didPrepare = false;
			this.shrouded = part.ShieldedFromAirstream;
			this.broken = false;
			this.remainingSampleMass = sample_amount * exp_info.sample_mass;
		}

		public void Update(Vessel vessel)
		{
			// get ec handler
			Resource_info ec = ResourceCache.Info(vessel, "ElectricCharge");

			// test for issues










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

		private string TestForIssues(Vessel v, Resource_info ec)
		{
			//var subject_id = Science.Generate_subject_id(experiment.experiment_id, v);

			if (broken)
				return "broken";

			if (shrouded && !exp_info.allow_shrouded)
				return "shrouded";

			// TODO : this won't work because the new result.subject_id will not exist...
			if (exp_info.crew_reset.Length > 0
				&& !string.IsNullOrEmpty(last_subject_id)
				&& result.subject_id != last_subject_id)
				return "reset required";

			if (ec.amount < double.Epsilon && exp_info.ec_rate > 0)
				return "no Electricity";

			if (!string.IsNullOrEmpty(exp_info.crew_operate))
			{
				var cs = new CrewSpecs(exp_info.crew_operate);
				if (!cs && Lib.CrewCount(v) > 0)
					return "crew on board";
				else if (cs && !cs.Check(v))
					return cs.Warning();
			}

			if (!exp_info.sample_collecting && remainingSampleMass < double.Epsilon
				&& exp_info.sample_mass > 0)
				return "depleted";

			if (!didPrepare && !string.IsNullOrEmpty(exp_info.crew_prepare))
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

	}

}
