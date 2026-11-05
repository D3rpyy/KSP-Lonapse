// Experimental (procedural talk): subtitle slots. A template is a normal phrase row whose English holds slots like
// {thing} or {day}. The audio is the Spanish line as always: a general sentence ("¿Quién ha cogido mis cosas?"), and
// the subtitle fills in the details ("Who took my helmet?"). Slots only live in the English, never in the Spanish.
//
// Slot kinds:
//   {pool}      a random value from pools.tsv. The same slot gives the same value in every line of one scene.
//   {pool:2}    another value from the same pool, different from {pool} in that scene.
//   {day} ...   a flight value the director gives (FlightSlots).
//   {other}     intercom only: the first name of the other kerbal in the chat. {self}: the speaker's own name.
// Pure code: no Unity or KSP references, so it compiles under DC_OFFLINE_TEST.
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Lonapse
{
    /// <summary>Values per pool from pools.tsv (columns pool and english).</summary>
    public sealed class SlotPools
    {
        private readonly Dictionary<string, List<string>> pools = new Dictionary<string, List<string>>();

        public int Count { get { return pools.Count; } }
        public bool Has(string pool) { return pool != null && pools.ContainsKey(pool); }

        public IList<string> Values(string pool)
        {
            List<string> v;
            return pool != null && pools.TryGetValue(pool, out v) ? v : null;
        }

        /// <summary>Reads pools.tsv: a header with pool and english, then one value per row. # starts a comment.
        /// Bad rows are skipped and described in errors. Same rules as load_pools in audio/build_lonapse.py.</summary>
        public static SlotPools Parse(IList<string> lines, List<string> errors)
        {
            SlotPools p = new SlotPools();
            if (lines == null) return p;
            int iPool = -1, iEn = -1, width = 0;
            for (int i = 0; i < lines.Count; i++)
            {
                string line = (lines[i] ?? "").TrimEnd('\r');
                if (line.Trim().Length == 0 || line.TrimStart().StartsWith("#")) continue;
                string[] f = line.Split('\t');
                if (iPool < 0)
                {
                    iPool = Array.IndexOf(f, "pool");
                    iEn = Array.IndexOf(f, "english");
                    width = f.Length;
                    if (iPool < 0 || iEn < 0) { if (errors != null) errors.Add("pools: header needs pool and english"); return p; }
                    continue;
                }
                if (f.Length != width) { if (errors != null) errors.Add("pools line " + (i + 1) + ": expected " + width + " columns"); continue; }
                string name = f[iPool].Trim(), val = f[iEn].Trim();
                if (!SubtitleSlots.IsSlotName(name)) { if (errors != null) errors.Add("pools line " + (i + 1) + ": pool '" + name + "' must match [a-z_]+"); continue; }
                if (val.Length == 0 || val.IndexOf('{') >= 0 || val.IndexOf('}') >= 0)
                {
                    if (errors != null) errors.Add("pools line " + (i + 1) + ": value must be non-empty text without { }");
                    continue;
                }
                if (SubtitleSlots.IsReserved(name)) { if (errors != null) errors.Add("pools line " + (i + 1) + ": '" + name + "' is a reserved slot name"); continue; }
                List<string> l;
                if (!p.pools.TryGetValue(name, out l)) p.pools[name] = l = new List<string>();
                if (!l.Contains(val)) l.Add(val);
            }
            return p;
        }
    }

    public static class SubtitleSlots
    {
        /// <summary>Flight values the director fills in. Same list as FLIGHT_SLOTS in audio/build_lonapse.py.</summary>
        public static readonly string[] FlightSlots = { "day", "here", "vessel", "alt_km" };
        /// <summary>Intercom only: the other kerbal and the speaker. Same as PERSON_SLOTS in the script.</summary>
        public static readonly string[] PersonSlots = { "other", "self" };
        public const string Other = "other", Self = "self";

        // {name} or {name:2}
        static readonly Regex SlotRe = new Regex(@"\{([a-z_]+)(?::([1-9]))?\}", RegexOptions.CultureInvariant);

        public static bool IsSlotName(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            foreach (char c in s) if (!((c >= 'a' && c <= 'z') || c == '_')) return false;
            return true;
        }

        /// <summary>body and the flight and person slots cannot be pool names.</summary>
        public static bool IsReserved(string name)
        {
            return name == "body" || Array.IndexOf(FlightSlots, name) >= 0 || Array.IndexOf(PersonSlots, name) >= 0;
        }

        public static bool HasSlots(string text) { return !string.IsNullOrEmpty(text) && SlotRe.IsMatch(text); }

        /// <summary>The slot names in a text, without the :n part, each once. {body} is left out.</summary>
        public static List<string> Names(string text)
        {
            var r = new List<string>();
            if (string.IsNullOrEmpty(text)) return r;
            foreach (Match m in SlotRe.Matches(text))
            {
                string n = m.Groups[1].Value;
                if (n != "body" && !r.Contains(n)) r.Add(n);
            }
            return r;
        }

        /// <summary>
        /// Fills the slots of one scene. Lines without slots are returned as they are, a line with slots as a copy
        /// with the filled English (the clip key stays, so the same clip plays and the memory works as before).
        /// <paramref name="values"/> holds the flight values. <paramref name="lineValues"/> (may be null) gives extra
        /// values per line index, for {other} and {self}. Pool values come from <paramref name="pick"/> (pool, values),
        /// so the caller can use the line memory. A slot that cannot be filled becomes "..." and goes to unresolved.
        /// </summary>
        public static List<PhraseEntry> Fill(IList<PhraseEntry> lines, SlotPools pools, IDictionary<string, string> values,
                                             Func<int, IDictionary<string, string>> lineValues,
                                             Func<string, IList<string>, string> pick, List<string> unresolved)
        {
            var r = new List<PhraseEntry>();
            if (lines == null) return r;
            // one value per slot (and per :n) for the whole scene
            var chosen = new Dictionary<string, string>();
            for (int i = 0; i < lines.Count; i++)
            {
                PhraseEntry e = lines[i];
                if (e == null || !HasSlots(e.English)) { r.Add(e); continue; }
                IDictionary<string, string> per = lineValues != null ? lineValues(i) : null;
                string src = e.English;
                var sb = new System.Text.StringBuilder(src.Length + 16);
                int at = 0;
                foreach (Match m in SlotRe.Matches(src))
                {
                    sb.Append(src, at, m.Index - at);
                    string v = Resolve(m, e, i, pools, values, per, pick, chosen, unresolved);
                    sb.Append(AtSentenceStart(src, m.Index) ? CapFirst(v) : v);
                    at = m.Index + m.Length;
                }
                sb.Append(src, at, src.Length - at);
                r.Add(CopyWithEnglish(e, sb.ToString()));
            }
            return r;
        }

        static string Resolve(Match m, PhraseEntry e, int line, SlotPools pools, IDictionary<string, string> values,
                              IDictionary<string, string> per, Func<string, IList<string>, string> pick,
                              Dictionary<string, string> chosen, List<string> unresolved)
        {
            string name = m.Groups[1].Value, n = m.Groups[2].Success ? m.Groups[2].Value : "1";
            if (name == "body") return m.Value;   // {body} rows are expanded at build time, never here
            string v;
            if (per != null && per.TryGetValue(name, out v) && !string.IsNullOrEmpty(v)) return v;
            if (values != null && values.TryGetValue(name, out v) && !string.IsNullOrEmpty(v)) return v;
            string key = name + ":" + n;
            if (chosen.TryGetValue(key, out v)) return v;
            IList<string> pool = pools != null ? pools.Values(name) : null;
            if (pool == null || pool.Count == 0)
            {
                if (unresolved != null) unresolved.Add(name + " in " + e.ClipKey + " (line " + line + ")");
                return "...";
            }
            // {pool:2} differs from the values already taken from that pool in this scene
            var free = new List<string>();
            foreach (string c in pool) if (!chosen.ContainsValue(c)) free.Add(c);
            IList<string> from = free.Count > 0 ? (IList<string>)free : pool;
            v = pick != null ? pick(name, from) : from[0];
            if (string.IsNullOrEmpty(v)) v = from[0];
            chosen[key] = v;
            return v;
        }

        /// <summary>True when position i starts the text or a sentence: only spaces and quotes after the start or
        /// after . ! ? before it. Then a slot value there gets a capital letter ("{thing} is gone." gives "Helmet is
        /// gone."). The written text itself is never changed.</summary>
        public static bool AtSentenceStart(string s, int i)
        {
            for (int k = i - 1; k >= 0; k--)
            {
                char c = s[k];
                if (char.IsWhiteSpace(c) || c == '"' || c == '\'') continue;
                return c == '.' || c == '!' || c == '?';
            }
            return true;
        }

        public static string CapFirst(string v)
        {
            return string.IsNullOrEmpty(v) ? v : char.ToUpperInvariant(v[0]) + v.Substring(1);
        }

        /// <summary>The flight values for Fill. day: the mission day, starting at 1 (Kerbin days of 6 h, or Earth days
        /// of 24 h with that game setting). alt_km: whole km, one decimal below 10 km. Empty names are left out, so
        /// the slot gives "..." instead of an empty word.</summary>
        public static Dictionary<string, string> FlightValues(double missionTime, bool kerbinDays, string here,
                                                              string vessel, double altM)
        {
            var d = new Dictionary<string, string>();
            double dayLen = kerbinDays ? 6 * 3600.0 : 24 * 3600.0;
            double met = double.IsNaN(missionTime) || missionTime < 0 ? 0 : missionTime;
            d["day"] = ((long)Math.Floor(met / dayLen) + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (!string.IsNullOrEmpty(here)) d["here"] = here;
            if (!string.IsNullOrEmpty(vessel)) d["vessel"] = vessel;
            if (!double.IsNaN(altM) && !double.IsInfinity(altM))
            {
                double km = Math.Max(0, altM) / 1000.0;
                d["alt_km"] = km.ToString(km < 10 ? "0.#" : "0", System.Globalization.CultureInfo.InvariantCulture);
            }
            return d;
        }

        /// <summary>"Jebediah Kerman" gives "Jebediah". A name without a space stays as it is.</summary>
        public static string FirstName(string full)
        {
            if (string.IsNullOrEmpty(full)) return full;
            string t = full.Trim();
            int sp = t.IndexOf(' ');
            return sp > 0 ? t.Substring(0, sp) : t;
        }

        static PhraseEntry CopyWithEnglish(PhraseEntry e, string english)
        {
            return new PhraseEntry
            {
                ClipKey = e.ClipKey, Event = e.Event, Severity = e.Severity, Band = e.Band, Role = e.Role, Scene = e.Scene,
                Seq = e.Seq, Gender = e.Gender, Phase = e.Phase, Cause = e.Cause, Trait = e.Trait, Persona = e.Persona,
                Topic = e.Topic, Core = e.Core, Beep = e.Beep, Body = e.Body, Alt = e.Alt, Spanish = e.Spanish,
                LonapseText = e.LonapseText, English = english, Folder = e.Folder
            };
        }
    }
}
