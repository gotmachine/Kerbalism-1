using System;
using CommNet;
using KSP.Localization;

namespace KERBALISM
{
	public static class ConnManager
	{
		/// <summary>
		/// Shows the Network status, ControlPath, Signal strength
		/// </summary>
		public static void ConnMan(this Panel p, Vessel v)
		{
			// avoid corner-case when this is called in a lambda after scene changes
			v = FlightGlobals.FindVessel(v.id);

			// if vessel doesn't exist anymore, leave the panel empty
			if (v == null) return;

			// get info from the cache
			Vessel_info vi = Cache.VesselInfo(v);

			// if not a valid vessel, leave the panel empty
			if (!vi.is_valid) return;

			// set metadata
			p.Title(Lib.BuildString(Lib.Ellipsis(v.vesselName, Styles.ScaleStringLength(40)), " <color=#cccccc>CONNECTION MANAGER</color>"));
			p.Width(Styles.ScaleWidthFloat(365.0f));
			p.paneltype = Panel.PanelType.connection;

			// time-out simulation
			if (p.Timeout(vi)) return;

			// draw ControlPath section
			p.AddSection("CONTROL PATH");
			if (vi.connection.linked && vi.connection.link_legs != null)
			{
				foreach(var leg in vi.connection.link_legs)
					p.AddContent(leg.text, Lib.HumanReadablePerc(leg.linkQuality), leg.tooltip);
			}
			else p.AddContent("<i>no connection</i>", string.Empty);
		}
	}
}
