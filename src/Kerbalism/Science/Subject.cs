using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace KERBALISM
{

	// the subject object is referenced from the DataProcess and Result objects
	// it is NOT serialized, but a dictionary of all >valid< subjects is generated at startup
	// the < 1 Mb memory cost is a small price for the execution time (and code complexity) savings
	// it's also used to keep track of how much science has been done globally
	public class Subject
	{
		// unique id for the whole game
		public string SubjectId				{ get; protected set; }

		// subject ID components
		public ExperimentInfo expInfo		{ get; protected set; }
		public CelestialBody body			{ get; protected set; }
		public KerbalismSituation situation { get; protected set; }
		public string biome					{ get; protected set; }

		// max size of the results transmit buffer
		public long MaxBufferSize			{ get; protected set; } 

		// DataProcess will generate invalid subjects that are used to keep track of situation changes
		// these invalid subjects should NEVER be passed to a Result or to the subject cache
		public bool isValid					{ get; protected set; }

		// often used data caching, recalculated on savegame loading
		public long dataStoredRnD;		// kept synchronized with the stock ScienceSubject "science" field (Science.Credit() method)
		public long dataStoredFlight;	// kept synchronized every time some data is generated/destroyed in flight


		/// <summary>ctor for building the global subject cache</summary>
		public Subject(ExperimentInfo expInfo, KerbalismSituation situation, CelestialBody body, string biome, bool isValid)
		{
			this.expInfo = expInfo;
			this.situation = situation;
			this.body = body;
			this.biome = biome;
			this.isValid = isValid;
			SubjectId = Lib.BuildString(expInfo.id, "@", body.name, situation.ToString(), biome.Replace(" ", string.Empty));
			dataStoredRnD = 0;
			dataStoredFlight = 0;
			MaxBufferSize = DataSizeForScienceValue(Science.buffer_science_value);
		}

		/// <summary>currently, this ctor should only be used to create invalid subjects</summary>
		private Subject(ExperimentInfo expInfo, Vessel vessel, bool isValid)
		{
			this.expInfo = expInfo;
			situation = expInfo.GetSituation(vessel);
			body = vessel.mainBody;
			dataStoredRnD = 0;
			dataStoredFlight = 0;
			MaxBufferSize = DataSizeForScienceValue(Science.buffer_science_value);

			isValid = situation != KerbalismSituation.None;

			if (expInfo.BiomeIsRelevant(situation))
				biome = Lib.GetBiome(vessel, expInfo.allowMiniBiomes);
			else
				biome = string.Empty;

			if (isValid)
				SubjectId = Lib.BuildString(expInfo.id, "@", body.name, situation.ToString(), biome.Replace(" ", string.Empty));
			else
				SubjectId = string.Empty;
		}

		/// <summary>
		/// create/retrieve the subject object for the vessel current situation
		/// return false if the subject is invalid (situation not available according to the ExperimentInfo definition)
		/// </summary>
		public static Subject GetCurrentSubject(ExperimentInfo exp_info, Vessel vessel)
		{
			// create the subject_id
			string subject_id = GetCurrentSubjectId(exp_info, vessel);

			// try to get it from the cache
			Subject subject = Science.GetSubjectFromCache(subject_id);
			if (subject != null)
				return subject;

			// if not found, the situation isn't valid for this subject (cache contains all the valid subjects)
			subject = new Subject(exp_info, vessel, false);
			return subject;
		}

		public static string GetCurrentSubjectId(ExperimentInfo exp_info, Vessel vessel)
		{
			// get the situation
			KerbalismSituation situation = exp_info.GetSituation(vessel);

			// if biome is relevant, return subject with biome string
			if (exp_info.BiomeIsRelevant(situation))
				return Lib.BuildString(exp_info.id, "@", vessel.mainBody.name, situation.ToString(),
					Lib.GetBiome(vessel, exp_info.allowMiniBiomes).Replace(" ", string.Empty));

			// else it's just body and situation
			else
				return Lib.BuildString(exp_info.id, "@", vessel.mainBody.name, situation.ToString());
		}

		public bool HasChanged(Vessel vessel)
		{
			KerbalismSituation current_sit = expInfo.GetSituation(vessel);
			if (situation != current_sit)
				return true;
			if (body != vessel.mainBody)
				return true;
			if (expInfo.BiomeIsRelevant(current_sit) && Lib.GetBiome(vessel, expInfo.allowMiniBiomes) != biome)
				return true;
			return false;
		}

		public long DataSizeForScienceValue(double scienceValue)
		{
			return (long)((scienceValue * expInfo.dataScale) / BodyScienceValue());
		}

		public float BodyScienceValue()
		{
			return ExperimentInfo.BodyScienceValue(body, situation);
		}

		/// <summary>
		/// return science credits value for a subject of the given size
		/// (if size == -1, the max size is used)
		/// </summary>
		public double ScienceValueBase(long size = -1)
		{
			if (size < 0)
				size = expInfo.fullSize;

			// get value of the subject
			return size / expInfo.dataScale * BodyScienceValue();
		}

		/// <summary>
		/// data amount already retrieved in RnD
		/// </summary>
		public long DataStoredInRnD()
		{
			return DataSizeForScienceValue(ScienceCreditBaseStoredInRnD());
		}

		/// <summary>
		/// science credits already retrieved in RnD, with the ScienceGainMultiplier NOT applied
		/// </summary>
		public double ScienceCreditBaseStoredInRnD()
		{
			ScienceSubject RnD_subject = ResearchAndDevelopment.GetSubjectByID(SubjectId);
			return RnD_subject != null ? RnD_subject.science : 0;
		}

		/// <summary>
		/// return science credits value for a subject of the given size,
		/// minus what has already been credited (typically credits retrieved in RnD)
		/// (if size == 0, the max size is used)
		/// </summary>
		public double ScienceValueRemaining(double subjectScienceValueBase)
		{
			// note : stock apply scienceCap and do a "lerping" degradation in its formula, we don't
			double credits = Math.Max(subjectScienceValueBase - ScienceCreditBaseStoredInRnD(), 0.0);

			credits *= HighLogic.CurrentGame.Parameters.Career.ScienceGainMultiplier;
			return credits;
		}

		/// <summary>
		/// return remaining science credits for a subject,
		/// accounting for data already retrieved in RnD,
		/// and with the ScienceGainMultiplier applied
		/// </summary>
		public double ScienceValueRemainingInRnD(long size = -1)
		{
			if (size < 0)
				size = expInfo.fullSize;

			return ScienceValueRemaining(ScienceValueBase(size));
		}

		/// <summary>
		/// return remaining science credits for a subject,
		/// accounting for data retrieved in RnD and data present in all vessel drive,
		/// and with the ScienceGainMultiplier applied
		/// </summary>
		public double ScienceValueRemainingTotal(long size = -1)
		{
			if (size < 0) size = expInfo.fullSize;
			size -= Science.GetFlightSubjectData(SubjectId);
			if (size < 0) return 0;

			return ScienceValueRemaining(ScienceValueBase(size));
		}

		/// <summary>
		/// return science credits value for a subject,
		/// with the stock ScienceGainMultiplier applied
		/// </summary>
		public double ScienceValueGame(long size = -1)
		{
			return ScienceValueBase(size) * HighLogic.CurrentGame.Parameters.Career.ScienceGainMultiplier;
		}

		public string ScienceValueInfo(bool showUncredited = true)
		{
			StringBuilder sb = new StringBuilder(50);
			sb.Append("<color=#00ffffff>");
			sb.Append(ScienceValueGame(dataStoredFlight + dataStoredRnD).ToString("F1"));
			sb.Append(" / ");
			sb.Append(ScienceValueGame().ToString("F1"));
			sb.Append("</color>");
			if (showUncredited)
			{
				double totsci = ScienceValueRemainingInRnD();
				if (totsci > 0)
				{
					sb.Append(" (Uncredited : <color=#00ffffff>");
					sb.Append(totsci.ToString("F1"));
					sb.Append("</color>)");
				}
			}
			return sb.ToString();
		}

		public long DataRemainingTotal()
		{
			return Math.Max(expInfo.fullSize - Science.GetFlightSubjectData(SubjectId) - DataStoredInRnD(), 0);
		}



		/// <summary>
		/// return a UI friendly title for the subject
		/// </summary>
		public string Title => expInfo.title;

		/// <summary>
		/// return a UI friendly subtitle for the subject
		/// </summary>
		public string SubjectTitle(bool shortForm = false)
		{
			if (!isValid)
				return Lib.BuildString(body.name, ": invalid situation");

			if (shortForm)
				if (string.IsNullOrEmpty(biome))
					return Lib.BuildString(body.name, "/", ExperimentInfo.SituationString(situation));
				else
					return Lib.BuildString(body.name, "/", ExperimentInfo.SituationString(situation), "/", biome);

			if (string.IsNullOrEmpty(biome))
				return Lib.BuildString(ExperimentInfo.SituationString(situation)," at ", body.name);
			else
				return Lib.BuildString(ExperimentInfo.SituationString(situation), " at ", body.name, " (", biome, ")");
		}

		/// <summary>
		/// return a UI friendly description for the subject
		/// </summary>
		public string Description() => string.Empty;
	}
}
