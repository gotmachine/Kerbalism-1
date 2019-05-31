using System;
using System.Collections.Generic;
using UnityEngine;


namespace KERBALISM
{


	public static class DB
	{




		public static void Load(ConfigNode node)
		{
			// get version (or use current one for new savegames)
			version = Lib.ConfigValue(node, "version", Lib.Version());

			// get unique id (or generate one for new savegames)
			uid = Lib.ConfigValue(node, "uid", Lib.RandomInt(int.MaxValue));

			// if this is an unsupported version, print warning
			if (string.CompareOrdinal(version, "1.1.5.0") < 0)
			{
				Lib.Log("loading save from unsupported version " + version);
			}

			// load kerbals data
			kerbals = new Dictionary<string, KerbalData>();
			if (node.HasNode("kerbals"))
			{
				foreach (var kerbal_node in node.GetNode("kerbals").GetNodes())
				{
					kerbals.Add(From_safe_key(kerbal_node.name), new KerbalData(kerbal_node));
				}
			}

			// load vessels data
			vessels = new Dictionary<Guid, VesselData>();
			if (node.HasNode("vessels2")) // old vessels used flightId, we switched to Guid with vessels2
			{
				foreach (var vessel_node in node.GetNode("vessels2").GetNodes())
				{
					vessels.Add(Lib.Parse.ToGuid(vessel_node.name), new VesselData(vessel_node));
				}
			}

			// load drives
			drives = new Dictionary<uint, Drive>();
			if (node.HasNode("drives")) // old vessels used flightId, we switched to Guid with vessels2
			{
				foreach (var drive_node in node.GetNode("drives").GetNodes())
				{
					uint partId = Lib.Parse.ToUInt(drive_node.name);
					drives.Add(partId, new Drive(drive_node, version, partId));
				}
			}

			// load bodies data
			bodies = new Dictionary<string, BodyData>();
			if (node.HasNode("bodies"))
			{
				foreach (var body_node in node.GetNode("bodies").GetNodes())
				{
					bodies.Add(From_safe_key(body_node.name), new BodyData(body_node));
				}
			}

			// load dataprocesses
			processes = new Dictionary<uint, List<DataProcess>>();
			if (node.HasNode("processes"))
			{
				foreach (var process_node in node.GetNode("processes").GetNodes())
				{
					DataProcess process = DataProcess.Load(process_node);
					if (process == null) continue;
					AddDataProcess(Lib.Parse.ToUInt(process_node.name), process);
				}
			}

			// load landmark data
			if (node.HasNode("landmarks"))
			{
				landmarks = new LandmarkData(node.GetNode("landmarks"));
			}
			else
			{
				landmarks = new LandmarkData();
			}

			// load ui data
			if (node.HasNode("ui"))
			{
				ui = new UIData(node.GetNode("ui"));
			}
			else
			{
				ui = new UIData();
			}

			// if an old savegame was imported, log some debug info
			if (version != Lib.Version()) Lib.Log("savegame converted from version " + version);
		}

		public static void Save(ConfigNode node)
		{
			// save version
			node.AddValue("version", Lib.Version());

			// save unique id
			node.AddValue("uid", uid);

			// save kerbals data
			var kerbals_node = node.AddNode("kerbals");
			foreach (var p in kerbals)
			{
				p.Value.Save(kerbals_node.AddNode(To_safe_key(p.Key)));
			}

			// save vessels data
			var vessels_node = node.AddNode("vessels2");
			foreach (var p in vessels)
			{
				p.Value.Save(vessels_node.AddNode(p.Key.ToString()));
			}

			// save drives
			var drives_node = node.AddNode("drives");
			foreach (var p in drives)
			{
				p.Value.Save(drives_node.AddNode(p.Key.ToString()));
			}

			// save bodies data
			var bodies_node = node.AddNode("bodies");
			foreach (var p in bodies)
			{
				p.Value.Save(bodies_node.AddNode(To_safe_key(p.Key)));
			}

			// save processes
			var processes_node = node.AddNode("processes");
			foreach (var p in processes)
			{
				for (int i = 0; i < p.Value.Count; i++)
				{
					p.Value[i].Save(processes_node.AddNode(p.Key.ToString()));
				}
			}

			// save landmark data
			landmarks.Save(node.AddNode("landmarks"));

			// save ui data
			ui.Save(node.AddNode("ui"));
		}


		public static KerbalData Kerbal(string name)
		{
			if (!kerbals.ContainsKey(name))
			{
				kerbals.Add(name, new KerbalData());
			}
			return kerbals[name];
		}


		public static VesselData Vessel(Vessel v)
		{
			if (!vessels.ContainsKey(Lib.VesselID(v)))
			{
				vessels.Add(Lib.VesselID(v), new VesselData());
			}
			return vessels[Lib.VesselID(v)];
		}


		public static Drive Drive(uint partId, string partTitle, long fileCapacity, long sampleCapacity, bool isPrivate)
		{
			if(!drives.ContainsKey(partId))
			{
				var d = new Drive(partId, partTitle, fileCapacity, sampleCapacity, isPrivate);
				drives.Add(partId, d);
			}
			return drives[partId];
		}

		public static BodyData Body(string name)
		{
			if (!bodies.ContainsKey(name))
			{
				bodies.Add(name, new BodyData());
			}
			return bodies[name];
		}

		public static DataProcess GetDataProcess(uint partId, string processId)
		{
			if (processes.ContainsKey(partId))
			{
				for (int i = 0; i < processes[partId].Count; i++)
				{
					if (processes[partId][i].processId == processId) return processes[partId][i];
				}
			}
			return null;
		}

		public static bool AddDataProcess(uint partId, DataProcess process)
		{
			if (processes.ContainsKey(partId))
			{
				for (int i = 0; i < processes[partId].Count; i++)
				{
					if (processes[partId][i].processId == process.processId)
					{
						Lib.Log("WARNING : trying to create a duplicate of dataProcess '" + process.processId + "'. " +
							"Duplicate science PartModules are not allowed !");
						return false;
					}
				}
				processes[partId].Add(process);
			}
			processes.Add(partId, new List<DataProcess> { process });
			return true;
		}

		public static bool RemoveDataProcess(uint partId, DataProcess process)
		{
			if (processes.ContainsKey(partId))
			{
				for (int i = 0; i < processes[partId].Count; i++)
				{
					if (processes[partId][i].processId == process.processId)
					{
						processes[partId].RemoveAt(i);
						return true;
					}
				}
			}
			return false;
		}

		// Note : because DB.subjects must only contain >valid< subjects and because
		// subject_id is not known when a new subject is created, this doesn't do the subject creation
		/// <summary>
		/// return an existing subject, or null if it doesn't exist yet.
		/// For creating a new subject, use the Subject.GetOrCreate() method
		/// </summary>
		public static Subject GetSubject(string subject_id)
		{
			if (subjects.ContainsKey(subject_id))
			{
				return subjects[subject_id];
			}
			return null;
		}

		public static Boolean ContainsKerbal(string name)
		{
			return kerbals.ContainsKey(name);
		}

		/// <summary>
		/// Remove a Kerbal and his lifetime data from the database
		/// </summary>
		public static void KillKerbal(String name, bool reallyDead)
		{
			if(reallyDead) {
				kerbals.Remove(name);
			} else {
				// called when a vessel is destroyed. don't remove the kerbal just yet,
				// check with the roster if the kerbal is dead or not
				Kerbal(name).Recover();
			}
		}

		/// <summary>
		/// Resets all process data of a kerbal, except lifetime data
		/// </summary>
		public static void RecoverKerbal(string name)
		{
			if(ContainsKerbal(name))
			{
				if(Kerbal(name).eva_dead)
				{
					kerbals.Remove(name);
				} else {
					Kerbal(name).Recover();
				}
			}
		}

		public static Dictionary<string, KerbalData> Kerbals() {
			return kerbals;
		}

		public static string To_safe_key(string key) { return key.Replace(" ", "___"); }
		public static string From_safe_key(string key) { return key.Replace("___", " "); }

		public static string version;                          // savegame version
		public static int uid;                                 // savegame unique id
		private static Dictionary<string, KerbalData> kerbals; // store data per-kerbal
		public static Dictionary<Guid, VesselData> vessels;    // store data per-vessel, indexed by root part id
		public static Dictionary<uint, Drive> drives;         // all drives, of all vessels, indexed by part id
		public static Dictionary<uint, List<DataProcess>> processes; // all science processes, of all vessels, indexed by part id, as a list (multiple processes per part)
		public static Dictionary<string, Subject> subjects;     // store science/experiment subjects. 
		public static Dictionary<string, BodyData> bodies;     // store data per-body
		public static LandmarkData landmarks;                  // store landmark data
		public static UIData ui;                               // store ui data
	}


} // KERBALISM



