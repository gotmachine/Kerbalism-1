using System;
using KSP.Localization;

namespace KERBALISM
{
	/// <summary> Stores a single vessels communication info</summary>
	public sealed class ConnectionInfo
	{
		/// <summary> true if there is a connection back to DSN </summary>
		public bool linked = false;

		/// <summary> status of the connection </summary>
		public LinkStatus status = LinkStatus.no_link;

		/// <summary> Link leg info for UI </summary>
		public LinkLeg[] link_legs;

		/// <summary> science data rate. note that internal transmitters can not transmit science data only telemetry data </summary>
		public double rate = 0.0;

		/// <summary> transmitter ec cost</summary>
		public double ec = 0.0;

		/// <summary> signal strength </summary>
		public double strength = 0.0;

		/// <summary> receiving node name </summary>
		public string target_name = string.Empty;

		// constructor
		/// <summary> Creates a <see cref="ConnectionInfo"/> object for the specified vessel from it's antenna modules</summary>
		public ConnectionInfo(Vessel v, bool powered, bool storm)
		{
			// set RemoteTech powered and storm state
			if (RemoteTech.Enabled)
			{
				RemoteTech.SetPoweredDown(v.id, !powered);
				RemoteTech.SetCommsBlackout(v.id, storm);
			}

			// return no connection if there is no ec left
			if (!powered)
			{
				// hysteresis delay
				if (DB.Vessel(v).hyspos_signal >= 5.0)
				{
					DB.Vessel(v).hyspos_signal = 5.0;
					DB.Vessel(v).hysneg_signal = 0.0;
					return;
				}
				DB.Vessel(v).hyspos_signal += 0.1;
			}
			else
			{
				// hysteresis delay
				DB.Vessel(v).hysneg_signal += 0.1;
				if (DB.Vessel(v).hysneg_signal < 5.0)
					return;
				DB.Vessel(v).hysneg_signal = 5.0;
				DB.Vessel(v).hyspos_signal = 0.0;
			}

			IAntennaInfo ai = null;

			if (RemoteTech.Enabled) // RemoteTech signal system
				ai = new AntennaInfoRT(v);
			else if (API.GetAntennaInfoFactory() != null)
				ai = API.GetAntennaInfoFactory().Create(v, storm);
			else if (HighLogic.fetch.currentGame.Parameters.Difficulty.EnableCommNet)
				ai = new AntennaInfoCommNet(v, storm);

			if (ai != null)
			{ 
				linked = ai.Linked;
				if(linked)
				{
					ec = ai.EcConsumption;
					rate = ai.DataRate * PreferencesBasic.Instance.transmitFactor;
					status = ai.LinkStatus;
					strength = ai.Strength;
					target_name = ai.TargetName;
					link_legs = ai.LinkLegs;
				}
			}
			// the simple stupid always connected signal system
			else
			{
				AntennaInfoCommNet antennaInfo = new AntennaInfoCommNet(v, storm);
				ec = antennaInfo.EcConsumption * 0.16; // Consume 16% of the stock ec. Workaround for drain consumption with CommNet, ec consumption turns similar of RT
				rate = antennaInfo.DataRate * PreferencesBasic.Instance.transmitFactor;

				linked = true;
				status = LinkStatus.direct_link;
				strength = 1;
				target_name = "DSN: KSC";
			}
		}
	}
} // KERBALISM
