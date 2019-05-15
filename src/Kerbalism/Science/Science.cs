using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;


namespace KERBALISM
{

	public static class Science
	{
		// this controls how fast science is credited while it is being transmitted.
		// try to be conservative here, because crediting introduces a lag
		public const double buffer_science_value = 0.4; // min. 0.01 value
		public const double min_buffer_size = 0.01; // min. 10kB

		// this is for auto-transmit throttling
		public const double min_file_size = 0.002;

		// pseudo-ctor
		public static void Init()
		{
			// make the science dialog invisible, just once
			if (Features.Science)
			{
				GameObject prefab = AssetBase.GetPrefab("ScienceResultsDialog");
				if (Settings.ScienceDialog)
				{
					prefab.gameObject.AddOrGetComponent<Hijacker>();
				}
				else
				{
					prefab.gameObject.AddOrGetComponent<MiniHijacker>();
				}

				// load EXPERIMENT_VARIANT nodes
				ConfigNode[] var_nodes = GameDatabase.Instance.GetConfigNodes("EXPERIMENT_VARIANT");
				for (int i = 0; i < var_nodes.Length ; i++)
				{
					ExperimentVariant exp_variant = new ExperimentVariant(var_nodes[i]);
					if (!exp_variants.ContainsKey(exp_variant.id))
						exp_variants.Add(exp_variant.id, exp_variant);
					else
						Lib.Log("WARNING : Duplicate EXPERIMENT_VARIANT '" + exp_variant.id + "' wasn't loaded");
				}

				// load EXPERIMENT_INFO nodes
				ConfigNode[] exp_nodes = GameDatabase.Instance.GetConfigNodes("EXPERIMENT_INFO");
				for (int i = 0; i < exp_nodes.Length; i++)
				{
					ExperimentInfo exp_info = new ExperimentInfo(exp_nodes[i]);
					if (!exp_infos.ContainsKey(exp_info.id))
						exp_infos.Add(exp_info.id, exp_info);
					else
						Lib.Log("WARNING : Duplicate EXPERIMENT_INFO '" + exp_info.id + "' wasn't loaded");
				}
			}
		}

		// consume EC for transmission, and transmit science data
		public static void Update(Vessel v, Vessel_info vi, VesselData vd, Vessel_resources resources, double elapsed_s)
		{
			// do nothing if science system is disabled
			if (!Features.Science) return;



			// get connection info and transmit capacity
			long transmitCapacity;
			ConnectionInfo conn = vi.connection;
			if (conn == null
				|| !conn.linked
				|| ResourceCache.Info(v, "ElectricCharge").amount < double.Epsilon)
				transmitCapacity = 0;
			else
				transmitCapacity = Lib.MBToBit(conn.rate * elapsed_s);

			// TODO : prepare all labs

			// prepare all experiments
			List<ExperimentProcess> pending_exp = new List<ExperimentProcess>();
			for (int i = 0; i < vd.experiments.Count; i++)
			{
				if (vd.experiments[i].Prepare(v, elapsed_s))
				{
					pending_exp.Add(vd.experiments[i]);
				}
			}

			// get all file results from all drives
			List<ExperimentResult> ts_results = new List<ExperimentResult>();
			foreach (Drive2 drive in Drive2.GetDrives(v))
			{
				ts_results.AddRange(drive.FindAll(p => p.type == FileType.File));
			}

			// TODO : CHECK : put first files that were already being transferred
			ts_results.Sort((x, y) => y.transmit_rate.CompareTo(x.transmit_rate));

			// reset transmit_rate
			ts_results.ForEach(r => r.transmit_rate = 0);

			// filter to get only files flagged for transmit
			ts_results.RemoveAll(r => !r.process);


			// TODO : process labs data


			// transmit and store experiments data
			// note : in manual mode, if transmit capacity is enough for all data to be transmitted, the experiment will run forever.
			for (int i = 0; i < pending_exp.Count; i++)
			{
				// transmit if :
				// - there is some transmit capacity
				// - result is a file (not a sample)
				// - result is flagged for transfer or this is a new result and auto-transmit is true
				if (transmitCapacity > 0
					&& pending_exp[i].type == FileType.File
					&& ((pending_exp[i].result != null && pending_exp[i].result.process)
						|| (pending_exp[i].result == null && PreferencesScience.Instance.transmitScience)))
				{
					long transmitted = pending_exp[i].dataPending < transmitCapacity ? pending_exp[i].dataPending : transmitCapacity;
					if (transmitted > 0)
					{
						// at this point some data is transmitted so we need a result object for the transmit buffer
						// the result is created empty and will stay empty if all the data is transmitted.
						// this way it will appear in the file manager UI
						if (pending_exp[i].result == null)
						{
							// get a drive, even a full one (we are creating a zero size file)
							Drive2 drive = Drive2.GetDriveBestCapacity(v, pending_exp[i].type, 0, pending_exp[i].privateHdId);
							if (drive != null)
							{
								pending_exp[i].result = new ExperimentResult(drive, pending_exp[i].type, pending_exp[i].subject);
								ts_results.Add(pending_exp[i].result);
							}
						}

						if (pending_exp[i].result != null)
						{
							transmitCapacity -= transmitted;
							pending_exp[i].dataPending -= transmitted;
							pending_exp[i].dataProcessed += transmitted;
							pending_exp[i].result.transmit_buffer += transmitted;
							pending_exp[i].result.transmit_rate = (long)(transmitted / elapsed_s);
						}
					}
				}

				// we have transmitted all we can, now try storing the remaining data in drives
				while (pending_exp[i].dataPending > 0)
				{
					if (pending_exp[i].result == null)
					{
						// get a drive with some space on it
						Drive2 drive = Drive2.GetDriveBestCapacity(v, pending_exp[i].type, 1, pending_exp[i].privateHdId);
						if (drive != null)
							pending_exp[i].result = new ExperimentResult(drive, pending_exp[i].type, pending_exp[i].subject);
					}

					if (pending_exp[i].result == null)
					{
						break;
					}
					else
					{
						long stored = Math.Min(pending_exp[i].result.SizeCapacityAvailable(), pending_exp[i].dataPending);

						pending_exp[i].result.size += stored;
						if (pending_exp[i].type == FileType.Sample)
							pending_exp[i].sampleAmount -= stored;

						// if drive is full, try to find another one
						if (stored < pending_exp[i].dataPending)
							pending_exp[i].result = null;

						pending_exp[i].dataPending -= stored;
						pending_exp[i].dataProcessed += stored;
					}
				}
			}

			// if there is some transmit capacity left, transmit data stored in drives
			if (transmitCapacity > 0)
			{
				for (int i = 0; i < ts_results.Count; i++)
				{
					if (ts_results[i].size <= 0) continue;
					long transmitted = ts_results[i].size < transmitCapacity ? ts_results[i].size : transmitCapacity;

					ts_results[i].size -= transmitted;
					ts_results[i].transmit_buffer += transmitted;
					ts_results[i].transmit_rate = (long)(transmitted / elapsed_s);

					transmitCapacity -= transmitted;
					if (transmitCapacity <= 0) break;
				}
			}

			// avoid corner-case when RnD isn't live during scene changes
			// - this avoid losing science if the buffer reach threshold during a scene change
			// TODO : only skip the following for loop
			if (HighLogic.CurrentGame.Mode != Game.Modes.SANDBOX && ResearchAndDevelopment.Instance == null) return;

			// all file sizes are now updated
			// actually register data transmitted for files whose buffer is full, and delete empty files
			for (int i = 0; i < ts_results.Count; i++)
			{
				if (ts_results[i].transmit_buffer > 0)
				{
					if (ts_results[i].transmit_buffer > ts_results[i].buffer_full)
					{
						Credit(ts_results[i].subject_id, ts_results[i].transmit_buffer, true, v.protoVessel);
						ts_results[i].transmit_buffer = 0;
					}
					else if (ts_results[i].size == 0 && ts_results[i].transmit_rate == 0)
					{
						Credit(ts_results[i].subject_id, ts_results[i].transmit_buffer, true, v.protoVessel);
						ts_results[i].Delete();
					}
					// TODO : message when subject is completed
					//			Message.Post(
					//				Lib.BuildString(Lib.HumanReadableScience(totalValue), " ", Experiment(exp_filename).FullName(exp_filename), " completed"),
					//			  Lib.TextVariant(
					//					"Our researchers will jump on it right now",
					//					"There is excitement because of your findings",
					//					"The results are causing a brouhaha in R&D"
					//				));
				}
			}

			// TODO : EC and resource consumption
			// TODO : not enough storage issue sent back to the process




			//ConnectionInfo conn = vi.connection;
			//if (conn == null || String.IsNullOrEmpty(vi.transmitting)) return;

			//// get filename of data being downloaded
			//var exp_filename = vi.transmitting;

			////var drive = FindDrive(v, exp_filename);

			//// if some data is being downloaded
			//// - avoid cornercase at scene changes
			//if (exp_filename.Length > 0 && drive != null)
			//{
			//	// get file
			//	File file = drive.files[exp_filename];

			//	// determine how much data is transmitted
			//	double transmitted = Math.Min(file.size, conn.rate * elapsed_s);

			//	// consume data in the file
			//	file.size -= transmitted;

			//	// accumulate in the buffer
			//	file.buff += transmitted;

			//	bool credit = file.size <= double.Epsilon;

			//	// this is the science value remaining for this experiment
			//	var remainingValue = Value(exp_filename, 0);

			//	// this is the science value of this sample
			//	var dataValue = Value(exp_filename, file.buff);

			//	if (!credit && file.buff > min_buffer_size) credit = dataValue > buffer_science_value;

			//	// if buffer is full, or file was transmitted completely
			//	if (credit)
			//	{
			//		var totalValue = TotalValue(exp_filename);

			//		// collect the science data
			//		Credit(exp_filename, file.buff, true, v.protoVessel);

			//		// reset the buffer
			//		file.buff = 0.0;

			//		// this was the last useful bit, there is no more value in the experiment
			//		if (remainingValue >= 0.1 && remainingValue - dataValue < 0.1)
			//		{
						
			//			Message.Post(
			//				Lib.BuildString(Lib.HumanReadableScience(totalValue), " ", Experiment(exp_filename).FullName(exp_filename), " completed"),
			//			  Lib.TextVariant(
			//					"Our researchers will jump on it right now",
			//					"There is excitement because of your findings",
			//					"The results are causing a brouhaha in R&D"
			//				));
			//		}
			//	}

			//	// if file was transmitted completely
			//	if (file.size <= double.Epsilon)
			//	{
			//		// remove the file
			//		drive.Delete_file(exp_filename);
			//	}
			//}
		}

		// return name of file being transmitted from vessel specified
		// TODO : adapt this, and see how we can adjust EC consumption
		public static string Transmitting(Vessel v, bool linked)
		{
			// never transmitting if science system is disabled
			if (!Features.Science) return string.Empty;

			// not transmitting if unlinked
			if (!linked) return string.Empty;

			// not transmitting if there is no ec left
			if (ResourceCache.Info(v, "ElectricCharge").amount <= double.Epsilon) return string.Empty;

			// get first file flagged for transmission, AND has a ts at least 5 seconds old or is > 0.001Mb in size
			foreach (var drive in Drive.GetDrives(v, true))
			{
				double now = Planetarium.GetUniversalTime();
				foreach (var p in drive.files)
				{
					if (drive.GetFileSend(p.Key) && (p.Value.ts + 3 < now || p.Value.size > min_file_size)) return p.Key;
				}
			}

			// no file flagged for transmission
			return string.Empty;
		}


		// credit science for the experiment subject specified
		public static float Credit(string subject_id, double size, bool transmitted, ProtoVessel pv)
		{
			var credits = KERBALISM.ExperimentVariant.Value(subject_id, size);

			// credit the science
			var subject = ResearchAndDevelopment.GetSubjectByID(subject_id);
			if(subject == null)
			{
				// TODO : actually create the subject !
				Lib.Log("WARNING: science subject " + subject_id + " cannot be credited in R&D");
			}
			else
			{
				subject.science += credits / HighLogic.CurrentGame.Parameters.Career.ScienceGainMultiplier;
				subject.scientificValue = ResearchAndDevelopment.GetSubjectValue(subject.science, subject);
				ResearchAndDevelopment.Instance.AddScience(credits, transmitted ? TransactionReasons.ScienceTransmission : TransactionReasons.VesselRecovery);

				// fire game event
				// - this could be slow or a no-op, depending on the number of listeners
				//   in any case, we are buffering the transmitting data and calling this
				//   function only once in a while
				GameEvents.OnScienceRecieved.Fire(credits, subject, pv, false);

				API.OnScienceReceived.Fire(credits, subject, pv, transmitted);
			}

			// return amount of science credited
			return credits;
		}

		// return module acting as container of an experiment
		public static IScienceDataContainer Container(Part p, string experiment_id)
		{
			// first try to get a stock experiment module with the right experiment id
			// - this support parts with multiple experiment modules, like eva kerbal
			foreach (ModuleScienceExperiment exp in p.FindModulesImplementing<ModuleScienceExperiment>())
			{
				if (exp.experimentID == experiment_id) return exp;
			}

			// if none was found, default to the first module implementing the science data container interface
			// - this support third-party modules that implement IScienceDataContainer, but don't derive from ModuleScienceExperiment
			return p.FindModuleImplementing<IScienceDataContainer>();
		}


		/// <summary>
		/// return the ExperimentInfo object corresponding to a subject_id, formatted as "experiment_id@situation"
		/// </summary>
		public static ExperimentVariant GetExperimentInfoFromSubject(string subject_id)
		{
			return GetExperimentInfo(ExperimentVariant.GetExperimentId(subject_id));
		}

		/// <summary>
		/// return the ExperimentInfo object corresponding to a "experiment_id"
		/// </summary>
		public static ExperimentVariant GetExperimentInfo(string experiment_id)
		{
			if (!exp_variants.ContainsKey(experiment_id))
			{
				Lib.Log("ERROR: No ExperimentInfo found for id " + experiment_id);
				return null;
			}
			return exp_variants[experiment_id];
		}




		#region Stored data cache

		// Rebuild the stored experiements data cache
		public static void UpdateStoredDataCache()
		{
			storedData.Clear();

			foreach (var drive in DB.drives.Values)
			{
				foreach (var file in drive.files) AddStoredData(file.Key, file.Value.size);
				foreach (var sample in drive.samples) AddStoredData(sample.Key, sample.Value.size);
			}
		}

		// Remove all data stored in a drive from the experiements data cache
		public static void ClearStoredDataInDrive(Drive drive)
		{
			foreach (string subject_id in drive.files.Keys) ClearStoredData(subject_id);
			foreach (string subject_id in drive.samples.Keys) ClearStoredData(subject_id);
		}

		// Get stored data amount in all vessels
		public static long GetStoredData(string subject_id)
		{ return storedData.ContainsKey(subject_id) ? storedData[subject_id] : 0; }

		// Add data amount to the stored experiements data cache
		public static void AddStoredData(string subject_id, long amount)
		{
			if (storedData.ContainsKey(subject_id))
				storedData[subject_id] += amount;
			else
				storedData.Add(subject_id, amount);
		}

		// Remove data amount to the stored experiements data cache
		public static void RemoveStoredData(string subject_id, long amount)
		{ if (storedData.ContainsKey(subject_id)) storedData[subject_id] = Math.Max(storedData[subject_id] - amount, 0); }

		// Remove all data for the experiement subject from the stored experiements data cache
		public static void ClearStoredData(string subject_id)
		{ storedData.Remove(subject_id); }

		#endregion

		#region Experiments utils


		// TODO : migrate to ExperimentVariant
		public static string RequirementText(string requirement)
		{
			var parts = Lib.Tokenize(requirement, ':');

			var condition = parts[0];
			string value = string.Empty;
			if (parts.Count > 1) value = parts[1];
						
			switch (condition)
			{
				case "OrbitMinInclination": return Lib.BuildString("Min. inclination ", value, "°");
				case "OrbitMaxInclination": return Lib.BuildString("Max. inclination ", value, "°");
				case "OrbitMinEccentricity": return Lib.BuildString("Min. eccentricity ", value);
				case "OrbitMaxEccentricity": return Lib.BuildString("Max. eccentricity ", value);
				case "OrbitMinArgOfPeriapsis": return Lib.BuildString("Min. argument of Pe ", value);
				case "OrbitMaxArgOfPeriapsis": return Lib.BuildString("Max. argument of Pe ", value);
				case "AltitudeMin": return Lib.BuildString("Min. altitude ", Lib.HumanReadableRange(Double.Parse(value)));
				case "AltitudeMax":
					var v = Double.Parse(value);
					if (v >= 0) return Lib.BuildString("Max. altitude ", Lib.HumanReadableRange(v));
					return Lib.BuildString("Min. depth ", Lib.HumanReadableRange(-v));
				case "RadiationMin": return Lib.BuildString("Min. radiation ", Lib.HumanReadableRadiation(Double.Parse(value)));
				case "RadiationMax": return Lib.BuildString("Max. radiation ", Lib.HumanReadableRadiation(Double.Parse(value)));
				case "Body": return PrettyBodyText(value);
				case "TemperatureMin": return Lib.BuildString("Min. temperature ", Lib.HumanReadableTemp(Double.Parse(value)));
				case "TemperatureMax": return Lib.BuildString("Max. temperature ", Lib.HumanReadableTemp(Double.Parse(value)));
				case "CrewMin": return Lib.BuildString("Min. crew ", value);
				case "CrewMax": return Lib.BuildString("Max. crew ", value);
				case "CrewCapacityMin": return Lib.BuildString("Min. crew capacity ", value);
				case "CrewCapacityMax": return Lib.BuildString("Max. crew capacity ", value);
				case "VolumePerCrewMin": return Lib.BuildString("Min. vol./crew ", Lib.HumanReadableVolume(double.Parse(value)));
				case "VolumePerCrewMax": return Lib.BuildString("Max. vol./crew ", Lib.HumanReadableVolume(double.Parse(value)));
				case "MaxAsteroidDistance": return Lib.BuildString("Max. asteroid distance ", Lib.HumanReadableRange(double.Parse(value)));

				case "AtmosphereBody": return "Body with atmosphere";
				case "AtmosphereAltMin": return Lib.BuildString("Min. atmosphere altitude ", value);
				case "AtmosphereAltMax": return Lib.BuildString("Max. atmosphere altitude ", value);
					
				case "SurfaceSpeedMin": return Lib.BuildString("Min. surface speed ", Lib.HumanReadableSpeed(double.Parse(value)));
				case "SurfaceSpeedMax": return Lib.BuildString("Max. surface speed ", Lib.HumanReadableSpeed(double.Parse(value)));
				case "VerticalSpeedMin": return Lib.BuildString("Min. vertical speed ", Lib.HumanReadableSpeed(double.Parse(value)));
				case "VerticalSpeedMax": return Lib.BuildString("Max. vertical speed ", Lib.HumanReadableSpeed(double.Parse(value)));
				case "SpeedMin": return Lib.BuildString("Min. speed ", Lib.HumanReadableSpeed(double.Parse(value)));
				case "SpeedMax": return Lib.BuildString("Max. speed ", Lib.HumanReadableSpeed(double.Parse(value)));
				case "DynamicPressureMin": return Lib.BuildString("Min. dynamic pressure ", Lib.HumanReadablePressure(double.Parse(value)));
				case "DynamicPressureMax": return Lib.BuildString("Max. dynamic pressure ", Lib.HumanReadablePressure(double.Parse(value)));
				case "StaticPressureMin": return Lib.BuildString("Min. pressure ", Lib.HumanReadablePressure(double.Parse(value)));
				case "StaticPressureMax": return Lib.BuildString("Max. pressure ", Lib.HumanReadablePressure(double.Parse(value)));
				case "AtmDensityMin": return Lib.BuildString("Min. atm. density ", Lib.HumanReadablePressure(double.Parse(value)));
				case "AtmDensityMax": return Lib.BuildString("Max. atm. density ", Lib.HumanReadablePressure(double.Parse(value)));
				case "AltAboveGroundMin": return Lib.BuildString("Min. ground altitude ", Lib.HumanReadableRange(double.Parse(value)));
				case "AltAboveGroundMax": return Lib.BuildString("Max. ground altitude ", Lib.HumanReadableRange(double.Parse(value)));

				case "MissionControlLevelMin": return Lib.BuildString(ScenarioUpgradeableFacilities.GetFacilityName(SpaceCenterFacility.MissionControl), " level ", value);
				case "MissionControlLevelMax": return Lib.BuildString(ScenarioUpgradeableFacilities.GetFacilityName(SpaceCenterFacility.MissionControl), " max. level ", value);
				case "AdministrationLevelMin": return Lib.BuildString(ScenarioUpgradeableFacilities.GetFacilityName(SpaceCenterFacility.Administration), " level ", value);
				case "AdministrationLevelMax": return Lib.BuildString(ScenarioUpgradeableFacilities.GetFacilityName(SpaceCenterFacility.Administration), " max. level ", value);
				case "TrackingStationLevelMin": return Lib.BuildString(ScenarioUpgradeableFacilities.GetFacilityName(SpaceCenterFacility.TrackingStation), " level ", value);
				case "TrackingStationLevelMax": return Lib.BuildString(ScenarioUpgradeableFacilities.GetFacilityName(SpaceCenterFacility.TrackingStation), " max. level ", value);
				case "AstronautComplexLevelMin": return Lib.BuildString(ScenarioUpgradeableFacilities.GetFacilityName(SpaceCenterFacility.AstronautComplex), " level ", value);
				case "AstronautComplexLevelMax": return Lib.BuildString(ScenarioUpgradeableFacilities.GetFacilityName(SpaceCenterFacility.AstronautComplex), " max. level ", value);

				case "Part": return Lib.BuildString("Needs part ", value);
				case "Module": return Lib.BuildString("Needs module ", value);

				default:
					return Lib.SpacesOnCaps(condition);
			}
		}

		public static string PrettyBodyText(string requires)
		{
			string result = "";
			foreach(var s in Lib.Tokenize(requires, ';'))
			{
				if (result.Length > 0) result += ", ";
				if (s[0] == '!') result += "not " + s.Substring(1);
				else result += s;
			}
			return result;
		}

		#endregion



		

		// experiment info 
		static readonly Dictionary<string, ExperimentVariant> exp_variants = new Dictionary<string, ExperimentVariant>();
		static readonly Dictionary<string, ExperimentInfo> exp_infos = new Dictionary<string, ExperimentInfo>();

		static readonly Dictionary<string, long> storedData = new Dictionary<string, long>();

	}

} // KERBALISM

