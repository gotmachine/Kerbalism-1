using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;



namespace KERBALISM
{

	public sealed class HardDrive : PartModule, IScienceDataContainer, ISpecifics, IModuleInfo, IPartMassModifier
	{
		[KSPField] public double dataCapacity = -1;             // drive capacity, in Mb. -1 = unlimited
		[KSPField] public double sampleCapacity = -1;           // drive capacity, in Mb. -1 = unlimited
		[KSPField] public string experiment_id = string.Empty;  // if set, restricts write access to the experiment on the same part, with the given experiment_id.

		[KSPField(isPersistant = true)]
		private bool isPrivate = false; 

		[KSPField(guiActive = false, guiActiveEditor = true, guiName = "Data storage")]
		public string capacity = string.Empty;

		private Drive drive;
		private float moduleMass;

		public override void OnStart(StartState state)
		{
			// don't break tutorial scenarios
			if (Lib.DisableScenario(this))
				return;

			if (drive == null && Lib.IsFlight())
				drive = DB.Drive(part.flightID, part.partInfo.title, dataCapacity > 0 ? Lib.MBToBit(dataCapacity) : -1, sampleCapacity > 0 ? Lib.MBToBit(sampleCapacity) : -1, isPrivate);

			moduleMass = GetMass();

			// What is this for ? Mid flight part adding mods (KIS...) compatibility maybe ?
			if (vessel != null) Cache.RemoveVesselObjectsCache(vessel, "drives"); 
		}

		/// <summary> this is called by the pseudo-ctor DataProcess.GetProcessOnPartModuleLoad </summary>
		public void SetPrivate(bool isPrivate)
		{
			this.isPrivate = isPrivate;
			if (drive != null) drive.isPrivate = isPrivate;
		}

		public void FixedUpdate()
		{
			moduleMass = GetMass();
		}

		public void Update()
		{
			if (Lib.IsFlight())
			{
				// show DATA UI button, with size info
				Events["DataManager"].guiName = Lib.StatusToggle("Data manager", GetStorageInfo());
				Events["DataManager"].active = true; // !IsPrivate();

				bool activeVesselIsEVA =
					FlightGlobals.ActiveVessel != null
					&& FlightGlobals.ActiveVessel.isEVA
					&& !EVA.IsDead(FlightGlobals.ActiveVessel);

				// show TakeData eva action button, if there is something to take
				Events["EVATakeData"].active = activeVesselIsEVA && drive.Empty();

				// show StoreData eva action button, if the drive isn't private
				Events["EVAStoreData"].active = activeVesselIsEVA && !IsPrivate();

				// don't show transfer button for private drives and if there is only one drive
				bool transferVisible = !IsPrivate() && Drive.GetDrives(vessel).Count > 1;
				Events["TransferData"].active = transferVisible;
				Events["TransferData"].guiActive = transferVisible;
			}
			else
			{
				capacity = GetStorageInfo();
			}
		}

		public bool IsPrivate()
		{
			return drive.isPrivate;
		}

		private float GetMass()
		{
			if (drive != null) return (float)drive.GetMass();
			else return 0f;
		}

		public string GetStorageInfo()
		{
			// drive is only available in flight
			if (drive != null)
				return drive.GetStorageInfo();

			StringBuilder sb = new StringBuilder();

			if (dataCapacity != 0)
			{
				sb.Append("files : ");
				if (dataCapacity > 0)
					sb.Append(Lib.HumanReadableDataSize(dataCapacity));
				else if (dataCapacity == -1)
					sb.Append("unlimited");
			}

			if (sampleCapacity != 0)
			{
				if (dataCapacity != 0)
					sb.Append(", ");

				sb.Append("samples : ");
				if (sampleCapacity > 0)
				{
					sb.Append(sampleCapacity);
					sb.Append(" slot");
				}
				else if (sampleCapacity == -1)
					sb.Append("unlimited");
			}

			return sb.ToString();
		}

		public Drive GetDrive()
		{
			return drive;
		}

		[KSPEvent(guiActive = true, guiName = "_", active = true)]
		public void DataManager()
		{
			UI.Open((Panel p) => p.Fileman(vessel));
		}

		// TODO : for the 3 transfer events, in the button label, add the size of the data to be transferred and the available space in the destination 
		[KSPEvent(guiName = "#KERBALISM_HardDrive_TransferData", active = false)]
		public void TransferData()
		{
			// transfer results from the whole vessel
			Drive.GetAllResultsToTransfer(vessel).ForEach(result => drive.Add(result));
		}


		// Note on EVA / limited capacity : there is no way to abort a boarding event once it has been initiated
		// Boarding can be disabled by setting HighLogic.CurrentGame.Parameters.Flight.CanBoard to false, but that doesn't help
		// What we can do instead is to get the protected "KerbalEVA.currentAirlockPart" property on the EVA partmodule,
		// when it's not null, that mean that the "[press B to board]" indication is shown and the player can board
		// What we can do is that while not null, if data capacity isn't enough, post a warning message at regular interval

		[KSPEvent(guiActive = false, guiActiveUnfocused = true, guiActiveUncommand = true, guiName = "#KERBALISM_HardDrive_TakeData", active = true)]
		public void EVATakeData()
		{
			// disable for dead eva kerbals
			Vessel evaVessel = FlightGlobals.ActiveVessel;
			if (evaVessel == null || EVA.IsDead(evaVessel)) return;

			// get the EVA kerbal drive
			Drive evaDrive = Drive.GetDrives(evaVessel).FirstOrDefault();
			if (evaDrive == null) return;

			// transfer results from this drive
			drive.GetResultsToTransfer(vessel).ForEach(result => evaDrive.Add(result));


			//Message.Post
			//	(
			//		Lib.Color("red", Lib.BuildString("WARNING: not evering copied"), true),
			//		Lib.BuildString("Storage is at capacity")
			//	);
		}

		[KSPEvent(guiActive = false, guiActiveUnfocused = true, guiActiveUncommand = true, guiName = "#KERBALISM_HardDrive_TransferData", active = true)]
		public void EVAStoreData()
		{
			// disable for dead eva kerbals
			Vessel evaVessel = FlightGlobals.ActiveVessel;
			if (evaVessel == null || EVA.IsDead(evaVessel)) return;

			// get the EVA kerbal drive
			Drive evaDrive = Drive.GetDrives(evaVessel).FirstOrDefault();
			if (evaDrive == null) return;

			// transfer results to this drive
			evaDrive.GetResultsToTransfer(vessel).ForEach(result => drive.Add(result));

			//Message.Post
			//(
			//	Lib.Color("red", Lib.BuildString("WARNING: not evering copied"), true),
			//	Lib.BuildString("Storage is at capacity")
			//);
		}


		// part tooltip
		public override string GetInfo()
		{
			return Specs().Info();
		}

		#region IScienceDataContainer implementation (stock interface)

		// Note on this : currently, in a stock game, the interface will never be used.
		// It's purpose is to allow compatibility with mods that use the stock experiment module.
		// While it should technically work, mods implementing custom modules that try
		// to manipulate to stock science data will probably have issues.

		public ScienceData[] GetData()
		{
			// generate and return stock science data
			List<ScienceData> data = new List<ScienceData>();
			for (int i = 0; i < drive.Count; i++)
			{
				float xmitScalar = drive[i].type == FileType.File ? 1f : 0f;
				data.Add(new ScienceData((float)Lib.BitToMB(drive[i].Size), xmitScalar, xmitScalar, drive[i].subjectId, drive[i].Title));
			}
			return data.ToArray();
		}

		public void ReturnData(ScienceData data)
		{
			FileType type = data.baseTransmitValue > 0 || data.transmitBonus > 0 ? FileType.File : FileType.Sample;
			long dataSize = Lib.MBToBit(data.dataAmount);

			// complete partial results
			foreach (Result result in Drive.FindPartialResults(vessel, data.subjectID, type, 0, out long totalData))
			{
				dataSize -= result.AddData(dataSize);
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
					long dataAdded = Math.Min(drive.CapacityAvailable(type), dataSize);
					dataSize -= dataAdded;
					new Result(drive, type, data.subjectID, dataAdded);
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

		// specifics support
		public Specifics Specs()
		{
			Specifics specs = new Specifics();
			specs.Add("File capacity", dataCapacity >= 0 ? Lib.HumanReadableDataSize(dataCapacity) : "unlimited");
			specs.Add("Sample capacity", sampleCapacity >= 0 ? sampleCapacity.ToString("F1") : "unlimited");
			return specs;
		}

		// module info support
		public string GetModuleTitle() { return "Data storage"; }
		public override string GetModuleDisplayName() { return "Data storage"; }
		public string GetPrimaryField() { return string.Empty; }
		public Callback<Rect> GetDrawModulePanelCallback() { return null; }

		// module mass support
		public float GetModuleMass(float defaultMass, ModifierStagingSituation sit) { return moduleMass; }
		public ModifierChangeWhen GetModuleMassChangeWhen() { return ModifierChangeWhen.CONSTANTLY; }
	}


} // KERBALISM


