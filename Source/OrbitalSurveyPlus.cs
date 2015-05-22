using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace OrbitalSurveyPlus
{
    [KSPAddon(KSPAddon.Startup.SpaceCentre, false)]
    public class OrbitalSurveyPlus : MonoBehaviour
    {
        public static float OrbitsToScan
        {
            get;
            private set;
        }

        public static int OrbitMinimum
        {
            get;
            private set;
        }

        public static int OrbitMaximum
        {
            get;
            private set;
        }

        public static float ScannerElectricDrain
        {
            get;
            private set;
        }

        public static bool RequirePolarOrbit
        {
            get;
            private set;
        }

        public static bool ExtendedSurvey
        {
            get;
            private set;
        }

        public static bool BiomeMapRequiresScan
        {
            get;
            private set;
        }

        

        private string sOrbitsToScan;
        private string sOrbitMinimum;
        private string sOrbitMaximum;
        private string sElectricDrain;

        private static bool primaryInitialize = true;

        private static ApplicationLauncherButton appButtonBiomeOverlay = null;
        private static ApplicationLauncherButton appButtonConfigWindow = null;
        private static bool showConfigWindow = false;
        private static string settingsPath = "GameData/OrbitalSurveyPlus/settings.cfg";

        private static int lastBiomeTextureId = 0;

        private static Rect configWindowRect;

        public void Awake()
        {
            //only do this portion once ever
            if (primaryInitialize)
            {
                primaryInitialize = false;
                Log("Initializing");

                //initialize app buttons
                appButtonBiomeOverlay = ApplicationLauncher.Instance.AddModApplication(
                    ToggleBiomeOverlay,
                    ToggleBiomeOverlay,
                    null,
                    null,
                    null,
                    null,
                    ApplicationLauncher.AppScenes.FLIGHT | ApplicationLauncher.AppScenes.MAPVIEW,
                    GameDatabase.Instance.GetTexture("OrbitalSurveyPlus/Textures/OSPIcon-Biome", false)
                    );

                appButtonConfigWindow = ApplicationLauncher.Instance.AddModApplication(
                    ShowConfigWindow,
                    HideConfigWindow,
                    null,
                    null,
                    HideConfigWindow,
                    null,
                    ApplicationLauncher.AppScenes.SPACECENTER,
                    GameDatabase.Instance.GetTexture("OrbitalSurveyPlus/Textures/OSPIcon-Config", false)
                    );


                //initialize settings defaults
                SetConfigsToDefault();

                //load settings if config file exists
                if (System.IO.File.Exists(settingsPath))
                {
                    ConfigNode settingsRoot = ConfigNode.Load(settingsPath);
                    ConfigNode settings = settingsRoot.GetNode("SETTINGS");

                    if (settings.HasValue("biomeMapRequiresScan"))
                    {
                        bool result;
                        bool a = bool.TryParse(settings.GetValue("biomeMapRequiresScan"), out result);
                        if (a) OrbitalSurveyPlus.BiomeMapRequiresScan = result;
                    }

                    if (settings.HasValue("extendedSurvey"))
                    {
                        bool result;
                        bool a = bool.TryParse(settings.GetValue("extendedSurvey"), out result);
                        if (a) OrbitalSurveyPlus.ExtendedSurvey = result;
                    }

                    if (settings.HasValue("orbitsToScan"))
                    {
                        float result;
                        bool a = float.TryParse(settings.GetValue("orbitsToScan"), out result);
                        if (a) OrbitalSurveyPlus.OrbitsToScan = result;
                    }


                    if (settings.HasValue("orbitMinimum"))
                    {
                        int result;
                        bool a = Int32.TryParse(settings.GetValue("orbitMinimum"), out result);
                        if (a) OrbitalSurveyPlus.OrbitMinimum = result;
                    }


                    if (settings.HasValue("orbitMaximum"))
                    {
                        int result;
                        bool a = Int32.TryParse(settings.GetValue("orbitMaximum"), out result);
                        if (a) OrbitalSurveyPlus.OrbitMaximum = result;
                    }


                    if (settings.HasValue("requirePolarOrbit"))
                    {
                        bool result;
                        bool a = Boolean.TryParse(settings.GetValue("requirePolarOrbit"), out result);
                        if (a) OrbitalSurveyPlus.RequirePolarOrbit = result;
                    }


                    if (settings.HasValue("electricDrain"))
                    {
                        float result;
                        bool a = float.TryParse(settings.GetValue("electricDrain"), out result);
                        if (a) OrbitalSurveyPlus.ScannerElectricDrain = result;
                    }
                }
                else
                {
                    Log("setings.cfg not found - creating new one with defaults");
                    Save();
                }

                //config window position
                configWindowRect = new Rect(50f, 100f, 300f, 180f);
            }

            //set in-game menu strings
            SetUIStrings();

        }

        public static void ToggleBiomeOverlay()
        {
            //since button is a toggle, keep it at "disabled" graphic
            appButtonBiomeOverlay.SetFalse(false);

            Vessel vessel = FlightGlobals.ActiveVessel;
            CelestialBody body = FlightGlobals.getMainBody();
            
            //sanity check
            if (vessel == null || body == null)
            {
                Log("Error: vessel or body is null!");
                return;
            }
            
            //scan check
            if (OrbitalSurveyPlus.BiomeMapRequiresScan && !ResourceMap.Instance.IsPlanetScanned(body.flightGlobalsIndex))
            {
                ScreenMessages.PostScreenMessage(String.Format("Biome Map Unavailable: No survey data available for {0}",
                    body.RevealName()), 
                    5.0f, ScreenMessageStyle.UPPER_CENTER);
                return;
            }
            
            //run map comparison before forcing "HideOverlay()" on any scanner modules which deletes current map (took me hours to find out this was happening...)
            //state check
            bool biomeMapShowing = false;
            if (body.ResourceMap != null && body.ResourceMap.GetInstanceID() == OrbitalSurveyPlus.lastBiomeTextureId)
            {
                biomeMapShowing = true;
            }

            //check to see if Orbital Surveyor is on current vessel
            //active necessary functions
            foreach (Part p in vessel.Parts)
            {
                foreach (PartModule m in p.Modules)
                {
                    if (m.moduleName == "ModuleOrbitalScannerPlus")
                    {
                        ModuleOrbitalScannerPlus scanner = (ModuleOrbitalScannerPlus)m;
                        scanner.HideOverlay();
                    }
                }
            }

            //overlay switch
            if (biomeMapShowing)
            {
                //turn off
                body.SetResourceMap(null);
            }
            else
            {
                //turn on
                UnityEngine.Texture2D biomeTexture = body.BiomeMap.CompileRGB();
                body.SetResourceMap(biomeTexture);
                OrbitalSurveyPlus.lastBiomeTextureId = body.ResourceMap.GetInstanceID();
            }
        }

        public static void ShowConfigWindow()
        {
            showConfigWindow = true;
        }

        public static void HideConfigWindow()
        {
            showConfigWindow = false;
        }

        public void OnGUI()
        {
            if (showConfigWindow)
            {
                Rect rect = GUILayout.Window(
                    GetInstanceID(), 
                    OrbitalSurveyPlus.configWindowRect, 
                    DrawConfigWindow, 
                    "Orbital Survey Plus",
                    GUILayout.ExpandHeight(true),
                    GUILayout.ExpandWidth(true)
                );

                OrbitalSurveyPlus.configWindowRect = rect;
            }
        }

        public void DrawConfigWindow(int windowId)
        {
            bool change = false;
            

            GUILayout.BeginVertical();

            //------------------------------

            GUILayout.BeginHorizontal();
            bool biomeMapRequiresScan = GUILayout.Toggle(OrbitalSurveyPlus.BiomeMapRequiresScan, "  Biome Map Requires Scan");
            if (biomeMapRequiresScan != OrbitalSurveyPlus.BiomeMapRequiresScan)
            {
                OrbitalSurveyPlus.BiomeMapRequiresScan = biomeMapRequiresScan;
                change = true;
            }
            GUILayout.EndHorizontal();

            //------------------------------

            GUILayout.BeginHorizontal();
            bool extendedSurvey = GUILayout.Toggle(OrbitalSurveyPlus.ExtendedSurvey, "  Extend Orbital Surveyor (scan is not instant)");
            if (extendedSurvey != OrbitalSurveyPlus.ExtendedSurvey)
            {
                OrbitalSurveyPlus.ExtendedSurvey = extendedSurvey;
                change = true;
            }
            GUILayout.EndHorizontal();

            //************************************************************
            bool showExtendedSurveyOptions = extendedSurvey;
            //************************************************************

            //------------------------------

            GUILayout.BeginHorizontal();
            string labelRequirePolarOrbit = showExtendedSurveyOptions ?
                "  Extended Scan Requires Polar Orbit" :
                "<color=#6d6d6d>  Extended Scan Requires Polar Orbit</color>";
            bool requirePolarOrbit = GUILayout.Toggle(OrbitalSurveyPlus.RequirePolarOrbit, labelRequirePolarOrbit);
            if (requirePolarOrbit != OrbitalSurveyPlus.RequirePolarOrbit)
            {
                OrbitalSurveyPlus.RequirePolarOrbit = requirePolarOrbit;
                change = true;
            }
            
            GUILayout.EndHorizontal();
            

            //------------------------------------------------------

            GUILayout.BeginHorizontal();

            GUILayout.BeginVertical();

            GUILayout.Label(showExtendedSurveyOptions ?
                "Orbits Needed to Scan" :
                "<color=#6d6d6d>Orbits Needed to Scan</color>");
            GUILayout.Label(showExtendedSurveyOptions ?
                "Scanner ElectricCharge Drain:" :
                "<color=#6d6d6d>Scanner ElectricCharge Drain:</color>");
            GUILayout.Label("Survey Minimum Altitude:");
            GUILayout.Label("Survey Maximum Altitude:");
            GUILayout.EndVertical();


            //-------------------------------------------------------

            GUILayout.BeginVertical();

            GUILayout.BeginHorizontal();
            sOrbitsToScan = GUILayout.TextField(sOrbitsToScan);

            float orbitsToScan;
            if (float.TryParse(sOrbitsToScan, out orbitsToScan) &&
                orbitsToScan != OrbitalSurveyPlus.OrbitsToScan)
            {
                OrbitalSurveyPlus.OrbitsToScan = orbitsToScan;
                change = true;
            }
            GUILayout.EndHorizontal();

            //-------------------------------------------------------

            GUILayout.BeginHorizontal();
            sElectricDrain = GUILayout.TextField(sElectricDrain);

            float electricDrain;
            if (float.TryParse(sElectricDrain, out electricDrain) &&
                electricDrain != OrbitalSurveyPlus.ScannerElectricDrain)
            {
                OrbitalSurveyPlus.ScannerElectricDrain = electricDrain;
                change = true;
            }
            GUILayout.EndHorizontal();

            //-------------------------------------------------------

            GUILayout.BeginHorizontal();
            sOrbitMinimum = GUILayout.TextField(sOrbitMinimum);
            
            int orbitMinimum;
            if (int.TryParse(sOrbitMinimum, out orbitMinimum) &&
                orbitMinimum != OrbitalSurveyPlus.OrbitMinimum)
            {
                OrbitalSurveyPlus.OrbitMinimum = orbitMinimum;
                change = true;
            }
            GUILayout.EndHorizontal();

            //-------------------------------------------------------

            GUILayout.BeginHorizontal();
            sOrbitMaximum = GUILayout.TextField(sOrbitMaximum);
            
            int orbitMaximum;
            if (int.TryParse(sOrbitMaximum, out orbitMaximum) &&
                orbitMaximum != OrbitalSurveyPlus.OrbitMaximum)
            {
                OrbitalSurveyPlus.OrbitMaximum = orbitMaximum;
                change = true;
            }
            GUILayout.EndHorizontal();

            //-------------------------------------------------------
            GUILayout.EndVertical();

            GUILayout.EndHorizontal();

            //------------------------------

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Reset to Defaults"))
            {
                SetConfigsToDefault();
                SetUIStrings();
                change = true;
            }
            GUILayout.EndHorizontal();

            //------------------------------

            GUILayout.EndVertical();

            GUI.DragWindow();

            if (change)
            {
                Save();
            }
        }

        public static void SetConfigsToDefault()
        {
            OrbitalSurveyPlus.BiomeMapRequiresScan = true;
            OrbitalSurveyPlus.ExtendedSurvey = true;
            OrbitalSurveyPlus.OrbitsToScan = 1.00f;
            OrbitalSurveyPlus.OrbitMinimum = 25000;
            OrbitalSurveyPlus.OrbitMaximum = 1500000;
            OrbitalSurveyPlus.ScannerElectricDrain = 0.75f;
            OrbitalSurveyPlus.RequirePolarOrbit = true;
            
        }

        public void SetUIStrings()
        {
            sOrbitsToScan = OrbitalSurveyPlus.OrbitsToScan.ToString();
            sOrbitMinimum = OrbitalSurveyPlus.OrbitMinimum.ToString();
            sOrbitMaximum = OrbitalSurveyPlus.OrbitMaximum.ToString();
            sElectricDrain = OrbitalSurveyPlus.ScannerElectricDrain.ToString();

            //make sure floats are shown to be decimals to user
            if (!sOrbitsToScan.Contains("."))
            {
                sOrbitsToScan += ".0";
            }

            if (!sElectricDrain.Contains("."))
            {
                sElectricDrain += ".0";
            }
        }

        public void Save()
        {
            ConfigNode file = new ConfigNode();

            ConfigNode settings = file.AddNode("SETTINGS");

            settings.AddValue("biomeMapRequiresScan", OrbitalSurveyPlus.BiomeMapRequiresScan);
            settings.AddValue("extendedSurvey", OrbitalSurveyPlus.ExtendedSurvey);
            settings.AddValue("orbitsToScan", OrbitalSurveyPlus.OrbitsToScan);
            settings.AddValue("orbitMinimum", OrbitalSurveyPlus.OrbitMinimum);
            settings.AddValue("orbitMaximum", OrbitalSurveyPlus.OrbitMaximum);
            settings.AddValue("electricDrain", OrbitalSurveyPlus.ScannerElectricDrain);
            settings.AddValue("requirePolarOrbit", OrbitalSurveyPlus.RequirePolarOrbit);
            

            file.Save(settingsPath);
        }

        public static void Log(String log)
        {
            PDebug.Log("[OrbitalSurveyPlus] " + log);
        }

    }
}
