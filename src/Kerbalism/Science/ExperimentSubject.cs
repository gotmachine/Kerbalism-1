using KSP.Localization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace KERBALISM
{
	/// <summary>
	/// utility class for testing a subject validity and generating the subject_id string
	/// </summary>
	public sealed class ExperimentSubject
	{
		public readonly string subject_id;
		public readonly ExperimentInfo exp_info;
		public readonly KerbalismSituation situation;
		public readonly CelestialBody body;
		public readonly string biome;
		public readonly bool isValid;

		public ExperimentSubject(ExperimentInfo exp_info, Vessel vessel)
		{
			this.exp_info = exp_info;
			situation = exp_info.GetSituation(vessel);
			isValid = exp_info.IsAvailable(situation);

			if (exp_info.BiomeIsRelevant(situation))
				biome = Lib.GetBiome(vessel, exp_info.allowKSCBiomes);
			else
				biome = string.Empty;

			body = vessel.mainBody;

			if (isValid)
				subject_id = Lib.BuildString(exp_info.id, "@", body.name, situation.ToString(), biome.Replace(" ", string.Empty));
			else
				subject_id = string.Empty;	
		}

		// TODO : add an overload with an "out ExperimentSubject subject"
		// if return false, return the new subject else return the current one
		public bool HasChanged(Vessel vessel)
		{
			KerbalismSituation current_sit = exp_info.GetSituation(vessel);
			if (situation != current_sit)
				return false;
			if (!exp_info.IsAvailable(current_sit))
				return false;
			if (body != vessel.mainBody)
				return false;
			if (exp_info.BiomeIsRelevant(current_sit) && Lib.GetBiome(vessel, exp_info.allowKSCBiomes) != biome)
				return false;
			return true;
		}

		public long DataSizeForScienceValue(double scienceValue)
		{
			return (long)((scienceValue * exp_info.dataScale) / BodyScienceValue());
		}

		/// <summary>
		/// return science credits value for a subject
		/// </summary>
		public double ScienceValueBase(long size = 0)
		{
			if (size == 0)
				size = exp_info.dataSize;

			// get value of the subject
			return Math.Min(size / exp_info.dataScale * BodyScienceValue(), exp_info.scienceCap);
		}

		/// <summary>
		/// return science credits value for a subject, with the ScienceGainMultiplier applied
		/// </summary>
		public double ScienceValueGame(long size = 0)
		{
			return ScienceValueBase(size) * HighLogic.CurrentGame.Parameters.Career.ScienceGainMultiplier;
		}

		/// <summary>
		/// return remaining science credits for a subject,
		/// accounting for data retrieved in RnD only,
		/// and with the ScienceGainMultiplier applied
		/// </summary>
		public double ScienceValueRemainingRnD(long size = 0)
		{
			if (size == 0)
				size = exp_info.dataSize;

			return ScienceValueRemaining(ScienceValueBase(size));
		}

		/// <summary>
		/// return remaining science credits for a subject,
		/// accounting for data retrieved in RnD and data present in all vessel drive,
		/// and with the ScienceGainMultiplier applied
		/// </summary>
		public double ScienceValueRemainingTotal(long size = 0)
		{
			if (size == 0)
				size = exp_info.dataSize;

			size -= Science.GetStoredData(subject_id);
			if (size < double.Epsilon)
				return 0;

			return ScienceValueRemaining(ScienceValueBase(size));
		}

		private double ScienceValueRemaining(double subjectScienceValueBase)
		{
			// get already collected value in RnD
			ScienceSubject RnD_subject = ResearchAndDevelopment.GetSubjectByID(subject_id);
			double storedScienceValue = RnD_subject != null ? RnD_subject.science : 0;

			// get science value
			// - the stock system 'degrade' science value after each credit, we don't
			double credits = Math.Max(Math.Min(storedScienceValue + subjectScienceValueBase, exp_info.scienceCap) - storedScienceValue, 0.0);

			credits *= HighLogic.CurrentGame.Parameters.Career.ScienceGainMultiplier;
			return credits;
		}

		public long DataRemainingTotal()
		{
			long size = exp_info.dataSize;

			// get already collected data in all vessels
			size -= Science.GetStoredData(subject_id);

			// get already collected subject in RnD
			ScienceSubject RnD_subject = ResearchAndDevelopment.GetSubjectByID(subject_id);
			float bodyScienceValue = BodyScienceValue();
			if (RnD_subject != null && bodyScienceValue > 0)
			{
				// substract size of data collected in RnD
				size -= (long)((RnD_subject.science * exp_info.dataScale) / bodyScienceValue);
			}

			if (size < 0) return 0;
			return size;
		}

		public float BodyScienceValue()
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

		public override string ToString()
		{
			string biomeStr = string.IsNullOrEmpty(biome) ? string.Empty : Lib.BuildString(" ", biome);
			return Lib.BuildString(
				exp_info.experimentTitle,
				" while ",
				ExperimentInfo.SituationString(situation),
				" at ",
				body.displayName,
				biomeStr);
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
	}
}
