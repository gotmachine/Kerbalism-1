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
		public ExperimentVariantInfo exp_info;
		public ExperimentResult result;

		double data_pending;
		double data_consumed;

		/// <summary>
		/// Use only in the editor or when the vessel is first launched
		/// </summary>
		/// <param name="experimentModule"></param>
		public ExperimentProcess(Part part, string experiment_id, double sample_amount, bool recording, bool forcedRun)
		{
			exp_info = Science.GetExperimentInfo(experiment_id);
			this.part_id = part.flightID;
			this.recording = recording;
			this.forcedRun = forcedRun;
			this.didPrepare = false;
			this.shrouded = part.ShieldedFromAirstream;
			this.broken = false;
			this.remainingSampleMass = sample_amount * exp_info.sample_mass;
		}
	}

}
