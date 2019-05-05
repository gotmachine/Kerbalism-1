using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace KERBALISM
{
	/// <summary>
	/// drive data as a dictionnary
	/// key is the "experiment@situation" string (usual var name is "subject_id")
	/// value is a list of ExperimentData representing a single file or sample (allowing for duplicates)
	/// </summary>
	public class Drive2 : Dictionary<string, List<ExperimentResult>>
	{
		public double dataCapacity;
		public int sampleCapacity;
		public string name = String.Empty;
		public bool is_private = false;


		public void AddData(ConfigNode node)
		{
			ExperimentResult expRes = new ExperimentResult(node);
			if (ContainsKey(DB.From_safe_key(node.name)))
				this[DB.From_safe_key(node.name)].Add(expRes);
			else
				Add(DB.From_safe_key(node.name), new List<ExperimentResult> { expRes });
		}

		/// <summary>Add a result, merging with an existing partial result if present</summary>
		public void AddData(ExperimentResult expRes)
		{
			expRes.ClampToMaxSize();

			if (!ContainsKey(expRes.subject_id))
				Add(expRes.subject_id, new List<ExperimentResult> { expRes });
			else
			{
				for (int i = 0; i < this[expRes.subject_id].Count; i++)
				{
					// if there is already an incomplete result, complete it before creating a new result
					if (this[expRes.subject_id][i].type == expRes.type &&
						this[expRes.subject_id][i].size < this[expRes.subject_id][i].MaxSize())
					{
						double addedData = Math.Min(this[expRes.subject_id][i].MaxSize() - this[expRes.subject_id][i].size, expRes.size);
						this[expRes.subject_id][i].size += addedData;
						if (expRes.type == ExperimentResult.DataType.Sample)
						{
							double factor = addedData / expRes.size;
							this[expRes.subject_id][i].mass += factor * expRes.mass;
							// update mass left
							expRes.mass *= 1.0 - factor;
						}
						// update size left
						expRes.size -= addedData;

						if (expRes.size > 0)
						{
							// there is still some data left, create a new result
							this[expRes.subject_id].Add(expRes);
							return;
						}
						else
						{
							expRes = null;
							return;
						}
					}
				}
			}
		}

		public void AddData(string subject_id, ExperimentResult.DataType type, double size, double mass = 0)
		{


		}


	}
}
