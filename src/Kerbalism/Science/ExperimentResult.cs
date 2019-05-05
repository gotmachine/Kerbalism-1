using System;
using System.Collections.Generic;

namespace KERBALISM
{


	public sealed class ExperimentResult
	{
		public enum DataType { File, Sample }

		/// <summary> DataType.File or DataType.Sample </summary>
		public DataType type;
		/// <summary> "experiment@situation" </summary>
		public string subject_id;
		/// <summary> file size in Mb, or sample volume (1 slot = size / 1024) </summary>
		public double size;
		/// <summary> flagged for transfer to another drive </summary>
		public bool transfer;
		/// <summary> file : flagged for transmission, sample : flagged for analysis in a lab </summary>
		public bool process;
		/// <summary> file specific : data transmitted but not credited </summary>
		public double transmit_buffer;
		/// <summary> file specific : last change time </summary>
		public double ts;
		/// <summary> sample specific</summary>
		public double mass;

		public ExperimentResult(DataType type, string subject_id, double size = 0.0)
		{
			this.type = type;
			this.subject_id = subject_id;
			this.size = size;
		}

		public ExperimentResult(ConfigNode node)
		{
			type = Lib.ConfigValue(node, "type", "") == nameof(DataType.File) ? DataType.File : DataType.Sample;
			subject_id = Lib.ConfigValue(node, "subject_id", "invalid");
			size = Lib.ConfigValue(node, "size", 0.0);
			transfer = Lib.ConfigValue(node, "transfer", false);
			process = Lib.ConfigValue(node, "process", false);
			transmit_buffer = Lib.ConfigValue(node, "transmit_buffer", 0.0);
			mass = Lib.ConfigValue(node, "mass", 0.0);
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

		public double MaxSize()
		{
			return Science.Experiment(subject_id).data_max;
		}

		public void ClampToMaxSize()
		{
			if (size > MaxSize())
			{
				if (type == DataType.Sample) mass *= size / MaxSize();
				size = MaxSize();
			}
		}
	}
}
