// Snapshot of the vessel state for one physics tick, and the sampler that fills it.
using System;
#if !DC_OFFLINE_TEST
using System.Collections.Generic;
using UnityEngine;
#endif

namespace Lonapse
{
    public struct FlightSample
    {
        public bool valid;
        public double omega;        // rad/s
        public double g;            // geeForce_immediate, clamped 0..gClamp
        public double qKPa;
        public double vs;           // vertical speed, m/s, negative = down
        public double h;            // height above ground, m
        public double srfSpeed;
        public double atmDensity;
        public double mach;
        public int situation;       // (int)Vessel.Situations
        public bool landed;
        public bool packed;
        public double throttle;
        // 4 Hz group
        public double heatRatio;
        public double shockTemp;
        public double extTemp;
        public bool shockTempValid;
        public bool inPlasmaStock;
        // 1 Hz group
        public double fuelFrac;     // ships: liquid/solid/mono rule (FuelMath). EVA: jetpack propellant fraction
        public bool engineOut;      // an engine flamed out while it still had propellant (launch cap)
        public double ecFrac;       // power events: connected ElectricCharge amount / max, 1 when max is 0
        public bool hasSolar;       // the vessel has a solar panel (ModuleDeployableSolarPanel, fixed panels too), 1 Hz
        public bool hasShield;      // the vessel has a heat shield (ModuleAblator), 1 Hz
        public bool chuteStowed;    // a parachute is still packed or armed (STOWED or ACTIVE), 1 Hz
        public bool chuteOpen;      // a parachute is open or half open (SEMIDEPLOYED or DEPLOYED), 1 Hz
        public double ecMax;        // connected ElectricCharge capacity (power events need ecMax >= powerMinMax)
        // EVA. Defaults are the neutral values for a ship.
        public bool eva;            // Kerbal on EVA
        public double evaPropFrac;  // jetpack "EVA Propellant" fraction (KerbalEVA.Fuel / FuelCapacity), valid when eva
        public double evaEcFrac;    // ElectricCharge fraction, valid when eva (stock kerbals carry none, so 1)
        public double crewDist;     // m to the nearest other crewed vessel, +infinity when none, valid when eva
        public bool evaThrusting;   // KerbalEVA.JetpackIsThrusting, per tick
        public bool evaRagdoll;     // KerbalEVA.isRagdoll, per tick
        public float evaRun;        // 0..1 how hard the EVA kerbal runs on the ground (walk speed 0, run speed 1), per tick
        // rover, per tick. Set for a rover with or without crew.
        public bool rover;
        public float roverRough;    // std dev of vertical acceleration over 1 s, m/s2
        public float roverAirTime;  // s since the wheels last touched the ground
        public float roverTilt;     // deg away from the attitude the rover had when it last stood still
        // flight phase inputs, per tick
        public string body;         // FlightPhase.BodyKey(mainBody.bodyName): stock key or "generic"
        public double alt;          // m above sea level
        public double atmDepth;     // m, 0 when the body has no atmosphere
        public string phase;        // current FlightPhase, set by the director (not by the sampler)
        // probe mode. Filled by the gate code, not by the sampler. Defaults mean "no probe".
        public string probeCoreKey; // ProbeCores key of the root-most probe core, null when the vessel is not in probe mode
        public bool probeAsleep;    // a probe core reports NotEnoughResources or is hibernating

        public PhaseInput ToPhaseInput()
        {
            PhaseInput i;
            i.situation = (PhaseSituation)situation;
            i.verticalSpeed = vs;
            i.altitude = alt;
            i.height = h;
            i.atmosphereDepth = atmDepth;
            i.atmDensity = atmDensity;
            i.mach = mach;
            i.throttle = throttle;
            i.isEva = eva;
            i.srfSpeed = srfSpeed;
            return i;
        }
    }

    /// <summary>
    /// Fuel fraction rule (pure, tested offline).
    /// Resources: 0 LiquidFuel, 1 Oxidizer, 2 SolidFuel, 3 MonoPropellant, 4 XenonGas.
    /// Groups: liquid = {LiquidFuel, Oxidizer}, solid, mono, xenon. Result = MAX over the groups that count, so the
    /// vessel is "low on fuel" only when every propellant it is burning is low.
    /// - With engine info (at least one ignited engine found): a resource counts only if an ignited engine burns it.
    ///   Without engine info: only liquid and solid count, mono and xenon never.
    /// - A group's fraction is the MIN over its counting members with max > 0 (LF+Ox run out together).
    /// - Empty SolidFuel (amount ~0) never counts: spent SRBs, sepratrons and escape towers often stay attached.
    /// - No counting group -> 1 (no fuel stress).
    /// </summary>
    public static class FuelMath
    {
        public const int LF = 0, OX = 1, SOLID = 2, MONO = 3, XENON = 4, Count = 5;
        public static readonly string[] Names = { "LiquidFuel", "Oxidizer", "SolidFuel", "MonoPropellant", "XenonGas" };
        private static readonly int[][] Groups = { new[] { LF, OX }, new[] { SOLID }, new[] { MONO }, new[] { XENON } };

        /// <summary>EVA jetpack fuel: MonoPropellant amount / max, 1 when there is no max. Clamped 0..1.</summary>
        public static double EvaFraction(double amount, double max)
        {
            if (!(max > 0)) return 1.0;
            double f = DcMath.Finite(amount / max);
            return f < 0 ? 0 : (f > 1 ? 1 : f);
        }

        public static double Fraction(double[] amount, double[] max, bool[] usedByEngine, bool haveEngineInfo)
        {
            double best = -1;
            for (int g = 0; g < Groups.Length; g++)
            {
                double gFrac = 2;
                bool any = false;
                for (int k = 0; k < Groups[g].Length; k++)
                {
                    int i = Groups[g][k];
                    if (i >= amount.Length || i >= max.Length) continue;
                    if (!(max[i] > 0)) continue;
                    bool counts = haveEngineInfo ? (i < usedByEngine.Length && usedByEngine[i]) : (i == LF || i == OX || i == SOLID);
                    if (!counts) continue;
                    if (i == SOLID && amount[i] <= Math.Max(1e-3, max[i] * 1e-4)) continue;
                    double f = DcMath.Finite(amount[i] / max[i]);
                    if (f < 0) f = 0;
                    if (f < gFrac) gFrac = f;
                    any = true;
                }
                if (any && gFrac > best) best = gFrac;
            }
            if (best < 0) return 1.0;
            return best > 1 ? 1.0 : best;
        }
    }

#if !DC_OFFLINE_TEST
    public class FlightSampler
    {
        private Vessel lastVessel;
        private FlightIntegrator fi;
        private float heatTimer;
        private float fuelTimer;
        private bool resIdsReady;
        private readonly int[] resIds = { -1, -1, -1, -1, -1 };
        private readonly double[] fuelAmount = new double[FuelMath.Count];
        private readonly double[] fuelMax = new double[FuelMath.Count];
        private readonly bool[] fuelUsed = new bool[FuelMath.Count];
        private int ecResId = -1;
        private CelestialBody lastBody;
        private string bodyKey = FlightPhase.GenericBody;

        // cached slow groups
        private double heatRatio, shockTemp, extTemp, fuelFrac = 1.0;
        private bool shockValid, inPlasma;
        private int shockNre;
        private bool engineOut;
        // engines in flameout with propellant left: real time the flameout was first seen. An engine counts as "out"
        // (engine_out cause, real failure) only for EngineOutHold s, so one failure does not repeat forever.
        private readonly Dictionary<ModuleEngines, float> flameoutSince = new Dictionary<ModuleEngines, float>();
        private readonly List<ModuleEngines> flameoutDrop = new List<ModuleEngines>();
        public const float EngineOutHold = 20f;
        private double evaPropFrac = 1.0, evaEcFrac = 1.0, crewDist = double.PositiveInfinity;
        private double ecFrac = 1.0, ecMax;
        private bool hasSolar, hasShield, chuteStowed, chuteOpen;

        // rover roughness and air time, reset with the vessel
        private RollingStat roverAccel = new RollingStat(1f);
        private double lastVs;
        private bool hasLastVs;
        private float airTime;
        // rover tilt baseline: body-up in the vessel frame, taken while standing still
        private Vector3 restUpLocal;
        private bool hasRestUp;
        private float stillTime;
        // EVA module of the active kerbal, refreshed at 1 Hz
        private KerbalEVA evaModule;
        private float evaAirTime;   // s the EVA kerbal has been off the ground

        public void Reset()
        {
            lastVessel = null;
            fi = null;
            heatTimer = 0f;
            fuelTimer = 0f;
            heatRatio = 0; shockTemp = 0; extTemp = 0; fuelFrac = 1.0;
            shockValid = false; inPlasma = false;
            engineOut = false;
            flameoutSince.Clear();
            evaPropFrac = 1.0; evaEcFrac = 1.0; crewDist = double.PositiveInfinity;
            ecFrac = 1.0; ecMax = 0;
            hasSolar = false; hasShield = false; chuteStowed = false; chuteOpen = false;
            roverAccel = new RollingStat(1f);
            hasLastVs = false;
            airTime = 0f;
            hasRestUp = false;
            stillTime = 0f;
            evaModule = null;
        }

        private void EnsureResourceIds()
        {
            if (resIdsReady) return;
            resIdsReady = true;
            try
            {
                PartResourceLibrary lib = PartResourceLibrary.Instance;
                for (int i = 0; i < FuelMath.Count; i++)
                {
                    PartResourceDefinition d = lib != null ? lib.GetDefinition(FuelMath.Names[i]) : null;
                    if (d != null) resIds[i] = d.id;
                    else Log.Warn("Resource definition not found: " + FuelMath.Names[i]);
                }
                PartResourceDefinition ec = lib != null ? lib.GetDefinition("ElectricCharge") : null;
                if (ec != null) ecResId = ec.id;
                else Log.Warn("Resource definition not found: ElectricCharge");
            }
            catch (Exception e) { Log.Warn("Resource lookup failed: " + e.Message); }
        }

        public void Sample(Vessel v, float dt, Tuning tu, ref FlightSample s)
        {
            s.valid = false;
            if (v == null) return;
            if (!ReferenceEquals(v, lastVessel))
            {
                Reset();
                lastVessel = v;
                heatTimer = 999f;  // force slow groups on the first tick
                fuelTimer = 999f;
            }

            try
            {
                s.omega = DcMath.Finite((double)v.angularVelocity.magnitude);
                double g = DcMath.Finite(v.geeForce_immediate);
                s.g = g < 0 ? 0 : (g > tu.gClamp ? tu.gClamp : g);
                s.qKPa = DcMath.Finite(v.dynamicPressurekPa);
                s.vs = DcMath.Finite(v.verticalSpeed);
                s.srfSpeed = DcMath.Finite(v.srfSpeed);
                s.atmDensity = DcMath.Finite(v.atmDensity);
                s.mach = DcMath.Finite(v.mach);
            }
            catch (Exception e) { Log.Exception("FlightSampler.motion", e); }

            try
            {
                double hft = DcMath.Finite((double)v.heightFromTerrain);
                double radar = DcMath.Finite(v.radarAltitude);
                if (hft >= 0) s.h = hft;
                else if (radar > 0) s.h = radar;
                else s.h = DcMath.Finite(v.altitude);
                if (s.h < 0) s.h = 0;
            }
            catch (Exception e) { Log.Exception("FlightSampler.height", e); }

            try
            {
                s.alt = DcMath.Finite(v.altitude);
                CelestialBody b = v.mainBody;
                if (!ReferenceEquals(b, lastBody))
                {
                    lastBody = b;
                    bodyKey = b != null ? FlightPhase.BodyKey(b.bodyName) : FlightPhase.GenericBody;
                }
                s.body = bodyKey;
                s.atmDepth = b != null && b.atmosphere ? DcMath.Finite(b.atmosphereDepth) : 0.0;
            }
            catch (Exception e) { s.body = FlightPhase.GenericBody; s.atmDepth = 0; Log.Exception("FlightSampler.body", e); }

            try
            {
                s.situation = (int)v.situation;
                s.landed = v.LandedOrSplashed;
                s.packed = v.packed;
                s.throttle = v.ctrlState != null ? DcMath.Finite((double)v.ctrlState.mainThrottle) : 0;
            }
            catch (Exception e) { Log.Exception("FlightSampler.state", e); }

            heatTimer += dt;
            if (heatTimer >= 0.25f)
            {
                heatTimer = 0f;
                SampleHeat(v);
            }
            fuelTimer += dt;
            if (fuelTimer >= 1.0f)
            {
                fuelTimer = 0f;
                SampleFuel(v);
                SampleEva(v);
            }
            s.heatRatio = heatRatio;
            s.shockTemp = shockTemp;
            s.extTemp = extTemp;
            s.shockTempValid = shockValid;
            s.inPlasmaStock = inPlasma;
            s.fuelFrac = fuelFrac;
            s.engineOut = engineOut;
            s.ecFrac = ecFrac;
            s.hasSolar = hasSolar;
            s.hasShield = hasShield;
            s.chuteStowed = chuteStowed;
            s.chuteOpen = chuteOpen;
            s.ecMax = ecMax;
            s.eva = false;
            try { s.eva = v.isEVA; } catch (Exception e) { Log.Exception("FlightSampler.eva", e); }
            s.evaPropFrac = s.eva ? evaPropFrac : 1.0;
            s.evaEcFrac = s.eva ? evaEcFrac : 1.0;
            s.crewDist = s.eva ? crewDist : 0.0;
            s.evaThrusting = false;
            s.evaRagdoll = false;
            s.evaRun = 0f;
            if (!s.eva || s.landed) evaAirTime = 0f; else evaAirTime += dt;
            if (s.eva && evaModule != null)
            {
                try
                {
                    s.evaThrusting = evaModule.JetpackIsThrusting;
                    s.evaRagdoll = evaModule.isRagdoll;
                    // on the ground or between the hops of a low-gravity bound, not flying the jetpack
                    if (!s.evaRagdoll && !s.evaThrusting && evaAirTime < 1.0f)
                        s.evaRun = DcMath.EvaRunLevel((float)s.srfSpeed, evaModule.walkSpeed, evaModule.runSpeed);
                }
                catch (Exception e) { evaModule = null; Log.Exception("FlightSampler.evaState", e); }
            }
            SampleRover(v, dt, ref s);
            s.valid = true;
        }

        /// <summary>Rover: roughness from the spread of vertical acceleration, air time, tilt. Per tick.</summary>
        private void SampleRover(Vessel v, float dt, ref FlightSample s)
        {
            s.rover = false;
            try
            {
                if (dt > 0f)
                {
                    if (s.landed) airTime = 0f; else airTime += dt;
                    if (hasLastVs) roverAccel.Add((float)((s.vs - lastVs) / dt), dt);
                    lastVs = s.vs;
                    hasLastVs = true;
                }
                // Probe rovers too (rover moments and the stress of a probe rover)
                if (v.vesselType != VesselType.Rover) return;
                s.rover = true;
                s.roverRough = roverAccel.StdDev;
                s.roverAirTime = airTime;
                // Tilt relative to the rover's own resting attitude. The root part's axes say nothing about
                // which way is "down" (an SPH-built rover has transform.up pointing forward), so the body-up
                // direction is recorded in the vessel frame while the rover stands still on the ground,
                // and the tilt is the angle between that recorded direction and body-up now.
                s.roverTilt = 0f;
                Transform tr = v.transform;
                if (tr != null)
                {
                    Vector3 up = (Vector3)v.upAxis;
                    if (s.landed && s.srfSpeed < 0.5) stillTime += dt; else stillTime = 0f;
                    if (stillTime >= 2f || !hasRestUp)
                    {
                        if (s.landed && s.srfSpeed < 0.5)
                        {
                            restUpLocal = tr.InverseTransformDirection(up);
                            hasRestUp = true;
                        }
                    }
                    if (hasRestUp) s.roverTilt = Vector3.Angle(tr.TransformDirection(restUpLocal), up);
                }
            }
            catch (Exception e) { s.rover = false; Log.Exception("FlightSampler.rover", e); }
        }

        /// <summary>EVA distance to the nearest other crewed vessel on the same body (1 Hz). +infinity when none.</summary>
        private void SampleEva(Vessel v)
        {
            crewDist = double.PositiveInfinity;
            if (!v.isEVA) return;
            try
            {
                List<Vessel> all = FlightGlobals.Vessels;
                if (all == null) return;
                Vector3d me = v.GetWorldPos3D();
                for (int i = 0; i < all.Count; i++)
                {
                    Vessel o = all[i];
                    if (o == null || ReferenceEquals(o, v) || o.mainBody != v.mainBody) continue;
                    if (o.GetCrewCount() <= 0) continue;
                    double d = Vector3d.Distance(me, o.GetWorldPos3D());
                    if (d < crewDist) crewDist = d;
                }
            }
            catch (Exception e) { crewDist = double.PositiveInfinity; Log.Exception("FlightSampler.evaDist", e); }
        }

        private void SampleHeat(Vessel v)
        {
            try
            {
                double best = 0;
                List<Part> parts = v.parts;
                if (parts != null)
                {
                    for (int i = 0; i < parts.Count; i++)
                    {
                        Part p = parts[i];
                        if (p == null) continue;
                        if (p.skinMaxTemp > 0)
                        {
                            double r = p.skinTemperature / p.skinMaxTemp;
                            if (r > best) best = r;
                        }
                        if (p.maxTemp > 0)
                        {
                            double r = p.temperature / p.maxTemp;
                            if (r > best) best = r;
                        }
                    }
                }
                heatRatio = DcMath.Finite(best);
            }
            catch (Exception e) { Log.Exception("FlightSampler.heat", e); }

            try { extTemp = DcMath.Finite(v.externalTemperature); }
            catch (Exception e) { Log.Exception("FlightSampler.extTemp", e); }

            shockValid = false;
            try
            {
                if (fi == null) fi = v.GetComponent<FlightIntegrator>(); // Unity null also catches a destroyed FI
                // CalculateShockTemperature throws a NullReferenceException while the integrator is not set up yet
                // (vessel load or switch). Wait until it runs and has done its first frame.
                bool ready = fi != null && fi.isRunning && !fi.firstFrame && fi.Vessel != null
                             && FlightGlobals.ready && !v.packed && v.mainBody != null;
                if (ready)
                {
                    shockTemp = DcMath.Finite(fi.CalculateShockTemperature());
                    shockValid = true;
                }
            }
            catch (NullReferenceException)
            {
                // still initialising: fall back to externalTemperature without logging an error
                shockNre++;
                if (shockNre == 1) Log.Debug("shockTemp not available yet (FlightIntegrator initialising), using externalTemperature");
            }
            catch (Exception e) { Log.Exception("FlightSampler.shockTemp", e); }
            if (!shockValid) shockTemp = extTemp;

            try { inPlasma = v.Connection != null && v.Connection.InPlasma; }
            catch (Exception e) { inPlasma = false; Log.Exception("FlightSampler.plasma", e); }
        }

        private void SampleFuel(Vessel v)
        {
            EnsureResourceIds();
            try
            {
                for (int i = 0; i < FuelMath.Count; i++)
                {
                    fuelAmount[i] = 0; fuelMax[i] = 0; fuelUsed[i] = false;
                    if (resIds[i] < 0) continue;
                    double amount, max;
                    v.GetConnectedResourceTotals(resIds[i], out amount, out max, true);
                    fuelAmount[i] = DcMath.Finite(amount);
                    fuelMax[i] = DcMath.Finite(max);
                }
                bool engineInfo = ScanEngines(v);
                fuelFrac = FuelMath.Fraction(fuelAmount, fuelMax, fuelUsed, engineInfo);
                SampleCharge(v);
                if (v.isEVA)
                {
                    // Since KSP 1.11 the jetpack burns "EVA Propellant", exposed as KerbalEVA.Fuel / FuelCapacity.
                    // No jetpack gives FuelCapacity 0, which EvaFraction maps to 1 (no propellant stress).
                    evaModule = v.FindPartModuleImplementing<KerbalEVA>();
                    evaPropFrac = evaModule != null
                        ? FuelMath.EvaFraction(DcMath.Finite(evaModule.Fuel), DcMath.Finite(evaModule.FuelCapacity))
                        : 1.0;
                    fuelFrac = evaPropFrac;
                    evaEcFrac = ecFrac;
                }
            }
            catch (Exception e) { Log.Exception("FlightSampler.fuel", e); fuelFrac = 1.0; }
        }

        /// <summary>Connected ElectricCharge of the vessel (1 Hz). Fraction 1 when there is no capacity.</summary>
        private void SampleCharge(Vessel v)
        {
            hasSolar = false; hasShield = false; chuteStowed = false; chuteOpen = false;
            try
            {
                List<Part> parts = v.parts;
                for (int i = 0; parts != null && i < parts.Count; i++)
                {
                    Part p = parts[i];
                    if (p == null) continue;
                    if (!hasSolar && p.FindModuleImplementing<ModuleDeployableSolarPanel>() != null) hasSolar = true;
                    if (!hasShield && p.FindModuleImplementing<ModuleAblator>() != null) hasShield = true;
                    ModuleParachute chute = p.FindModuleImplementing<ModuleParachute>();
                    if (chute == null) continue;
                    var st = chute.deploymentState;
                    if (st == ModuleParachute.deploymentStates.STOWED || st == ModuleParachute.deploymentStates.ACTIVE) chuteStowed = true;
                    else if (st == ModuleParachute.deploymentStates.SEMIDEPLOYED || st == ModuleParachute.deploymentStates.DEPLOYED) chuteOpen = true;
                }
            }
            catch (Exception e) { hasSolar = false; hasShield = false; chuteStowed = false; chuteOpen = false; Log.Exception("FlightSampler.parts", e); }
            ecFrac = 1.0;
            ecMax = 0;
            if (ecResId < 0) return;
            try
            {
                double amount, max;
                v.GetConnectedResourceTotals(ecResId, out amount, out max, true);
                ecMax = DcMath.Finite(max);
                if (ecMax < 0) ecMax = 0;
                ecFrac = FuelMath.EvaFraction(DcMath.Finite(amount), ecMax);
            }
            catch (Exception e) { ecFrac = 1.0; ecMax = 0; Log.Exception("FlightSampler.charge", e); }
        }

        /// <summary>Marks the propellants burned by ignited engines. Returns false when no ignited engine is found.
        /// Also sets engineOut when an engine flamed out while it still had propellant (launch cap).
        /// A burnt-out SRB or a dry stage also reports flameout, but that is normal staging, not a failure.</summary>
        private bool ScanEngines(Vessel v)
        {
            bool found = false;
            engineOut = false;
            float now = Time.unscaledTime;
            flameoutDrop.Clear();
            foreach (ModuleEngines k in flameoutSince.Keys) flameoutDrop.Add(k);
            try
            {
                List<Part> parts = v.parts;
                if (parts == null) return false;
                for (int i = 0; i < parts.Count; i++)
                {
                    Part p = parts[i];
                    if (p == null || p.Modules == null) continue;
                    for (int m = 0; m < p.Modules.Count; m++)
                    {
                        ModuleEngines e = p.Modules[m] as ModuleEngines;
                        if (e != null && e.flameout && HasPropellantLeft(p, e))
                        {
                            float since;
                            if (!flameoutSince.TryGetValue(e, out since)) { since = now; flameoutSince[e] = now; Log.Info("Engine out with propellant left: " + p.partInfo.title); }
                            flameoutDrop.Remove(e);
                            if (now - since < EngineOutHold) engineOut = true;
                        }
                        if (e == null || !e.EngineIgnited || e.propellants == null) continue;
                        found = true;
                        for (int k = 0; k < e.propellants.Count; k++)
                        {
                            Propellant pr = e.propellants[k];
                            if (pr == null) continue;
                            for (int r = 0; r < FuelMath.Count; r++)
                                if (pr.name == FuelMath.Names[r]) fuelUsed[r] = true;
                        }
                    }
                }
            }
            catch (Exception ex) { Log.Exception("FlightSampler.engines", ex); return false; }
            // engines that are no longer in flameout (relit, staged away): a new flameout counts again
            for (int i = 0; i < flameoutDrop.Count; i++) flameoutSince.Remove(flameoutDrop[i]);
            return found;
        }

        // True when every propellant of the engine still has some resource available.
        // An engine that ran dry has at least one propellant at zero. Propellant.totalResourceAvailable is only
        // refreshed while the engine draws, so after a flameout it keeps the last value from before the tank ran dry
        // ("engine out" lines repeated after running out of fuel). The amount is read fresh here.
        private static bool HasPropellantLeft(Part p, ModuleEngines e)
        {
            if (e.propellants == null || e.propellants.Count == 0) return false;
            for (int k = 0; k < e.propellants.Count; k++)
            {
                Propellant pr = e.propellants[k];
                if (pr == null) continue;
                if (pr.ignoreForIsp) continue;
                double amount, max;
                p.GetConnectedResourceTotals(pr.id, pr.GetFlowMode(), out amount, out max, true);
                if (!(DcMath.Finite(amount) > pr.minResToLeave + 0.01)) return false;
            }
            return true;
        }
    }
#endif
}
