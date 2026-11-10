// Personality of one kerbal from the stock stats.
// Pure: no Unity or KSP dependencies, so the offline test harness can compile this file.
using System;

namespace Lonapse
{
    /// <summary>
    /// Persona flags and trait key of a kerbal. Flags: badass = isBadass, brave = courage &gt;= 0.65,
    /// nervous = courage &lt;= 0.35, dim = stupidity &gt;= 0.6, veteran. Several flags can be true at once.
    /// </summary>
    public sealed class Persona
    {
        public const float BraveAt = 0.65f;
        public const float NervousAt = 0.35f;
        public const float DimAt = 0.6f;
        public const float VeteranPanicBonus = 0.04f;
        public const float VeteranExponentFactor = 0.9f;
        public const float IntercomStupidityWeight = 0.4f;

        public const string Any = "any";
        public static readonly string[] TraitKeys = { "pilot", "engineer", "scientist", "tourist" };

        /// <summary>The share of a job event's crew lines (trait any) that goes to a kerbal with that job when one is
        /// aboard. The rest stays a random pick, so the others still speak up now and then.</summary>
        public const double JobShare = 0.8;

        /// <summary>The job that would speak up at an event: the pilot flies (burns, staging, docking, landing), the
        /// engineer watches power and parts, the scientist the science. Null for an event anyone talks about.</summary>
        public static string JobFor(string ev)
        {
            switch (ev)
            {
                case "burn_soon": case "plan_node": case "staging": case "throttle_cut": case "throttle_full":
                case "dock_approach": case "dock_close": case "dock_hard": case "docked":
                case "touchdown": case "splashdown": case "chute_full": case "chute_semi": case "soi_enter":
                    return "pilot";
                case "power_low": case "power_ok": case "part_lost":
                    return "engineer";
                case "science":
                    return "scientist";
                default:
                    return null;
            }
        }

        /// <summary>The job that answers a runtime MC line, by its id: fuel, nodes, docking and the orbit go to the
        /// pilot, power and parts to the engineer, science to the scientist. Null for the rest.</summary>
        public static string JobForLine(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            id = RuntimeLines.CanonicalId(id);
            string[] pilot = { "fuel", "node", "mono", "dock", "land", "flight", "sub", "orbit", "escape" };
            string[] engineer = { "ec", "parts" };
            foreach (string p in pilot) if (id.StartsWith(p, StringComparison.Ordinal)) return "pilot";
            foreach (string p in engineer) if (id.StartsWith(p, StringComparison.Ordinal)) return "engineer";
            if (id.StartsWith("sci", StringComparison.Ordinal)) return "scientist";
            return null;
        }
        public static readonly string[] PersonaKeys = { "badass", "brave", "nervous", "dim", "veteran" };

        public readonly float courage, stupidity;
        public readonly bool badass, brave, nervous, dim, veteran;
        /// <summary>Lowercase trait key: pilot, engineer, scientist, tourist, or any for an unknown trait.</summary>
        public readonly string trait;

        /// <summary>Neutral persona: no flags, trait any. Used before the roster has read the stats.</summary>
        public static readonly Persona None = new Persona(0.5f, 0.5f, false, false, null);

        public Persona(float courage, float stupidity, bool isBadass, bool veteran, string kspTrait)
        {
            this.courage = DcMath.Clamp01(courage);
            this.stupidity = DcMath.Clamp01(stupidity);
            badass = isBadass;
            brave = this.courage >= BraveAt;
            nervous = this.courage <= NervousAt;
            dim = this.stupidity >= DimAt;
            this.veteran = veteran;
            trait = TraitKey(kspTrait);
        }

        /// <summary>KSP trait string ("Pilot", "Engineer", "Scientist", "Tourist") to a lowercase key. Unknown gives any.</summary>
        public static string TraitKey(string kspTrait)
        {
            if (string.IsNullOrEmpty(kspTrait)) return Any;
            string t = kspTrait.Trim();
            for (int i = 0; i < TraitKeys.Length; i++)
                if (string.Equals(t, TraitKeys[i], StringComparison.OrdinalIgnoreCase)) return TraitKeys[i];
            return Any;
        }

        private static bool IsAny(string key)
        {
            return key == null || key.Trim().Length == 0 || string.Equals(key.Trim(), Any, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>True when this kerbal has the persona flag of a table row. Empty or any always matches, an unknown word never.</summary>
        public bool Matches(string rowPersona)
        {
            if (IsAny(rowPersona)) return true;
            return Has(rowPersona.Trim());
        }

        /// <summary>One flag by name, case-insensitive. False for an unknown name.</summary>
        public bool Has(string flag)
        {
            if (flag == null) return false;
            if (string.Equals(flag, "badass", StringComparison.OrdinalIgnoreCase)) return badass;
            if (string.Equals(flag, "brave", StringComparison.OrdinalIgnoreCase)) return brave;
            if (string.Equals(flag, "nervous", StringComparison.OrdinalIgnoreCase)) return nervous;
            if (string.Equals(flag, "dim", StringComparison.OrdinalIgnoreCase)) return dim;
            if (string.Equals(flag, "veteran", StringComparison.OrdinalIgnoreCase)) return veteran;
            return false;
        }

        /// <summary>True when this kerbal has the trait of a table row (for an intercom scene: speaker A). Empty or
        /// any always matches. tourist_pair wants a tourist, to_tourist anyone who is not one.</summary>
        public bool TraitMatches(string rowTrait)
        {
            if (IsAny(rowTrait)) return true;
            string t = rowTrait.Trim();
            if (string.Equals(t, TouristPair, StringComparison.OrdinalIgnoreCase)) return IsTourist;
            if (string.Equals(t, ToTourist, StringComparison.OrdinalIgnoreCase)) return !IsTourist;
            return trait != Any && string.Equals(t, trait, StringComparison.OrdinalIgnoreCase);
        }

        public bool IsTourist { get { return trait == Tourist; } }

        // ---- tourists ----
        // A tourist has never flown a ship. So a tourist never makes a crew callout or reads a job report, never
        // says a calm crew line meant for the professionals and only talks in dialogues written for tourists.
        // Scene traits for that: tourist (a tourist talks, in an intercom chat to a crew member who is not one),
        // to_tourist (a crew member talks to a tourist) and tourist_pair (two tourists). The last two are only
        // for intercom scenes.
        public const string Tourist = "tourist";
        public const string ToTourist = "to_tourist";
        public const string TouristPair = "tourist_pair";

        /// <summary>The scene trait only fits an intercom pair (to_tourist, tourist_pair).</summary>
        public static bool IsPairTrait(string sceneTrait)
        {
            return sceneTrait == ToTourist || sceneTrait == TouristPair;
        }

        /// <summary>An intercom scene fits the pair: with a tourist in it only the scenes written for that pair,
        /// without one never a tourist scene.</summary>
        public static bool PairFits(string sceneTrait, bool aTourist, bool bTourist)
        {
            if (aTourist && bTourist) return sceneTrait == TouristPair;
            if (aTourist) return sceneTrait == Tourist;
            if (bTourist) return sceneTrait == ToTourist;
            return sceneTrait != Tourist && !IsPairTrait(sceneTrait);
        }

        /// <summary>A radio scene (or a single line, a callout) fits this speaker. Never a pair trait. A tourist only
        /// says the scenes written for tourists, in every band: the crew's stress lines sound trained ("Small
        /// rotation. Correcting."). A scream is the one exception, fear needs no training.</summary>
        public static bool RadioFits(string sceneTrait, string ev, string band, bool tourist, bool scream = false)
        {
            if (IsPairTrait(sceneTrait)) return false;
            if (!tourist || sceneTrait == Tourist) return true;
            return scream && JobFor(ev) == null;
        }

        /// <summary>A runtime chat side fits: a tourist only takes a side that asks for a tourist (a=tourist).</summary>
        public static bool ChatSideFits(string want, string have)
        {
            bool isTourist = have != null && Array.IndexOf(have.Split(' '), Tourist) >= 0;
            if (!isTourist) return true;
            return want != null && Array.IndexOf(want.Split(' '), Tourist) >= 0;
        }

        /// <summary>True when at least one flag is set.</summary>
        public bool AnyFlag { get { return badass || brave || nervous || dim || veteran; } }

        /// <summary>Veteran stress tweak on the panic threshold: +0.04 for a veteran.</summary>
        public static float VeteranPanicAt(float panicAt, bool veteran)
        {
            return veteran ? panicAt + VeteranPanicBonus : panicAt;
        }

        /// <summary>Veteran stress tweak on the stress exponent: x0.9 for a veteran (damps stress).</summary>
        public static float VeteranExponent(float exponent, bool veteran)
        {
            return veteran ? exponent * VeteranExponentFactor : exponent;
        }

        /// <summary>Intercom speaker A weight: 1 + 0.4 x stupidity (dim kerbals start more chats).</summary>
        public float IntercomWeight { get { return 1f + IntercomStupidityWeight * stupidity; } }

        /// <summary>Flags as text for logs, for example "brave veteran", or "none".</summary>
        public string FlagText()
        {
            string s = "";
            for (int i = 0; i < PersonaKeys.Length; i++)
                if (Has(PersonaKeys[i])) s += (s.Length > 0 ? " " : "") + PersonaKeys[i];
            return s.Length > 0 ? s : "none";
        }

        public override string ToString() { return trait + " / " + FlagText(); }

        /// <summary>For the a= and b= checks of a runtime chat: the trait key and the flags, like "pilot brave dim".</summary>
        public string ChatKeys
        {
            get
            {
                string s = trait;
                for (int i = 0; i < PersonaKeys.Length; i++)
                    if (Has(PersonaKeys[i])) s += " " + PersonaKeys[i];
                return s;
            }
        }
    }
}
