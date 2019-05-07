using System;
using System.Collections.Generic;
using UnityEngine;


namespace KERBALISM
{


	public class VesselData
	{
		public VesselData()
		{
			msg_signal = false;
			msg_belt = false;
			cfg_ec = PreferencesMessages.Instance.ec;
			cfg_supply = PreferencesMessages.Instance.supply;
			cfg_signal = PreferencesMessages.Instance.signal;
			cfg_malfunction = PreferencesMessages.Instance.malfunction;
			cfg_storm = PreferencesMessages.Instance.storm;
			cfg_script = PreferencesMessages.Instance.script;
			cfg_highlights = PreferencesBasic.Instance.highlights;
			cfg_showlink = true;
			cfg_smartscience = true;
			storm_time = 0.0;
			storm_age = 0.0;
			storm_state = 0;
			group = "NONE";
			computer = new Computer();
			supplies = new Dictionary<string, SupplyData>();
			scansat_id = new List<uint>();
		}

		public VesselData(ConfigNode node)
		{
			msg_signal = Lib.ConfigValue(node, "msg_signal", false);
			msg_belt = Lib.ConfigValue(node, "msg_belt", false);
			cfg_ec = Lib.ConfigValue(node, "cfg_ec", PreferencesMessages.Instance.ec);
			cfg_supply = Lib.ConfigValue(node, "cfg_supply", PreferencesMessages.Instance.supply);
			cfg_signal = Lib.ConfigValue(node, "cfg_signal", PreferencesMessages.Instance.signal);
			cfg_malfunction = Lib.ConfigValue(node, "cfg_malfunction", PreferencesMessages.Instance.malfunction);
			cfg_storm = Lib.ConfigValue(node, "cfg_storm", PreferencesMessages.Instance.storm);
			cfg_script = Lib.ConfigValue(node, "cfg_script", PreferencesMessages.Instance.script);
			cfg_highlights = Lib.ConfigValue(node, "cfg_highlights", PreferencesBasic.Instance.highlights);
			cfg_showlink = Lib.ConfigValue(node, "cfg_showlink", true);
			cfg_smartscience = Lib.ConfigValue(node, "cfg_smartscience", true);
			storm_time = Lib.ConfigValue(node, "storm_time", 0.0);
			storm_age = Lib.ConfigValue(node, "storm_age", 0.0);
			storm_state = Lib.ConfigValue(node, "storm_state", 0u);
			group = Lib.ConfigValue(node, "group", "NONE");
			computer = node.HasNode("computer") ? new Computer(node.GetNode("computer")) : new Computer();

			supplies = new Dictionary<string, SupplyData>();
			foreach (var supply_node in node.GetNode("supplies").GetNodes())
			{
				supplies.Add(DB.From_safe_key(supply_node.name), new SupplyData(supply_node));
			}

			scansat_id = new List<uint>();
			foreach (string s in node.GetValues("scansat_id"))
			{
				scansat_id.Add(Lib.Parse.ToUInt(s));
			}
		}

		public void Save(ConfigNode node)
		{
			node.AddValue("msg_signal", msg_signal);
			node.AddValue("msg_belt", msg_belt);
			node.AddValue("cfg_ec", cfg_ec);
			node.AddValue("cfg_supply", cfg_supply);
			node.AddValue("cfg_signal", cfg_signal);
			node.AddValue("cfg_malfunction", cfg_malfunction);
			node.AddValue("cfg_storm", cfg_storm);
			node.AddValue("cfg_script", cfg_script);
			node.AddValue("cfg_highlights", cfg_highlights);
			node.AddValue("cfg_showlink", cfg_showlink);
			node.AddValue("cfg_smartscience", cfg_smartscience);
			node.AddValue("storm_time", storm_time);
			node.AddValue("storm_age", storm_age);
			node.AddValue("storm_state", storm_state);
			node.AddValue("group", group);
			computer.Save(node.AddNode("computer"));

			var supplies_node = node.AddNode("supplies");
			foreach (var p in supplies)
			{
				p.Value.Save(supplies_node.AddNode(DB.To_safe_key(p.Key)));
			}

			foreach (uint id in scansat_id)
			{
				node.AddValue("scansat_id", id.ToString());
			}
		}

		public SupplyData Supply(string name)
		{
			if (!supplies.ContainsKey(name))
			{
				supplies.Add(name, new SupplyData());
			}
			return supplies[name];
		}

		public ExperimentProcess GetExperimentProcess(Part part, string exp_info_id)
		{
			for (int i = 0; i < experiments.Count; i++)
			{
				if (experiments[i].part_id == part.flightID
					&& experiments[i].exp_info.id == exp_info_id)
					return experiments[i];
			}
			return null;
		}

		public ExperimentProcess AddExperimentProcess(Part part, string exp_info_id, double sample_amount, bool recording, bool forcedRun)
		{
			// having duplicates of an experiment on the same part is unsupported
			ExperimentProcess process = GetExperimentProcess(part, exp_info_id);
			if (process != null)
			{
				Lib.Log("WARNING : part '" + part.name + "' has a duplicate of experiment '" + exp_info_id + "'");
			}
			else
			{
				process = new ExperimentProcess(part, exp_info_id, sample_amount, recording, forcedRun);
				experiments.Add(process);
			}
			return process;
		}

		public bool msg_signal;       // message flag: link status
		public bool msg_belt;         // message flag: crossing radiation belt
		public bool cfg_ec;           // enable/disable message: ec level
		public bool cfg_supply;       // enable/disable message: supplies level
		public bool cfg_signal;       // enable/disable message: link status
		public bool cfg_malfunction;  // enable/disable message: malfunctions
		public bool cfg_storm;        // enable/disable message: storms
		public bool cfg_script;       // enable/disable message: scripts
		public bool cfg_highlights;   // show/hide malfunction highlights
		public bool cfg_showlink;     // show/hide link line
		public bool cfg_smartscience; // enable automatic experiement enabling/disabling
		public double storm_time;     // time of next storm (interplanetary CME)
		public double storm_age;      // time since last storm (interplanetary CME)
		public uint storm_state;      // 0: none, 1: inbound, 2: in progress (interplanetary CME)
		public string group;          // vessel group
		public Computer computer;     // store scripts
		public Dictionary<string, SupplyData> supplies; // supplies data
		public List<uint> scansat_id; // used to remember scansat sensors that were disabled

		// TODO : check performance using different type options
		// - most intensive use will be iterating over all elements in Science.Update() -> List is better
		// - second use is querying the ExpProcess from the partmodule.OnLoad/OnStart -> dictionary could be better
		// - because multiple experiements per part, would need a Dictionary<partId, ExpProcess[]> -> not great
		/// <summary>
		/// all ExperimentProcess for the vessel. Get/Add/Remove trough the *ExperimentProcess methods
		/// </summary>
		private List<ExperimentProcess> experiments;
	}


} // KERBALISM
