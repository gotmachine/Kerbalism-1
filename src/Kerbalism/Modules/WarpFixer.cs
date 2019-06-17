using System;
using System.Reflection;
using UnityEngine;


namespace KERBALISM
{


	// This module is used to disable stock solar panel output, by setting rate to zero.
	// The EC is instead produced using the resource cache, that give us correct behaviour
	// independent from timewarp speed and vessel EC capacity.
	// The stock module was not simply replaced with a custom solar panel module, because
	// dealing with tracking and with the "solar panel transforms zoo" was a pain.
	// We reuse computations done by the stock module as much as possible.
	public sealed class WarpFixer : PartModule
	{
		[KSPField(guiActive = false, guiName = "Status")] public string field_status;
		[KSPField(guiActive = false, guiName = "Visibility", guiUnits = "%", guiFormat = "F0")] public double field_visibility;
		[KSPField(guiActive = false, guiName = "Atmosphere", guiUnits = "%", guiFormat = "F0")] public double field_atmosphere;
		[KSPField(guiActive = false, guiName = "Exposure", guiUnits = "%", guiFormat = "F0")] public double field_exposure;
		[KSPField(guiActive = false, guiName = "Output", guiUnits = " kW", guiFormat = "F3")] public double field_output;

		// persistence
		[KSPField(isPersistant = true)] public double output_rate;      // nominal rate at Kerbin
		[KSPField(isPersistant = true)] public double output_factor;    // combination of all efficiency factors : cosine_factor, panel specific conditions
		[KSPField(isPersistant = true)] public bool can_produce;        // should the panel generate Ec at all : false if occluded, broken, not deployed

		SupportedPanel solarPanel;

		public override void OnStart(StartState state)
		{
			// don't break tutorial scenarios
			if (Lib.DisableScenario(this)) return;

			// do nothing in the editor
			if (state == StartState.Editor) return;

			// find the module based on explicitely supported modules
			foreach (PartModule pm in part.Modules)
			{
				// stock module
				if (pm is ModuleDeployableSolarPanel)
				{
					solarPanel = new StockPanel();
					output_rate = solarPanel.OnStart(pm);
					break;
				}

				switch (pm.moduleName)
				{
					case "ModuleCurvedSolarPanel":      // NFS curved panel, custom implementation
						break;
					case "SSTUSolarPanelStatic":		// SSTU fixed panel, custom implementation
						break;
					case "SSTUSolarPanelDeployable":	// SSTU deployable panel, custom implementation
						break;
					case "SSTUModularPart":				// SSTU part that can have integrated solar panels
						break;
				}
			}

			if (solarPanel == null)
				enabled = isEnabled = moduleIsEnabled = false;
		}

		public void Update()
		{
			// do nothing in editor
			if (Lib.IsEditor() || solarPanel == null) return;

			solarPanel.OnUpdate();
		}

		public void FixedUpdate()
		{
			// do nothing in editor
			if (Lib.IsEditor() || solarPanel == null) return;

			// get resource handler
			Resource_info ec = ResourceCache.Info(vessel, "ElectricCharge");

			// get vessel data from cache
			Vessel_info info = Cache.VesselInfo(vessel);

			// do nothing if vessel is invalid
			if (!info.is_valid) return;

			bool analytical_sunlight = info.sunlight > 0.0 && info.sunlight < 1.0;


			// output factor stays fixed at high timewarp rates because we can't reliably evaluate it :
			// - local occlusion depend on sun_dir / vessel orientation wich will be totally random
			// - same for cosine factor
			if (!analytical_sunlight)
				output_factor = solarPanel.OnFixedUpdate(info.sun_dir, out can_produce);

			if (can_produce && info.sunlight > 0.0)
			{
				// calculate normalized solar flux
				// - this include fractional sunlight if integrated over orbit
				// - this include atmospheric absorption if inside an atmosphere
				double norm_solar_flux = info.solar_flux / Sim.SolarFluxAtHome();

				// calculate output
				double output = output_rate         // nominal panel charge rate at 1 AU
								* output_factor     // cosine factor of panel orientation and other panel specific factors
								* norm_solar_flux;  // normalized flux at panel distance from sun;                    

				// produce EC
				ec.Produce(output * Kerbalism.elapsed_s, "panel");

				// update ui
				field_visibility = info.sunlight * 100.0;
				field_atmosphere = info.atmo_factor * 100.0;
				field_exposure = output_factor * 100.0;
				field_output = output;
				Fields["field_visibility"].guiActive = analytical_sunlight;
				Fields["field_atmosphere"].guiActive = info.atmo_factor < 1.0;
				Fields["field_exposure"].guiActive = true;
				Fields["field_output"].guiActive = true;
			}
			else
			{
				// hide ui
				Fields["field_visibility"].guiActive = false;
				Fields["field_atmosphere"].guiActive = false;
				Fields["field_exposure"].guiActive = false;
				Fields["field_output"].guiActive = false;
			}

			// update status ui
			field_status = analytical_sunlight
			? "<color=#ffff22>Integrated over the orbit</color>"
			//: locally_occluded
			//? "<color=#ff2222>Occluded by vessel</color>"
			: info.sunlight < 1.0
			? "<color=#ff2222>Occluded by celestial body</color>"
			: string.Empty;
			Fields["field_status"].guiActive = field_status.Length > 0;
		}

		public static void BackgroundUpdate(Vessel v, ProtoPartModuleSnapshot m, Vessel_info vi, Resource_info ec, double elapsed_s)
		{
			if (!Lib.Proto.GetBool(m, "can_produce"))
				return;

			// nominal panel charge rate at 1 AU
			double output_rate = Lib.Proto.GetDouble(m, "output_rate");

			// Cosine factor of panel orientation and other panel factors.
			// We don't recalculate this for unloaded vessels, rationale :
			// - the player has no way to keep an optimal attitude while unloaded
			// - using the last factor ensure output consistency and is easy to do
			// - it's a good approximation of what would be realistic
			double output_factor = Lib.Proto.GetDouble(m, "output_factor");

			// calculate normalized solar flux
			// - this include fractional sunlight if integrated over orbit
			// - this include atmospheric absorption if inside an atmosphere
			double norm_solar_flux = vi.solar_flux / Sim.SolarFluxAtHome();

			// calculate output
			double output =
				output_rate         // nominal panel charge rate at 1 AU
				* output_factor     
				* norm_solar_flux;  // normalized flux at panel distance from sun

			// produce EC
			ec.Produce(output * elapsed_s, "panel");
		}

		private abstract class SupportedPanel
		{
			public abstract double OnStart(PartModule module);
			public abstract double OnFixedUpdate(Vector3d sunDir, out bool canProduce);
			public virtual void OnUpdate() { }
		}

		private abstract class SupportedPanel<T> : SupportedPanel where T : PartModule
		{
			public T panelModule;
		}

		private class StockPanel : SupportedPanel<ModuleDeployableSolarPanel>
		{
			public override double OnStart(PartModule module)
			{
				panelModule = (ModuleDeployableSolarPanel)module;

				// store rate
				double output_rate = panelModule.resHandler.outputResources[0].rate;

				// reset rate
				// - This break mods that evaluate solar panel output for a reason or another (eg: AmpYear, BonVoyage).
				//   We fix that by exploiting the fact that resHandler was introduced in KSP recently, and most of
				//   these mods weren't updated to reflect the changes or are not aware of them, and are still reading
				//   chargeRate. However the stock solar panel ignore chargeRate value during FixedUpdate.
				//   So we only reset resHandler rate.
				panelModule.resHandler.outputResources[0].rate = 0.0f;

				// hide ui
				panelModule.Fields["status"].guiActive = false;
				panelModule.Fields["sunAOA"].guiActive = false;
				panelModule.Fields["flowRate"].guiActive = false;

				return output_rate;
			}

			public override double OnFixedUpdate(Vector3d sunDir, out bool canProduce)
			{
				canProduce =
					panelModule.moduleIsEnabled
					&& panelModule.deployState == ModuleDeployablePart.DeployState.EXTENDED
					&& panelModule.hit.collider != null; // detect occlusion from other vessel parts

				// TODO : integrate time_factor as a WarpFixer field
				double time_factor = panelModule.timeEfficCurve.Evaluate((float)((Planetarium.GetUniversalTime() - panelModule.launchUT) * 1.1574074074074073E-05));

				// return cosine factor based on sun angle on the panel surface
				return Math.Max(Vector3d.Dot(sunDir, panelModule.trackingDotTransform.forward), 0.0);
			}
		}

		private class NFSCurvedPanel : SupportedPanel<PartModule>
		{
			private Transform[] panels;         // model transforms named after the "PanelTransformName" field
			private bool deployable;            // "Deployable" field
			private Action panelModuleUpdate;   // delegate for the module Update() method

			public override double OnStart(PartModule module)
			{
				// get the module
				panelModule = module;

				// get a delegate for Update() method (avoid performance penality of reflection)
				panelModuleUpdate = (Action)Delegate.CreateDelegate(panelModule.GetType(), panelModule, "Update");

				// ensure the module Start() has been called
				Lib.ReflectionCall(panelModule, "Start");

				// get values from module
				deployable = Lib.ReflectionValue<bool>(panelModule, "Deployable");
				string transform_name = Lib.ReflectionValue<string>(panelModule, "PanelTransformName");

				// get panel components
				panels = module.part.FindModelTransforms(transform_name);
				if (panels.Length == 0)
					return 0.0;

				// disable the module at the Unity level, we will handle its updates manually
				panelModule.enabled = false;

				// return panel nominal rate
				return Lib.ReflectionValue<float>(panelModule, "TotalEnergyRate");
			}

			public override double OnFixedUpdate(Vector3d sunDir, out bool canProduce)
			{
				canProduce = panelModule.moduleIsEnabled
					&& (!deployable || Lib.ReflectionValue<string>(panelModule, "SavedState") == "EXTENDED"); // ModuleDeployablePart.DeployState.EXTENDED

				if (!canProduce)
					return 0.0;

				double outputFactor = 0.0;

				// get part orientation
				Quaternion rot = panelModule.vessel.transform.rotation * panelModule.part.transform.rotation;

				// get a scalar aggregate of all panels cosine factor + occlusion checks
				foreach (Transform panelT in panels)
				{
					if (!Physics.Raycast(panelT.position, sunDir))
						outputFactor += Math.Max(Vector3d.Dot(sunDir, (rot * panelT.forward).normalized), 0.0);
				}
				outputFactor /= panels.Length;

				return outputFactor;
			}

			public override void OnUpdate()
			{
				panelModuleUpdate();
			}
		}

		private class SSTUStaticPanel : SupportedPanel<PartModule>
		{
			public override double OnStart(PartModule module)
			{
				throw new NotImplementedException();
			}

			public override double OnFixedUpdate(Vector3d sunDir, out bool canProduce)
			{
				throw new NotImplementedException();
			}
		}
	}
} // KERBALISM

