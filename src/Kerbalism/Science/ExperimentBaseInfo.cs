using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace KERBALISM
{
	/// <summary>
	/// Defined in a EXPERIMENT_BASEINFO node, it is our version the stock ScienceExperiment (EXPERIMENT_DEFINITION in configs)
	/// <para/> Will override any stock EXPERIMENT_DEFINITION with the same id
	/// <para/> Any stock EXPERIMENT_DEFINITION not overridden will also be copied as an ExperimentBaseInfo
	/// </summary>
	public sealed class ExperimentBaseInfo
	{
		public string id;

		public string title;

		public float baseValue;

		public float scienceCap;

		public List<string> situations;

		public List<string> biomeMask;

		public uint situationMask;

		public uint biomeMask;

		public float dataScale;

		// TODO : when loading a non overridden stock def, map the stock requirements to all variants
		// public bool requireAtmosphere;
		// public float requiredExperimentLevel;


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

			// TEMPORARY
			ExperimentBaseInfo exp_baseinfo = new ExperimentBaseInfo();
			ExperimentSituations sit = ExperimentSituations.FlyingHigh;
			CelestialBody body = new CelestialBody();

			// FORMULA RECREATION
			double dataScale = exp_baseinfo.dataScale; // subject.dataScale = ExperimentBaseInfo.dataScale
			double subjectValue; // subject.subjectValue 
			switch (sit)
			{
				case ExperimentSituations.SrfLanded:
					subjectValue = body.scienceValues.LandedDataValue;
					break;
				case ExperimentSituations.SrfSplashed:
					subjectValue = body.scienceValues.SplashedDataValue;
					break;
				case ExperimentSituations.FlyingLow:
					subjectValue = body.scienceValues.FlyingLowDataValue;
					break;
				case ExperimentSituations.InSpaceHigh:
					subjectValue = body.scienceValues.InSpaceHighDataValue;
					break;
				case ExperimentSituations.InSpaceLow:
					subjectValue = body.scienceValues.InSpaceLowDataValue;
					break;
				case ExperimentSituations.FlyingHigh:
					subjectValue = body.scienceValues.FlyingHighDataValue;
					break;
				default:
					subjectValue = 1f;
					break;
			}

			// ResearchAndDevelopment.GetReferenceDataValue(size, subject) -> size / subject.dataScale * subject.subjectValue
			double R = size / dataScale * subjectValue;

			double S = subject.science;
			double C = subject.scienceCap;

			// get science value
			// - the stock system 'degrade' science value after each credit, we don't
			double R = ResearchAndDevelopment.GetReferenceDataValue((float)size, subject);
			// 

			double S = subject.science;
			double C = subject.scienceCap;
			double credits = Math.Max(Math.Min(S + Math.Min(R, C), C) - S, 0.0);

			credits *= HighLogic.CurrentGame.Parameters.Career.ScienceGainMultiplier;

			return (float)credits;
		}
	}
}
