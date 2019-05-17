using System;
using System.Collections.Generic;

namespace KERBALISM
{

	/// <summary>
	/// Represent drive content, either a file (can be transmitted) or a sample (has mass).
	/// </summary>
	public sealed class ExperimentResult
	{

		/// <summary> File or Sample </summary>
		public FileType type;
		/// <summary> for experiments, format is "experiment_id@situation" </summary>
		public string subject_id;
		/// <summary> will be shown in the file manager and in RnD archives</summary>
		public string title;
		/// <summary> result size in bit (for samples, 1 slot = 1024 MB) </summary>
		public long size;
		/// <summary> flagged for transfer to another drive </summary>
		public bool transfer;
		/// <summary> file : flagged for transmission, sample : flagged for analysis in a lab </summary>
		public bool process;
		/// <summary> file only : data transmitted but not credited </summary>
		public long transmitBuffer;
		/// <summary> file only : size limit for transmit_buffer </summary>
		public readonly long bufferFull;
		/// <summary> file only : current transmit rate in bit/s. If 0, file wasn't transmitted during last simulation step</summary>
		public long transmit_rate;
		/// <summary>max size in bit</summary>
		public readonly long maxSize;
		/// <summary>sample only : mass in ton/bit</summary>
		public readonly double massPerBit;

		private Drive drive;
		public Drive GetDrive => drive; 

		/// <summary>
		/// Create the result and add it to the provided drive. 
		/// If an error occur the object will be GC'ed unless the calling code keep a reference to it. 
		/// In case the calling code keep a reference, validity of the result should be checked by using the "IsDeleted()" method
		/// </summary>
		public ExperimentResult(Drive drive, ConfigNode node)
		{

			subject_id = Lib.ConfigValue(node, "subject_id", "invalid");
			title = Lib.ConfigValue(node, "subject_id", "");
			size = Lib.ConfigValue(node, "size", -1);
			transfer = Lib.ConfigValue(node, "transfer", false);
			process = Lib.ConfigValue(node, "process", false);
			transmitBuffer = Lib.ConfigValue(node, "transmitBuffer", 0);
			bufferFull = Lib.ConfigValue(node, "bufferFull", 0);
			maxSize = Lib.ConfigValue(node, "maxSize", long.MaxValue / 2);
			massPerBit = Lib.ConfigValue(node, "massPerBit", 0);

			switch (Lib.ConfigValue(node, "type", "invalid"))
			{
				case nameof(FileType.File): type = FileType.File; break;
				case nameof(FileType.Sample): type = FileType.Sample; break;
				default:
					Lib.Log("WARNING : result '" + subject_id + "' wasn't loaded from save, type was invalid.");
					return;
			}

			if (subject_id == "invalid")
			{
				Lib.Log("WARNING : result with undefined subject_id wasn't loaded from save.");
				return;
			}
				
			if (size < 0)
			{
				Lib.Log("WARNING : result '" + subject_id + "' wasn't loaded from save, size was invalid");
				return;
			}
				
			// if we return before setting the references, the object will be GC'ed
			this.drive = drive;
			drive.Add(this);
		}

		public ExperimentResult(Drive drive, FileType type, ExperimentSubject subject, long size = 0)
		{
			this.drive = drive;
			drive.Add(this);

			this.type = type;
			this.size = size;
			subject_id = subject.subject_id;
			title = subject.ToString();
			transfer = false;
			process = type == FileType.File ? PreferencesScience.Instance.transmitScience : PreferencesScience.Instance.analyzeSamples;
			transmitBuffer = 0;
			maxSize = subject.exp_info.dataSize;
			massPerBit = subject.exp_info.massPerBit;
			bufferFull = subject.DataSizeForScienceValue(Science.buffer_science_value);
		}

		
		/// <summary>generic constructor for non-experiment results (API, scansat...).</summary>
		/// <param name="size">in bit</param>
		/// <param name="maxSize">in bit, max allowed is 1024 Terabyte</param>
		/// <param name="massPerBit">in ton/bit</param>
		public ExperimentResult(Drive drive, FileType type, string subject_id, string title, long size = 0, long maxSize = 0, double massPerBit = 0)
		{
			this.drive = drive;
			drive.Add(this);

			this.type = type;
			this.size = size;
			this.subject_id = subject_id;
			this.title = title;
			transfer = false;
			process = type == FileType.File ? PreferencesScience.Instance.transmitScience : PreferencesScience.Instance.analyzeSamples;
			transmitBuffer = 0;
			this.massPerBit = massPerBit;
			bufferFull = Science.min_buffer_size;

			if (maxSize == 0 || maxSize > Science.max_file_size)
				this.maxSize = Science.max_file_size;
			else
				this.maxSize = maxSize;
		}

		/// <summary>
		/// constructor for the Drive IList interface methods, used for result transfers between drives
		/// </summary>
		/// <param name="size">if == 0, the oldResult.size will be used</param>
		/// <param name="index">if >= 0, the result will be insered at index instead of added</param>
		public ExperimentResult(Drive drive, ExperimentResult oldResult, long size = 0, int index = -1)
		{
			this.drive = drive;
			if (index == -1)
				drive.Add(this);
			else
				drive.Insert(index, this);

			type = oldResult.type;
			this.size = size == 0 ? oldResult.size : size;
			subject_id = oldResult.subject_id;
			title = oldResult.title;
			transfer = false; // this ctor is used for copy/transfers, it make sense to reset the flag
			process = oldResult.process;
			transmitBuffer = oldResult.transmitBuffer;
			massPerBit = oldResult.massPerBit;
			bufferFull = oldResult.bufferFull;
			maxSize = oldResult.maxSize;
		}


		public bool IsDeleted()
		{
			return drive == null;
		}

		public void Save(ConfigNode node)
		{
			node.AddValue("type", nameof(type));
			node.AddValue("subject_id", subject_id);
			node.AddValue("size", size);
			node.AddValue("transfer", transfer);
			node.AddValue("process", process);
			node.AddValue("transmit_buffer", transmitBuffer);
			node.AddValue("mass", mass);
		}

		public long SlotSize()
		{
			return Lib.SampleSizeToFullSlots(size);
		}

		public void Delete()
		{
			drive.Remove(this);
			DeleteDriveRef();
		}

		public void DeleteDriveRef()
		{
			drive = null;
		}

		public float GetMass()
		{
			if (type == FileType.Sample)
				return (float)(size * massPerBit);
			else
				return 0f;
		}

		/// <summary>
		/// returns size (in bits) available on the drive, including space available in partially filled slots
		/// </summary>
		public long SizeCapacityAvailable()
		{
			return drive.SizeCapacityAvailable(type, subject_id);
		}

		public override string ToString()
		{
			// TODO : get the situation
			//return Lib.BuildString(exp_info.experimentTitle, " (", Situation(subject_id), ")");
			return exp_info.experimentTitle;
		}
		//public double MaxSize()
		//{
		//	return Science.Experiment(subject_id).data_max;
		//}

		//public void ClampToMaxSize()
		//{
		//	if (size > MaxSize())
		//	{
		//		if (type == FileType.Sample) mass *= size / MaxSize();
		//		size = MaxSize();
		//	}
		//}
	}
}
