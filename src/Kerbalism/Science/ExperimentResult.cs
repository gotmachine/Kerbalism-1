using System;
using System.Collections.Generic;

namespace KERBALISM
{

	/// <summary>
	/// Represent drive content, either a file (can be transmitted) or a sample (has mass).
	/// </summary>
	public sealed class ExperimentResult
	{

		/// <summary> FileType.File or FileType.Sample </summary>
		public FileType type;
		/// <summary> "experiment@situation" </summary>
		public string subject_id;
		/// <summary> file size in bits, or sample volume (1 slot = 1024 MB) </summary>
		public long size;
		/// <summary> flagged for transfer to another drive </summary>
		public bool transfer;
		/// <summary> file : flagged for transmission, sample : flagged for analysis in a lab </summary>
		public bool process;
		/// <summary> file specific : data transmitted but not credited </summary>
		public long transmit_buffer;
		/// <summary> file specific : limit for transmit_buffer </summary>
		public readonly long buffer_full;

		public long transmit_rate;
		/// <summary> sample specific</summary>
		public double mass;

		public bool isDeleted;

		private Drive2 drive;
		private ExperimentInfo exp_info;

		public ExperimentResult(ConfigNode node, Drive2 drive)
		{
			type = Lib.ConfigValue(node, "type", "") == nameof(FileType.File) ? FileType.File : FileType.Sample;
			subject_id = Lib.ConfigValue(node, "subject_id", "invalid");
			size = Lib.ConfigValue(node, "size", 0);
			transfer = Lib.ConfigValue(node, "transfer", false);
			process = Lib.ConfigValue(node, "process", false);
			transmit_buffer = Lib.ConfigValue(node, "transmit_buffer", 0);
			mass = Lib.ConfigValue(node, "mass", 0.0);
			buffer_full = Lib.ConfigValue(node, "buffer_full", 0);
			this.drive = drive;
			drive.Add(this);

			exp_info = 
		}

		public ExperimentResult(Drive2 drive, FileType type, ExperimentSubject subject, long size = 0)
		{
			this.drive = drive;
			drive.Add(this);

			exp_info = subject.exp_info;
			this.type = type;
			this.size = size;
			subject_id = subject.subject_id;
			transfer = false;
			process = type == FileType.File ? PreferencesScience.Instance.transmitScience : PreferencesScience.Instance.analyzeSamples;
			transmit_buffer = 0;
			mass = 0;
			buffer_full = subject.DataSizeForScienceValue(Science.buffer_science_value);
		}

		// TODO - ExperimentResult : generic constructor for non-experiment related results (API, scansat...)
		// we probably need a maxSize property to be handle
		public ExperimentResult(Drive2 drive, FileType type, string subject_id, long size = 0, long maxSize = 0, double massPerBit = 0)
		{
			this.drive = drive;
			drive.Add(this);

			exp_info = subject.exp_info;
			this.type = type;
			this.size = size;
			subject_id = subject.subject_id;
			transfer = false;
			process = type == FileType.File ? PreferencesScience.Instance.transmitScience : PreferencesScience.Instance.analyzeSamples;
			transmit_buffer = 0;
			mass = 0;
			buffer_full = subject.DataSizeForScienceValue(Science.buffer_science_value);
		}

		public void Save(ConfigNode node)
		{
			node.AddValue("type", nameof(type));
			node.AddValue("subject_id", subject_id);
			node.AddValue("size", size);
			node.AddValue("transfer", transfer);
			node.AddValue("process", process);
			node.AddValue("transmit_buffer", transmit_buffer);
			node.AddValue("mass", mass);
		}

		public long SlotSize()
		{
			return Lib.SampleSizeToFullSlots(size);
		}

		public void Delete()
		{
			isDeleted = true;
			drive.Remove(this);
		}

		public float GetSampleMass()
		{
			if (type == FileType.Sample)
				return (float)(size * exp_info.massPerBit);
			else
				return 0f;
		}

		/// <summary>
		/// returns size (in bits) available on this result drive, including space available in partially filled slots
		/// </summary>
		public long SizeCapacityAvailable()
		{
			long capacity = drive.CapacityAvailable(type);
			if (type == FileType.Sample)
			{
				long partialSlotCapacity = Lib.slotSize - (size % Lib.slotSize);
				return (capacity * Lib.slotSize) + partialSlotCapacity;
			}
			else
			{
				return capacity;
			}
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
