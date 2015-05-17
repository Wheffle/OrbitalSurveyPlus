/*
 * Author: Chase Barnes ("Wheffle")
 * <altoid287@gmail.com>
 * 
 */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace OrbitalSurveyPlus
{
    public class ModuleOrbitalSurveyorPlus : ModuleOrbitalSurveyor, IAnimatedModule, IResourceConsumer
    {

        [KSPField(guiActive = true, guiName = "Survey Progress", guiFormat = "P0", isPersistant = true)]
        float scanPercent = 0;

        [KSPField(guiActive = true, guiName = "Status")]
        string scanStatus = "standby";

        [KSPField(isPersistant = true)]
        string currentBody = "";

        [KSPField(isPersistant = true)]
        double lastUpdate = -1;

        private bool freshLoaded = false;

        private float orbitsToScan = 1f;
        //private int defaultOrbitMin = 25000; //in meters above sea level
        //private int defaultOrbitMax = 1500000; //in meters above sea level
        private bool requirePolarOrbit = true;
        private double electricDrain = 0.75; //per second

        private PartResourceDefinition resType = PartResourceLibrary.Instance.GetDefinition("ElectricCharge");

        private int polarIncTolerance = 10;

        private int orbitMin = 25000;
        private int orbitMax = 1500000;

        private bool scanDone = false;

        private Orbit orbit = null;
        private CelestialBody body = null;

        public override void OnStart(StartState state)
        {
            base.OnStart(state);

            //change "Perform orbital survey" button name to make more sense with this mod
            Events["PerformSurvey"].guiName = "Transmit survey data";

            //load settings
            ConfigNode settingsRoot = ConfigNode.Load("GameData/OrbitalSurveyPlus/settings.cfg");
            ConfigNode settings = settingsRoot.GetNode("SETTINGS");
            
            if (settings.HasValue("orbitsToScan"))
            {
                double result;
                bool a = Double.TryParse(settings.GetValue("orbitsToScan"), out result);
                if (a) orbitsToScan = (float)result;
            }

            if (settings.HasValue("orbitMinimum"))
            {
                int result;
                bool a = Int32.TryParse(settings.GetValue("orbitMinimum"), out result);
                if (a) minThreshold = result;
            }

            if (settings.HasValue("orbitMaximum"))
            {
                int result;
                bool a = Int32.TryParse(settings.GetValue("orbitMaximum"), out result);
                if (a) maxThreshold = result;
            }

            if (settings.HasValue("requirePolarOrbit"))
            {
                bool result;
                bool a = Boolean.TryParse(settings.GetValue("requirePolarOrbit"), out result);
                if (a) requirePolarOrbit = result;
            }

            if (settings.HasValue("electricDrain"))
            {
                double result;
                bool a = Double.TryParse(settings.GetValue("electricDrain"), out result);
                if (a) electricDrain = result;
            }

            //vessel load flags
            scanDone = CheckPlanetScanned();
            freshLoaded = true;
        }

        public override void OnFixedUpdate()
        {
            base.OnFixedUpdate();

            //get orbit and body
            orbit = vessel.GetOrbit();
            body = orbit.referenceBody;

            //check to see if body is same as currentBody,
            //otherwise reset scan progress to 0
            if (body.RevealName() != currentBody)
            {
                //new body encountered: reset scanner and update orbit parameters
                scanPercent = 0;
                lastUpdate = -1;
                currentBody = body.RevealName();

                //update orbit cutoffs here
                UpdateOrbitParameters();

                //appropriately activate scanner
                scanDone = CheckPlanetScanned();
                SetScannerActive(!scanDone);
            }

            if (!scanDone)
            {
                //check to see if scan is done
                if (CheckPlanetScanned())
                {
                    //scan is done, shut down scanning portion of module
                    SetScannerActive(false);
                }
                else
                {
                    //perform scan
                    Scan();
                }
            }

            freshLoaded = false;
        }

        private void UpdateOrbitParameters()
        {
            orbitMin = Math.Max(minThreshold, ((int)body.Radius) / 10);
            orbitMax = Math.Min(maxThreshold, ((int)body.Radius) * 5);

            //Pe under atmosphere doesn't count as stable orbit, but that is
            //checked "on the fly" to mimic how the stock surveyor works
        }

        private void Scan()
        {

            //check to see if scan is needed
            if (scanPercent < 1)
            {
                //check suitable orbit
                if (ConditionsMet())
                {
                    //get time since last update
                    double ut = Planetarium.GetUniversalTime();
                    if (lastUpdate == -1)
                    {
                        //first update: do nothing
                        lastUpdate = ut;
                    }
                    else if (ut - lastUpdate > 0)
                    {
                        //time lapse has occurred: update
                        double timeElapsed = ut - lastUpdate;
                        lastUpdate = ut;

                        //if vessel was freshly loaded and in the middle of a scan, decide what to do
                        bool skipDrain = false;
                        if (freshLoaded)
                        {
                            if (!HasElectricChargeGenerator())
                            {
                                //if no power generator exists, put scan update on hold
                                ScreenMessages.PostScreenMessage("Survey Scan Was Idle: No power generator detected", 6.0f, ScreenMessageStyle.UPPER_CENTER);
                                return;
                            }
                            else
                            {
                                //power generator onboard, but skip electrical drain for next update (could be huge)
                                skipDrain = true;
                            }
                        }

                        //drain electric charge
                        double drain = electricDrain * timeElapsed;
                        Vessel.ActiveResource ar = vessel.GetActiveResource(resType);

                        if (skipDrain || ar.amount >= drain)
                        {
                            //calculate scan percentage completed
                            double period = orbit.period;
                            double pct_scanned = timeElapsed / period * orbitsToScan;
                            scanPercent += (float)pct_scanned;

                            if (scanPercent >= 1)
                            {
                                scanPercent = 1;
                                ScreenMessages.PostScreenMessage("Survey Scan Completed", 6.0f, ScreenMessageStyle.UPPER_CENTER);
                            }

                            scanStatus = "scanning";
                        }
                        else
                        {
                            scanStatus = "not enough " + resType.name;
                        }

                        if (!skipDrain) part.RequestResource(resType.id, drain);

                    }

                }
                else
                {
                    //orbit is not suitable
                    scanStatus = "unsuitable orbit";
                }
            }
            else
            {
                //scan is completed
                scanStatus = "survey completed";
            }

        }

        private bool ConditionsMet()
        {
            if (orbit == null || body == null) return false;

            double pe = orbit.PeA;
            double ap = orbit.ApA;
            double inc = orbit.inclination;

            //check Pe and Ap are within parameters
            bool orbitCheck = pe > orbitMin && ap < orbitMax && ap > 0;

            //if planet has atmosphere, Pe under atmosphere doesn't count as stable orbit
            if (body.atmosphere) orbitCheck = orbitCheck && pe > body.atmosphereDepth;

            //check inclination
            bool incCheck = !requirePolarOrbit ||
                (inc > 90 - polarIncTolerance && inc < 90 + polarIncTolerance);

            if (orbitCheck && incCheck)
            {
                return true;
            }

            return false;
        }

        private void SetScannerActive(bool active)
        {
            if (active)
            {
                scanDone = false;
                Fields["scanPercent"].guiActive = true;
                Fields["scanStatus"].guiActive = true;
            }
            else
            {
                scanDone = true;
                Fields["scanPercent"].guiActive = false;
                Fields["scanStatus"].guiActive = false;
            }
        }

        private bool CheckPlanetScanned()
        {
            if (body == null) return false;
            return ResourceMap.Instance.IsPlanetScanned(body.flightGlobalsIndex);
        }

        private bool HasElectricChargeGenerator()
        {
            //determine whether vessel has power generation
            //This only 
            foreach (Part p in vessel.GetActiveParts())
            {

                foreach (PartModule module in p.Modules)
                {
                    if (module.moduleName == "ModuleDeployableSolarPanel")
                    {
                        ModuleDeployableSolarPanel panel = (ModuleDeployableSolarPanel)module;
                        if (panel.panelState == ModuleDeployableSolarPanel.panelStates.EXTENDED) return true;
                    }

                    if (module.moduleName == "ModuleGenerator")
                    {
                        ModuleGenerator gen = (ModuleGenerator)module;
                        if (gen.generatorIsActive)
                        {
                            foreach (ModuleGenerator.GeneratorResource genRes in gen.outputList)
                            {
                                if (genRes.name == resType.name && genRes.rate > 0) return true;
                            }
                        }
                    }
                }
                
            }

            return false;
        }


        public new void PerformSurvey()
        {
            //block stock "PerformSurvey" event from firing unless scan is at 100%

            if (scanPercent < 1)
            {
                if (!ConditionsMet())
                {
                    ScreenMessages.PostScreenMessage(
                        String.Format("Survey Scan Incomplete: You must be in a stable {0} between {1}km and {2}km",
                        requirePolarOrbit ? "polar orbit" : "orbit", orbitMin / 1000, orbitMax / 1000),
                        5.0f, ScreenMessageStyle.UPPER_CENTER);
                }
                else
                {
                    ScreenMessages.PostScreenMessage("Survey Scan Incomplete (" + Math.Round(scanPercent * 100) + "%)", 3.0f, ScreenMessageStyle.UPPER_CENTER);
                }
            }
            else
            {
                base.PerformSurvey();
            }
        }

        
        public override string GetInfo()
        {
            //show electric charge info 
            String s = base.GetInfo();
            if (electricDrain > 0)
            {
                s += "\n\n<b><color=#99ff00ff>Requires (while scanning):</color></b>";
                s += "\nElectric Charge: " + electricDrain + "/sec.";
            }
            return s;
        }

        public List<PartResourceDefinition> GetConsumedResources()
        {
            List<PartResourceDefinition> list = new List<PartResourceDefinition>();
            list.Add(resType);
            return list;
        }

        public new void EnableModule()
        {
            base.EnableModule();
            lastUpdate = -1;
            freshLoaded = false;
        }

        
    }
}
