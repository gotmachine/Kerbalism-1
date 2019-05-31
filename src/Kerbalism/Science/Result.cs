using System;
using System.Collections.Generic;
using UnityEngine;

namespace KERBALISM
{
	/// <summary>
	/// Represent drive content, either a file (can be transmitted) or a sample (has mass).
	/// </summary>
	public sealed class Result
	{
		// persisted fields

		/// <summary> File or Sample </summary>
		public FileType type;
		/// <summary> for experiments, format is "experiment_id@situation" </summary>
		public string subjectId;
		/// <summary> flagged for transfer to another drive </summary>
		public bool transfer;
		/// <summary> file : flagged for transmission, sample : flagged for analysis in a lab </summary>
		public bool process;
		/// <summary> result size in bit (for samples, 1 slot = 1024 MB) </summary>
		private long size;
		/// <summary> file only : data transmitted but not credited </summary>
		private long bufferSize;
		/// <summary>
		/// NOT IMPLEMENTED : the max amount of science this result can yeld, regardless of the base value
		/// would need to be set from the processes in Process(), after the result has been created in Science.Update()
		/// </summary>
		public float scienceCap;

		// non persisted field. Those are used for UI purposes AND to know if empty Results should be deleted.

		/// <summary> file only : current transmit rate in bit/s. If 0, file wasn't transmitted during last simulation step</summary>
		public long transmitRate;
		/// <summary> rate at which data is added in bit/s (from DataProcesses)</summary>
		public long processRate;

		// references :

		/// <summary> the Subject for this result, noatbly contain the ExperiementInfo reference </summary>
		private Subject subject;
		/// <summary>
		/// Reference to the drive this result is on.
		/// Never change or set to null, use the Delete() method to delete a result,
		/// and use the Drive methods to transfer between drives
		/// </summary>
		private Drive drive;

		// shorthand properties for often used fields from Subject or ExperimentInfo

		/// <summary> file only : size limit for bufferSize </summary>
		public long MaxBufferSize => subject.MaxBufferSize;
		/// <summary>max size in bit</summary>
		public long MaxSize => subject.expInfo.fullSize;
		/// <summary>experiment base science points value (body multiplier and ScienceGainMultiplier not applied)</summary>
		public double MaxScienceValue => subject.expInfo.scienceValue;
		/// <summary>sample only : mass in ton/bit</summary>
		public double MassPerBit => subject.expInfo.massPerBit;
		/// <summary> will be shown as the main title in the file manager</summary>
		public string Title => subject.Title;
		/// <summary> will be shown as a subtext in the file manager</summary>
		public string Situation => subject.SubjectTitle();
		/// <summary> will be shown as a tooltip in the file manager</summary>
		public string Description => subject.Description();

		// getters for private fields (size must only be changed using the Add/Remove methods)
		public long Size => size;
		public long SizeWithBuffer => size + BufferSize;
		public double SizeMB => Lib.BitToMB(size);
		/// <summary> the drive this result is stored on</summary>
		public Drive GetDrive => drive;
		/// <summary> this result's subject</summary>
		public Subject Subject => subject;

		/// <summary>
		/// Create the result and add it to the provided drive. 
		/// If an error occur the object will be GC'ed unless the calling code keep a reference to it. 
		/// In case the calling code keep a reference, validity of the result should be checked by using the "IsDeleted()" method
		/// </summary>
		public Result(Drive drive, ConfigNode node)
		{
			subjectId = Lib.ConfigValue(node, "subject_id", "invalid");
			size = Lib.ConfigValue(node, "size", -1);
			transfer = Lib.ConfigValue(node, "transfer", false);
			process = Lib.ConfigValue(node, "process", false);
			bufferSize = Lib.ConfigValue(node, "transmitBuffer", 0);

			switch (Lib.ConfigValue(node, "type", -1))
			{
				case (int)FileType.File: type = FileType.File; break;
				case (int)FileType.Sample: type = FileType.Sample; break;
				default:
					Lib.Log("LOADING ERROR : result '" + subjectId + "' wasn't loaded from save, type was invalid.");
					return;
			}

			subject = Science.GetSubjectFromCache(subjectId);

			if (subject == null)
			{
				Lib.Log("LOADING ERROR : Subject not found for result with subject_id '" + subjectId + "'");
				return;
			}

			// if we return before setting the references, the object should be GC'ed
			this.drive = drive;
			drive.Add(this);
		}

		/// <summary>main ctor</summary>
		public Result(Drive drive, FileType type, Subject subject, long size = 0)
		{
			this.subject = subject;
			this.type = type;
			this.size = size;
			subjectId = subject.SubjectId;
			transfer = false;
			process = type == FileType.File ? PreferencesScience.Instance.transmitScience : PreferencesScience.Instance.analyzeSamples;
			bufferSize = 0;

			if (!subject.isValid)
			{
				Lib.Log("WARNING : Creating a result with invalid subject : '" + subject.SubjectId + "'");
				return;
			}

			// if we return before setting the references, the object should be GC'ed
			this.drive = drive;
			drive.Add(this);
		}

		/// <summary>generic constructor for custom results (Stock, API...)</summary>
		/// <param name="size">in bit</param>
		/// <param name="maxSize">in bit, max allowed is 1024 Terabyte</param>
		/// <param name="scienceValue">in science point for maxSize</param>
		/// <param name="massPerBit">in ton/bit</param>
		public Result(Drive drive, FileType type, string subject_id, long size = 0)
		{
			this.type = type;
			this.size = size;
			this.subjectId = subject_id;
			transfer = false;
			process = type == FileType.File ? PreferencesScience.Instance.transmitScience : PreferencesScience.Instance.analyzeSamples;
			bufferSize = 0;

			subject = Science.GetSubjectFromCache(subject_id);
			if (subject == null)
			{
				Lib.Log("ERROR : Subject not found for result with subject_id '" + subject_id + "'");
				return;
			}

			// if we return before setting the references, the object should be GC'ed
			this.drive = drive;
			drive.Add(this);
		}

		/// <summary>
		/// constructor for the Drive IList interface methods, used for result transfers between drives
		/// </summary>
		/// <param name="size">if == 0, the oldResult.size will be used</param>
		/// <param name="index">if >= 0, the result will be insered at index instead of added at the end</param>
		public Result(Drive drive, Result oldResult, long size = 0, int index = -1)
		{
			this.drive = drive;
			if (index == -1)
				drive.Add(this);
			else
				drive.Insert(index, this);

			type = oldResult.type;
			this.size = size == 0 ? oldResult.size : size;
			subject = oldResult.subject;
			subjectId = oldResult.subjectId;
			transfer = false; // this ctor is used for copy/transfers : always reset the transfer flag
			process = oldResult.process;
			bufferSize = oldResult.BufferSize;
		}

		public void Save(ConfigNode node)
		{
			node.AddValue("type", (int)type);
			node.AddValue("subject_id", subjectId);
			node.AddValue("title", Title);
			node.AddValue("size", size);
			node.AddValue("transfer", transfer);
			node.AddValue("process", process);
		}

		/// <summary> science value of the result, ignoring the ScienceGainMultiplier</summary>
		public double ScienceValueBase(long dataSize = 0)
		{
			if (dataSize == 0) dataSize = size;
			return ((double)dataSize / (double)MaxSize) * MaxScienceValue * subject.BodyScienceValue();
		}

		/// <summary> science value of the result, including the ScienceGainMultiplier</summary>
		public double ScienceValueGame(long dataSize = 0)
		{
			return ScienceValueBase(dataSize) * HighLogic.CurrentGame.Parameters.Career.ScienceGainMultiplier;
		}


		/// <summary> get the stock ScienceSubject object that should be stored in RnD. </summary>
		public ScienceSubject ParseToStockSubject(bool onlyTransmitBuffer)
		{
			// Comments are how (I think) stock is setting each value :

			// set at subject creation : ScienceExperiment.dataScale
			float dataScale = (float)(MaxSize / MaxScienceValue);

			// set at subject creation : body multiplier
			float subjectValue = subject.BodyScienceValue();

			// set at subject creation : ScienceExperiment.scienceCap * body multiplier
			float scienceCap = (float)(MaxScienceValue * subjectValue);

			// the current amount of science point stored, set to 0 at subject creation
			// increased every time some science points are credited (SubmitScienceData() method)
			// HighLogic.CurrentGame.Parameters.Career.ScienceGainMultiplier NOT applied
			float science = onlyTransmitBuffer ?
				(float)ScienceValueBase(BufferSize) :
				(float)ScienceValueBase(size + BufferSize);

			ScienceSubject stockSubject = new ScienceSubject(subjectId, Title, dataScale, subjectValue, scienceCap);
			stockSubject.science = science;
			stockSubject.scientificValue = ResearchAndDevelopment.GetSubjectValue(stockSubject.science, stockSubject);
			return stockSubject;
		}

		/// <summary> Will delete the result from the drive and allow the object to be GC'ed</summary>
		public void Delete()
		{
			drive.Remove(this);
		}

		public bool IsDeleted()
		{
			return drive == null;
		}

		/// <summary> Do not use unless you already have deleted the reference to the result in the drive</summary>
		public void DeleteDriveRef()
		{
			Science.AddFlightSubjectData(subjectId, -size);
			drive = null;
		}

		/// <summary>
		/// Add some data to the result. Return false if not everything has been stored,
		/// dataAmount is changed to the actual amount added
		/// </summary>
		public bool AddData(ref long dataAmount)
		{
			if (dataAmount > MaxSize - size)
			{
				dataAmount = MaxSize - size;
				size += dataAmount;
				Science.AddFlightSubjectData(subjectId, dataAmount);
				return false;
			}
			size += dataAmount;
			Science.AddFlightSubjectData(subjectId, dataAmount);
			return true;
		}

		/// <summary>
		/// Try to add some data to the result and return the actual amount added.
		/// </summary>
		public long AddData(long dataAmount)
		{
			dataAmount = Math.Min(dataAmount, MaxSize - size);
			size += dataAmount;
			Science.AddFlightSubjectData(subjectId, dataAmount);
			return dataAmount;
		}

		/// <summary>
		/// Remove some data from the result. Return false if the result is now empty,
		/// dataAmount is changed to the actual amount removed
		/// </summary>
		public bool RemoveData(ref long dataAmount)
		{
			if (dataAmount > size)
			{
				dataAmount = size;
				size = 0;
				Science.AddFlightSubjectData(subjectId, -dataAmount);
				return false;
			}
			size -= dataAmount;
			Science.AddFlightSubjectData(subjectId, -dataAmount);
			return true;
		}

		/// <summary>
		/// Set the data size to zero
		/// </summary>
		public void RemoveAllData()
		{
			Science.AddFlightSubjectData(subjectId, -size);
			size = 0;
		}

		// always update the global in-flight data cache when changing the buffer size
		public long BufferSize
		{
			get => bufferSize;

			set
			{
				Science.AddFlightSubjectData(subjectId, value - bufferSize);
				bufferSize = value;
			}
		}


		/// <summary>mass of the result. Files will return 0.</summary>
		public double GetMass()
		{
			if (type == FileType.Sample)
				return size * MassPerBit;
			else
				return 0.0;
		}

		/// <summary> return true if transfer conditions are met</summary>
		public bool CanTransfer(Vessel vessel)
		{
			return CanTransfer(Lib.CrewCount(vessel));
		}

		/// <summary> return true if transfer conditions are met</summary>
		public bool CanTransfer(int crewCount)
		{
			return type == FileType.File || (type == FileType.Sample && (PreferencesScience.Instance.sampleTransfer || crewCount > 0));
		}

		/// <summary>
		/// returns size (in bits) available on this result drive
		/// </summary>
		public long DriveCapacityAvailable()
		{
			return drive.CapacityAvailable(type);
		}
	}
}
