using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;



namespace KERBALISM
{

	public sealed class HardDrive : PartModule, IScienceDataContainer, ISpecifics, IModuleInfo, IPartMassModifier
	{
		[KSPField] public double dataCapacity = -1;             // drive capacity, in Mb. -1 = unlimited
		[KSPField] public int sampleCapacity = -1;              // drive capacity, in slots. -1 = unlimited
		[KSPField] public string title = "Kerbodyne ZeroBit";   // drive name to be displayed in file manager
		[KSPField] public string experiment_id = string.Empty;  // if set, restricts write access to the experiment on the same part, with the given experiment_id.

		[KSPField(isPersistant = true)] public uint hdId = 0;

		[KSPField(guiActive = false, guiName = "Science storage", guiActiveEditor = true)] public string capacity;

		[KSPField(guiName = "Experiments mode", guiActiveEditor = false, guiActive = true, isPersistant = false),
		UI_Toggle(controlEnabled = true, disabledText = "Manual", enabledText = "Smart", invertButton = false, scene = UI_Scene.All)]
		public bool smartScience = true;

		private Drive drive;
		private double totalSampleMass;

		public override void OnStart(StartState state)
		{
			// don't break tutorial scenarios
			if (Lib.DisableScenario(this)) return;

			if (hdId == 0) hdId = part.flightID;

			if(drive == null)
			{
				if (!Lib.IsFlight())
					drive = new Drive(title, dataCapacity, sampleCapacity);
				else
					drive = DB.Drive(hdId, title, dataCapacity, sampleCapacity);
			}

			if(vessel != null) Cache.RemoveVesselObjectsCache(vessel, "drives");

			drive.is_private |= experiment_id.Length > 0;
			UpdateSampleMass();

			if (Lib.IsFlight())
			{
				if (PreferencesScience.Instance.smartScience)
				{
					Fields["smartScience"].guiActive = true;
					Fields["smartScience"].guiActiveEditor = false;
					smartScience = DB.Vessel(vessel).cfg_smartscience;
					Fields["smartScience"].uiControlFlight.onFieldChanged = SwitchSmartScience;
				}
				else
				{
					Fields["smartScience"].guiActive = false;
					smartScience = false;
					DB.Vessel(vessel).cfg_smartscience = false;
				}
			}

		}

		private void SwitchSmartScience(BaseField arg1, object arg2)
		{
			// stop all experiements when switching to manual mode
			if (!smartScience) vessel.FindPartModulesImplementing<Experiment>().ForEach(ex => ex.Stop(null));
			// propagate to all other toggles on the vessel and to the config value
			vessel.FindPartModulesImplementing<HardDrive>().ForEach(hd => hd.smartScience = smartScience);
			DB.Vessel(vessel).cfg_smartscience = smartScience;
		}

		public override void OnLoad(ConfigNode node)
		{
			base.OnLoad(node);

			if (HighLogic.LoadedScene == GameScenes.LOADING)
			{
				drive = new Drive();
				return;
			}
		}

		public void SetDrive(Drive drive)
		{
			this.drive = drive;
			drive.is_private |= experiment_id.Length > 0;
			UpdateSampleMass();
		}

		public void FixedUpdate()
		{
			UpdateSampleMass();
		}

		public void Update()
		{
			capacity = GetStorageInfo();
			if (Lib.IsFlight())
			{
				// show DATA UI button, with size info
				Events["ToggleUI"].guiName = Lib.StatusToggle("Science", capacity);
				Events["ToggleUI"].active = true; // !IsPrivate();

				// show TakeData eva action button, if there is something to take
				Events["TakeData"].active = !drive.Empty();

				// show StoreData eva action button, if active vessel is an eva kerbal and there is something to store from it
				Vessel v = FlightGlobals.ActiveVessel;
				Events["StoreData"].active = !IsPrivate() && v != null && v.isEVA && !EVA.IsDead(v);

				// hide TransferLocation button
				var transferVisible = !IsPrivate();
				if(transferVisible)
				{
					transferVisible = Drive.GetDrives(vessel, true).Count > 1;
				}
				Events["TransferData"].active = transferVisible;
				Events["TransferData"].guiActive = transferVisible;
			}
		}

		public bool IsPrivate()
		{
			return drive.is_private;
		}

		private void UpdateSampleMass()
		{
			double mass = 0;
			foreach (var sample in drive.samples.Values) mass += sample.mass;
			totalSampleMass = mass;
		}

		public string GetStorageInfo()
		{
			StringBuilder capacitySB = new StringBuilder();

			if (Lib.IsFlight())
			{
				if (dataCapacity > 0)
				{
					capacitySB.Append(Lib.HumanReadableDataSize(drive.FilesSize(), dataCapacity));
				}
				else if (dataCapacity == -1)
				{
					capacitySB.Append(Lib.HumanReadableDataSize(drive.FilesSize()));
					capacitySB.Append(" used");
				}

				if (sampleCapacity != 0)
				{
					if (dataCapacity != 0)
						capacitySB.Append(", ");

					capacitySB.Append(drive.SamplesSize());
					if (sampleCapacity > 0)
					{
						capacitySB.Append("/");
						capacitySB.Append(sampleCapacity);
						capacitySB.Append(" slot");
					}
					else
					{
						capacitySB.Append(" slot used");
					}
					if (drive.SamplesSize() > 0)
					{
						double totalMass = 0;
						foreach (var sample in drive.samples.Values) totalMass += sample.mass;
						capacitySB.Append(" (");
						capacitySB.Append(Lib.HumanReadableMass(totalMass));
						capacitySB.Append(")");
					}
				}
			}
			else
			{
				if (dataCapacity != 0)
				{
					capacitySB.Append("data ");
					if (dataCapacity > 0)
						capacitySB.Append(Lib.HumanReadableDataSize(dataCapacity));
					else if (dataCapacity == -1)
						capacitySB.Append("infinite");
				}

				if (sampleCapacity != 0)
				{
					if (dataCapacity != 0)
						capacitySB.Append(", ");

					capacitySB.Append("sample ");
					if (sampleCapacity > 0)
					{
						capacitySB.Append(sampleCapacity);
						capacitySB.Append(" slot");
					}
					else if (sampleCapacity == -1)
						capacitySB.Append("infinite");
				}

			}
			return capacitySB.ToStringAndRelease();
		}

		public Drive GetDrive()
		{
			return drive;
		}

		[KSPEvent(guiActive = true, guiName = "_", active = true)]
		public void ToggleUI()
		{
			UI.Open((Panel p) => p.Fileman(vessel));
		}


		[KSPEvent(guiName = "#KERBALISM_HardDrive_TransferData", active = false)]
		public void TransferData()
		{
			var hardDrives = vessel.FindPartModulesImplementing<HardDrive>();
			foreach(var hardDrive in hardDrives)
			{
				if (hardDrive == this) continue;
				hardDrive.drive.Move(drive, PreferencesScience.Instance.sampleTransfer || Lib.CrewCount(vessel) > 0);
			}
		}


		[KSPEvent(guiActive = false, guiActiveUnfocused = true, guiActiveUncommand = true, guiName = "#KERBALISM_HardDrive_TakeData", active = true)]
		public void TakeData()
		{
			// disable for dead eva kerbals
			Vessel v = FlightGlobals.ActiveVessel;
			if (v == null || EVA.IsDead(v)) return;

			// transfer data
			if(!Drive.Transfer(drive, v, PreferencesScience.Instance.sampleTransfer || Lib.CrewCount(v) > 0))
			{
				Message.Post
				(
					Lib.Color("red", Lib.BuildString("WARNING: not evering copied"), true),
					Lib.BuildString("Storage is at capacity")
				);
			}
		}


		[KSPEvent(guiActive = false, guiActiveUnfocused = true, guiActiveUncommand = true, guiName = "#KERBALISM_HardDrive_TransferData", active = true)]
		public void StoreData()
		{
			// disable for dead eva kerbals
			Vessel v = FlightGlobals.ActiveVessel;
			if (v == null || EVA.IsDead(v)) return;

			// transfer data
			if(!Drive.Transfer(v, drive, PreferencesScience.Instance.sampleTransfer || Lib.CrewCount(v) > 0))
			{
				Message.Post
				(
					Lib.Color("red", Lib.BuildString("WARNING: not evering copied"), true),
					Lib.BuildString("Storage is at capacity")
				);
			}
		}


		// part tooltip
		public override string GetInfo()
		{
			return Specs().Info();
		}


		// science container implementation
		public ScienceData[] GetData()
		{
			// generate and return stock science data
			List<ScienceData> data = new List<ScienceData>();
			foreach (var pair in drive.files)
			{
				File file = pair.Value;
				var exp = Science.Experiment(pair.Key);
				data.Add(new ScienceData((float)file.size, 1.0f, 1.0f, pair.Key, exp.FullName(pair.Key)));
			}
			foreach (var pair in drive.samples)
			{
				Sample sample = pair.Value;
				var exp = Science.Experiment(pair.Key);
				data.Add(new ScienceData((float)sample.size, 0.0f, 0.0f, pair.Key, exp.FullName(pair.Key)));
			}
			return data.ToArray();
		}

		// TODO do something about limited capacity...
		// EVAs returning should get a warning if needed
		public void ReturnData(ScienceData data)
		{
			// store the data
			bool result = false;
			if (data.baseTransmitValue > float.Epsilon || data.transmitBonus > double.Epsilon)
			{
				result = drive.Record_file(data.subjectID, data.dataAmount);
			}
			else
			{
				var experimentInfo = Science.Experiment(data.subjectID);
				var sampleMass = Science.GetSampleMass(data.subjectID);
				var mass = sampleMass / experimentInfo.data_max * data.dataAmount;

				result = drive.Record_sample(data.subjectID, data.dataAmount, mass);
			}
		}

		public void DumpData(ScienceData data)
		{
			// remove the data
			if (data.baseTransmitValue > float.Epsilon || data.transmitBonus > double.Epsilon)
			{
				drive.Delete_file(data.subjectID, data.dataAmount);
			}
			else
			{
				drive.Delete_sample(data.subjectID, data.dataAmount);
			}
		}

		public void ReviewData()
		{
			UI.Open((p) => p.Fileman(vessel));
		}

		public void ReviewDataItem(ScienceData data)
		{
			ReviewData();
		}

		public int GetScienceCount()
		{
			// We are forced to return zero, or else EVA kerbals re-entering a pod
			// will complain about being unable to store the data (but they shouldn't)
			return 0;

			/*Drive drive = DB.Vessel(vessel).drive;

			// if not the preferred drive
			if (drive.location != part.flightID) return 0;

			// return number of entries
			return drive.files.Count + drive.samples.Count;*/
		}

		public bool IsRerunnable()
		{
			// don't care
			return false;
		}

		//public override string GetModuleDisplayName() { return "Hard Drive"; }

		// specifics support
		public Specifics Specs()
		{
			Specifics specs = new Specifics();
			specs.Add("File capacity", dataCapacity >= 0 ? Lib.HumanReadableDataSize(dataCapacity) : "unlimited");
			specs.Add("Sample capacity", sampleCapacity >= 0 ? Lib.HumanReadableSampleSize(sampleCapacity) : "unlimited");
			return specs;
		}

		// module info support
		public string GetModuleTitle() { return "Hard Drive"; }
		public override string GetModuleDisplayName() { return "Hard Drive"; }
		public string GetPrimaryField() { return string.Empty; }
		public Callback<Rect> GetDrawModulePanelCallback() { return null; }

		// module mass support
		public float GetModuleMass(float defaultMass, ModifierStagingSituation sit) { return (float)totalSampleMass; }
		public ModifierChangeWhen GetModuleMassChangeWhen() { return ModifierChangeWhen.CONSTANTLY; }
	}


} // KERBALISM


