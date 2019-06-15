using System;
using System.Collections.Generic;
using UnityEngine;
using KSP.Localization;
using System.Collections;


// - migrate the static things to instance
// - remove the vessel, migrate the situation checking to a IsAvailable(vessel) method

/*
 *
 *

// TODO : FINAL REFACTOR
- Subjects as a HashSet in ExperimentInfo
- that will allow fast get/update to the subjects stored data (happens very often)
- additional requirements :
	- this might benefit from having a bitArray of situations, bodies, and biomes, but that mean we need to have fast access lists of
		- situations, bodies, biomes for each body
	- fastest way to check biome is by using the array of CBAttributeMapSO.MapAttribute objects
	- low occurence : get the subject from the situation+body+biome 
	- very high occurence : check if a subject is still valid for the current vessel
		- get current situation -> either way, having a bitmask of possible situations could make things a lot faster  :
			- get all current situations, then check the best one : need instantiatiting a list each time
			- get best situation : need 
		- get current
Example config :

Stock "InSpaceLow" multipliers
Bop		9×
Dres	7×
Duna	7×
Eeloo	12×
Eve		7×
Gilly	8×
Ike		7×
Jool	7×
Kerbin	1×
Kerbol	11×
Laythe	9×
Minmus	4×
Moho	8×
Mun		3×
Pol		9×
Tylo	10×
Vall	9×

Kerbalism custom situations and their default multiplier (applied to the InSpaceLow stock multiplier) :
InSpace			1×
InnerBelt		1.4×
OuterBelt		1.4×
Magnetosphere	1.3×
Reentry			1×
Interstellar	15×

Stock situations :
SrfLanded
SrfSplashed
FlyingLow
FlyingHigh
InSpaceLow
InSpaceHigh


EXPERIMENT_INFO
{
	// if the ID from a stock EXPERIMENT_DEFINITION is used, the values defined
	// here will override those defined in the stock EXPERIMENT_DEFINITION
	// Note : quick tests show that this is possible, but unexpected issues may arise
	id = mysteryGoo
	title = Goo Observation

	// remove any of those to prevent a result from being available
	// ommit "@biome" to make the result non biome dependant
	situation = SrfLanded@Biomes
	situation = SrfSplashed@biomes
	situation = FlyingLow@biomes
	situation = FlyingHigh@biomes
	situation = InSpaceLow@biomes
	situation = InSpaceHigh@biomes

	situation = SrfLanded@Biomes@Kerbin,Laythe
	situation = SrfLanded@Global



	SITUATION
	{
		name = SrfLanded
		// optional, science value factor is applied on top of the stock "InSpaceLow" body multiplier. See the situation list for the default values.
		scienceMultiplier =

		// One value per line, multiple line allowed to combine the conditions.
		// Use the "!" prefix for "not" (ex : "!sun", "!Duna"), not available for "all" and "global". body = global makes the experiment body independant and force biomes to be global as well.
		body =				// Default : all. Other values : global, homeBody, sun, planets, moons, rockBodies, atmosphereBodies, oceanBodies, bodyName.

		// One value per line, multiple line allowed to combine the conditions.
		// Use the "!" prefix for "not" (ex : "!sun", "!Duna")
		// Use the "*" prefix for a partial match (biomes only, case insensitive), examples : "*pole", "*polar", "*crater"			
		biome =				// Default : all. Other values : global, biomename
		miniBiome =			// Default : global. Other values :  all, biomename
	}

	dataSize =				// in MB for a full result
	sampleVolume =			// in m3 for a full result NOT IMPLEMENTED, MAY OR MAY NOT BE ADDED 
	sampleMass =			// in t for a full result
	scienceValue =			// in science points
	creditValue =			// NOT IMPLEMENTED, JUST AN IDEA

	patchAsStockDefinition = true // default = false. If set to true, the experiment will be internally exposed as a stock EXPERIMENT_DEFINITION

	// All the following fields are fundamentally incompatible with the stock system
	// so if patchAsStockDefinition is true, using them may (or not) cause issues
	// to any mod that expect this to be a stock EXPERIMENT_DEFINITION (contract packs...)
	situation = InnerBelt@biomes
	situation = OuterBelt@biomes
	situation = Magnetosphere@biomes
	situation = Reentry@biomes
	situation = Interstellar@biomes

	allowMiniBiomes =		// if false, mini-biomes (in stock, KSC biomes) will be ignored
	bodyIsIrrelevant =		// NOT IMPLEMENTED, JUST AN IDEA
	bodyRadiusScale =		// NOT IMPLEMENTED, JUST AN IDEA : if set, dataSize is for
							// the radius of the home body (Kerbin) and the data size for the
							// biggest body is dataSize * bodyRadiusScale

	// note : the purpose of variants is to centralize and streamline the patching/config 
	// balancing process instead of having a massive, hard to maintain ModuleManager hell.
	VARIANT
	{
		masterVariant = true		// if true, other variants don't need to redefine everything, the values from the master will be used
		id = mysteryGooBase			// unique id to be used for the experiement partmodule "variant_id"
		experiment_desc =			// optional, some nice lines of text
		duration =					// time in minutes it takes to produce a full result
		ec_rate = 0.18				// EC consumption rate per-second
		isSample =					// if true, the experiment will generate a sample
		sample_amount = 1			// amount of full results the experiement can generate (changes how much sampling volume is provided)
		sample_collecting =			// if true, the experiment will generate mass instead of "converting" its available sample mass
		allow_shrouded =			// if true, the experiment can be run while shrouded
		requires =					// optional, additional requirements that must be met
		crew_operate =				// optional, if set, crew has to be on vessel while recording
		crew_reset =				// optional, if set, experiment will stop recording after situation change
		crew_prepare =				// optional, if set, experiment will require crew to set up before it can start recording
		resources =					// optional, resources consumed by this experiment
		hide_when_unavailable = false
	}
	
	VARIANT
	{
		id = mysteryGooUpgrade
		sample_amount = 2			// amount of full results the experiement can generate (changes how much sampling volume is provided)
	}
}

MODULE
{
	name = Experiment
	variant_id = mysteryGooBase
	anim_deploy =
	anim_deploy_reverse =
	anim_loop = 
	anim_loop_reverse =
	// surface checking fields shoudl be on the module too

	UPGRADES
	{
		UPGRADE
		{
			name__ = Goo-Storage-Upgrade
			techRequired__ = basicScience
			variant_id = mysteryGooUpgrade
		}
	}
}

*/

namespace KERBALISM
{
	/// <summary>
	/// This is an extension to the situations that stock KSP provides.
	/// </summary>
	public enum KerbalismSituation
	{
		// TODO : order this so the higher priority situation are first.


		//None = 0,
		//SrfLanded = (1 << 0), // 1
		//SrfSplashed = (1 << 1), // 2
		//FlyingLow = (1 << 2), // 4
		//FlyingHigh = (1 << 3), // 8
		//InSpaceLow = (1 << 4), // 16
		//InSpaceHigh = (1 << 5), // 32

		//// Kerbalism extensions

		//InSpace = (1 << 6), // 64
		//InnerBelt = (1 << 7), // 128
		//OuterBelt = (1 << 8), // 256
		//Magnetosphere = (1 << 9), // 512
		//Reentry = (1 << 10), // 1024
		//Interstellar = (1 << 11) // 2048


		None = 0,
		
		SrfLanded = (1 << 0), // 1
		SrfSplashed = (1 << 1), // 2

		FlyingLow = (1 << 2), // 4
		Reentry = (1 << 3), // 8
		FlyingHigh = (1 << 4), // 16

		Magnetosphere = (1 << 5), // 32
		InnerBelt = (1 << 6), // 64
		OuterBelt = (1 << 7), // 128
		Interstellar = (1 << 8), // 256

		InSpace = (1 << 9), // 512
		InSpaceLow = (1 << 10), // 1024
		InSpaceHigh = (1 << 11) // 2048

		// Kerbalism extensions



		
		

	}

	/// <summary>
	/// Replacement for KSPs own ExperimentSituations
	/// </summary>
	public sealed class ExperimentInfo
	{
		// Important notes :
		// - if the ID from a stock EXPERIMENT_DEFINITION is used, the stock values will be overriden by the ExperimentInfo ones.
		// - if patchAsStockDefinition == true, the ExperimentInfo will be parsed as a new stock EXPERIMENT_DEFINITION,
		//   allowing contracts and mods to use it for their own purpose (wich may lead to issues if custom situations/requirements are used)
		// - scienceCap is unsupported, we always set scienceCap = scienceValue. May come back later as something tied to the variants
		// - compatibility with (stock) contracts when using custom situations/requirements can maybe be fixed by checking contract validity
		//   trough the GameEvents.Contract.OnOffered event
		// - all data sizes are expressed as an integer (long type) value in bits, while stock use a floating point value in MB (1MB = 8388608 bits)
		//   this is done to avoid floating-point precision issues when manipulating file sizes, but it has a some drawbacks :
		//		- risk of max value overflowing when doing math operations or storing large values (long.MaxValue => 1048576 TB)
		//		- dataRates (transmission, data generation...) introduce a duration precision issue, especially if low datarates are used :
		//			- a datarate less than 50 bit/s will be completly ignored at 1x timewarp (~0.02s timestep => 50/0.02 = 1bit/timestep)
		//			- for 500 bit/s (~0.00006 MB/s), max loss of precision is 10% at 1x timewarp (~0.02s timestep).
		//			- for 5000 bit/s (~0.00059 MB/s), max loss of precision is 1% at 1x timewarp (~0.02s timestep).
		//			- timewarping (and lag) will increase precision since that increase the duration of a single timestep
		//			- unloaded vessels will be less affected than loaded vessels (their timestep duration is usually longer)

		// Config derived fields :
		// UNDONE public bool bodyIsIrrelevant;   // if true experiment will be the same for every body
		public string id;
		public string title;
		public long fullSize;			// in bits for a full result (defined as a float in MB in the config)
		public double scienceValue;      // in science points for a full result
		public double sampleMass;        // mass in tons for a full sample
		public uint situationMask;      // parsed from the "situation = situation@biomes" config format
		public uint biomeMask;          // parsed from the "situation = situation@biomes" config format
		public bool allowMiniBiomes;    // if false, the biome at the KSC will by shores instead of the KSC mini-biomes

		// those work exactly the same as stock
		public float requiredExperimentLevel;
		public bool requireAtmosphere;

		// internal values :
		public double dataScale;          // = fullSize / scienceValue. Same as stock but the unit is in bit/point instead of MB/point
		public readonly double massPerBit;
		public ScienceExperiment stockDefinition;

		private readonly Dictionary<string, Subject> subjects;
		private readonly KerbalismSituation[] situations;



		public ExperimentInfo(ConfigNode node)
		{
			// TODO : ExperimentInfo deserialization
			id = Lib.ConfigValue(node, "id", string.Empty);
			title = Lib.ConfigValue(node, "title", id);
			fullSize = Lib.MBToBit(Lib.ConfigValue(node, "fullSize", 0.0));
			scienceValue = Lib.ConfigValue(node, "scienceValue", 1.0);
			allowMiniBiomes = Lib.ConfigValue(node, "allowMiniBiomes", true);
			requiredExperimentLevel = Lib.ConfigValue(node, "requiredExperimentLevel", 0f);
			requireAtmosphere = Lib.ConfigValue(node, "requireAtmosphere", false);
			sampleMass = Lib.ConfigValue(node, "sampleMass", 0.0);

			// set mass/data ratio of samples
			massPerBit = sampleMass / fullSize;
			// determince dataScale
			dataScale = fullSize / scienceValue;

			// parse situation/biome bitmasks
			situationMask = 0;
			biomeMask = 0;
			uint stockSituationMask = 0;
			uint stockBiomeMask = 0;
			foreach (string sitStr in node.GetValues("situation"))
			{
				string[] sitDef = sitStr.Split('@');
				Array.ForEach(sitDef, s => s.Trim(' '));
				if (sitDef.Length == 0 || string.IsNullOrEmpty(sitDef[0]))
					continue;
				KerbalismSituation sit;
				try
				{
					sit = (KerbalismSituation)Enum.Parse(typeof(KerbalismSituation), sitDef[0]);
				}
				catch (Exception)
				{
					Lib.Log("CFG LOAD ERROR : could not parse situation '" + sitDef[0] + "' for ExperimentInfo '" + id + "'");
					continue;
				}

				uint sitVal = (uint)sit;

				situationMask += sitVal;
				if (sitVal <= (uint)KerbalismSituation.InSpaceHigh)
					stockSituationMask += sitVal;

				if (sitDef.Length == 2 && sitDef[1] == "biomes")
				{
					biomeMask += sitVal;
					if (sitVal <= (uint)KerbalismSituation.InSpaceHigh)
						stockBiomeMask += sitVal;
				}
			}

			// if this ExperimentInfo already exists as a stock EXPERIMENT_DEFINITION, we will patch the stock values
			stockDefinition = ResearchAndDevelopment.GetExperiment(id);

			if (stockDefinition == null && Lib.ConfigValue(node, "patchAsStockDefinition", false))
			{
				// get the private stock experiment dictionary
				var experiments = Lib.ReflectionValue<Dictionary<string, ScienceExperiment>>
				(
				  ResearchAndDevelopment.Instance,
				  "experiments"
				);

				// create and add the new definition
				if (experiments != null)
				{
					stockDefinition = new ScienceExperiment();
					stockDefinition.id = id;
					experiments.Add(id, stockDefinition);
				}
				else
				{
					Lib.Log("CFG LOAD ERROR : could not patch EXPERIMENT_INFO '" + id + "' as a stock EXPERIMENT_DEFINITION.");
				}
			}

			// patch the existing or new definition with our values
			if (stockDefinition != null)
			{
				stockDefinition.baseValue = (float)scienceValue;
				stockDefinition.scienceCap = (float)scienceValue;
				stockDefinition.dataScale = (float)(Lib.BitToMB(fullSize) / scienceValue);
				stockDefinition.experimentTitle = title;
				stockDefinition.requireAtmosphere = requireAtmosphere;
				stockDefinition.requiredExperimentLevel = requiredExperimentLevel;
				stockDefinition.situationMask = stockSituationMask;
				stockDefinition.biomeMask = stockBiomeMask;
			}
		}

		public List<string> AvailableSituations()
		{
			List<string> result = new List<string>();
			string availableSit = string.Empty;

			foreach (KerbalismSituation situation in (KerbalismSituation[])Enum.GetValues(typeof(KerbalismSituation)))
			{
				if (situation == KerbalismSituation.None)
					continue;

				availableSit = MaskToString(situation);

				if (availableSit != string.Empty)
					result.Add(availableSit);
			}

			return result;
		}

		public string MaskToString(KerbalismSituation sit)
		{
			if (((uint)sit & situationMask) == 0)
				return string.Empty;

			if (((uint)sit & biomeMask) == 0)
				return SituationString(sit);
			else
				return Lib.BuildString(SituationString(sit), " (Biomes)");
		}

		/// <summary>
		/// The KSP stock function has the nasty habit of returning, on occasion,
		/// situations that should not exist (flying high/low with bodies that
		/// don't have atmosphere), so we have to force the situations a bit.
		/// </summary>
		//public KerbalismSituation GetSituation(Vessel vessel)
		//{
		//	switch (vessel.situation)
		//	{
		//		case Vessel.Situations.SPLASHED:
		//			return IsAvailable(KerbalismSituation.SrfSplashed, vessel.mainBody) ? KerbalismSituation.SrfSplashed : KerbalismSituation.None;
		//		case Vessel.Situations.PRELAUNCH:
		//		case Vessel.Situations.LANDED:
		//			return IsAvailable(KerbalismSituation.SrfLanded, vessel.mainBody) ? KerbalismSituation.SrfLanded : KerbalismSituation.None;
		//	}

		//	if (vessel.mainBody.atmosphere && vessel.altitude < vessel.mainBody.atmosphereDepth)
		//	{
		//		if (vessel.altitude < vessel.mainBody.scienceValues.flyingAltitudeThreshold)
		//		{
		//			return IsAvailable(KerbalismSituation.FlyingLow, vessel.mainBody) ? KerbalismSituation.FlyingLow : KerbalismSituation.None;
		//		}
		//		// note : checking vessel.mach > ~8-10 may be more accurate than using srfSpeed
		//		else if (IsAvailable(KerbalismSituation.Reentry, vessel.mainBody)
		//			&& vessel.loaded
		//			&& vessel.orbit.ApA > vessel.mainBody.atmosphereDepth
		//			&& vessel.verticalSpeed < -50.0
		//			&& vessel.srfSpeed > 1984)
		//		{
		//			return KerbalismSituation.Reentry;
		//		}

		//		return IsAvailable(KerbalismSituation.FlyingHigh, vessel.mainBody) ? KerbalismSituation.FlyingHigh : KerbalismSituation.None;
		//	}

		//	var vi = Cache.VesselInfo(vessel);

		//	// the radiation related situations will override spaceHigh and spaceLow
		//	// we also assume that magnetosphere will override the belts
		//	if (IsAvailable(KerbalismSituation.Magnetosphere, vessel.mainBody) && vi.magnetosphere) return KerbalismSituation.Magnetosphere;
		//	if (IsAvailable(KerbalismSituation.InnerBelt, vessel.mainBody) && vi.inner_belt) return KerbalismSituation.InnerBelt;
		//	if (IsAvailable(KerbalismSituation.OuterBelt, vessel.mainBody) && vi.outer_belt) return KerbalismSituation.OuterBelt;

		//	if (vessel.altitude < vessel.mainBody.scienceValues.spaceAltitudeThreshold)
		//		return IsAvailable(KerbalismSituation.InSpaceLow, vessel.mainBody) ? KerbalismSituation.InSpaceLow : KerbalismSituation.None;

		//	// interstellar should only override space high ?
		//	if (IsAvailable(KerbalismSituation.Interstellar, vessel.mainBody) && vi.interstellar) return KerbalismSituation.Interstellar;

		//	return IsAvailable(KerbalismSituation.InSpaceHigh, vessel.mainBody) ? KerbalismSituation.InSpaceHigh : KerbalismSituation.None;
		//}

		public override string ToString()
		{
			return title;
		}

		//public bool IsAvailable(KerbalismSituation situation, CelestialBody body)
		//{
		//	if (requireAtmosphere)
		//	{
		//		return body.atmosphere && (situationMask & (int)situation) != 0;
		//	}
		//	return ((int)situationMask & (int)situation) != 0;
		//}

		public bool BiomeIsRelevant(KerbalismSituation situation)
		{
			return ((int)biomeMask & (int)situation) != 0;
		}

		public static float BodyScienceValue(CelestialBody body, KerbalismSituation situation)
		{
			var values = body.scienceValues;

			switch (situation)
			{
				case KerbalismSituation.SrfLanded: return values.LandedDataValue;
				case KerbalismSituation.SrfSplashed: return values.SplashedDataValue;
				case KerbalismSituation.FlyingLow: return values.FlyingLowDataValue;
				case KerbalismSituation.FlyingHigh: return values.FlyingHighDataValue;
				case KerbalismSituation.InSpaceLow: return values.InSpaceLowDataValue;
				case KerbalismSituation.InSpaceHigh: return values.FlyingHighDataValue;

				case KerbalismSituation.InnerBelt:
				case KerbalismSituation.OuterBelt:
					return 1.3f * values.InSpaceHighDataValue;

				case KerbalismSituation.Reentry: return 1.5f * values.FlyingHighDataValue;
				case KerbalismSituation.Magnetosphere: return 1.1f * values.InSpaceHighDataValue;
				case KerbalismSituation.Interstellar: return 15f * values.InSpaceHighDataValue;
			}

			Lib.Log("Science: invalid/unknown situation " + situation.ToString());
			return 0;
		}

		public static string SituationString(KerbalismSituation situation)
		{
			switch (situation)
			{
				case KerbalismSituation.SrfLanded: return "Landed";
				case KerbalismSituation.SrfSplashed: return "Splashed";
				case KerbalismSituation.FlyingLow: return "Flying low";
				case KerbalismSituation.FlyingHigh: return "Flying high";
				case KerbalismSituation.InSpaceLow: return "Space low";
				case KerbalismSituation.InSpaceHigh: return "Space high";
				case KerbalismSituation.InnerBelt: return "Inner belt";
				case KerbalismSituation.OuterBelt: return "Outer belt";
				case KerbalismSituation.Reentry: return "Reentry";
				case KerbalismSituation.Magnetosphere: return "Magnetopause";
				case KerbalismSituation.Interstellar: return "Interstellar";
				default: return string.Empty;
			}
		}

		//public List<Subject> GetSubjects(CelestialBody body = null, KerbalismSituation situation = KerbalismSituation.None)
		//{
		//	List<Subject> subjects = new List<Subject>();
		//	BodySubjects[] bodySubjects = null;
		//	if (body == null)
		//	{
		//		bodySubjects = allSubjects;
		//	}	
		//	else
		//	{
		//		foreach (BodySubjects bodySubs in allSubjects)
		//		{
		//			if (bodySubs.Body == body)
		//			{
		//				bodySubjects = new BodySubjects[] { bodySubs };
		//				break;
		//			}
		//		}
		//	}

		//	if (bodySubjects == null) return subjects;

		//	foreach (BodySubjects bodySub in bodySubjects)
		//	{
		//		foreach (SituationSubjects sitSubjects in bodySub.Situations)
		//		{
		//			if (situation == KerbalismSituation.None)
		//			{
		//				subjects.AddRange(sitSubjects.Subjects);
		//			}
		//			else if (sitSubjects.situation == situation)
		//			{
		//				subjects.AddRange(sitSubjects.Subjects);
		//				return subjects;
		//			}
		//		}
		//	}

		//	return subjects;
		//}

		// Note : maybe it's better to return a list of valid situations for the vessel, and then filter it based on the available situations.
		// might be cleaner than this mess.
		// Note : this fully rely on the fact that the enum is ordered by priority (some situations will override others).
		// Any addition/removal from the enum will break the following method
		public KerbalismSituation GetSituation(Vessel vessel)
		{
			CBAttributeMapSO.MapAttribute biome = Lib.GetBiome(vessel);
			Vessel_info vi = null;

			for (int i = 0; i < availableSituations.Length; i++)
			{
				if (availableSituations[i] == null || !availableSituations[i].IsAvailable(vessel.mainBody, biome))
					continue;

				KerbalismSituation situation = (KerbalismSituation)(1 << i);

				switch (situation)
				{
					case KerbalismSituation.SrfLanded:
						if (vessel.situation == Vessel.Situations.PRELAUNCH || vessel.situation == Vessel.Situations.LANDED)
							return situation; break;
					case KerbalismSituation.SrfSplashed:
						if (vessel.situation == Vessel.Situations.SPLASHED)
							return situation; break;

					// atmo situations
					case KerbalismSituation.FlyingLow:
						// if not in atmoshphere, skip the other atmo situations (this assume the 2 next situations are reentry and flying high)
						if (!vessel.mainBody.atmosphere || vessel.altitude > vessel.mainBody.atmosphereDepth)
						{ i += 2; break; }	
						if (vessel.altitude < vessel.mainBody.scienceValues.flyingAltitudeThreshold)
							return situation; break;
					case KerbalismSituation.Reentry:
						// note : checking vessel.mach > ~8-10 may be more accurate than using srfSpeed
						if (vessel.loaded
							&& vessel.orbit.ApA > vessel.mainBody.atmosphereDepth
							&& vessel.verticalSpeed < -50.0
							&& vessel.srfSpeed > 1984)
							return situation; break;
					case KerbalismSituation.FlyingHigh:
						return situation;

					// the radiation related situations will override spaceHigh and spaceLow
					// we also assume that magnetosphere will override the belts
					case KerbalismSituation.Magnetosphere:
						vi = Cache.VesselInfo(vessel);
						if (vi.magnetosphere)
							return situation; break;
					case KerbalismSituation.InnerBelt:
						if (vi.inner_belt)
							return situation; break;
					case KerbalismSituation.OuterBelt:
						if (vi.outer_belt)
							return situation; break;
					case KerbalismSituation.Interstellar:
						if (vi.interstellar)
							return situation; break;

					// the generic space situation will override the others
					case KerbalismSituation.InSpace:
						return situation;
					case KerbalismSituation.InSpaceLow:
						if (vessel.altitude < vessel.mainBody.scienceValues.spaceAltitudeThreshold)
							return situation; break;
					case KerbalismSituation.InSpaceHigh:
						return situation;
				}
			}
			return KerbalismSituation.None;
		}

		private void BuildAvailableSituations()
		{
			int situationsCount = Enum.GetValues(typeof(KerbalismSituation)).Length - 1;
			availableSituations = new SituationInfo[situationsCount];
			for (int i = 0; i < situationsCount; i++)
			{
				if ((situationMask & (1 << i)) != 0)
				{
					availableSituations[i] = new SituationInfo();
					// TODO : actually populate the situation
				}
				else
				{
					availableSituations[i] = null;
				}
				
			}
		}

		private SituationInfo[] availableSituations;

		private class SituationInfo
		{
			public double SituationMultiplier { get; private set; }
			public double TotalScienceValue { get; private set; }
			// Note on how to use this :
			// - if the dictionary is null, the experiment is body independant (only one subject per situation, regardless of the body)
			// - if the BitArray is null, the situation/body combination is biome independant
			// - the BitArray is a bitmask of the available biomes, indexes match thoses from the body.BiomeMap.Attributes array.
			public Dictionary<CelestialBody, BitArray> availableBodiesAndBiomes;

			public bool IsAvailable(CelestialBody body, CBAttributeMapSO.MapAttribute biome)
			{
				// if the situation is body independant
				if (availableBodiesAndBiomes == null)
					return true;

				// if the situation isn't available for the current body
				if (!availableBodiesAndBiomes.ContainsKey(body))
					return false;

				// if the situation is biome independant
				if (availableBodiesAndBiomes[body] == null)
					return true;

				// if the vessel current biome is available in this experiment, return true, else return false
				return availableBodiesAndBiomes[body][Science.GetBiomeIndex(biome)];
			}

			public override string ToString()
			{
				// if the situation is body independant
				if (availableBodiesAndBiomes == null)
					return "(global)";

				return string.Empty;
				// TODO : finish that

				//// if the situation isn't available for the current body
				//if (!availableBodiesAndBiomes.ContainsKey(body))
				//	return false;

				//// if the situation is biome independant
				//if (availableBodiesAndBiomes[body] == null)
				//	return true;

				//// if the vessel current biome is available in this experiment, return true, else return false
				//return availableBodiesAndBiomes[body][Science.GetBiomeIndex(biome)];
			}
		}
	}
}
