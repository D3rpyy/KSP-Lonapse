// Flight phase classification. Pure C#, no Unity or KSP, so the offline tests can compile it.
using System;

namespace Lonapse
{
    /// <summary>
    /// Phase names as used in the `phase` column of the phrase TSV files, plus the matching rules.
    /// serious = launch, ascent, reentry, descent. goofy = prelaunch, orbit, coast, landed.
    /// blackout is neither: it is set by BlackoutMachine, never by the classifier.
    /// </summary>
    public static class FlightPhase
    {
        public const string Any = "any";
        public const string Prelaunch = "prelaunch";
        public const string Launch = "launch";
        public const string Ascent = "ascent";
        public const string Orbit = "orbit";
        public const string Coast = "coast";
        public const string Reentry = "reentry";
        public const string Descent = "descent";
        public const string Landed = "landed";
        public const string Blackout = "blackout";
        public const string Serious = "serious";
        public const string Goofy = "goofy";

        /// <summary>The concrete phases a vessel can be in (no any/serious/goofy groups).</summary>
        public static readonly string[] All = { Prelaunch, Launch, Ascent, Orbit, Coast, Reentry, Descent, Landed, Blackout };

        public const string GenericBody = "generic";

        /// <summary>Stock body keys (lowercase). KSP calls Kerbol "Sun", BodyKey maps that.</summary>
        public static readonly string[] StockBodies =
        {
            "kerbol", "moho", "eve", "gilly", "kerbin", "mun", "minmus", "duna", "ike",
            "dres", "jool", "laythe", "vall", "tylo", "bop", "pol", "eeloo"
        };

        public static bool IsSerious(string phase)
        {
            return phase == Launch || phase == Ascent || phase == Reentry || phase == Descent;
        }

        public static bool IsGoofy(string phase)
        {
            return phase == Prelaunch || phase == Orbit || phase == Coast || phase == Landed;
        }

        /// <summary>
        /// True when a phrase row tagged rowPhase may play in the current phase.
        /// Empty or null rowPhase counts as any. "any" matches everything, blackout included.
        /// "serious" and "goofy" match their groups and never blackout. A concrete phase matches only itself.
        /// A null or empty current phase only matches "any".
        /// </summary>
        public static bool Matches(string rowPhase, string current)
        {
            if (string.IsNullOrEmpty(rowPhase) || rowPhase == Any) return true;
            if (string.IsNullOrEmpty(current)) return false;
            if (rowPhase == Serious) return IsSerious(current);
            if (rowPhase == Goofy) return IsGoofy(current);
            return rowPhase == current;
        }

        /// <summary>
        /// Body key for {body} phrase variants: lowercase stock body name, "Sun" (and "Kerbol") -> "kerbol",
        /// anything else (planet packs, null) -> "generic".
        /// </summary>
        public static string BodyKey(string bodyName)
        {
            if (string.IsNullOrEmpty(bodyName)) return GenericBody;
            string k = bodyName.Trim().ToLowerInvariant();
            if (k == "sun") k = "kerbol";
            for (int i = 0; i < StockBodies.Length; i++)
                if (StockBodies[i] == k) return k;
            return GenericBody;
        }
    }

    /// <summary>Mirror of Vessel.Situations, same flag values, so (PhaseSituation)(int)v.situation works.</summary>
    public enum PhaseSituation
    {
        None = 0,
        LANDED = 1,
        SPLASHED = 2,
        PRELAUNCH = 4,
        FLYING = 8,
        SUB_ORBITAL = 16,
        ORBITING = 32,
        ESCAPING = 64,
        DOCKED = 128
    }

    /// <summary>Inputs for one classifier step. Units: m, m/s, kg/m3, throttle 0..1.</summary>
    public struct PhaseInput
    {
        public PhaseSituation situation;
        public double verticalSpeed;    // m/s, negative = down
        public double altitude;         // m above sea level
        public double height;           // m above terrain (or sea), used for "near the ground"
        public double atmosphereDepth;  // m, 0 = body has no atmosphere
        public double atmDensity;       // kg/m3
        public double mach;
        public double throttle;         // main throttle 0..1
        public bool isEva;
        public double srfSpeed;         // m/s
    }

    /// <summary>
    /// Turns the vessel state into a flight phase with hysteresis.
    ///
    /// Raw rules (Candidate), first match wins. "In atmosphere" = atmosphereDepth > 0 and altitude below it.
    ///  1. PRELAUNCH: prelaunch, or launch when throttle > 0 (the crew is lighting the engines). EVA: landed.
    ///  2. LANDED or SPLASHED (EVA on the ground included): landed.
    ///  3. ORBITING or DOCKED: orbit.  ESCAPING: coast.
    ///  4. Airborne (FLYING, SUB_ORBITAL) and climbing (vs > 2 m/s) with throttle > 0, less than 90 s after
    ///     leaving the ground, and either less than 20 s after liftoff or below 1500 m above terrain: launch.
    ///     Not for a kerbal on EVA. A vessel loaded while already airborne has no liftoff time, so never launch.
    ///  5. Airborne in atmosphere:
    ///     - climbing: ascent.
    ///     - descending (vs < -2 m/s): reentry when mach >= 3, or when already in reentry and mach >= 1.5.
    ///       Otherwise descent.
    ///     - level: keep the current in-atmosphere phase (launch counts as ascent). Otherwise ascent with
    ///       throttle > 0, descent without.
    ///  6. Airborne above the atmosphere of a body that has one: ascent while thrusting and the current phase is
    ///     launch or ascent (circularisation burn). Otherwise coast.
    ///  7. Airborne over an airless body:
    ///     - descending (vs < -2 m/s) and below 5000 m above terrain, or below 20000 m with throttle > 0: descent.
    ///     - climbing with throttle > 0 and below 20000 m above terrain: ascent.
    ///     - otherwise coast.
    ///  8. Anything else: coast.
    ///
    /// Hysteresis: a new candidate must hold for HoldSeconds (5 s) before Current changes. Instant switches:
    /// the first update, prelaunch -> launch when throttle > 0, and leaving the ground (prelaunch or landed to an
    /// airborne situation) when the candidate is launch. Time going backwards (revert, quickload) restarts the hold.
    /// The classifier never returns blackout. BlackoutMachine drives that on top of this result.
    /// </summary>
    public sealed class FlightPhaseClassifier
    {
        public const double HoldSeconds = 5.0;
        public const double ClimbVs = 2.0;
        public const double DescendVs = -2.0;
        public const double LaunchMaxTime = 90.0;
        public const double LaunchShortTime = 20.0;
        public const double LaunchMaxHeight = 1500.0;
        public const double ReentryMach = 3.0;
        public const double ReentryKeepMach = 1.5;
        public const double AirlessDescentHeight = 5000.0;
        public const double AirlessPoweredHeight = 20000.0;

        /// <summary>Current phase, null before the first Update.</summary>
        public string Current;

        private string pending;
        private double pendingSince;
        private bool wasOnGround;
        private bool haveGround;
        private double liftoffTime = double.NaN;

        public void Reset()
        {
            Current = null;
            pending = null;
            pendingSince = 0;
            wasOnGround = false;
            haveGround = false;
            liftoffTime = double.NaN;
        }

        public static bool OnGround(PhaseSituation s)
        {
            return s == PhaseSituation.PRELAUNCH || s == PhaseSituation.LANDED || s == PhaseSituation.SPLASHED;
        }

        public static bool Airborne(PhaseSituation s)
        {
            return s == PhaseSituation.FLYING || s == PhaseSituation.SUB_ORBITAL;
        }

        public string Update(PhaseInput i, double now)
        {
            bool onGround = OnGround(i.situation);
            bool leftGround = haveGround && wasOnGround && !onGround;
            if (onGround) liftoffTime = double.NaN;
            else if (leftGround) liftoffTime = now;
            wasOnGround = onGround;
            haveGround = true;

            string cand = Candidate(i, now);
            // Leaving the pad is always a launch, even in the first frames when the climb rate is still small.
            if (Current == FlightPhase.Prelaunch && !onGround && !i.isEva) cand = FlightPhase.Launch;

            if (Current == null)
            {
                Current = cand;
                pending = null;
                return Current;
            }
            if (cand == Current)
            {
                pending = null;
                return Current;
            }
            bool instant = (Current == FlightPhase.Prelaunch && cand == FlightPhase.Launch)
                           || (leftGround && cand == FlightPhase.Launch);
            if (instant)
            {
                Current = cand;
                pending = null;
                return Current;
            }
            if (cand != pending || now < pendingSince)
            {
                pending = cand;
                pendingSince = now;
            }
            if (now - pendingSince >= HoldSeconds)
            {
                Current = cand;
                pending = null;
            }
            return Current;
        }

        /// <summary>The raw phase for this input, without hysteresis. See the class comment for the rules.</summary>
        public string Candidate(PhaseInput i, double now)
        {
            double vs = Finite(i.verticalSpeed);
            double alt = Finite(i.altitude);
            double h = Finite(i.height);
            double depth = Finite(i.atmosphereDepth);
            double mach = Finite(i.mach);
            bool thrust = Finite(i.throttle) > 0.0;
            bool climbing = vs > ClimbVs;
            bool descending = vs < DescendVs;

            switch (i.situation)
            {
                case PhaseSituation.PRELAUNCH:
                    // Not the throttle: KSP puts a new vessel on the pad at 50 % throttle. Launch starts when the
                    // situation leaves PRELAUNCH (the leftGround instant switch below).
                    if (i.isEva) return FlightPhase.Landed;
                    return FlightPhase.Prelaunch;
                case PhaseSituation.LANDED:
                case PhaseSituation.SPLASHED:
                    return FlightPhase.Landed;
                case PhaseSituation.ORBITING:
                case PhaseSituation.DOCKED:
                    return FlightPhase.Orbit;
                case PhaseSituation.ESCAPING:
                    return FlightPhase.Coast;
                case PhaseSituation.FLYING:
                case PhaseSituation.SUB_ORBITAL:
                    break;
                default:
                    return FlightPhase.Coast;
            }

            // airborne from here
            if (!i.isEva && climbing && thrust && !double.IsNaN(liftoffTime))
            {
                double since = now - liftoffTime;
                if (since >= 0 && since < LaunchMaxTime && (since < LaunchShortTime || h < LaunchMaxHeight))
                    return FlightPhase.Launch;
            }

            bool hasAtm = depth > 0;
            bool inAtm = hasAtm && alt < depth;
            if (inAtm)
            {
                if (climbing) return FlightPhase.Ascent;
                if (descending)
                {
                    if (mach >= ReentryMach) return FlightPhase.Reentry;
                    if (Current == FlightPhase.Reentry && mach >= ReentryKeepMach) return FlightPhase.Reentry;
                    return FlightPhase.Descent;
                }
                if (Current == FlightPhase.Launch) return FlightPhase.Ascent;
                if (Current == FlightPhase.Ascent || Current == FlightPhase.Reentry || Current == FlightPhase.Descent)
                    return Current;
                return thrust ? FlightPhase.Ascent : FlightPhase.Descent;
            }
            if (hasAtm)
            {
                if (thrust && (Current == FlightPhase.Launch || Current == FlightPhase.Ascent)) return FlightPhase.Ascent;
                return FlightPhase.Coast;
            }
            // airless body
            if (descending && (h < AirlessDescentHeight || (thrust && h < AirlessPoweredHeight))) return FlightPhase.Descent;
            if (climbing && thrust && h < AirlessPoweredHeight) return FlightPhase.Ascent;
            return FlightPhase.Coast;
        }

        private static double Finite(double x)
        {
            return double.IsNaN(x) || double.IsInfinity(x) ? 0.0 : x;
        }
    }
}
