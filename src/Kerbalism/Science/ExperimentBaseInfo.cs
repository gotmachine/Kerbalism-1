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


		Science
	}
}
