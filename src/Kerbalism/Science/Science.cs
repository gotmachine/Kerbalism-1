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
		private const double buffer_science_value = 0.4; // min. 0.01 value
		private const double min_buffer_size = 0.01; // min. 10kB

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

				// load EXPERIMENT_INFO nodes
				ConfigNode[] nodes = GameDatabase.Instance.GetConfigNodes("EXPERIMENT_INFO");
				for (int i = 0; i < nodes.Length ; i++)
				{
					ExperimentInfo exp_info = new ExperimentInfo(nodes[i]);
					if (!exp_infos.ContainsKey(exp_info.id))
						exp_infos.Add(exp_info.id, exp_info);
					else
						Lib.Log("WARNING : Duplicate EXPERIMENT_INFO '"+ exp_info.id + "' wasn't loaded");
				}
			}
		}

		private static Drive FindDrive(Vessel v, string filename)
		{
			foreach (var d in Drive.GetDrives(v, true))
			{
				if (d.files.ContainsKey(filename))
				{
					return d;
				}
			}
			return null;
		}

		// consume EC for transmission, and transmit science data
		public static void Update(Vessel v, Vessel_info vi, VesselData vd, Vessel_resources resources, double elapsed_s)
		{
			// do nothing if science system is disabled
			if (!Features.Science) return;



			// avoid corner-case when RnD isn't live during scene changes
			// - this avoid losing science if the buffer reach threshold during a scene change
			if (HighLogic.CurrentGame.Mode != Game.Modes.SANDBOX && ResearchAndDevelopment.Instance == null) return;


			/*
			- In experiment part module, create a "ExperimentProcess" object that contains :
				- module state related bools
				- issue string
				- remainingSampleMass
				- privateHdId
				- a reference to the ExperimentInfo object
				- a reference to the ExperimentResult object (can be null)
				- double data_pending
				- double data_consumed

			- In laboratory part module, create a "SampleProcess" object that contains :
				- lab enabled bool
				- a reference to the ExperimentInfo object
				- a reference to the sample ExperimentResult object (can be null)
				- a reference to the file ExperimentResult object (can be null)
				- double data_pending
				- double data_consumed

			- store these objects in two new dictionaries in the VesselData object accessible and persisted in DB.vessels 

			- on Science.Update :

				- foreach ExperimentProcess :
					- check the situation, update the issue string
					- if situation has changed, search for the corresponding ExperimentResult in drives
					- if manual mode, check if the size of all files for this ExperimentInfo is a multiple of the max_amount
					- if smart mode, check the science value left
					- check EC and resources availability -> how to clamp data production to actual resource availability ?
					- check that only one ExperimentProcess is running for the same ExperimentInfo
					- if all is OK
						- set ExperimentProcess.data_pending to the max potential data amount
						- add the ExperimentProcess to a "exp_process_pending" list

				- foreach SampleProcess :
					- check lab resources availability / crew conditions, update the issue string
					- if OK :
						- get the ExperimentResult sample to be processed
						- add the potential data amount to SampleProcess.data_pending
						- add the SampleProcess to a "sample_process_pending" list

				- get transmit_capacity, the data amount we can transmit

				- from the two pending lists (samples should be first ?)
					- get the one we want to transmit
					- substract the amount transmitted from the ExperimentProcess/SampleProcess.data_pending
					- substract the amount transmitted from transmit_capacity
					- add the amount transmitted to ExperimentProcess/SampleProcess.data_consumed
					- if the ExperimentResult is null, create it
					- update the ExperimentResult.transmit_buffer (formerly file.buff)
					- credit the science if the transmit_buffer has reached the buffer_science_value
					- if the max science value has been credited
						- if manual mode, set ExperimentProcess.running = false
						- delete the ExperimentResult object on the drive and set data_pending = 0
					- if there is still some transmit_capacity left, repeat with the next ExperimentProcess/SampleProcess

				- for each SampleProcess in sample_process_pending :
					- if smart mode, clamp data_pending to the data amount needed to get all science points
					- if manual mode :
						- search other drives for the same subject_id
						- clamp data pending + all the data already present for this subject_id to the next greater multiple of ExperimentInfo.data_max
					- if ExperimentResult is null find a drive and create it.
					- if space available on drive > data_pending,
						- data_consumed += data_pending;
						- add data_pending to the ExperimentResult
					- else :
						- store what is possible on this drive, keep track of data_consumed
						- find another drive, create a new file, repeat until data_pending = 0 or all drives are full.
					- substract data_consumed from the sample ExperimentResult.size, if size = 0 delete the ExperimentResult from the drive
					- consume EC according to the data_consumed/max data produced ratio

				- for each ExperimentProcess in exp_process_pending :
					- if smart mode, clamp data_pending to the data amount needed to get all science points
					- if manual mode :
						- search other drives for the same subject_id
						- clamp data pending + all the data already present for this subject_id to the next greater multiple of ExperimentInfo.data_max
					- if ExperimentResult is null find a drive and create it.
					- if space available on drive > data_pending,
						- data_consumed += data_pending;
						- ExperimentResult.size += data_pending
						- remove some ExperimentProcess.remainingSampleMass and add it to the ExperimentResult.mass
					- else :
						- store what is possible on this drive, keep track of data_consumed
						- find another drive, create a new file, repeat until data_pending = 0 or all drives are full.
					- consume EC and resources according to the data_consumed/max data produced ratio
			*/

			// get connection info
			ConnectionInfo conn = vi.connection;
			if (conn == null || String.IsNullOrEmpty(vi.transmitting)) return;

			// get filename of data being downloaded
			var exp_filename = vi.transmitting;

			var drive = FindDrive(v, exp_filename);

			// if some data is being downloaded
			// - avoid cornercase at scene changes
			if (exp_filename.Length > 0 && drive != null)
			{
				// get file
				File file = drive.files[exp_filename];

				// determine how much data is transmitted
				double transmitted = Math.Min(file.size, conn.rate * elapsed_s);

				// consume data in the file
				file.size -= transmitted;

				// accumulate in the buffer
				file.buff += transmitted;

				bool credit = file.size <= double.Epsilon;

				// this is the science value remaining for this experiment
				var remainingValue = Value(exp_filename, 0);

				// this is the science value of this sample
				var dataValue = Value(exp_filename, file.buff);

				if (!credit && file.buff > min_buffer_size) credit = dataValue > buffer_science_value;

				// if buffer is full, or file was transmitted completely
				if (credit)
				{
					var totalValue = TotalValue(exp_filename);

					// collect the science data
					Credit(exp_filename, file.buff, true, v.protoVessel);

					// reset the buffer
					file.buff = 0.0;

					// this was the last useful bit, there is no more value in the experiment
					if (remainingValue >= 0.1 && remainingValue - dataValue < 0.1)
					{
						
						Message.Post(
							Lib.BuildString(Lib.HumanReadableScience(totalValue), " ", Experiment(exp_filename).FullName(exp_filename), " completed"),
						  Lib.TextVariant(
								"Our researchers will jump on it right now",
								"There is excitement because of your findings",
								"The results are causing a brouhaha in R&D"
							));
					}
				}

				// if file was transmitted completely
				if (file.size <= double.Epsilon)
				{
					// remove the file
					drive.Delete_file(exp_filename);
				}
			}
		}

		// return name of file being transmitted from vessel specified
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
			var credits = KERBALISM.ExperimentInfo.Value(subject_id, size);

			// credit the science
			var subject = ResearchAndDevelopment.GetSubjectByID(subject_id);
			if(subject == null)
			{
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
		public static ExperimentInfo GetExperimentInfoFromSubject(string subject_id)
		{
			return GetExperimentInfo(ExperimentInfo.GetExperimentId(subject_id));
		}

		/// <summary>
		/// return the ExperimentInfo object corresponding to a "experiment_id"
		/// </summary>
		public static ExperimentInfo GetExperimentInfo(string experiment_id)
		{
			if (!exp_infos.ContainsKey(experiment_id))
			{
				Lib.Log("ERROR: No ExperimentInfo found for id " + experiment_id);
				return null;
			}
			return exp_infos[experiment_id];
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
		public static double GetStoredData(string subject_id)
		{ return storedData.ContainsKey(subject_id) ? storedData[subject_id] : 0; }

		// Add data amount to the stored experiements data cache
		public static void AddStoredData(string subject_id, double amount)
		{
			if (storedData.ContainsKey(subject_id))
				storedData[subject_id] += amount;
			else
				storedData.Add(subject_id, amount);
		}

		// Remove data amount to the stored experiements data cache
		public static void RemoveStoredData(string subject_id, double amount)
		{ if (storedData.ContainsKey(subject_id)) storedData[subject_id] = Math.Max(storedData[subject_id] - amount, 0); }

		// Remove all data for the experiement subject from the stored experiements data cache
		public static void ClearStoredData(string subject_id)
		{ storedData.Remove(subject_id); }

		#endregion

		#region Experiments utils
		// TODO : move those elsewhere. maybe ExperimentInfo ?

		public static string Generate_subject_id(string experiment_id, Vessel v)
		{
			var body = v.mainBody;
			ScienceExperiment experiment = ResearchAndDevelopment.GetExperiment(experiment_id);
			ExperimentSituation sit = GetExperimentSituation(v);

			var sitStr = sit.ToString();
			if(!string.IsNullOrEmpty(sitStr))
			{
				if (sit.BiomeIsRelevant(experiment))
					sitStr += ScienceUtil.GetExperimentBiome(v.mainBody, v.latitude, v.longitude);
			}

			// generate subject id
			return Lib.BuildString(experiment_id, "@", body.name, sitStr);
		}

		public static string Generate_subject(string experiment_id, Vessel v)
		{
			var subject_id = Generate_subject_id(experiment_id, v);

			// in sandbox, do nothing else
				if (ResearchAndDevelopment.Instance == null) return subject_id;

			// if the subject id was never added to RnD
			if (ResearchAndDevelopment.GetSubjectByID(subject_id) == null)
			{
				// get subjects container using reflection
				// - we tried just changing the subject.id instead, and
				//   it worked but the new id was obviously used only after
				//   putting RnD through a serialization->deserialization cycle
				var subjects = Lib.ReflectionValue<Dictionary<string, ScienceSubject>>
				(
				  ResearchAndDevelopment.Instance,
				  "scienceSubjects"
				);

				var experiment = ResearchAndDevelopment.GetExperiment(experiment_id);
				var sit = GetExperimentSituation(v);
				var biome = ScienceUtil.GetExperimentBiome(v.mainBody, v.latitude, v.longitude);
				float multiplier = Multiplier(v.mainBody, sit);
				var cap = multiplier * experiment.baseValue;

				// create new subject
				ScienceSubject subject = new ScienceSubject
				(
				  		subject_id,
						Lib.BuildString(experiment.experimentTitle, " (", Lib.SpacesOnCaps(sit + biome), ")"),
						experiment.dataScale,
				  		multiplier,
						cap
				);

				// add it to RnD
				subjects.Add(subject_id, subject);
			}

			return subject_id;
		}

		private static float Multiplier(CelestialBody body, ExperimentSituation sit)
		{
			return sit.Multiplier(body);
		}



		public static ExperimentSituation GetExperimentSituation(Vessel v)
		{
			return new ExperimentSituation(v);
		}

		private static bool TestBody(string bodyName, string requirement)
		{
			foreach(string s in Lib.Tokenize(requirement, ';'))
			{
				if (s == bodyName) return true;
				if(s[0] == '!' && s.Substring(1) == bodyName) return false;
			}
			return false;
		}



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

		//public static void RegisterSampleMass(string experiment_id, double sampleMass)
		//{
		//	// get experiment id out of subject id
		//	int i = experiment_id.IndexOf('@');
		//	var id = i > 0 ? experiment_id.Substring(0, i) : experiment_id;

		//	if (sampleMasses.ContainsKey(id))
		//	{
		//		if (Math.Abs(sampleMasses[id] - sampleMass) > double.Epsilon)
		//			Lib.Log("Science Warning: different sample masses for Experiment " + id + " defined.");
		//	}
		//	else
		//	{
		//		sampleMasses.Add(id, sampleMass);
		//		Lib.Log("Science: registered sample mass for " + id + ": " + sampleMass.ToString("F3"));
		//	}
		//}

		//public static double GetSampleMass(string experiment_id)
		//{
		//	// get experiment id out of subject id
		//	int i = experiment_id.IndexOf('@');
		//	var id = i > 0 ? experiment_id.Substring(0, i) : experiment_id;

		//	if (!sampleMasses.ContainsKey(id)) return 0;
		//	return sampleMasses[id];
		//}

		

		// experiment info 
		static readonly Dictionary<string, ExperimentInfo> exp_infos = new Dictionary<string, ExperimentInfo>();

		static readonly Dictionary<string, double> storedData = new Dictionary<string, double>();

	}

} // KERBALISM

