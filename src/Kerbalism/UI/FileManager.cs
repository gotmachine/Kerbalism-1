using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;


namespace KERBALISM
{
	// TODO : better string construction accounting for single/plural and empty things
	// TODO : infinite capacity drive handling
	public static class FileManager
	{
		/// <summary>
		/// If short_strings parameter is true then the strings used for display of the data will be shorter when inflight.
		/// </summary>
		public static void Fileman(this Panel p, Vessel v, bool short_strings = false)
		{
			// avoid corner-case when this is called in a lambda after scene changes
			v = FlightGlobals.FindVessel(v.id);

			// if vessel doesn't exist anymore, leave the panel empty
			if (v == null) return;

			// get info from the cache
			Vessel_info vi = Cache.VesselInfo(v);

			// if not a valid vessel, leave the panel empty
			if (!vi.is_valid) return;

			// set metadata
			p.Title(Lib.BuildString(Lib.Ellipsis(v.vesselName, Styles.ScaleStringLength(40)), " <color=#cccccc>DATA MANAGER</color>"));
			p.Width(Styles.ScaleWidthFloat(465.0f));
			p.paneltype = Panel.PanelType.data;

			// time-out simulation
			if (p.Timeout(vi)) return;

			// single-loop caching (at the cost of some garbage) to avoid looping over everything a gazillon times.
			List<Drive> drives = Drive.GetDrives(v);
			int drivesCount = drives.Count;
			long[] drivesFileCap = new long[drivesCount];
			int[] drivesFileCount = new int[drivesCount];
			long[] drivesFileUse = new long[drivesCount];
			long[] drivesSampleCap = new long[drivesCount];
			int[] drivesSampleCount = new int[drivesCount];
			long[] drivesSampleUse = new long[drivesCount];
			double[] drivesSampleMass = new double[drivesCount];
			bool[] driveEmpty = new bool[drivesCount];
			long transferrableFilesSize = 0;
			long transferrableSamplesSize = 0;
			int emptyDrivesCount = 0;
			bool infiniteFileCap = false;
			bool infiniteSampleCap = false;

			bool emptyFilter = true; // TODO : emptyFilter stored on the VesselData ?

			for (int i = 0; i < drivesCount; i++)
			{
				drivesFileCap[i] = drives[i].fileCapacity;
				drivesSampleCap[i] = drives[i].sampleCapacity;
				drives[i].GetStorageInfo(out drivesFileCount[i], out drivesFileUse[i], out drivesSampleCount[i], out drivesSampleUse[i], out drivesSampleMass[i]);
				driveEmpty[i] = drivesFileCount[i] == 0 && drivesSampleCount[i] == 0;
				if (driveEmpty[i]) emptyDrivesCount++;
				if (drivesFileCap[i] < 0) infiniteFileCap = true;
				if (drivesSampleCap[i] < 0) infiniteSampleCap = true;
				drives[i].GetTransferrableResultsSize(v, ref transferrableFilesSize, ref transferrableSamplesSize);
			}

			// tootip text with the size of the data flagged to transfer
			string resultTransferTooltip = Lib.BuildString(
					"Transfer selected data on this storage unit\n",
					transferrableFilesSize > 0 ? "files : " : string.Empty,
					transferrableFilesSize > 0 ? Lib.HumanReadableDataSize(transferrableFilesSize) : string.Empty,
					transferrableSamplesSize > 0 ? "samples : " : string.Empty,
					transferrableSamplesSize > 0 ? Lib.HumanReadableSampleSize(transferrableSamplesSize) : string.Empty);

			// "X storage units (X empty)"
			string mainHeaderTitle = Lib.BuildString
				(
					drivesCount.ToString(),
					" storage units (",
					emptyDrivesCount.ToString(),
					" empty)"
				);

			// "X files : X/X MB, X samples : X/X slots (X Kg)"
			string mainHeaderDesc = Lib.BuildString(
					drivesFileCount.Sum().ToString(),
					" files : ",
					Lib.HumanReadableDataUsage(drivesFileUse.Sum(), drivesFileCap.Sum()),
					", ",
					drivesSampleCount.Sum().ToString(),
					" samples : ",
					((float)drivesSampleUse.Sum() / Lib.slotSize).ToString("F1"),
					"/",
					((float)drivesSampleCap.Sum() / Lib.slotSize).ToString("F1"),
					" slots (",
					Lib.HumanReadableMass(drivesSampleMass.Sum()),
					")");

			// main header
			p.AddSection(mainHeaderTitle, mainHeaderDesc, null, () => { emptyFilter = !emptyFilter; });
			//if (emptyFilter)
			//	p.AddIcon(Icons.drive, "Show all drives", () => { emptyFilter = !emptyFilter; });
			//else
			//	p.AddIcon(Icons.drive_filter, "Filter empty drives", () => { emptyFilter = !emptyFilter; });


			for (int i = 0; i < drivesCount; i++)
			{
				//if (driveEmpty[i] && emptyFilter)
				//	continue;

				// drives header
				// "partname - X files : X/X MB, X samples : X/X slots (X Kg)"
				string driveHeader = Lib.BuildString(
					drives[i].isPrivate ? "<color=#ffff00>" : string.Empty,
					Lib.Ellipsis(drives[i].partName, Styles.ScaleStringLength(25)),
					drives[i].isPrivate ? "</color> - " : " - ",
					drivesFileCount.Sum().ToString(),
					" files : ",
					Lib.HumanReadableDataUsage(drivesFileUse.Sum(), drivesFileCap.Sum()),
					", ",
					drivesSampleCount.Sum().ToString(),
					" samples : ",
					((float)drivesSampleUse.Sum() / Lib.slotSize).ToString("F1"),
					"/",
					((float)drivesSampleCap.Sum() / Lib.slotSize).ToString("F1"),
					" slots (",
					Lib.HumanReadableMass(drivesSampleMass.Sum()),
					")");

				p.AddSection(driveHeader);

				// drives header : "transfer data here" button, hide transfer for private drives
				if (!drives[i].isPrivate)
				{
					p.AddIcon(Icons.transfer_here, resultTransferTooltip, () => { Drive.GetAllResultsToTransfer(v).ForEach(r => drives[i].Add(r)); });
				}

				// result entry
				for (int j = 0; j < drives[i].Count; j++)
				{
					Result result = drives[i][j];
					string resLabel = Lib.BuildString(
					  "<b>",
					  Lib.Ellipsis(result.Title, Styles.ScaleStringLength(short_strings ? 24 : 38)),
					  "</b> <size=", Styles.ScaleInteger(10).ToString(), ">",
					  Lib.Ellipsis(result.Situation, Styles.ScaleStringLength((short_strings ? 32 : 62) - Lib.Ellipsis(result.Title, Styles.ScaleStringLength(short_strings ? 24 : 38)).Length)),
					  "</size>");

					string resValue = Lib.HumanReadableDataSize(result.Size);
					if (result.transmitRate > 0)
						resValue = Lib.BuildString(resValue, " <color=#00ff00>↑", Lib.HumanReadableDataRate(result.transmitRate), "</color>");

					// TODO : science value / value remaining (total value), data size / total size, mass, stock exp_def RESULT string
					string resTooltip = Lib.BuildString(
					  result.type.ToString(), " : ", result.Title, "\n",
					  "<color=#aaaaaa>", result.Situation, "</color>", "\n"
					  );

					// main label
					p.AddContent(resLabel, resValue, resTooltip, (Action)null, () => Highlighter.Set(drives[i].partId, Color.cyan));

					// file/sample icon
					if (result.type == FileType.File)
					{
						if (result.processRate > 0)
							p.AddIcon(Icons.file_green, Lib.BuildString("<b>File</b> (+", Lib.HumanReadableDataRate(result.processRate), ")"), () => { }, true);
						else
							p.AddIcon(Icons.file, "<b>File</b>", () => { }, true);
					}
					else
					{
						if (result.processRate > 0)
							p.AddIcon(Icons.sample_green, Lib.BuildString("<b>Sample</b> (+", Lib.HumanReadableDataRate(result.processRate), ")"), () => { }, true);
						else
							p.AddIcon(Icons.sample, "<b>Sample</b>", () => { }, true);
					}

					// transmit/analysis button
					if (result.type == FileType.File)
					{
						if (result.process)
						{
							if (result.transmitRate > 0)
								p.AddIcon(Icons.send_green, Lib.BuildString("Flagged for transmission to <b>DSN</b>, sending at <color=#00ff00>", Lib.HumanReadableDataRate(result.transmitRate), "</color>"), () => { result.process = false; });
							else
								p.AddIcon(Icons.send_cyan, "Flagged for transmission to <b>DSN</b>", () => { result.process = false; });
						}
						else
						{
							p.AddIcon(Icons.send_white, "Flag for transmission to <b>DSN</b>", () => { result.process = true; });
						}
					}
					else
					{
						if (result.process)
							p.AddIcon(Icons.lab_cyan, "Flagged for analysis in a <b>laboratory</b>", () => { result.process = false; });
						else
							p.AddIcon(Icons.lab_white, "Flag for analysis in a <b>laboratory</b>", () => { result.process = true; });
					}

					// transfer button
					if (result.IsTransferrable(v))
					{
						if (result.transfer)
							p.AddIcon(Icons.transfer_cyan, "Flagged for transfer to another storage unit", () => { result.transfer = false; });
						else
							p.AddIcon(Icons.transfer_white, "Flag for transfer to another storage unit", () => { result.transfer = true; });
					}
					else
					{
						p.AddIcon(Icons.transfer_yellow, "Cannot transfer : crew required", () => { });
					}

					// delete button
					p.AddIcon(Icons.delete, result.type == FileType.File ? "Delete the file" : "Dump the sample", () =>
					{
						Lib.Popup("Warning!",
							Lib.BuildString("Do you really want to ", result.type == FileType.File ? "delete : " : "dump : ", result.Title, " ?"),
							new DialogGUIButton(result.type == FileType.File ? "Delete it" : "Dump it", () => result.Delete()),
							new DialogGUIButton("Keep it", () => { }));
					}
					);
				}
			}
		}
	}
} // KERBALISM
