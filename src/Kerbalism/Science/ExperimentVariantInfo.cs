using System;
using System.Collections.Generic;
using UnityEngine;
using KSP.Localization;


namespace KERBALISM
{

	/// <summary>
	/// Stores information about an experiment and provide various static methods related to experiments and subjects
	/// </summary>
	public sealed class ExperimentVariantInfo
	{
		public ExperimentVariantInfo(ConfigNode node)
		{
			experiment_id = Lib.ConfigValue(node, "id", "");
			experiment_variant_id = Lib.ConfigValue(node, "variant_id", "");

			// ensure that the id is unique
			if (experiment_variant_id)
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
				expdef = ResearchAndDevelopment.GetExperiment(id);
			}
			catch (Exception e)
			{
				Lib.Log("ERROR: failed to load experiment " + id + ": " + e.Message);
				throw e;
			}

			// deduce short name for the subject
			title = expdef != null ? expdef.experimentTitle : Lib.UppercaseFirst(id);

			// deduce max data amount
			data_max = expdef != null ? expdef.baseValue * expdef.dataScale : double.MaxValue;
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
			s = MaskToString("Landed", 1, expdef.situationMask, expdef.biomeMask); if (!string.IsNullOrEmpty(s)) result.Add(s);
			s = MaskToString("Splashed", 2, expdef.situationMask, expdef.biomeMask); if (!string.IsNullOrEmpty(s)) result.Add(s);
			s = MaskToString("Flying Low", 4, expdef.situationMask, expdef.biomeMask); if (!string.IsNullOrEmpty(s)) result.Add(s);
			s = MaskToString("Flying High", 8, expdef.situationMask, expdef.biomeMask); if (!string.IsNullOrEmpty(s)) result.Add(s);
			s = MaskToString("In Space Low", 16, expdef.situationMask, expdef.biomeMask); if (!string.IsNullOrEmpty(s)) result.Add(s);
			s = MaskToString("In Space High", 32, expdef.situationMask, expdef.biomeMask); if (!string.IsNullOrEmpty(s)) result.Add(s);
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

		// TODO : this is a stub showing how we should register experiments in the RnD instance
		public static void AddExperimentToRnD(ExperimentVariantInfo exp_info)
		{
			var experiments = Lib.ReflectionValue<Dictionary<string, ScienceExperiment>>
			(
			  ResearchAndDevelopment.Instance,
			  "experiments"
			);

			var exp = new ScienceExperiment();
			exp.baseValue = exp_info.baseValue;
			exp.dataScale = exp_info.dataScale;
			...

			experiments.Add(exp_info.id, exp);
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

			ScienceSubject subject = new ScienceSubject(;
			CelestialBody body;
			body.scienceValues.FlyingHighDataValue


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

		private static string MaskToString(string text, uint flag, uint situationMask, uint biomeMask)
		{
			string result = string.Empty;
			if ((flag & situationMask) == 0) return result;
			result = text;
			if ((flag & biomeMask) != 0) result += " (Biomes)";
			return result;
		}

		/// <summary>stock experiment definition</summary>
		private ScienceExperiment expdef;

		/// <summary>title of the experiment. Keep short else UI won't be able to show everything.</summary>
		public string title { get; private set; }

		/// <summary>CFG : the base experiment is what will be registered as a result</summary>
		public string base_id { get; private set; }

		/// <summary>
		/// CFG : if, set allow module experiments to be upgraded / have variants. If set :
		/// - "variant_id" has to be used as the "experiment_id" in the experiment partmodule cfg
		/// - all variants will generate the same experiment results as the main definition
		/// - baseValue, scienceCap, situations, biomes and dataScale values will be those of the main definition
		/// </summary>
		public string variant_id { get; private set; }

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


	}

} // KERBALISM

