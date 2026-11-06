// Stress 0..1 from flight state. Pure C# (no Unity), so the offline tests can drive it.
using System;

namespace Lonapse
{
    public class StressModel
    {
        public const int Spin = 0, GForce = 1, Q = 2, Ground = 3, Heat = 4, Plasma = 5, Fuel = 6, Impulse = 7, Rover = 8, Eva = 9, Count = 10;
        public static readonly string[] Names = { "spin", "gforce", "q", "ground", "heat", "plasma", "fuel", "impulse", "rover", "eva" };

        // loss of control: spin component at or above this for LossOfControlTime seconds is a real failure
        public const float LossOfControlSpin = 0.9f, LossOfControlTime = 2f;

        public readonly float[] comp = new float[Count];     // raw component values 0..1
        public readonly float[] weight = new float[Count];   // weights from tuning
        private readonly float[] fastComp = new float[Count]; // impulse and ground only, for the fast attack

        public float raw;          // combined, unsmoothed
        public float stress;       // smoothed
        public float gF;           // low-passed g
        public float gStrain;      // smoothed
        public float plasma;       // smoothed plasma index p
        public float plasmaRaw;
        public double qv;          // q (kPa) * srfSpeed
        public float impulse;
        public float warmupLeft;
        private float evaSettleLeft;  // EVA: seconds after a ragdoll in which spin and G still count as nothing
        public float motionQuietLeft; // docking: spin, G and impulse ignored, new impulses dropped
        public float belowTimer;   // how long raw has been below stress
        public float lossOfControlTime; // seconds the spin component has been at or above LossOfControlSpin

        // CommNet blackout thresholds (set by the addon from PhysicsGlobals, these are the Physics.cfg defaults)
        public double qvMin = 500, qvMax = 2500, tempForBlackout = 1100, densityForBlackout = 5e-5;

        public Tuning tu;

        public StressModel(Tuning tuning)
        {
            tu = tuning ?? new Tuning();
            ResetAll();
        }

        public void ResetAll()
        {
            for (int i = 0; i < Count; i++) comp[i] = 0f;
            raw = stress = gF = gStrain = plasma = plasmaRaw = impulse = belowTimer = motionQuietLeft = 0f;
            lossOfControlTime = 0f;
            evaSettleLeft = 0f;
            qv = 0;
            warmupLeft = tu.warmup;
        }

        /// <summary>After vessel change, go off rails or scene start: G readings are bogus for a while.</summary>
        public void StartWarmup()
        {
            warmupLeft = tu.warmup;
            gF = 0f;
        }

        /// <summary>Docking contact: ignore motion components for the given real seconds (extends, never shortens).</summary>
        public void QuietMotion(float seconds)
        {
            if (seconds > motionQuietLeft) motionQuietLeft = seconds;
        }

        /// <summary>Parts broke during the couple: stop ignoring motion right away.</summary>
        public void EndQuietMotion()
        {
            motionQuietLeft = 0f;
        }

        /// <param name="critical">true for part deaths: never dropped by the docking quiet window</param>
        public void AddImpulse(float x, bool critical = false)
        {
            if (x <= 0f || float.IsNaN(x)) return;
            if (motionQuietLeft > 0f && !critical) return; // gentle couple/dock jolts are not stress
            impulse = DcMath.Clamp01(impulse + x);
        }

        /// <summary>Probabilistic OR: 1 - prod(1 - w_i * c_i).</summary>
        public static float Combine(float[] c, float[] w)
        {
            double prod = 1.0;
            for (int i = 0; i < c.Length && i < w.Length; i++)
            {
                double x = DcMath.Clamp01(w[i]) * DcMath.Clamp01(c[i]);
                prod *= 1.0 - x;
            }
            return DcMath.Clamp01((float)(1.0 - prod));
        }

        public static float GroundComponent(bool landed, double vs, double h, Tuning tu)
        {
            if (landed || vs >= 0 || h >= tu.groundMaxH) return 0f;
            double tti = h / Math.Max(-vs, 0.1);
            return DcMath.Ramp(-vs, tu.groundVsLo, tu.groundVsHi) * (float)Math.Sqrt(DcMath.Ramp(tu.groundTti - tti, 0, tu.groundTtiSpan));
        }

        /// <summary>
        /// Rover stress: the largest of three ramps. Rough ground at speed, air time, and tilt.
        /// Speed and roughness must both be high for the terrain term, so a flat road at low speed gives 0.
        /// </summary>
        public static float RoverComponent(float speed, float roughness, float airTime, float tiltDeg, Tuning tu)
        {
            float terrain = DcMath.Ramp(speed, tu.roverSpeedLo, tu.roverSpeedHi) * DcMath.Ramp(roughness, tu.roverRoughLo, tu.roverRoughHi);
            float air = DcMath.Ramp(airTime, tu.roverAirTimeLo, tu.roverAirTimeHi);
            float tilt = DcMath.Ramp(tiltDeg, tu.roverTiltLo, tu.roverTiltHi);
            return DcMath.Clamp01(Math.Max(terrain, Math.Max(air, tilt)));
        }

        /// <summary>
        /// Docking impulse: 0 at or below soft, impulseMax at or above hard, linear in between.
        /// The caller adds it as a critical impulse, so the docking quiet window does not hide it.
        /// </summary>
        public static float DockImpulse(float relSpeed, float soft, float hard, float impulseMax)
        {
            if (hard < soft) { float t = soft; soft = hard; hard = t; }
            return Math.Max(0f, impulseMax) * DcMath.Ramp(relSpeed, soft, hard);
        }

        /// <summary>
        /// EVA stress: 0..1, for real EVA dangers only. Normal jetpack spin is handled by the spin weight.
        /// (a) jetpack propellant below lowProp while thrusting. (b) EC below lowEC and no crewed vessel within farDist.
        /// crewDist is in metres. Pass +infinity when no other crewed vessel exists.
        /// </summary>
        public static float EvaComponent(double propFrac, bool thrusting, double ecFrac, double crewDist,
                                         float lowProp, float lowEC, float farDist)
        {
            float prop = thrusting ? DcMath.Ramp(lowProp - propFrac, 0, lowProp) : 0f;
            float ec = (ecFrac < lowEC && crewDist > farDist) ? DcMath.Ramp(lowEC - ecFrac, 0, lowEC) : 0f;
            return DcMath.Clamp01(Math.Max(prop, ec));
        }

        /// <summary>
        /// True when the flight has a real failure: overheating, loss of control, or an engine flameout.
        /// Only a real failure lifts the launch cap.
        /// </summary>
        public static bool RealFailure(double heatRatio, float heatHi, bool engineOut, float lossOfControlTime)
        {
            return heatRatio >= heatHi || engineOut || lossOfControlTime >= LossOfControlTime;
        }

        public void Tick(ref FlightSample s, float dt)
        {
            if (dt <= 0f || float.IsNaN(dt)) return;
            if (dt > 0.5f) dt = 0.5f;

            // EVA: jetpack turns and the bumps and tumbles of walking or ragdolling are not hazards.
            // Spin and G are scaled by evaSpinWeight on EVA. They are ignored while the kerbal is ragdolled,
            // standing or lying on the ground, and for evaSettleTime after a ragdoll (getting up, rolling).
            // Falling toward the ground stays a hazard through the Ground component.
            if (s.eva && s.evaRagdoll) evaSettleLeft = tu.evaSettleTime;
            else if (evaSettleLeft > 0f) evaSettleLeft -= dt;
            bool evaStill = s.eva && (s.evaRagdoll || s.landed || evaSettleLeft > 0f);
            float evaMotion = s.eva ? (evaStill ? 0f : tu.evaSpinWeight) : 1f;
            weight[Spin] = s.eva ? tu.wSpin * evaMotion : tu.wSpin; weight[GForce] = tu.wG * evaMotion; weight[Q] = tu.wQ; weight[Ground] = tu.wGround;
            weight[Heat] = tu.wHeat; weight[Plasma] = tu.wPlasma; weight[Fuel] = tu.wFuel; weight[Impulse] = tu.wImpulse;
            weight[Rover] = tu.wRover; weight[Eva] = tu.wEva;

            if (warmupLeft > 0f) warmupLeft -= dt;
            // On rails or right after load / unpack / vessel switch the physics state jitters (bogus G spikes,
            // wobbling landed or docked vessels, parts settling). Spin, G and impulse are ignored until it settles.
            if (motionQuietLeft > 0f) motionQuietLeft -= dt;
            bool suppressG = s.packed || warmupLeft > 0f || motionQuietLeft > 0f;

            // G low-pass
            if (suppressG) gF = 0f;
            else gF += ((float)s.g - gF) * DcMath.ExpAlpha(dt, tu.gTau);
            gF = DcMath.Finite(gF);

            comp[Spin] = suppressG ? 0f : DcMath.Smooth(s.omega, tu.spinLo, tu.spinHi);
            comp[GForce] = suppressG ? 0f : DcMath.Ramp(gF, tu.gLo, tu.gHi);
            comp[Q] = DcMath.Ramp(s.qKPa, tu.qLo, tu.qHi);
            comp[Ground] = GroundComponent(s.landed, s.vs, s.h, tu);
            comp[Heat] = DcMath.Ramp(s.heatRatio, tu.heatLo, tu.heatHi);
            // On EVA the jetpack propellant is handled by the Eva component, so Fuel stays 0 there (no double count).
            comp[Fuel] = (!s.eva && s.throttle > tu.fuelThrottle && !s.landed) ? DcMath.Ramp(tu.fuelLow - s.fuelFrac, 0, tu.fuelLow) : 0f;
            comp[Rover] = (s.rover && !suppressG) ? RoverComponent((float)s.srfSpeed, s.roverRough, s.roverAirTime, s.roverTilt, tu) : 0f;
            comp[Eva] = (s.eva && !s.packed)
                ? EvaComponent(s.evaPropFrac, s.evaThrusting, s.evaEcFrac, s.crewDist, tu.evaLowProp, tu.evaLowEC, tu.evaFarDist)
                : 0f;
            lossOfControlTime = comp[Spin] >= LossOfControlSpin ? lossOfControlTime + dt : 0f;

            // plasma index, mirrors CommNet's blackout model
            qv = DcMath.Finite(s.qKPa * s.srfSpeed);
            float p = (s.shockTemp >= tempForBlackout && s.atmDensity >= densityForBlackout) ? DcMath.Ramp(qv, qvMin, qvMax) : 0f;
            if (s.inPlasmaStock) p = 1f;
            plasmaRaw = p;
            plasma += (p - plasma) * DcMath.ExpAlpha(dt, tu.plasmaTau);
            comp[Plasma] = DcMath.Clamp01(plasma);

            impulse *= (float)Math.Exp(-dt / Math.Max(tu.impulseTau, 0.01f));
            // the docking quiet window hides spin/G but not impulses that got through (part deaths)
            bool suppressImpulse = s.packed || warmupLeft > 0f;
            // on EVA nothing hits the kerbal: hops and bumps are no impulse
            comp[Impulse] = (suppressImpulse || s.eva) ? 0f : impulse;

            raw = Combine(comp, weight);
            // sudden events only: impulses and an imminent ground impact
            for (int i = 0; i < fastComp.Length; i++) fastComp[i] = (i == Impulse || i == Ground) ? comp[i] : 0f;
            float rawFast = Combine(fastComp, weight);

            // slow attack for lasting stress, fast attack for sudden events, peak hold, slow decay
            if (raw > stress)
            {
                belowTimer = 0f;
                stress += (raw - stress) * DcMath.ExpAlpha(dt, tu.attackTau);
                if (rawFast > stress) stress += (rawFast - stress) * DcMath.ExpAlpha(dt, tu.fastAttackTau);
            }
            else
            {
                belowTimer += dt;
                if (belowTimer >= tu.holdTime)
                    stress += (raw - stress) * DcMath.ExpAlpha(dt, tu.decayTau);
            }
            stress = DcMath.Clamp01(stress);

            float gsTarget = suppressG ? 0f : DcMath.Ramp(gF, tu.gStrainLo, tu.gStrainHi);
            float gsTau = gsTarget > gStrain ? tu.gStrainAttack : tu.gStrainDecay;
            gStrain = DcMath.Clamp01(gStrain + (gsTarget - gStrain) * DcMath.ExpAlpha(dt, gsTau));
        }
    }
}
