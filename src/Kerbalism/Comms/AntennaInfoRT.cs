using System;
using System.Collections.Generic;

namespace KERBALISM
{
	/// <summary> Return antenna info for RemoteTech </summary>
	public sealed class AntennaInfoRT: IAntennaInfo
	{
		private double rate = 0.0;
		private double ec = 0.0;
		private bool linked;
		private LinkStatus linkStatus;
		private string target_name;
		private double strength;
		private LinkLeg[] linkLegs;

		public AntennaInfoRT(Vessel v)
		{
			// if vessel is loaded, don't calculate ec, RT already handle it.
			if (v.loaded)
			{
				// find transmitters
				foreach (Part p in v.parts)
				{
					foreach (PartModule m in p.Modules)
					{
						// calculate internal (passive) transmitter ec usage @ 0.5W each
						if (m.moduleName == "ModuleRTAntennaPassive") ec += 0.0005;
						// calculate external transmitters
						else if (m.moduleName == "ModuleRTAntenna")
						{
							// only include data rate and ec cost if transmitter is active
							if (Lib.ReflectionValue<bool>(m, "IsRTActive"))
							{
								rate += (Lib.ReflectionValue<float>(m, "RTPacketSize") / Lib.ReflectionValue<float>(m, "RTPacketInterval"));
							}
						}
					}
				}
			}
			// if vessel is not loaded
			else
			{
				// find proto transmitters
				foreach (ProtoPartSnapshot p in v.protoVessel.protoPartSnapshots)
				{
					// get part prefab (required for module properties)
					Part part_prefab = PartLoader.getPartInfoByName(p.partName).partPrefab;
					foreach (ProtoPartModuleSnapshot m in p.modules)
					{
						// calculate internal (passive) transmitter ec usage @ 0.5W each
						if (m.moduleName == "ModuleRTAntennaPassive") ec += 0.0005;
						// calculate external transmitters
						else if (m.moduleName == "ModuleRTAntenna")
						{
							// only include data rate and ec cost if transmitter is active
							if (Lib.Proto.GetBool(m, "IsRTActive"))
							{
								bool mFound = false;
								// get all modules in prefab
								foreach (PartModule pm in part_prefab.Modules)
								{
									if (pm.moduleName == m.moduleName)
									{
										mFound = true;
										ModuleResource mResource = pm.resHandler.inputResources.Find(r => r.name == "ElectricCharge");
										float? packet_size = Lib.SafeReflectionValue<float>(pm, "RTPacketSize");
										float? packet_Interval = Lib.SafeReflectionValue<float>(pm, "RTPacketInterval");

										// workaround for old savegames
										if (mResource == null || packet_size == null || packet_Interval == null)
										{
											Lib.DebugLog("Old SaveGame PartModule ModuleRTAntenna for part '{0}' on unloaded vessel '{1}', using default values as a workaround", p.partName, v.vesselName);
											Lib.DebugLog("ElectricCharge isNull: '{0}', RTPacketSize isNull: '{1}', RTPacketInterval isNull: '{2}'", mResource == null, packet_size == null, packet_Interval == null);
											rate += 6.6666;          // 6.67 Mb/s in 100% factor
											ec += 0.025;             // 25 W/s
										}
										else
										{
											rate += (float)packet_size / (float)packet_Interval;
											ec += mResource.rate;
										}
									}
								}
								if (!mFound)
								{
									Lib.DebugLog("Could not find PartModule ModuleRTAntenna for part {0} on unloaded vessel {1}, using default values as a workaround", p.partName, v.vesselName);
									rate += 6.6666;          // 6.67 Mb/s in 100% factor
									ec += 0.025;             // 25 W/s
								}
							}
						}
					}
				}
			}

			linked = RemoteTech.Connected(v.id);
			if (!linked) linked = RemoteTech.ConnectedToKSC(v.id);
			if (!linked)
			{
				if (RemoteTech.GetCommsBlackout(v.id))
					linkStatus = LinkStatus.plasma;
				else
					linkStatus = LinkStatus.no_link;
				ec = 0;
				return;
			}

			linkStatus = RemoteTech.TargetsKSC(v.id) ? LinkStatus.direct_link : LinkStatus.indirect_link;
			target_name = linkStatus == LinkStatus.direct_link
				? Lib.Ellipsis("DSN: " + (RemoteTech.NameTargetsKSC(v.id) ?? ""), 20)
				: Lib.Ellipsis(RemoteTech.NameFirstHopToKSC(v.id) ?? "", 20);

			var controlPath = RemoteTech.GetCommsControlPath(v.id);

			// Get the lowest rate in ControlPath
			if (controlPath != null)
			{
				// Get rate from the firstHop, each Hop will do the same logic, then we will have the lowest rate for the path
				if (controlPath.Length > 0)
				{
					double dist = RemoteTech.GetCommsDistance(v.id, controlPath[0]);
					strength = 1 - (dist / Math.Max(RemoteTech.GetCommsMaxDistance(v.id, controlPath[0]), 1));

					// If using relay, get the lowest rate
					if (linkStatus != LinkStatus.direct_link)
					{
						Vessel target = FlightGlobals.FindVessel(controlPath[0]);
						strength *= Cache.VesselInfo(target).connection.strength;
						rate = Math.Min(Cache.VesselInfo(target).connection.rate, rate * strength);
					}
					else
						rate *= strength;
				}

				List<LinkLeg> legs = new List<LinkLeg>();
				Guid i = v.id;
				foreach (Guid id in controlPath)
				{
					LinkLeg leg = new LinkLeg();

					leg.text = Lib.Ellipsis(RemoteTech.GetSatelliteName(i) + " \\ " + RemoteTech.GetSatelliteName(id), 35);
					leg.linkQuality = Math.Ceiling((1 - (RemoteTech.GetCommsDistance(i, id) / RemoteTech.GetCommsMaxDistance(i, id))) * 10000) / 10000;
					leg.tooltip = "\nDistance: " + Lib.HumanReadableRange(RemoteTech.GetCommsDistance(i, id)) +
						"\nMax Distance: " + Lib.HumanReadableRange(RemoteTech.GetCommsMaxDistance(i, id));
					legs.Add(leg);

					i = id;
				}
				linkLegs = legs.ToArray();
			}
		}

		public bool Linked => linked;
		public double EcConsumption => ec;
		public double DataRate => rate;
		public LinkStatus LinkStatus => linkStatus;
		public double Strength => strength;
		public string TargetName => target_name;
		public LinkLeg[] LinkLegs => linkLegs;
	}
}
