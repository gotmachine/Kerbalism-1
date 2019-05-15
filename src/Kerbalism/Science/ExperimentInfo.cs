using System;
using System.Collections.Generic;
using UnityEngine;
using KSP.Localization;

// TODO : finish migrating ExperimentSituation to become the replacement of the stock ScienceExperiment :
// - migrate the static things to instance
// - remove the vessel, migrate the situation checking to a IsAvailable(vessel) method

namespace KERBALISM
{
	/// <summary>
	/// This is an extension to the situations that stock KSP provides.
	/// </summary>
	public enum KerbalismSituation
	{
		None = 0,

		SrfLanded = (1 << 0), // 1
		SrfSplashed = (1 << 1), // 2
		FlyingLow = (1 << 2), // 4
		FlyingHigh = (1 << 3), // 8
		InSpaceLow = (1 << 4), // 16
		InSpaceHigh = (1 << 5), // 32

		// Kerbalism extensions
		InnerBelt = (1 << 6), // 64
		OuterBelt = (1 << 7), // 128
		Magnetosphere = (1 << 8), // 256
		Reentry = (1 << 9), // 512
		Interstellar = (1 << 10) // 1024
	}

	/// <summary>
	/// Replacement for KSPs own ExperimentSituations
	/// </summary>
	public sealed class ExperimentInfo
	{
		//private Vessel vessel;
		//private KerbalismSituation sit = KerbalismSituation.None;

		public string id;
		public string experimentTitle;

		/// <summary> CFG : mass in tons for a full sample, used only if a sample is generated</summary>
		public float sample_mass { get; private set; }

		// TODO : actually implement all the ExperimentInfo fields
		public uint situationMask;
		public uint biomeMask;

		public long baseValue; // size in bits
		public long dataScale; // size in bits

		public float scienceCap;


		// beware : those will make CC / contract packs potentially ask doing experiments for situations that can't be done
		public bool bodyIsIrrelevant; // TODO : not implemented, if true experiement will be the same for every body
		public bool allowKSCBiomes; // if true, the biome at the KSC will by "shores"

		public readonly long dataSize;
		public readonly double massPerBit;

		// multiplier for Kerbalism custom situations
		// InnerBelt/OuterBelt/Magnetosphere/Interstellar apply on the space high body multiplier
		// Reentry apply on the space high body multiplier
		public Dictionary<string, double> SituationMultiplers;
		

		public ExperimentInfo(ConfigNode node)
		{
			// TODO : ExperimentInfo deserialization

			dataSize = baseValue * dataScale;
			massPerBit = sample_mass / dataSize;
		}

		public List<string> Situations()
		{
			List<string> result = new List<string>();

			string s;

			s = MaskToString(KerbalismSituation.SrfLanded, situationMask, biomeMask); if (!string.IsNullOrEmpty(s)) result.Add(s);
			s = MaskToString(KerbalismSituation.SrfSplashed, situationMask, biomeMask); if (!string.IsNullOrEmpty(s)) result.Add(s);
			s = MaskToString(KerbalismSituation.FlyingLow, situationMask, biomeMask); if (!string.IsNullOrEmpty(s)) result.Add(s);
			s = MaskToString(KerbalismSituation.FlyingHigh, situationMask, biomeMask); if (!string.IsNullOrEmpty(s)) result.Add(s);
			s = MaskToString(KerbalismSituation.InSpaceLow, situationMask, biomeMask); if (!string.IsNullOrEmpty(s)) result.Add(s);
			s = MaskToString(KerbalismSituation.InSpaceHigh, situationMask, biomeMask); if (!string.IsNullOrEmpty(s)) result.Add(s);

			s = MaskToString(KerbalismSituation.InnerBelt, situationMask, biomeMask); if (!string.IsNullOrEmpty(s)) result.Add(s);
			s = MaskToString(KerbalismSituation.OuterBelt, situationMask, biomeMask); if (!string.IsNullOrEmpty(s)) result.Add(s);
			s = MaskToString(KerbalismSituation.Magnetosphere, situationMask, biomeMask); if (!string.IsNullOrEmpty(s)) result.Add(s);
			s = MaskToString(KerbalismSituation.Reentry, situationMask, biomeMask); if (!string.IsNullOrEmpty(s)) result.Add(s);
			s = MaskToString(KerbalismSituation.Interstellar, situationMask, biomeMask); if (!string.IsNullOrEmpty(s)) result.Add(s);

			return result;
		}

		private static string MaskToString(KerbalismSituation sit, uint situationMask, uint biomeMask)
		{
			string result = string.Empty;
			if (((int)sit & situationMask) == 0) return result;
			result = Lib.SpacesOnCaps(sit.ToString().Replace("Srf", ""));
			if (((int)sit & biomeMask) != 0) result += " (Biomes)";
			return result;
		}

		/// <summary>
		/// The KSP stock function has the nasty habit of returning, on occasion,
		/// situations that should not exist (flying high/low with bodies that
		/// don't have atmosphere), so we have to force the situations a bit.
		/// </summary>
		/// 		

		public KerbalismSituation GetSituation(Vessel vessel)
		{
			
			switch (vessel.situation)
			{
				case Vessel.Situations.SPLASHED:
					return KerbalismSituation.SrfSplashed;
				case Vessel.Situations.PRELAUNCH:
				case Vessel.Situations.LANDED:
					return KerbalismSituation.SrfLanded;
			}

			if (vessel.mainBody.atmosphere && vessel.altitude < vessel.mainBody.atmosphereDepth)
			{
				if (vessel.altitude < vessel.mainBody.scienceValues.flyingAltitudeThreshold)
				{
					return KerbalismSituation.FlyingLow;
				}
				else if (vessel.loaded
					&& vessel.orbit.ApA > vessel.mainBody.atmosphereDepth
					&& vessel.verticalSpeed < 0
					&& vessel.srfSpeed > 1984)
				{
					return KerbalismSituation.Reentry;
				}
				return KerbalismSituation.FlyingHigh;
			}

			if (vessel.altitude < vessel.mainBody.scienceValues.spaceAltitudeThreshold)
			{
				return KerbalismSituation.InSpaceLow;
			}

			var vi = Cache.VesselInfo(vessel);

			// these situations will override spaceHigh and that is ok
			// but what should prevail : magnetosphere or belts ?
			//if (vi.magnetosphere) return KerbalismSituations.Magnetosphere;
			//if (vi.outer_belt) return KerbalismSituations.OuterBelt;
			//if (vi.inner_belt) return KerbalismSituations.InnerBelt;

			if (vi.interstellar) return KerbalismSituation.Interstellar;

			return KerbalismSituation.InSpaceHigh;
		}

		public override string ToString()
		{
			return sit.ToString();
		}



		internal bool IsAvailable(ScienceExperiment exp)
		{
			return exp.IsAvailableWhile(sit, vessel.mainBody);
		}

		internal bool IsAvailable()
		{
			// make sure to not supersede SpaceHigh with our custom situations, otherwise
			// those experiments won't run any more while in space high and in a belt
			var s = sit;
			if ((situationMask & (int)s) != 0) return true;

			if (s >= KerbalismSituation.InnerBelt) s = KerbalismSituation.InSpaceHigh;
			return (situationMask & (int)s) != 0;
		}

		public bool IsAvailable(KerbalismSituation situation)
		{
			if (((int)situationMask & (int)situation) != 0) return true;
			return true;
		}

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
					return 1.3f * Math.Max(values.InSpaceHighDataValue, values.InSpaceLowDataValue);

				case KerbalismSituation.Reentry: return 1.5f * values.FlyingHighDataValue;
				case KerbalismSituation.Magnetosphere: return 1.1f * values.FlyingHighDataValue;
				case KerbalismSituation.Interstellar: return 15f * values.InSpaceHighDataValue;
			}

			Lib.Log("Science: invalid/unknown situation " + situation.ToString());
			return 0;
		}

		private static KerbalismSituation StockSituation(KerbalismSituation s)
		{
			if (s < KerbalismSituation.InnerBelt)
				return s;

			if (s == KerbalismSituation.Reentry)
				return KerbalismSituation.FlyingHigh;

			return KerbalismSituation.InSpaceHigh;
		}
	}
}
