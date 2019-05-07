using System;
using System.Collections.Generic;
using UnityEngine;
using KSP.Localization;


namespace KERBALISM
{

	public class kerbalismSurveyor : ModuleOrbitalSurveyor
	{
		protected override void sendDataToComms()
		{
			base.sendDataToComms();
		}
	}

	/// <summary>
	/// Stores information about an experiment and provide various static methods related to experiments and subjects
	/// </summary>
	public sealed class ExperimentInfo
	{
		public ExperimentInfo(ConfigNode node)
		{
			id = Lib.ConfigValue(node, "id", "");
			exp_def_id = Lib.ConfigValue(node, "exp_def_id", "");
			experiment_desc = Lib.ConfigValue(node, "experiment_desc", string.Empty);
			data_rate = Lib.ConfigValue(node, "data_rate", 0.01f);
			ec_rate = Lib.ConfigValue(node, "ec_rate", 0.01f);
			sample_mass = Lib.ConfigValue(node, "sample_mass", 0f);
			sample_collecting = Lib.ConfigValue(node, "sample_collecting", false);
			allow_shrouded = Lib.ConfigValue(node, "allow_shrouded", true);
			requires = Lib.ConfigValue(node, "requires", string.Empty);
			crew_operate = Lib.ConfigValue(node, "crew_operate", string.Empty);
			crew_reset = Lib.ConfigValue(node, "crew_reset", string.Empty);
			crew_prepare = Lib.ConfigValue(node, "crew_prepare", string.Empty);
			resources = Lib.ConfigValue(node, "resources", string.Empty);



			// get experiment definition
			// - available even in sandbox
			try
			{
				exp_def = ResearchAndDevelopment.GetExperiment(exp_def_id);
			}
			catch (Exception e)
			{
				Lib.Log("ERROR: failed to load EXPERIMENT_INFO '" + id + "', could not get EXPERIMENT_DEFINITION '" + exp_def_id + "': " + e.Message);
				throw e;
			}

			if (exp_def == null)
			{
				Lib.Log("ERROR: failed to load EXPERIMENT_INFO '" + id + "', could not get EXPERIMENT_DEFINITION '" + exp_def_id + "'");
				return;
			}

			// data_max is used everywhere, do the math once and for all
			data_max = exp_def.baseValue * exp_def.dataScale;

			// parse requirements to an array for fast checking
			List<Requirement> temp_reqs = new List<Requirement>();
			foreach (string s in requires.Split(','))
			{
				s.Trim();
				string[] key_value = s.Split(':');
				if (key_value.Length > 0)
				{
					key_value[0].Trim();
					if (key_value.Length > 1)
					{
						key_value[1].Trim();
						if (key_value[0] == "Body")
							// parse body allowed/restricted subvalues to a string array
							temp_reqs.Add(ParseRequireBodies(key_value[1]));
						else
							temp_reqs.Add(ParseRequiresValues(key_value[0], key_value[1]));
					}
					else
					{
						temp_reqs.Add(new Requirement(key_value[0], key_value[0], null));
					}
				}
			}
			reqs = temp_reqs.ToArray();
		}

		public string SubjectName(string subject_id)
		{
			return Lib.BuildString(title, " (", Situation(subject_id), ")");
		}

		public bool IsSample()
		{
			return !(sample_mass == 0);
		}

		/// <summary>
		/// returns a list of all possible situations for this experiment
		/// </summary>
		public List<string> Situations()
		{
			List<string> result = new List<string>();
			string s;
			s = MaskToString("Landed", 1, exp_def.situationMask, exp_def.biomeMask); if (!string.IsNullOrEmpty(s)) result.Add(s);
			s = MaskToString("Splashed", 2, exp_def.situationMask, exp_def.biomeMask); if (!string.IsNullOrEmpty(s)) result.Add(s);
			s = MaskToString("Flying Low", 4, exp_def.situationMask, exp_def.biomeMask); if (!string.IsNullOrEmpty(s)) result.Add(s);
			s = MaskToString("Flying High", 8, exp_def.situationMask, exp_def.biomeMask); if (!string.IsNullOrEmpty(s)) result.Add(s);
			s = MaskToString("In Space Low", 16, exp_def.situationMask, exp_def.biomeMask); if (!string.IsNullOrEmpty(s)) result.Add(s);
			s = MaskToString("In Space High", 32, exp_def.situationMask, exp_def.biomeMask); if (!string.IsNullOrEmpty(s)) result.Add(s);
			return result;
		}

		public List<KeyValuePair<string, double>> ParseResources(bool logErrors = false)
		{
			var reslib = PartResourceLibrary.Instance.resourceDefinitions;

			List<KeyValuePair<string, double>> defs = new List<KeyValuePair<string, double>>();
			foreach (string s in Lib.Tokenize(resources, ','))
			{
				// definitions are Resource@rate
				var p = Lib.Tokenize(s, '@');
				if (p.Count != 2) continue;             // malformed definition
				string res = p[0];
				if (!reslib.Contains(res)) continue;    // unknown resource
				double rate = double.Parse(p[1]);
				if (res.Length < 1 || rate < double.Epsilon) continue;  // rate <= 0
				defs.Add(new KeyValuePair<string, double>(res, rate));
			}
			return defs;
		}



		public string TestRequirements(Vessel v)
		{
			CelestialBody body = v.mainBody;
			Vessel_info vi = Cache.VesselInfo(v);

			bool good = true;
			for (int i = 0; i < reqs.Length; i++)
			{
				// testing for condition = false
				switch (reqs[i].key)
				{
					case "OrbitMinInclination":	if (!(v.orbit.inclination >= (double)reqs[i].value))
							return Lib.BuildString("Min. inclination ", reqs[i].value_str, "°"); break;


					case "OrbitMaxInclination": if (!(v.orbit.inclination <= double.Parse(value); break;
					case "OrbitMinEccentricity": if (!(v.orbit.eccentricity >= double.Parse(value); break;
					case "OrbitMaxEccentricity": if (!(v.orbit.eccentricity <= double.Parse(value); break;
					case "OrbitMinArgOfPeriapsis": if (!(v.orbit.argumentOfPeriapsis >= double.Parse(value); break;
					case "OrbitMaxArgOfPeriapsis": if (!(v.orbit.argumentOfPeriapsis <= double.Parse(value); break;

					case "TemperatureMin": 
                        if (!(vi.temperature >= double.Parse(value); break;
					case "TemperatureMax": 
                        if (!(vi.temperature <= double.Parse(value); break;
					case "AltitudeMin": 
                        if (!(v.altitude >= double.Parse(value); break;
					case "AltitudeMax": 
                        if (!(v.altitude <= double.Parse(value); break;
					case "RadiationMin": 
                        if (!(vi.radiation >= double.Parse(value); break;
					case "RadiationMax": 
                        if (!(vi.radiation <= double.Parse(value); break;
					case "Microgravity": 
                        if (!(vi.zerog; break;
					case "Body": 
                        if (!(TestBody(v.mainBody.name, value); break;
					case "Shadow": 
                        if (!(vi.sunlight < double.Epsilon; break;
					case "Sunlight": 
                        if (!(vi.sunlight > 0.5; break;
					case "CrewMin": 
                        if (!(vi.crew_count >= int.Parse(value); break;
					case "CrewMax": 
                        if (!(vi.crew_count <= int.Parse(value); break;
					case "CrewCapacityMin": 
                        if (!(vi.crew_capacity >= int.Parse(value); break;
					case "CrewCapacityMax": 
                        if (!(vi.crew_capacity <= int.Parse(value); break;
					case "VolumePerCrewMin": 
                        if (!(vi.volume_per_crew >= double.Parse(value); break;
					case "VolumePerCrewMax": 
                        if (!(vi.volume_per_crew <= double.Parse(value); break;
					case "Greenhouse": 
                        if (!(vi.greenhouses.Count > 0; break;
					case "Surface": 
                        if (!(Lib.Landed(v); break;
					case "Atmosphere": 
                        if (!(body.atmosphere && v.altitude < body.atmosphereDepth; break;
					case "AtmosphereBody": 
                        if (!(body.atmosphere; break;
					case "AtmosphereAltMin": 
                        if (!(body.atmosphere && (v.altitude / body.atmosphereDepth) >= double.Parse(value); break;
					case "AtmosphereAltMax": 
                        if (!(body.atmosphere && (v.altitude / body.atmosphereDepth) <= double.Parse(value); break;

					case "SunAngleMin": 
                        if (!(Lib.SunBodyAngle(v) >= double.Parse(value); break;
					case "SunAngleMax": 
                        if (!(Lib.SunBodyAngle(v) <= double.Parse(value); break;

					case "Vacuum": 
                        if (!(!body.atmosphere || v.altitude > body.atmosphereDepth; break;
					case "Ocean": 
                        if (!(body.ocean && v.altitude < 0.0; break;
					case "PlanetarySpace": 
                        if (!(body.flightGlobalsIndex != 0 && !Lib.Landed(v) && v.altitude > body.atmosphereDepth; break;
					case "AbsoluteZero": 
                        if (!(vi.temperature < 30.0; break;
					case "InnerBelt": 
                        if (!(vi.inner_belt; break;
					case "OuterBelt": 
                        if (!(vi.outer_belt; break;
					case "MagneticBelt": 
                        if (!(vi.inner_belt || vi.outer_belt; break;
					case "Magnetosphere": 
                        if (!(vi.magnetosphere; break;
					case "Thermosphere": 
                        if (!(vi.thermosphere; break;
					case "Exosphere": 
                        if (!(vi.exosphere; break;
					case "InterPlanetary": 
                        if (!(body.flightGlobalsIndex == 0 && !vi.interstellar; break;
					case "InterStellar": 
                        if (!(body.flightGlobalsIndex == 0 && vi.interstellar; break;

					case "SurfaceSpeedMin": 
                        if (!(v.srfSpeed >= double.Parse(value); break;
					case "SurfaceSpeedMax": 
                        if (!(v.srfSpeed <= double.Parse(value); break;
					case "VerticalSpeedMin": 
                        if (!(v.verticalSpeed >= double.Parse(value); break;
					case "VerticalSpeedMax": good = v.verticalSpeed <= double.Parse(value); break;
					case "SpeedMin": good = v.speed >= double.Parse(value); break;
					case "SpeedMax": good = v.speed <= double.Parse(value); break;
					case "DynamicPressureMin": good = v.dynamicPressurekPa >= double.Parse(value); break;
					case "DynamicPressureMax": good = v.dynamicPressurekPa <= double.Parse(value); break;
					case "StaticPressureMin": good = v.staticPressurekPa >= double.Parse(value); break;
					case "StaticPressureMax": good = v.staticPressurekPa <= double.Parse(value); break;
					case "AtmDensityMin": good = v.atmDensity >= double.Parse(value); break;
					case "AtmDensityMax": good = v.atmDensity <= double.Parse(value); break;
					case "AltAboveGroundMin": good = v.heightFromTerrain >= double.Parse(value); break;
					case "AltAboveGroundMax": good = v.heightFromTerrain <= double.Parse(value); break;

					case "Part": good = Lib.HasPart(v, value); break;
					case "Module": good = Lib.FindModules(v.protoVessel, value).Count > 0; break;

					case "AstronautComplexLevelMin":
						good = !ScenarioUpgradeableFacilities.Instance.enabled || ScenarioUpgradeableFacilities.GetFacilityLevel(SpaceCenterFacility.AstronautComplex) >= (double.Parse(value) - 1) / 2.0;
						break;
					case "AstronautComplexLevelMax":
						good = !ScenarioUpgradeableFacilities.Instance.enabled || ScenarioUpgradeableFacilities.GetFacilityLevel(SpaceCenterFacility.AstronautComplex) <= (double.Parse(value) - 1) / 2.0;
						break;

					case "TrackingStationLevelMin":
						good = !ScenarioUpgradeableFacilities.Instance.enabled || ScenarioUpgradeableFacilities.GetFacilityLevel(SpaceCenterFacility.TrackingStation) >= (double.Parse(value) - 1) / 2.0;
						break;
					case "TrackingStationLevelMax":
						good = !ScenarioUpgradeableFacilities.Instance.enabled || ScenarioUpgradeableFacilities.GetFacilityLevel(SpaceCenterFacility.TrackingStation) <= (double.Parse(value) - 1) / 2.0;
						break;

					case "MissionControlLevelMin":
						good = !ScenarioUpgradeableFacilities.Instance.enabled || ScenarioUpgradeableFacilities.GetFacilityLevel(SpaceCenterFacility.MissionControl) >= (double.Parse(value) - 1) / 2.0;
						break;
					case "MissionControlLevelMax":
						good = !ScenarioUpgradeableFacilities.Instance.enabled || ScenarioUpgradeableFacilities.GetFacilityLevel(SpaceCenterFacility.MissionControl) <= (double.Parse(value) - 1) / 2.0;
						break;

					case "AdministrationLevelMin":
						good = !ScenarioUpgradeableFacilities.Instance.enabled || ScenarioUpgradeableFacilities.GetFacilityLevel(SpaceCenterFacility.Administration) >= (double.Parse(value) - 1) / 2.0;
						break;
					case "AdministrationLevelMax":
						good = !ScenarioUpgradeableFacilities.Instance.enabled || ScenarioUpgradeableFacilities.GetFacilityLevel(SpaceCenterFacility.Administration) <= (double.Parse(value) - 1) / 2.0;
						break;

					case "MaxAsteroidDistance": good = AsteroidDistance(v) <= double.Parse(value); break;
				}

				if (!good) return reqs[i];
			}


			var sit = new ExperimentSituation(v);
			if (!sit.IsAvailable(exp_def))
				return new KeyValuePair<string, string>("Invalid situation", string.Empty);

			return new KeyValuePair<string, string>("Invalid situation", string.Empty);

		}

		private bool TestBody(string[] body_reqs, string body_name)
		{
			for (int i = 0; i < body_reqs.Length; i++)
				if (body_reqs[i] == body_name) return true;
			return false;
		}

		private Requirement ParseRequireBodies(string body_list)
		{
			List<string> bodies = new List<string>();
			string body_key = string.Empty;
			foreach (string vb in body_list.Split(';'))
			{
				vb.Trim();
				if (vb[0] == '!' && body_key != "AllowedBodies")
				{
					if (body_key == string.Empty) body_key = "RestrictedBodies";
					bodies.Add(vb.Substring(1));
				}
				else if (body_key != "RestrictedBodies")
				{
					if (body_key == string.Empty) body_key = "AllowedBodies";
					bodies.Add(vb);
				}
			}
			return new Requirement(body_key, string.Empty, bodies.ToArray());
		}

		private Requirement ParseRequiresValues(string key, string value)
		{
			switch (key)
			{
				case "OrbitMinInclination":
				case "OrbitMaxInclination":
				case "OrbitMinEccentricity":
				case "OrbitMaxEccentricity":
				case "OrbitMinArgOfPeriapsis":
				case "OrbitMaxArgOfPeriapsis":
				case "TemperatureMin":
				case "TemperatureMax":
				case "AltitudeMin":
				case "AltitudeMax":
				case "RadiationMin":
				case "RadiationMax":
				case "VolumePerCrewMin":
				case "VolumePerCrewMax":
				case "AtmosphereAltMin":
				case "AtmosphereAltMax":
				case "SunAngleMin":
				case "SunAngleMax":
				case "SurfaceSpeedMin":
				case "SurfaceSpeedMax":
				case "VerticalSpeedMin":
				case "VerticalSpeedMax":
				case "SpeedMin":
				case "SpeedMax":
				case "DynamicPressureMin":
				case "DynamicPressureMax":
				case "StaticPressureMin":
				case "StaticPressureMax":
				case "AtmDensityMin":
				case "AtmDensityMax":
				case "AltAboveGroundMin":
				case "AltAboveGroundMax":
				case "MaxAsteroidDistance":
				case "AstronautComplexLevelMin":
				case "AstronautComplexLevelMax":
				case "TrackingStationLevelMin":
				case "TrackingStationLevelMax":
				case "MissionControlLevelMin":
				case "MissionControlLevelMax":
				case "AdministrationLevelMin":
				case "AdministrationLevelMax":
					return new Requirement(key, value, double.Parse(value));
				case "CrewMin":
				case "CrewMax":
				case "CrewCapacityMin":
				case "CrewCapacityMax":
					return new Requirement(key, value, int.Parse(value));
				default:
					return new Requirement(key, value, value);
			}
		}



		/// <summary>
		/// Get experiment id from a full subject id
		/// </summary>
		public static string GetExperimentId(string subject_id)
		{
			int i = subject_id.IndexOf('@');
			return i > 0 ? subject_id.Substring(0, i) : subject_id;
		}

		/// <summary>
		/// return total value of some data about a subject, in science credits
		/// </summary>
		public static float TotalValue(string subject_id)
		{
			var exp = Science.GetExperimentInfoFromSubject(subject_id);
			var size = exp.data_max;

			// get science subject
			// - if null, we are in sandbox mode
			var subject = ResearchAndDevelopment.GetSubjectByID(subject_id);
			if (subject == null) return 0.0f;

			float credits = ResearchAndDevelopment.GetReferenceDataValue((float)size, subject);
			credits *= HighLogic.CurrentGame.Parameters.Career.ScienceGainMultiplier;

			return credits;
		}

		/// <summary>
		/// return remaining collectable value of some data about a subject, in science credits
		/// </summary>
		/// <param name="size">use non-zero value to get the science value of a subject of the specific data size</param>
		/// <param name="includeStoredInDrives">include value for the data currently stored in all vessels drives but not yet recovered or transmitted</param>
		public static float Value(string subject_id, double size = 0, bool includeStoredInDrives = false)
		{
			if (size < double.Epsilon)
			{
				var exp = Science.GetExperimentInfoFromSubject(subject_id);
				size = includeStoredInDrives ? Math.Max(exp.data_max - Science.GetStoredData(subject_id), 0) : exp.data_max;
			}

			// get science subject
			// - if null, we are in sandbox mode
			var subject = ResearchAndDevelopment.GetSubjectByID(subject_id);
			if (subject == null) return 0.0f;

			// get science value
			// - the stock system 'degrade' science value after each credit, we don't
			double R = ResearchAndDevelopment.GetReferenceDataValue((float)size, subject);

			double S = subject.science;
			double C = subject.scienceCap;
			double credits = Math.Max(Math.Min(S + Math.Min(R, C), C) - S, 0.0);

			credits *= HighLogic.CurrentGame.Parameters.Career.ScienceGainMultiplier;

			return (float)credits;
		}

		/// <summary>
		/// returns  a pretty printed situation description for the UI
		/// </summary>
		public static string Situation(string subject_id)
		{
			int i = subject_id.IndexOf('@');
			var situation = subject_id.Length < i + 2
				? Localizer.Format("#KERBALISM_ExperimentInfo_Unknown")
				: Lib.SpacesOnCaps(subject_id.Substring(i + 1));
			situation = situation.Replace("Srf ", string.Empty).Replace("In ", string.Empty);
			return situation;
		}

		private double AsteroidDistance(Vessel vessel)
		{
			var target = vessel.targetObject;
			var vesselPosition = Lib.VesselPosition(vessel);

			// while there is a target, only consider the targeted vessel
			if (!vessel.loaded || target != null)
			{
				// asteroid MUST be the target if vessel is unloaded
				if (target == null) return double.MaxValue;

				var targetVessel = target.GetVessel();
				if (targetVessel == null) return double.MaxValue;

				if (targetVessel.vesselType != VesselType.SpaceObject) return double.MaxValue;

				// this assumes that all vessels of type space object are asteroids.
				// should be a safe bet unless Squad introduces alien UFOs.
				var asteroidPosition = Lib.VesselPosition(targetVessel);
				return Vector3d.Distance(vesselPosition, asteroidPosition);
			}

			// there's no target and vessel is not unloaded
			// look for nearby asteroids
			double result = double.MaxValue;
			foreach (Vessel v in FlightGlobals.VesselsLoaded)
			{
				if (v.vesselType != VesselType.SpaceObject) continue;
				var asteroidPosition = Lib.VesselPosition(v);
				double distance = Vector3d.Distance(vesselPosition, asteroidPosition);
				if (distance < result) result = distance;
			}
			return result;
		}



		private static string MaskToString(string text, uint flag, uint situationMask, uint biomeMask)
		{
			string result = string.Empty;
			if ((flag & situationMask) == 0) return result;
			result = text;
			if ((flag & biomeMask) != 0) result += " (Biomes)";
			return result;
		}

		/// <summary>stock experiment definition</summary>
		public ScienceExperiment exp_def { get; private set; }

		/// <summary>title of the experiment. Try to keep short.</summary>
		//public string title { get; private set; }

		/// <summary>CFG : unique id to be used in the partmodule "exp_info_id"</summary>
		public string id { get; private set; }

		/// <summary>CFG : id of the EXPERIMENT_DEFINITION that will be registered as a result</summary>
		public string exp_def_id { get; private set; }

		/// <summary>CFG : optional, some nice lines of text</summary>
		public string experiment_desc { get; private set; }

		/// <summary>= baseValue * dataScale. Data amount for a complete result in Mb. For sample, 1 slot = 1024 Mb.</summary>
		public double data_max { get; private set; }

		/// <summary>CFG : data production rate in Mb/s. For sample, 1 slot = 1024 Mb.</summary>
		public double data_rate { get; private set; }

		/// <summary>CFG : EC consumption rate per-second</summary>
		public double ec_rate { get; private set; }

		/// <summary> CFG : if set to anything but 0, the experiment can be a sample</summary>
		public float sample_mass { get; private set; }

		/// <summary>CFG : if set to true, the experiment will generate mass out of nothing</summary>
		public bool sample_collecting { get; private set; }

		/// <summary>CFG : true if experiment can be run while shrouded</summary>
		public bool allow_shrouded { get; private set; }

		/// <summary>CFG : optional, additional requirements that must be met</summary>
		public string requires { get; private set; }

		/// <summary>CFG : optional, operator crew. if set, crew has to be on vessel while recording</summary>
		public string crew_operate { get; private set; }

		/// <summary>CFG : optional, reset crew. if set, experiment will stop recording after situation change</summary>
		public string crew_reset { get; private set; }

		/// <summary>CFG : optional, prepare crew. if set, experiment will require crew to set up before it can start recording</summary>
		public string crew_prepare { get; private set; }

		/// <summary>CFG : optional, resources consumed by this experiment</summary>
		public string resources { get; private set; }

		// not ideal but least we won't be parsing everything all the time
		private Requirement[] reqs;

		private class Requirement
		{
			public string key;
			public string value_str;
			public object value;

			public Requirement(string key, string value_str, object value)
			{
				this.key = key;
				this.value_str = value_str;
				this.value = value;
			}
		}




		// TODO : this is a stub showing how we should register experiments in the RnD instance
		//public static void AddExperimentToRnD(ExperimentInfo exp_info)
		//{
		//	var experiments = Lib.ReflectionValue<Dictionary<string, ScienceExperiment>>
		//	(
		//	  ResearchAndDevelopment.Instance,
		//	  "experiments"
		//	);

		//	var exp = new ScienceExperiment();
		//	exp.baseValue = exp_info.baseValue;
		//	exp.dataScale = exp_info.dataScale;
		//	...

		//	experiments.Add(exp_info.id, exp);
		//}
	}

} // KERBALISM

