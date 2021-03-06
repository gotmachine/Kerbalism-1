﻿using System.Collections.Generic;
using System;
using KSP.Localization;
using CommNet;
using KSPAssets;

#if !KSP170 && !KSP16 && !KSP15 && !KSP14
using Expansions.Serenity.DeployedScience.Runtime;

namespace KERBALISM
{
	internal class AntennaInfoSerenity: AntennaInfoCommNet
	{
		private DeployedScienceCluster cluster;

		public AntennaInfoSerenity(Vessel v, DeployedScienceCluster cluster, bool storm, bool transmitting)
			: base(v, cluster.IsPowered, storm, transmitting)
		{
			this.cluster = cluster;
		}

		override public AntennaInfo AntennaInfo()
		{
			AntennaInfo result = base.AntennaInfo();
			result.ec = 0;
			result.rate = 0;
			result.powered = cluster.IsPowered;
			if(result.powered)
				result.rate = Settings.DataRateSurfaceExperiment;

			Init();

			return result;
		}
	}
}
#endif
