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
					drive = new Drive(title, dataCapacity, sampleCapacity); // Why do we need a drive in the editor ?
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

		#region IScienceDataContainer implementation (stock interface)

		// Note on this : while the interface technically work, it is very likely that using it will lead to weird behavior
		// Currently, in a stock game, the interface will never be used.
		// It's purpose is to allow compatibility with mods that use the stock experiment module.
		// But mods that implement custom modules that try to manipulate to stock science data will probably cause issues.

		// Also, note on EVA / limited capacity : there is no way to abort a boarding event once it has been initiated
		// Boarding can be disabled by setting HighLogic.CurrentGame.Parameters.Flight.CanBoard to false, but that doesn't help
		// What we can do instead is to get the protected "KerbalEVA.currentAirlockPart" property on the EVA partmodule,
		// when it's not null, that mean that the "[press B to board]" indication is shown and the player can board
		// What we can do is that while not null, if data capacity isn't enough, post a warning message at regular interval

		public ScienceData[] GetData()
		{
			// generate and return stock science data
			List<ScienceData> data = new List<ScienceData>();
			for (int i = 0; i < drive.Count; i++)
			{
				float xmitScalar = drive[i].type == FileType.File ? 1f : 0f;
				data.Add(new ScienceData((float)Lib.BitToMB(drive[i].size), xmitScalar, xmitScalar, drive[i].subject_id, drive[i].title));
			}
			return data.ToArray();
		}

		public void ReturnData(ScienceData data)
		{
			FileType type = data.baseTransmitValue > 0 || data.transmitBonus > 0 ? FileType.File : FileType.Sample;
			long dataSize = Lib.MBToBit(data.dataAmount);

			// complete partial results
			foreach (ExperimentResult result in Drive.FindPartialResults(vessel, data.subjectID, type, 0, out long totalData))
			{
				long dataAdded = Math.Min(result.maxSize - result.size, dataSize);
				result.size += dataAdded;
				dataSize -= dataAdded;
				if (dataSize <= 0) return;
			}

			ExperimentInfo expInfo = Science.GetExperimentInfoFromSubject(data.subjectID);

			while (dataSize > 0)
			{
				Drive drive = Drive.GetDriveBestCapacity(vessel, type);
				if (drive == null)
				{
					string sizeStr;

					if (type == FileType.File)
					{
						sizeStr = Lib.HumanReadableDataSize(dataSize);
					}
					else
					{
						if (expInfo != null)
							sizeStr = Lib.HumanReadableSampleSlotAndMass(dataSize, expInfo.massPerBit);
						else
							sizeStr = Lib.HumanReadableSampleSlots(dataSize);
					}

					Message.Post(Severity.warning, Lib.BuildString(
						"Not enough space available to store '",
						data.title,
						"', ",
						sizeStr,
						" were lost."));
					return;
				}
				else
				{

					long maxSize = expInfo != null ? expInfo.dataSize : 0;
					double massPerBit = expInfo != null ? expInfo.massPerBit : 0;
					long dataAdded = Math.Min(drive.SizeCapacityAvailable(type, data.subjectID), dataSize);
					dataSize -= dataAdded;
					new ExperimentResult(drive, type, data.subjectID, data.title, dataAdded, maxSize, massPerBit);
				}
			}
		}

		// we don't want anything from stock or other mods to be able to delete our data ?
		public void DumpData(ScienceData data)
		{
			// remove the data
			//if (data.baseTransmitValue > float.Epsilon || data.transmitBonus > double.Epsilon)
			//{
			//	drive.Delete_file(data.subjectID, data.dataAmount);
			//}
			//else
			//{
			//	drive.Delete_sample(data.subjectID, data.dataAmount);
			//}
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

		#endregion

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


