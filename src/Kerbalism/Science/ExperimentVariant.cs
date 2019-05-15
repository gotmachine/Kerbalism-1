using System;
using System.Collections.Generic;
using UnityEngine;
using KSP.Localization;


namespace KERBALISM
{

	/// <summary>
	/// Stores information about an experiment and provide various static methods related to experiments and subjects
	/// </summary>
	public sealed class ExperimentVariant
	{
		#region public methods

		public ExperimentVariant(ConfigNode node)
		{
			id = Lib.ConfigValue(node, "id", "");
			exp_def_id = Lib.ConfigValue(node, "exp_def_id", "");
			experiment_desc = Lib.ConfigValue(node, "experiment_desc", string.Empty);

			ec_rate = Lib.ConfigValue(node, "ec_rate", 0.01f);
			sample_mass = Lib.ConfigValue(node, "sample_mass", 0f);
			sample_collecting = Lib.ConfigValue(node, "sample_collecting", false);
			allow_shrouded = Lib.ConfigValue(node, "allow_shrouded", true);
			requires = Lib.ConfigValue(node, "requires", string.Empty);
			crew_operate = Lib.ConfigValue(node, "crew_operate", string.Empty);
			crew_reset = Lib.ConfigValue(node, "crew_reset", string.Empty);
			crew_prepare = Lib.ConfigValue(node, "crew_prepare", string.Empty);
			resources = Lib.ConfigValue(node, "resources", string.Empty);

			double MBrate = Lib.ConfigValue(node, "data_rate", 0.01);
			data_rate = Lib.MBToBit(MBrate);

			// get experiment definition
			// - available even in sandbox
			try
			{
				exp_info = ResearchAndDevelopment.GetExperiment(exp_def_id);
			}
			catch (Exception e)
			{
				Lib.Log("ERROR: failed to load EXPERIMENT_VARIANT '" + id + "', could not get EXPERIMENT_DEFINITION '" + exp_def_id + "': " + e.Message);
				throw e;
			}

			if (exp_info == null)
			{
				Lib.Log("ERROR: failed to load EXPERIMENT_VARIANT '" + id + "', could not get EXPERIMENT_DEFINITION '" + exp_def_id + "'");
				return;
			}

			// data_max is used everywhere, do the math once and for all
			data_max = exp_info.baseValue * exp_info.dataScale;

			// parse requirements
			ParseRequirements();

			// parse resources
			ParseResources();
		}

		public string SubjectName(string subject_id)
		{
			return Lib.BuildString(title, " (", Situation(subject_id), ")");
		}




		// TODO : REQUIREMENTS SCALAR EVALUATION
		// change this to return a List<KeyValuePair<string, double>>, with double being a [0,1] scalar
		// For some fast changing requirements whose checking reliability is affected by timewarp this would allow scaling the data generation
		// main example is Sunlight/Shadow, but we probably can do something about altitude, speed and sun based reqs
		// radiation and temperature based ones will be much more complicated to deal with
		// An "easy" way would be to split the last sim step into substeps, and check the validity at each substep
		// That would give us a "during last step, condition was valid for X sec" from which we can return a scalar
		// Needless to say, depending on the precision of the substeps this could be a huge performance issue
		// but maybe not too much if only we do this "on demand" and do some smart caching of the substeps
		public List<string> TestRequirements(Vessel v)
		{

			Vessel_info vi = Cache.VesselInfo(v);
			List<string> invalid_reqs = new List<string>();
			bool good = true;
			for (int i = 0; i < req_values.Length; i++)
			{
				// testing for condition = false
				switch (req_values[i].key)
				{
					case "OrbitMinInclination": good = v.orbit.inclination >= (double)req_values[i].value; break;
					case "OrbitMaxInclination": good = v.orbit.inclination <= (double)req_values[i].value; break;
					case "OrbitMinEccentricity": good = v.orbit.eccentricity >= (double)req_values[i].value; break;
					case "OrbitMaxEccentricity": good = v.orbit.eccentricity <= (double)req_values[i].value; break;
					case "OrbitMinArgOfPeriapsis": good = v.orbit.argumentOfPeriapsis >= (double)req_values[i].value; break;
					case "OrbitMaxArgOfPeriapsis": good = v.orbit.argumentOfPeriapsis <= (double)req_values[i].value; break;

					case "TemperatureMin": good = vi.temperature >= (double)req_values[i].value; break;
					case "TemperatureMax": good = vi.temperature <= (double)req_values[i].value; break;
					case "AltitudeMin": good = v.altitude >= (double)req_values[i].value; break;
					case "AltitudeMax": good = v.altitude <= (double)req_values[i].value; break;
					case "RadiationMin": good = vi.radiation >= (double)req_values[i].value; break;
					case "RadiationMax": good = vi.radiation <= (double)req_values[i].value; break;
					case "Microgravity": good = vi.zerog; break;
					case "AllowedBodies": good = TestBody((string[])req_values[i].value, v.mainBody.name); break;
					case "RestrictedBodies": good = !TestBody((string[])req_values[i].value, v.mainBody.name); break;
					case "Shadow": good = vi.sunlight < double.Epsilon; break;
					case "Sunlight": good = vi.sunlight > 0.5; break;
					case "CrewMin": good = vi.crew_count >= (int)req_values[i].value; break;
					case "CrewMax": good = vi.crew_count <= (int)req_values[i].value; break;
					case "CrewCapacityMin": good = vi.crew_capacity >= (int)req_values[i].value; break;
					case "CrewCapacityMax": good = vi.crew_capacity <= (int)req_values[i].value; break;
					case "VolumePerCrewMin": good = vi.volume_per_crew >= (double)req_values[i].value; break;
					case "VolumePerCrewMax": good = vi.volume_per_crew <= (double)req_values[i].value; break;
					case "Greenhouse": good = vi.greenhouses.Count > 0; break;
					case "Surface": good = Lib.Landed(v); break;
					case "Atmosphere": good = v.mainBody.atmosphere && v.altitude < v.mainBody.atmosphereDepth; break;
					case "AtmosphereBody": good = v.mainBody.atmosphere; break;
					case "AtmosphereAltMin": good = v.mainBody.atmosphere && (v.altitude / v.mainBody.atmosphereDepth) >= (double)req_values[i].value; break;
					case "AtmosphereAltMax": good = v.mainBody.atmosphere && (v.altitude / v.mainBody.atmosphereDepth) <= (double)req_values[i].value; break;

					case "SunAngleMin": good = Lib.SunBodyAngle(v) >= (double)req_values[i].value; break;
					case "SunAngleMax": good = Lib.SunBodyAngle(v) <= (double)req_values[i].value; break;

					case "Vacuum": good = !v.mainBody.atmosphere || v.altitude > v.mainBody.atmosphereDepth; break;
					case "Ocean": good = v.mainBody.ocean && v.altitude < 0.0; break;
					case "PlanetarySpace": good = v.mainBody.flightGlobalsIndex != 0 && !Lib.Landed(v) && v.altitude > v.mainBody.atmosphereDepth; break;
					case "AbsoluteZero": good = vi.temperature < 30.0; break;
					case "InnerBelt": good = vi.inner_belt; break;
					case "OuterBelt": good = vi.outer_belt; break;
					case "MagneticBelt": good = vi.inner_belt || vi.outer_belt; break;
					case "Magnetosphere": good = vi.magnetosphere; break;
					case "Thermosphere": good = vi.thermosphere; break;
					case "Exosphere": good = vi.exosphere; break;
					case "InterPlanetary": good = v.mainBody.flightGlobalsIndex == 0 && !vi.interstellar; break;
					case "InterStellar": good = v.mainBody.flightGlobalsIndex == 0 && vi.interstellar; break;

					case "SurfaceSpeedMin": good = v.srfSpeed >= (double)req_values[i].value; break;
					case "SurfaceSpeedMax": good = v.srfSpeed <= (double)req_values[i].value; break;
					case "VerticalSpeedMin": good = v.verticalSpeed >= (double)req_values[i].value; break;
					case "VerticalSpeedMax": good = v.verticalSpeed <= (double)req_values[i].value; break;
					case "SpeedMin": good = v.speed >= (double)req_values[i].value; break;
					case "SpeedMax": good = v.speed <= (double)req_values[i].value; break;
					case "DynamicPressureMin": good = v.dynamicPressurekPa >= (double)req_values[i].value; break;
					case "DynamicPressureMax": good = v.dynamicPressurekPa <= (double)req_values[i].value; break;
					case "StaticPressureMin": good = v.staticPressurekPa >= (double)req_values[i].value; break;
					case "StaticPressureMax": good = v.staticPressurekPa <= (double)req_values[i].value; break;
					case "AtmDensityMin": good = v.atmDensity >= (double)req_values[i].value; break;
					case "AtmDensityMax": good = v.atmDensity <= (double)req_values[i].value; break;
					case "AltAboveGroundMin": good = v.heightFromTerrain >= (double)req_values[i].value; break;
					case "AltAboveGroundMax": good = v.heightFromTerrain <= (double)req_values[i].value; break;

					case "Part": good = Lib.HasPart(v, (string)req_values[i].value); break;
					case "Module": good = Lib.FindModules(v.protoVessel, (string)req_values[i].value).Count > 0; break;

					case "AstronautComplexLevelMin":
						good = !ScenarioUpgradeableFacilities.Instance.enabled || ScenarioUpgradeableFacilities.GetFacilityLevel(SpaceCenterFacility.AstronautComplex) >= ((double)req_values[i].value - 1) / 2.0;
						break;
					case "AstronautComplexLevelMax":
						good = !ScenarioUpgradeableFacilities.Instance.enabled || ScenarioUpgradeableFacilities.GetFacilityLevel(SpaceCenterFacility.AstronautComplex) <= ((double)req_values[i].value - 1) / 2.0;
						break;

					case "TrackingStationLevelMin":
						good = !ScenarioUpgradeableFacilities.Instance.enabled || ScenarioUpgradeableFacilities.GetFacilityLevel(SpaceCenterFacility.TrackingStation) >= ((double)req_values[i].value - 1) / 2.0;
						break;
					case "TrackingStationLevelMax":
						good = !ScenarioUpgradeableFacilities.Instance.enabled || ScenarioUpgradeableFacilities.GetFacilityLevel(SpaceCenterFacility.TrackingStation) <= ((double)req_values[i].value - 1) / 2.0;
						break;

					case "MissionControlLevelMin":
						good = !ScenarioUpgradeableFacilities.Instance.enabled || ScenarioUpgradeableFacilities.GetFacilityLevel(SpaceCenterFacility.MissionControl) >= ((double)req_values[i].value - 1) / 2.0;
						break;
					case "MissionControlLevelMax":
						good = !ScenarioUpgradeableFacilities.Instance.enabled || ScenarioUpgradeableFacilities.GetFacilityLevel(SpaceCenterFacility.MissionControl) <= ((double)req_values[i].value - 1) / 2.0;
						break;

					case "AdministrationLevelMin":
						good = !ScenarioUpgradeableFacilities.Instance.enabled || ScenarioUpgradeableFacilities.GetFacilityLevel(SpaceCenterFacility.Administration) >= ((double)req_values[i].value - 1) / 2.0;
						break;
					case "AdministrationLevelMax":
						good = !ScenarioUpgradeableFacilities.Instance.enabled || ScenarioUpgradeableFacilities.GetFacilityLevel(SpaceCenterFacility.Administration) <= ((double)req_values[i].value - 1) / 2.0;
						break;

					case "MaxAsteroidDistance": good = AsteroidDistance(v) <= (double)req_values[i].value; break;
				}

				if (!good) invalid_reqs.Add(req_values[i].key);
			}
			return invalid_reqs;
		}

		#endregion
		#region private methods

		private void ParseResources(bool logErrors = false)
		{
			var reslib = PartResourceLibrary.Instance.resourceDefinitions;

			List<ObjectPair<string, double>> defs = new List<ObjectPair<string, double>>();
			foreach (string s in Lib.Tokenize(resources, ','))
			{
				// definitions are Resource@rate
				var p = Lib.Tokenize(s, '@');
				if (p.Count != 2) continue;             // malformed definition
				string res = p[0];
				if (!reslib.Contains(res)) continue;    // unknown resource
				double rate = double.Parse(p[1]);
				if (res.Length < 1 || rate < double.Epsilon) continue;  // rate <= 0
				defs.Add(new ObjectPair<string, double>(res, rate));
			}
			res_parsed = defs.ToArray();
		}

		private void ParseRequirements()
		{
			List<ObjectPair<string, object>> temp_reqs = new List<ObjectPair<string, object>>();
			foreach (string s in requires.Split(','))
			{
				s.Trim();
				string[] key_value = s.Split(':');
				if (key_value.Length > 0)
				{
					ObjectPair<string, object> req;
					key_value[0].Trim();
					if (key_value.Length > 1)
					{
						key_value[1].Trim();
						if (key_value[0] == "Body")
							// parse body allowed/restricted subvalues to a string array
							req = ParseRequireBodies(key_value[1]);
						else
							// key/value requirements
							req = ParseRequiresValues(key_value[0], key_value[1]);
						// save string values
						req_strings.Add(req.key, key_value[1]);
					}
					else
					{
						// key only
						req = new ObjectPair<string, object>(key_value[0], null);
					}
					temp_reqs.Add(req);
				}
			}
			req_values = temp_reqs.ToArray();
		}

		private ObjectPair<string, object> ParseRequireBodies(string body_list)
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
			return new ObjectPair<string, object>(body_key, bodies.ToArray());
		}

		private ObjectPair<string, object> ParseRequiresValues(string key, string value)
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
					return new ObjectPair<string, object>(key, double.Parse(value));
				case "CrewMin":
				case "CrewMax":
				case "CrewCapacityMin":
				case "CrewCapacityMax":
					return new ObjectPair<string, object>(key, int.Parse(value));
				default:
					return new ObjectPair<string, object>(key, value);
			}
		}

		private bool TestBody(string[] body_reqs, string body_name)
		{
			for (int i = 0; i < body_reqs.Length; i++)
				if (body_reqs[i] == body_name) return true;
			return false;
		}

		// TODO : be coherent and also require the asteroid to be targeted when loaded
		// and split the requirement test so the player gets a "target must be asteroid" issue message first
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
				if (distance < result) result = distance; // ???
			}
			return result;
		}
		#endregion
		#region static methods

		/// <summary>
		/// Get experiment id from a full subject id
		/// </summary>
		public static string GetExperimentId(string subject_id)
		{
			int i = subject_id.IndexOf('@');
			return i > 0 ? subject_id.Substring(0, i) : subject_id;
		}

		/// <summary>
		/// returns  a pretty printed situation description for the UI
		/// </summary>
		// TODO : move to ExperimentInfo
		public static string Situation(string subject_id)
		{
			int i = subject_id.IndexOf('@');
			var situation = subject_id.Length < i + 2
				? Localizer.Format("#KERBALISM_ExperimentInfo_Unknown")
				: Lib.SpacesOnCaps(subject_id.Substring(i + 1));
			situation = situation.Replace("Srf ", string.Empty).Replace("In ", string.Empty);
			return situation;
		}

		#endregion

		private static string MaskToString(string text, uint flag, uint situationMask, uint biomeMask)
		{
			string result = string.Empty;
			if ((flag & situationMask) == 0) return result;
			result = text;
			if ((flag & biomeMask) != 0) result += " (Biomes)";
			return result;
		}

		/// <summary>stock experiment definition</summary>
		public ExperimentInfo exp_info { get; private set; }

		/// <summary>CFG : unique id to be used in the partmodule "exp_variant_id"</summary>
		public string id { get; private set; }

		/// <summary>CFG : id of the EXPERIMENT_DEFINITION that will be registered as a result</summary>
		public string exp_def_id { get; private set; }

		/// <summary>CFG : optional, some nice lines of text</summary>
		public string experiment_desc { get; private set; }

		/// <summary>= baseValue * dataScale. Data amount for a complete result in MB. For sample, 1 slot = 1024 MB.</summary>
		//public long data_max { get; private set; }

		/// <summary>data production rate (internally stored in bit/s), defined in cfg in MB/. For sample, 1 slot = 1024 MB.</summary>
		public long data_rate { get; private set; }

		/// <summary>CFG : EC consumption rate per-second</summary>
		public double ec_rate { get; private set; }



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

		// not ideal because unboxing at but least we won't be parsing strings all the time and the array should be fast
		private ObjectPair<string, object>[] req_values;

		// for building the UI message, finding by key is needed
		private Dictionary<string, string> req_strings;

		// parsed resources
		public ObjectPair<string, double>[] res_parsed { get; private set; }

		/// <summary>
		/// Same as a KeyValuePair, but is a class instead of a struct
		/// </summary>
		public class ObjectPair<TKey, TValue>
		{
			public TKey key;
			public TValue value;

			public ObjectPair(TKey key, TValue value)
			{
				this.key = key;
				this.value = value;
			}
		}


		// TODO : this is a stub showing how we should register experiments in the RnD instance
		//public static void AddExperimentToRnD(ExperimentInfo exp_variant)
		//{
		//	var experiments = Lib.ReflectionValue<Dictionary<string, ScienceExperiment>>
		//	(
		//	  ResearchAndDevelopment.Instance,
		//	  "experiments"
		//	);

		//	var exp = new ScienceExperiment();
		//	exp.baseValue = exp_variant.baseValue;
		//	exp.dataScale = exp_variant.dataScale;
		//	...

		//	experiments.Add(exp_variant.id, exp);
		//}
	}

} // KERBALISM

