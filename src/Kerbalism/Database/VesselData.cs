using System;
using System.Collections.Generic;
using UnityEngine;
using System.Threading;


namespace KERBALISM
{


	public class VesselData
	{
		public VesselData()
		{
			msg_signal = false;
			msg_belt = false;
			cfg_ec = PreferencesMessages.Instance.ec;
			cfg_supply = PreferencesMessages.Instance.supply;
			cfg_signal = PreferencesMessages.Instance.signal;
			cfg_malfunction = PreferencesMessages.Instance.malfunction;
			cfg_storm = PreferencesMessages.Instance.storm;
			cfg_script = PreferencesMessages.Instance.script;
			cfg_highlights = PreferencesBasic.Instance.highlights;
			cfg_showlink = true;
			storm_time = 0.0;
			storm_age = 0.0;
			storm_state = 0;
			group = "NONE";
			computer = new Computer();
			supplies = new Dictionary<string, SupplyData>();
			scansat_id = new List<uint>();

			biomes = new VesselBiomes(this);
			sunlight = new Sunlight(this);
		}

		public VesselData(ConfigNode node)
		{
			msg_signal = Lib.ConfigValue(node, "msg_signal", false);
			msg_belt = Lib.ConfigValue(node, "msg_belt", false);
			cfg_ec = Lib.ConfigValue(node, "cfg_ec", PreferencesMessages.Instance.ec);
			cfg_supply = Lib.ConfigValue(node, "cfg_supply", PreferencesMessages.Instance.supply);
			cfg_signal = Lib.ConfigValue(node, "cfg_signal", PreferencesMessages.Instance.signal);
			cfg_malfunction = Lib.ConfigValue(node, "cfg_malfunction", PreferencesMessages.Instance.malfunction);
			cfg_storm = Lib.ConfigValue(node, "cfg_storm", PreferencesMessages.Instance.storm);
			cfg_script = Lib.ConfigValue(node, "cfg_script", PreferencesMessages.Instance.script);
			cfg_highlights = Lib.ConfigValue(node, "cfg_highlights", PreferencesBasic.Instance.highlights);
			cfg_showlink = Lib.ConfigValue(node, "cfg_showlink", true);
			storm_time = Lib.ConfigValue(node, "storm_time", 0.0);
			storm_age = Lib.ConfigValue(node, "storm_age", 0.0);
			storm_state = Lib.ConfigValue(node, "storm_state", 0u);
			group = Lib.ConfigValue(node, "group", "NONE");
			computer = node.HasNode("computer") ? new Computer(node.GetNode("computer")) : new Computer();

			supplies = new Dictionary<string, SupplyData>();
			foreach (var supply_node in node.GetNode("supplies").GetNodes())
			{
				supplies.Add(DB.From_safe_key(supply_node.name), new SupplyData(supply_node));
			}

			scansat_id = new List<uint>();
			foreach (string s in node.GetValues("scansat_id"))
			{
				scansat_id.Add(Lib.Parse.ToUInt(s));
			}

			biomes = new VesselBiomes(this);
			sunlight = new Sunlight(this);
		}

		public void Save(ConfigNode node)
		{
			node.AddValue("msg_signal", msg_signal);
			node.AddValue("msg_belt", msg_belt);
			node.AddValue("cfg_ec", cfg_ec);
			node.AddValue("cfg_supply", cfg_supply);
			node.AddValue("cfg_signal", cfg_signal);
			node.AddValue("cfg_malfunction", cfg_malfunction);
			node.AddValue("cfg_storm", cfg_storm);
			node.AddValue("cfg_script", cfg_script);
			node.AddValue("cfg_highlights", cfg_highlights);
			node.AddValue("cfg_showlink", cfg_showlink);
			node.AddValue("storm_time", storm_time);
			node.AddValue("storm_age", storm_age);
			node.AddValue("storm_state", storm_state);
			node.AddValue("group", group);
			computer.Save(node.AddNode("computer"));

			var supplies_node = node.AddNode("supplies");
			foreach (var p in supplies)
			{
				p.Value.Save(supplies_node.AddNode(DB.To_safe_key(p.Key)));
			}

			foreach (uint id in scansat_id)
			{
				node.AddValue("scansat_id", id.ToString());
			}
		}

		public SupplyData Supply(string name)
		{
			if (!supplies.ContainsKey(name))
			{
				supplies.Add(name, new SupplyData());
			}
			return supplies[name];
		}

		public bool msg_signal;       // message flag: link status
		public bool msg_belt;         // message flag: crossing radiation belt
		public bool cfg_ec;           // enable/disable message: ec level
		public bool cfg_supply;       // enable/disable message: supplies level
		public bool cfg_signal;       // enable/disable message: link status
		public bool cfg_malfunction;  // enable/disable message: malfunctions
		public bool cfg_storm;        // enable/disable message: storms
		public bool cfg_script;       // enable/disable message: scripts
		public bool cfg_highlights;   // show/hide malfunction highlights
		public bool cfg_showlink;     // show/hide link line
		public double storm_time;     // time of next storm (interplanetary CME)
		public double storm_age;      // time since last storm (interplanetary CME)
		public uint storm_state;      // 0: none, 1: inbound, 2: in progress (interplanetary CME)
		public string group;          // vessel group
		public Computer computer;     // store scripts
		public Dictionary<string, SupplyData> supplies; // supplies data
		public List<uint> scansat_id; // used to remember scansat sensors that were disabled

		//public VesselScalarAttr<KerbalismSituation>[] situations;
		public VesselBiomes biomes;
		public Sunlight sunlight;

		public List<VesselValue> valuesToUpdate = new List<VesselValue>();

		public double lastUT = 0.0;
		public const double subStepDurationTarget = 30.0;
		public double subStepDuration;
		public int subStepsCount;
		
		List<VesselSubStep> subSteps = new List<VesselSubStep>((int)(2000.0 / subStepDurationTarget) * 5);
		ManualResetEvent resetEvent;

		public int biomeMapRowWidth;
		public byte[] biomeMapdata;
		public CBAttributeMapSO biomeMap;

		public void UpdateCachedVars(Vessel vessel)
		{
			biomeMap = vessel.mainBody.BiomeMap;
			if (biomeMap != null)
			{
				biomeMapRowWidth = Lib.ReflectionValue<int>(biomeMap, "_rowWidth");
				biomeMapdata = Lib.ReflectionValue<byte[]>(biomeMap, "_data");
				
			}
		}


		// 570 ms at 100000x (200 substeps - 3 vessels)
		// 200 ms at 100000x (66 substeps)
		// 33 ms at 10000x (6 steps)
		public void UpdateST(Vessel vessel)
		{
			
			if (lastUT == 0.0 || TimeWarp.CurrentRate <= 5000f)
			{
				lastUT = Planetarium.GetUniversalTime();
				return;
			}
			Profiler.Start("substeps");

			UpdateCachedVars(vessel);

			double currentUT = Planetarium.GetUniversalTime();
			double stepDuration = currentUT - lastUT;
			subStepsCount = (int)(stepDuration / subStepDurationTarget);
			subStepDuration = stepDuration / subStepsCount;

			if (subSteps.Count < subStepsCount)
			{
				subSteps.Capacity = subStepsCount;
				for (int i = subSteps.Count - 1; i < subStepsCount; i++)
				{
					subSteps.Add(new VesselSubStep(this, vessel));
				}
			}

			foreach (VesselValue vv in valuesToUpdate)
			{
				vv.PreUpdate(subStepsCount);
			}

			for (int step = 0; step < subStepsCount; step++)
			{
				double stepUT = lastUT + (((double)step / subStepsCount) * stepDuration);
				subSteps[step].Prepare(currentUT, stepUT, step);
				subSteps[step].Update();
			}

			lastUT = Planetarium.GetUniversalTime();

			Profiler.Stop("substeps");

			Lib.Log("Timewarping at " + TimeWarp.CurrentRate.ToString("F0") + "x, duration : " + stepDuration.ToString("F0") + " - Steps : " + subStepsCount + " - DT : " + Time.deltaTime.ToString("F2") + "/" + Time.maximumDeltaTime.ToString("F2"));
			for (int i = 0; i < subStepsCount; i++)
			{
				Lib.Log("Sunlight : " + sunlight.attributes[i] + " - lat/long : " + biomes.latitude[i].ToString("F1") + "/" + biomes.longitude[i].ToString("F1") + " - Biome " + i + " : " + biomes.attributes[i].name);
			}

		}

		int workingThreads;
		// 260 ms at 100000x (200 substeps - 3 vessels)
		// 160 ms at 100000x (66 substeps)
		// 100 ms at 10000x (6 steps)
		public void UpdateMT(Vessel vessel)
		{
			
			if (lastUT == 0.0 || TimeWarp.CurrentRate <= 5000f)
			{
				lastUT = Planetarium.GetUniversalTime();
				return;
			}
			Profiler.Start("substeps");

			UpdateCachedVars(vessel);

			double currentUT = Planetarium.GetUniversalTime();
			double stepDuration = currentUT - lastUT;
			subStepsCount = (int)(stepDuration / subStepDurationTarget);
			subStepDuration = stepDuration / subStepsCount;

			if (subSteps.Count < subStepsCount)
			{
				subSteps.Capacity = subStepsCount;
				for (int i = subSteps.Count; i < subStepsCount; i++)
				{
					subSteps.Add(new VesselSubStep(this, vessel));
				}
			}

			//stepsDone = subStepsCount;
			resetEvent = new ManualResetEvent(false);

			foreach (VesselValue vv in valuesToUpdate)
			{
				vv.PreUpdate(subStepsCount);
			}

			for (int step = 0; step < subStepsCount; step++)
			{
				double stepUT = lastUT + (((double)step / subStepsCount) * stepDuration);
				subSteps[step].Prepare(currentUT, stepUT, step);
			}

			
			int stepsRemainingToSend = subStepsCount;
			int stepsPerThread = Math.Max(1, subStepsCount / Environment.ProcessorCount);
			workingThreads = Math.Min(subStepsCount / stepsPerThread, Environment.ProcessorCount);
			int threadCount = workingThreads;
			for (int i = 1; i <= threadCount; i++)
			{
				int arraySize = i == threadCount ? stepsRemainingToSend : stepsPerThread;
				VesselSubStep[] steps = subSteps.GetRange(stepsRemainingToSend - arraySize, arraySize).ToArray();
				stepsRemainingToSend -= arraySize;
				ThreadPool.QueueUserWorkItem(SubStepUpdateMT, steps);
			}

			resetEvent.WaitOne();

			lastUT = Planetarium.GetUniversalTime();

			Profiler.Stop("substeps");

			Lib.Log("Timewarping at " + TimeWarp.CurrentRate.ToString("F0") + "x, duration : " + stepDuration.ToString("F0") + " - Steps : " + subStepsCount + " - DT : " + Time.deltaTime.ToString("F2") + "/" + Time.maximumDeltaTime.ToString("F2"));
			for (int i = 0; i < subStepsCount; i++)
			{
				Lib.Log("Sunlight : " + sunlight.attributes[i] + " - lat/long : " + biomes.latitude[i].ToString("F1") + "/" + biomes.longitude[i].ToString("F1") + " - Biome " + i + " : " + biomes.attributes[i].name);
			}
		}

		private void SubStepUpdateMT(object subStepArray)
		{
			foreach (VesselSubStep substep in (VesselSubStep[])subStepArray)
			{
				substep.Update();
			}

			if (Interlocked.Decrement(ref workingThreads) == 0)
				resetEvent.Set();
		}

	}


	// Note sure how to do future evaluation : 
	// 1. unfortunately we can't 100% rely on GameEvents.onTimeWarpRateChanged because it is fired only when the private "tgt_rate" target rate is set.
	// Due to real rate lerping, there is no way to determine the actual rate that will be used next update.
	// But maybe what we can do is :
	// - get target rate (warpRates[currentRateIndex]) and CurrentRate
	// - if target rate > currentrate, increase the step targetUT to match target rate
	// - if target rate < currentrate, keep the same step targetUT until target rate has been reached, then decrease it to match the new rate (check every fixedupdate)


	// continous evaluation seems too messy to implement
	// Maybe the best way is to continuously do evaluation for a bit more than 2000s (100000x) for every vessel
	// - every fixedupdate, we do "AddSubSteps" : request an additional substep amount corresponding to how much time has passed.
	// when the vessel is updated (=/= fixedupdate) :
	// - if we "consume" more than a "max substepstep amout" (10000 s ?), we create a new 2000s step with the new position
	// - else we do "AddSubSteps"

	// how to to get positions thread safe :
	// - we can't directly use Orbit.getTruePositionAtUT (and other non-relative pos methods) as it use the reference celestialBody position / orbit (referenceBody.getTruePositionAtUT()).
	// - but we can use the underlying methods that is needed : getRelativePositionAtUT, everything called is thread-safe and self-contained in the orbit instance
	// - So what we need is a derived 

	/// <summary>
	/// Everything needed for thread-safe evaluation of the vessel environnemental conditions in the future.
	/// </summary>
	// We do a simplification by assuming bodies positions are static, but we take body rotation into account. rationale :
	// - As we plan to run this every update for every vessel, the inaccurracies introduced are probably very small
	// - Doing dynamic positions is not trivial because the whole planet orbit > mun orbit > vessel orbit chain would have to be repositionned

	

	public class ThreadSafeOrbit
	{
		private Orbit stockOrbit;
		public ThreadSafeOrbit referenceOrbit;
		public Vector3d sunWorldPos;

		public ThreadSafeOrbit(Orbit orbit)
		{
			stockOrbit = new Orbit(orbit);

			if (stockOrbit.referenceBody.orbit == null)
				// if parent is the sun, it has no orbit, and position is fixed regardless of the UT
				sunWorldPos = stockOrbit.referenceBody.position;
			else
				// recursively get the orbit of parent bodies
				referenceOrbit = new ThreadSafeOrbit(orbit.referenceBody.orbit);
		}

		/// <summary>Use only fields that represent static body properties (ex : rotationPeriod), never use any method or position/orbit related fields/properties</summary>
		public CelestialBody GetUnsafeReferenceBody => stockOrbit.referenceBody;

		// We can't use Orbit.GetTruePositionAtUT because it use referenceBody.position wich isn't thread-safe
		// Note that this can be quite costly as it recursively call itself for each parent body
		public Vector3d getTruePositionAtUT(double ut)
		{
			if (referenceOrbit != null)
				return getRelativePositionAtUT(ut).xzy + referenceOrbit.getTruePositionAtUT(ut);
			else
				return getRelativePositionAtUT(ut).xzy + sunWorldPos;
		}

		// Orbit.getRelativePositionAtUT is fully thread-safe
		public Vector3d getRelativePositionAtUT(double ut)
		{
			return stockOrbit.getRelativePositionAtUT(ut);
		}

		// Orbit.getPositionAtT/getPositionAtUT are NOT thread-safe because they use referenceBody.position
		public Vector3d getPositionAtUT(double ut)
		{
			return getPositionAtT(stockOrbit.getObtAtUT(ut));
		}

		// I'm not 100% sure stockOrbit.pos is always == referenceBody.position, but it seems to be the case
		// Orbit.getRelativePositionAtT is fully thread-safe
		public Vector3d getPositionAtT(double t)
		{
			return stockOrbit.pos + stockOrbit.getRelativePositionAtT(t).xzy;
		}

		// <summary>
		// Optimized method for getting the position of a vessel and the position of the parent and reference body
		// </summary>
		// <param name="thisPos">position of this object at ut in world space</param>
		// <param name="refPos">position of the reference body at ut in world space</param>
		// <returns>false if the reference body is the sun, true otherwise</returns>
		// Note : this assume the system don't have a more complex hierarchy than Sun > Planets > Moons. 4+ level bodies are unsupported



		/// <summary>
		/// Optimized method for getting the position of a vessel and the position of the parent and reference body
		/// </summary>
		/// <param name="vesselPos">position of the vessel at ut in world space</param>
		/// <param name="mainBodyPos">position of the mainBody at ut in world space (or zero if vessel is in orbit around the sun)</param>
		/// <param name="refBodyPos">position of the mainBody reference body at ut in world space (or zero if vessel is in orbit around a planet)</param>
		/// <returns>The type of the SOI of the vessel : Sun, Planet or Moon</returns>
		public VesselStep.SOIType getVesselAndReferencePositionsAtUT(double ut, out Vector3d vesselPos, out Vector3d mainBodyPos, out Vector3d refBodyPos)
		{
			VesselStep.SOIType soiType;

			if (referenceOrbit == null)
			{
				refBodyPos = Vector3d.zero;
				mainBodyPos = Vector3d.zero;
				vesselPos = getRelativePositionAtUT(ut).xzy;
				soiType = VesselStep.SOIType.Sun;
				return soiType;
			}
			else
			{
				if (referenceOrbit.referenceOrbit == null)
				{
					refBodyPos = Vector3d.zero;
					soiType = VesselStep.SOIType.Planet;
				}
				else
				{
					refBodyPos = referenceOrbit.referenceOrbit.getTruePositionAtUT(ut);
					soiType = VesselStep.SOIType.Moon;
				}
				mainBodyPos = referenceOrbit.getRelativePositionAtUT(ut).xzy + refBodyPos;
			}
			vesselPos = getRelativePositionAtUT(ut).xzy + mainBodyPos;
			return soiType;
		}
	}

	public class VesselStep
	{
		public enum SOIType { Sun, Planet, Moon }

		// this class contains 

		// vessel mainbody biome data, 100 % thread-safe
		public bool bodyHasBiomes;
		public int biomeMapRowWidth; // reflection-acquired (protected field)
		public byte[] biomeMapdata; // thread-safe ref, reflection-acquired (protected field)
		// partially thread-safe ref. public fields/properties reads are ok, methods are not
		public CBAttributeMapSO biomeMap;

		// mostly not thread-safe ref. use only fields that represent static body properties (ex : rotationPeriod)
		// Do not use any method, and definitely not use anything related to the body orbit / position
		public CelestialBody sun;
		public CelestialBody mainBody;
		public CelestialBody refBody;

		// 100% thread-safe copies
		public SOIType soiType;
		public Vector3d sunWorldPos;        // position of FlightGlobals.Bodies[0], doesn't change with UT
		public Vector3d mainBodyWorldPos;   // position of vessel.mainBody
		public Vector3d refBodyWorldPos;    // position of vessel.mainBody.referenceBody (not available if soiType == Sun)
		public Planetarium.CelestialFrame mainBodyFrame;  // methods are thread-safe, main usage is WorldToLocal(vesselWorldPos) to get latitude/longitude

		// 100% thread-safe copy, unless I missed something all methods in the Orbit class are thread-safe
		public Orbit vesselOrbit;

		public const double subStepDurationTarget = 30.0;
		public double startUT = 0.0;
		public double subStepDuration;
		public int subStepsCount;

		public VesselSubStep[] substeps;

		public VesselStep(Vessel vessel)
		{
			vesselOrbit = new Orbit(vessel.orbit);
		}

	}

	public class VesselSubStep
	{
		public int subStep;
		public double subStepUT;

		public VesselStep step;

		public Vector3d vesselWorldPos;

		public double latitude;
		public double longitude;
		public double altitude;

		public VesselData vesselData;

		//public List<VesselValue> valuesToUpdate;

		public VesselSubStep(VesselData vesselData, Vessel vessel)
		{
			this.vesselData = vesselData;
		}

		public void Prepare(double UT, double stepUT, int step)
		{
			this.subStepUT = stepUT;
			this.subStep = step;
		}

		public void Update()
		{
			// ~0.8 ms for getPositionAtUT
			// Nope. This use referenceBody.position, wich isn't thread safe if executed between updates
			// We need to globally calculate every celestialbody position for each substep and use the values in our own version of getTRUEPositionAtUT
			// that mean that we need to use a predetermined UT for each substep, and a fixed 
			vesselWorldPos = step.vesselOrbit.getPositionAtUT(subStepUT);
			Vector3d vesselLocalPos = step.mainBodyFrame.WorldToLocal(vesselWorldPos);

			double magnitude = vesselLocalPos.magnitude;

			altitude = magnitude - step.mainBody.Radius;
			vesselLocalPos /= magnitude;

			latitude = Math.Asin(vesselLocalPos.z) * (180.0 / Math.PI);
			if (double.IsNaN(latitude)) latitude = 0.0;

			longitude = Math.Atan2(vesselLocalPos.y, vesselLocalPos.x) * (180.0 / Math.PI);
			if (double.IsNaN(longitude)) longitude = 0.0;

			// longitude need to be corrected for body rotation, then reclamped to [-180;180]
			if (step.mainBody.rotates)
			{
				// TODO : not sure about this, maybe should be += ?
				longitude -= 360 * ((subStepUT - step.startUT) / step.mainBody.rotationPeriod); 
				if (longitude < -180.0) longitude += 360.0;
			}

			foreach (VesselValue value in vesselData.valuesToUpdate)
				value.Update(this);
		}
	}

	public abstract class VesselValue
	{
		public VesselValue(VesselData vd)
		{
			vd.valuesToUpdate.Add(this);
		}


		public virtual void PreUpdate(int subSteps) { }
		public abstract void Update(VesselSubStep substep);
		public virtual void PostUpdate() { }

	}

	public abstract class VesselValues<T> : VesselValue // where T : class
	{
		public VesselValues(VesselData vd) : base(vd) { }

		public T[] attributes;
		public double[] latitude;
		public double[] longitude;

		public override void PreUpdate(int subSteps)
		{
			if (attributes == null || attributes.Length != subSteps)
			{
				attributes = new T[subSteps];
				latitude = new double[subSteps];
				longitude = new double[subSteps];
			}
				
		}

		public override void Update(VesselSubStep substep)
		{
			attributes[substep.subStep] = EvaluateValue(substep);
			latitude[substep.subStep] = substep.latitude;
			longitude[substep.subStep] = substep.longitude;

		}

		public abstract T EvaluateValue(VesselSubStep substep);
	}

	public class VesselBiomes : VesselValues<CBAttributeMapSO.MapAttribute>
	{
		public VesselBiomes(VesselData vd) : base(vd) { }

		// ~0.9ms per call, this is a thread safe and 5x faster version of the CBAttributeMapSO.GetAtt() stock method.
		public override CBAttributeMapSO.MapAttribute EvaluateValue(VesselSubStep substep)
		{
			CBAttributeMapSO biomeMap = substep.vesselData.biomeMap;
			if (biomeMap == null)
				return null;

			double longitude = substep.longitude * (Math.PI / 180.0);
			double latitude = substep.latitude * (Math.PI / 180.0);

			//Profiler.Start("biomeEval");
			longitude -= Math.PI / 2.0;
			longitude = UtilMath.WrapAround(longitude, 0.0, Math.PI * 2.0);
			longitude = 1.0 - longitude * 0.15915494309189535;
			latitude = latitude * (1.0 / Math.PI) + 0.5;
			int x = (int)(longitude * biomeMap.Width);
			int y = (int)(latitude * biomeMap.Height);

			int index = x * biomeMap.BitsPerPixel + y * substep.vesselData.biomeMapRowWidth;

			byte[] data = substep.vesselData.biomeMapdata;
			
			Color biomeColor;
			switch (biomeMap.BitsPerPixel)
			{
				case 2:
					float rgb = 0.003921569f * data[index];
					biomeColor = new Color(rgb, rgb, rgb, 0.003921569f * data[index + 1]);
					break;
				case 3:
					biomeColor = new Color(0.003921569f * data[index], 0.003921569f * data[index + 1], 0.003921569f * data[index + 2], 1f);
					break;
				case 4:
					biomeColor = new Color(0.003921569f * data[index], 0.003921569f * data[index + 1], 0.003921569f * data[index + 2], 0.003921569f * data[index + 3]);
					break;
				default:
					return null;
			}
			 
			CBAttributeMapSO.MapAttribute result = biomeMap.Attributes[0];

			foreach (var attribute in biomeMap.Attributes)
			{
				if (biomeColor == attribute.mapColor)
				{
					result = attribute;
					break;
				}
			}

			//Profiler.Stop("biomeEval");
			return result;
		}
	}

	public class Sunlight : VesselValues<bool>
	{
		public Sunlight(VesselData vd) : base(vd) { }

		// ~0.6ms per call
		public override bool EvaluateValue(VesselSubStep substep)
		{

			// shortcuts
			Vector3d dir;
			double dist;
			CelestialBody sun = FlightGlobals.Bodies[0];
			CelestialBody mainbody = substep.vessel.mainBody;
			CelestialBody refbody = mainbody.referenceBody;

			// generate ray parameters
			dir = sun.position - substep.vesselWorldPos;
			dist = dir.magnitude;
			dir /= dist;
			dist -= sun.Radius;

			// raytrace
			return (sun == mainbody || Sim.Raytrace(substep.vesselWorldPos, dir, dist, mainbody))
				&& (sun == refbody || refbody == null || Sim.Raytrace(substep.vesselWorldPos, dir, dist, refbody));
		}
	}



} // KERBALISM
