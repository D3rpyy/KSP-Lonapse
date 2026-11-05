// Experimental (procedural talk): subtitle slots. A template is a normal phrase row whose English holds slots like
// {thing} or {day}. The audio is the Spanish line as always: a general sentence ("¿Quién ha cogido mis cosas?"), and
// the subtitle fills in the details ("Who took my helmet?"). Slots only live in the English, never in the Spanish.
//
// Slot kinds:
//   {pool}      a random value from pools.tsv. The same slot gives the same value in every line of one scene.
//   {pool:2}    another value from the same pool, different from {pool} in that scene.
//   {day} ...   a flight value the director gives (FlightSlots).
//   {other}     intercom only: the first name of the other kerbal in the chat. {self}: the speaker's own name.
// A pool value may hold slots itself ("Who took my {thing}?"), so a pool can be a list of whole sentences. And the
// english of a row may hold versions split by |: "Who took my {thing}? | Has anyone seen my {thing}?". The game
// picks one version per play. The audio fits every version, because the spanish is general.
// A version can start with conditions: "[landed] You're on the ground." When a line has versions whose conditions
// all hold right now, only those are picked. Else only the versions without conditions. So every line of a scene
// switches together, and a line that makes no sense on the ground can become a joke there.
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
                string why = SubtitleSlots.ValueProblem(val);
                if (why != null)
                {
                    if (errors != null) errors.Add("pools line " + (i + 1) + ": " + why);
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

        /// <summary>Most levels of pool values inside pool values. Deeper slots give "...". The build script checks
        /// that pools do not refer to each other in a circle.</summary>
        public const int MaxDepth = 6;
        public const char VersionSep = '|';

        /// <summary>A pool value is non-empty, has no | and only well-formed slots (no {body}). Null when it is fine.</summary>
        public static string ValueProblem(string val)
        {
            if (string.IsNullOrEmpty(val) || val.Trim().Length == 0) return "value is empty";
            if (val.IndexOf(VersionSep) >= 0) return "a value cannot hold |, give each version its own row";
            string rest = SlotRe.Replace(val, "");
            if (rest.IndexOf('{') >= 0 || rest.IndexOf('}') >= 0) return "value has a bad slot, use {name} or {name:2}";
            foreach (Match m in SlotRe.Matches(val))
                if (m.Groups[1].Value == "body") return "{body} cannot be used in a pool value";
            return null;
        }

        /// <summary>Conditions a version can have: the flight phases, kerbin (at Kerbin) and away (anywhere else).
        /// Same list as CONDITIONS in audio/build_lonapse.py.</summary>
        public static readonly string[] Conditions = { "prelaunch", "launch", "ascent", "orbit", "coast", "reentry",
                                                       "descent", "landed", "blackout", "kerbin", "away" };

        /// <summary>Splits "[landed kerbin] text" into its conditions and the text. A version without [ ] has no
        /// conditions. Returns false for an unknown condition or a [ without ].</summary>
        public static bool SplitConditions(string version, List<string> conds, out string text)
        {
            text = version == null ? "" : version.Trim();
            if (text.Length == 0 || text[0] != '[') return true;
            int close = text.IndexOf(']');
            if (close < 0) return false;
            bool ok = true;
            foreach (string c in text.Substring(1, close - 1).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (Array.IndexOf(Conditions, c) < 0) ok = false;
                if (conds != null) conds.Add(c);
            }
            text = text.Substring(close + 1).Trim();
            return ok && (conds == null || conds.Count > 0);
        }

        /// <summary>The conditions that hold for a flight: the phase (when it is one of Conditions) and kerbin or
        /// away from the body key.</summary>
        public static List<string> ConditionsFor(string phase, string bodyKey)
        {
            var r = new List<string>();
            if (!string.IsNullOrEmpty(phase) && Array.IndexOf(Conditions, phase) >= 0) r.Add(phase);
            if (!string.IsNullOrEmpty(bodyKey)) r.Add(bodyKey == "kerbin" ? "kerbin" : "away");
            return r;
        }

        /// <summary>True when the english needs Fill: it has slots or versions split by |.</summary>
        public static bool NeedsFill(string text)
        {
            return !string.IsNullOrEmpty(text) && (text.IndexOf(VersionSep) >= 0 || SlotRe.IsMatch(text));
        }

        /// <summary>The versions of an english text split by |, trimmed. A text without | is one version.</summary>
        public static List<string> Versions(string text)
        {
            var r = new List<string>();
            if (text == null) return r;
            foreach (string part in text.Split(VersionSep)) r.Add(part.Trim());
            return r;
        }

        /// <summary>
        /// Fills the slots of one scene. Lines that need nothing are returned as they are, any other line as a copy
        /// with the filled English (the clip key stays, so the same clip plays and the memory works as before).
        /// For a line with versions (|) one version is picked first, with pick("version:" + clip key, indices).
        /// <paramref name="values"/> holds the flight values. <paramref name="lineValues"/> (may be null) gives extra
        /// values per line index, for {other} and {self}. Pool values come from <paramref name="pick"/> (pool, values),
        /// so the caller can use the line memory. A pool value can hold slots, those are filled too. The same slot
        /// gives the same text in every line of the scene. A slot that cannot be filled becomes "..." and goes to
        /// unresolved.
        /// </summary>
        public static List<PhraseEntry> Fill(IList<PhraseEntry> lines, SlotPools pools, IDictionary<string, string> values,
                                             Func<int, IDictionary<string, string>> lineValues,
                                             Func<string, IList<string>, string> pick, List<string> unresolved)
        {
            return Fill(lines, pools, values, lineValues, pick, unresolved, null);
        }

        /// <summary>Fill with the <paramref name="conditions"/> that hold now (ConditionsFor), for versions like
        /// "[landed] ...". Null means none hold.</summary>
        public static List<PhraseEntry> Fill(IList<PhraseEntry> lines, SlotPools pools, IDictionary<string, string> values,
                                             Func<int, IDictionary<string, string>> lineValues,
                                             Func<string, IList<string>, string> pick, List<string> unresolved,
                                             ICollection<string> conditions)
        {
            var r = new List<PhraseEntry>();
            if (lines == null) return r;
            var st = new FillState();
            st.pools = pools; st.values = values; st.pick = pick; st.unresolved = unresolved;
            for (int i = 0; i < lines.Count; i++)
            {
                PhraseEntry e = lines[i];
                if (e == null || !NeedsFill(e.English)) { r.Add(e); continue; }
                st.per = lineValues != null ? lineValues(i) : null;
                st.where = e.ClipKey + " (line " + i + ")";
                string src = PickVersion(e, pick, conditions);
                r.Add(CopyWithEnglish(e, Expand(src, true, 0, st)));
            }
            return r;
        }

        sealed class FillState
        {
            public SlotPools pools;
            public IDictionary<string, string> values, per;
            public Func<string, IList<string>, string> pick;
            public List<string> unresolved;
            public string where;
            // slot (name:n) to its filled text, for the whole scene
            public readonly Dictionary<string, string> chosen = new Dictionary<string, string>();
            // pool to the raw values taken from it in this scene, so {pool:2} gets another one
            public readonly Dictionary<string, List<string>> used = new Dictionary<string, List<string>>();
        }

        static string PickVersion(PhraseEntry e, Func<string, IList<string>, string> pick, ICollection<string> conditions)
        {
            List<string> vs = Versions(e.English);
            if (vs.Count == 1) return vs[0];
            var texts = new string[vs.Count];
            var matching = new List<string>();
            var plain = new List<string>();
            for (int k = 0; k < vs.Count; k++)
            {
                var conds = new List<string>();
                SplitConditions(vs[k], conds, out texts[k]);
                string id = k.ToString(System.Globalization.CultureInfo.InvariantCulture);
                if (conds.Count == 0) { plain.Add(id); continue; }
                bool all = conditions != null;
                foreach (string c in conds) if (all && !conditions.Contains(c)) all = false;
                if (all) matching.Add(id);
            }
            List<string> idx = matching.Count > 0 ? matching : plain;
            if (idx.Count == 0) return texts[0];   // the build script makes sure a line has a version without [ ]
            string choice = pick != null ? pick("version:" + e.ClipKey, idx) : idx[0];
            int n;
            if (choice == null || !int.TryParse(choice, out n) || n < 0 || n >= vs.Count) n = int.Parse(idx[0]);
            return texts[n];
        }

        // Fills the slots of one text. atStart: the text itself starts a sentence (the top level, or a pool value
        // that sits at the start of a sentence). A slot value at the start of a sentence gets a capital.
        static string Expand(string src, bool atStart, int depth, FillState st)
        {
            var sb = new System.Text.StringBuilder(src.Length + 16);
            int at = 0;
            foreach (Match m in SlotRe.Matches(src))
            {
                sb.Append(src, at, m.Index - at);
                string v = Resolve(m, depth, st);
                sb.Append(AtSentenceStart(src, m.Index, atStart) ? CapFirst(v) : v);
                at = m.Index + m.Length;
            }
            sb.Append(src, at, src.Length - at);
            return sb.ToString();
        }

        static string Resolve(Match m, int depth, FillState st)
        {
            string name = m.Groups[1].Value, n = m.Groups[2].Success ? m.Groups[2].Value : "1";
            if (name == "body") return m.Value;   // {body} rows are expanded at build time, never here
            string v;
            if (st.per != null && st.per.TryGetValue(name, out v) && !string.IsNullOrEmpty(v)) return v;
            if (st.values != null && st.values.TryGetValue(name, out v) && !string.IsNullOrEmpty(v)) return v;
            string key = name + ":" + n;
            if (st.chosen.TryGetValue(key, out v)) return v;
            IList<string> pool = st.pools != null ? st.pools.Values(name) : null;
            if (pool == null || pool.Count == 0 || depth >= MaxDepth)
            {
                if (st.unresolved != null) st.unresolved.Add(name + " in " + st.where);
                return "...";
            }
            List<string> taken;
            if (!st.used.TryGetValue(name, out taken)) st.used[name] = taken = new List<string>();
            var free = new List<string>();
            foreach (string c in pool) if (!taken.Contains(c)) free.Add(c);
            IList<string> from = free.Count > 0 ? (IList<string>)free : pool;
            string raw = st.pick != null ? st.pick(name, from) : from[0];
            if (string.IsNullOrEmpty(raw)) raw = from[0];
            taken.Add(raw);
            // filled as if mid-sentence, the caller adds the capital when the slot starts a sentence
            v = HasSlots(raw) ? Expand(raw, false, depth + 1, st) : raw;
            st.chosen[key] = v;
            return v;
        }

        /// <summary>True when position i starts the text or a sentence: only spaces and quotes after the start or
        /// after . ! ? before it. Then a slot value there gets a capital letter ("{thing} is gone." gives "Helmet is
        /// gone."). The written text itself is never changed.</summary>
        public static bool AtSentenceStart(string s, int i)
        {
            return AtSentenceStart(s, i, true);
        }

        /// <summary>As above. textStarts: whether the start of s counts as the start of a sentence.</summary>
        public static bool AtSentenceStart(string s, int i, bool textStarts)
        {
            for (int k = i - 1; k >= 0; k--)
            {
                char c = s[k];
                if (char.IsWhiteSpace(c) || c == '"' || c == '\'') continue;
                return c == '.' || c == '!' || c == '?';
            }
            return textStarts;
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
