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
// Runtime voice: the spanish_rt column of a row holds the Spanish of each version (PhraseEntry.SpanishRt), so Piper
// says the version the subtitle shows. A version with a number only the runtime voice can say ({fuel}, {ec} ...) is
// only picked when the director gives that number, which it only does with the runtime voice on (Slots).
// Pure code: no Unity or KSP references, so it compiles under DC_OFFLINE_TEST.
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Lonapse
{
    /// <summary>Values per pool from pools.tsv (columns pool and english, optional spanish).</summary>
    public sealed class SlotPools
    {
        private readonly Dictionary<string, List<string>> pools = new Dictionary<string, List<string>>();
        // pool + tab + value (without [conditions]) to { gender or null, spanish }, for the runtime voice
        private readonly Dictionary<string, KeyValuePair<string, string>> spanish = new Dictionary<string, KeyValuePair<string, string>>();
        public static readonly string[] Genders = { "m", "f", "mp", "fp" };

        public int Count { get { return pools.Count; } }
        public int SpanishCount { get { return spanish.Count; } }
        public bool Has(string pool) { return pool != null && pools.ContainsKey(pool); }

        public IList<string> Values(string pool)
        {
            List<string> v;
            return pool != null && pools.TryGetValue(pool, out v) ? v : null;
        }

        /// <summary>The Spanish of a pool value (its english without [conditions]) and its gender (null: none).
        /// False when the value has no Spanish: a line that uses it then plays its recorded clip.</summary>
        public bool Spanish(string pool, string english, out string gender, out string es)
        {
            gender = null; es = null;
            KeyValuePair<string, string> kv;
            if (!spanish.TryGetValue(pool + "\t" + english, out kv)) return false;
            gender = kv.Key; es = kv.Value;
            return true;
        }

        /// <summary>Splits "f:piedra de la suerte" into its gender (m f mp fp, or null) and the Spanish.</summary>
        public static string SplitGender(string es, out string gender)
        {
            gender = null;
            es = (es ?? "").Trim();
            int colon = es.IndexOf(':');
            if (colon > 0 && colon <= 2 && Array.IndexOf(Genders, es.Substring(0, colon)) >= 0)
            {
                gender = es.Substring(0, colon);
                es = es.Substring(colon + 1).Trim();
            }
            return es;
        }

        /// <summary>Reads pools.tsv: a header with pool and english (spanish optional), then one value per row. #
        /// starts a comment. Bad rows are skipped and described in errors. Same rules as load_pools in
        /// audio/build_lonapse.py.</summary>
        public static SlotPools Parse(IList<string> lines, List<string> errors)
        {
            SlotPools p = new SlotPools();
            if (lines == null) return p;
            int iPool = -1, iEn = -1, iEs = -1, width = 0;
            for (int i = 0; i < lines.Count; i++)
            {
                string line = (lines[i] ?? "").TrimEnd('\r');
                if (line.Trim().Length == 0 || line.TrimStart().StartsWith("#")) continue;
                string[] f = line.Split('\t');
                if (iPool < 0)
                {
                    iPool = Array.IndexOf(f, "pool");
                    iEn = Array.IndexOf(f, "english");
                    iEs = Array.IndexOf(f, "spanish");
                    width = f.Length;
                    if (iPool < 0 || iEn < 0) { if (errors != null) errors.Add("pools: header needs pool and english"); return p; }
                    continue;
                }
                // a missing last cell is an empty spanish (editors drop a trailing tab)
                if (f.Length == width - 1 && iEs == width - 1) { var pad = new string[width]; f.CopyTo(pad, 0); pad[width - 1] = ""; f = pad; }
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
                if (iEs >= 0 && f[iEs].Trim().Length > 0)
                {
                    string g, text;
                    SubtitleSlots.SplitConditions(val, null, out text);
                    string es = SplitGender(f[iEs], out g);
                    if (es.Length == 0) { if (errors != null) errors.Add("pools line " + (i + 1) + ": empty spanish after the gender"); continue; }
                    p.spanish[name + "\t" + text] = new KeyValuePair<string, string>(g, es);
                }
            }
            return p;
        }
    }

    public static class SubtitleSlots
    {
        /// <summary>Flight values the director fills in on every flight (Slots.Flight). The other number slots only
        /// with the runtime voice on.</summary>
        public static readonly string[] FlightSlots = Slots.Flight;
        /// <summary>Intercom only: the other kerbal and the speaker. Same as PERSON_SLOTS in the script.</summary>
        public static readonly string[] PersonSlots = { "other", "self" };
        public const string Other = "other", Self = "self";

        // {name} or {name:2}
        static readonly Regex SlotRe = new Regex(@"\{([a-z_]+)(?::([1-9]))?\}", RegexOptions.CultureInvariant);
        /// <summary>A slot in a spanish_rt version: {thing}, {thing:2}, or with an article {el thing} {un thing} {del thing} {al thing}.</summary>
        public static readonly Regex EsSlotRe = new Regex(@"\{(?:(el|un|del|al) )?([a-z_]+)(?::([1-9]))?\}", RegexOptions.CultureInvariant);

        public static bool IsSlotName(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            foreach (char c in s) if (!((c >= 'a' && c <= 'z') || c == '_')) return false;
            return true;
        }

        /// <summary>The number, name and person slots of Slots cannot be pool names.</summary>
        public static bool IsReserved(string name) { return Slots.IsReserved(name); }

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
        public static string ValueProblem(string raw)
        {
            if (string.IsNullOrEmpty(raw) || raw.Trim().Length == 0) return "value is empty";
            string val;
            if (!SplitConditions(raw, null, out val)) return "bad [conditions], they are " + string.Join(" ", Conditions);
            if (val.Length == 0) return "value is empty";
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
                                                       "descent", "landed", "blackout", "kerbin", "away",
                                                       // parts on the vessel (ChatterDirector.VesselConditions)
                                                       "solar", "antenna", "legs", "ladder", "docking", "heatshield",
                                                       "chute", "engine", "strut", "probe",
                                                       // science experiments on the vessel
                                                       "goo", "sciencejr", "thermometer", "barometer", "seismic",
                                                       "gravity", "magnetometer" };

        /// <summary>The vessel condition of a ModuleScienceExperiment experimentID, null for another one.</summary>
        public static string ExperimentCondition(string experimentId)
        {
            switch (experimentId)
            {
                case "mysteryGoo": return "goo";
                case "mobileMaterialsLab": return "sciencejr";
                case "temperatureScan": return "thermometer";
                case "barometerScan": return "barometer";
                case "seismicScan": return "seismic";
                case "gravityScan": return "gravity";
                case "magnetometer": return "magnetometer";
                default: return null;
            }
        }

        /// <summary>Splits "[landed kerbin] text" into its conditions and the text. A version without [ ] has no
        /// conditions. Returns false for an unknown condition or a [ without ].</summary>
        public static bool SplitConditions(string version, List<string> conds, out string text)
        {
            text = version == null ? "" : version.Trim();
            if (text.Length == 0 || text[0] != '[') return true;
            int close = text.IndexOf(']');
            if (close < 0) return false;
            bool ok = true;
            int n = 0;
            foreach (string c in text.Substring(1, close - 1).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (Array.IndexOf(Conditions, c) < 0) ok = false;
                if (conds != null) conds.Add(c);
                n++;
            }
            text = text.Substring(close + 1).Trim();
            return ok && n > 0;
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
            return Fill(lines, pools, values, lineValues, pick, unresolved, conditions, false);
        }

        /// <summary>Fill, and with <paramref name="spoken"/> also the Spanish of each filled line in
        /// PhraseEntry.RuntimeSpanish, for the runtime voice, from the spanish_rt column and the pool Spanish. A line
        /// whose Spanish cannot be made (no spanish_rt for the version, or no Spanish for a value it uses) keeps
        /// RuntimeSpanish null and plays its recorded clip.</summary>
        public static List<PhraseEntry> Fill(IList<PhraseEntry> lines, SlotPools pools, IDictionary<string, string> values,
                                             Func<int, IDictionary<string, string>> lineValues,
                                             Func<string, IList<string>, string> pick, List<string> unresolved,
                                             ICollection<string> conditions, bool spoken)
        {
            bool named;
            return Fill(lines, pools, values, lineValues, pick, unresolved, conditions, spoken, false, out named);
        }

        /// <summary>Fill, and with <paramref name="fewNames"/> a version without a name ({other}, {self}, {kerbal},
        /// {vessel}) wins when the line has one. A name was said a moment ago. In every case a scene says at most one
        /// name when it can: after a line with a name the next lines prefer versions without. named: a picked
        /// version holds a name.</summary>
        public static List<PhraseEntry> Fill(IList<PhraseEntry> lines, SlotPools pools, IDictionary<string, string> values,
                                             Func<int, IDictionary<string, string>> lineValues,
                                             Func<string, IList<string>, string> pick, List<string> unresolved,
                                             ICollection<string> conditions, bool spoken, bool fewNames, out bool named)
        {
            named = false;
            var r = new List<PhraseEntry>();
            if (lines == null) return r;
            var st = new FillState();
            st.pools = pools; st.values = values; st.pick = pick; st.unresolved = unresolved; st.conditions = conditions; st.spoken = spoken;
            st.fewNames = fewNames;
            for (int i = 0; i < lines.Count; i++)
            {
                PhraseEntry e = lines[i];
                if (e == null || !NeedsFill(e.English)) { r.Add(e); continue; }
                st.per = lineValues != null ? lineValues(i) : null;
                st.where = e.ClipKey + " (line " + i + ")";
                int vi;
                string src = PickVersion(e, st, out vi);
                if (NamesSomeone(src)) { named = true; st.fewNames = true; }
                string en = Expand(Plurals(src, st, false), true, 0, st);
                r.Add(CopyWithEnglish(e, en, SpanishFor(e, vi, st)));
            }
            return r;
        }

        /// <summary>
        /// True when every line of the scene can be filled now: each line that needs Fill has a version for the
        /// conditions whose slots all have a value (a pool value whose conditions hold, a flight value, a person
        /// for {other} and {self}). The director skips a scene that cannot, so a template about the solar panels
        /// never plays on a ship without one.
        /// </summary>
        public static bool CanFill(IList<PhraseEntry> lines, SlotPools pools, IDictionary<string, string> values,
                                   Func<int, IDictionary<string, string>> lineValues, ICollection<string> conditions)
        {
            if (lines == null) return true;
            var st = new FillState();
            st.pools = pools; st.values = values; st.conditions = conditions;
            for (int i = 0; i < lines.Count; i++)
            {
                PhraseEntry e = lines[i];
                if (e == null || !NeedsFill(e.English)) continue;
                st.per = lineValues != null ? lineValues(i) : null;
                if (FillableVersions(e, st).Count == 0) return false;
            }
            return true;
        }

        sealed class FillState
        {
            public SlotPools pools;
            public IDictionary<string, string> values, per;
            public Func<string, IList<string>, string> pick;
            public List<string> unresolved;
            public ICollection<string> conditions;
            public string where;
            public bool spoken;
            // prefer versions without a name (one was said a moment ago, or earlier in this scene)
            public bool fewNames;
            // slot (name:n) to the pool value taken (its english without [ ]), for the Spanish
            public readonly Dictionary<string, string> chosenRaw = new Dictionary<string, string>();
            // slot (name:n) to its filled text, for the whole scene
            public readonly Dictionary<string, string> chosen = new Dictionary<string, string>();
            // pool to the raw values taken from it in this scene, so {pool:2} gets another one
            public readonly Dictionary<string, List<string>> used = new Dictionary<string, List<string>>();
            // pool to its values whose conditions hold and whose slots can be filled (null while being worked out)
            public readonly Dictionary<string, List<string>> usable = new Dictionary<string, List<string>>();
        }

        static bool Holds(List<string> conds, ICollection<string> now)
        {
            foreach (string c in conds) if (now == null || !now.Contains(c)) return false;
            return true;
        }

        // The pool values whose [conditions] hold and whose own slots can be filled, raw (with the [ ] part).
        static List<string> Usable(string pool, int depth, FillState st)
        {
            List<string> r;
            if (st.usable.TryGetValue(pool, out r)) return r ?? new List<string>();   // null: a pool inside itself
            st.usable[pool] = null;
            r = new List<string>();
            IList<string> all = st.pools != null ? st.pools.Values(pool) : null;
            if (all != null && depth < MaxDepth)
                foreach (string raw in all)
                {
                    var conds = new List<string>();
                    string text;
                    SplitConditions(raw, conds, out text);
                    if (Holds(conds, st.conditions) && TextFillable(text, depth + 1, st)) r.Add(raw);
                }
            st.usable[pool] = r;
            return r;
        }

        static bool TextFillable(string text, int depth, FillState st)
        {
            foreach (Match m in SlotRe.Matches(text))
            {
                string name = m.Groups[1].Value;
                int n = m.Groups[2].Success ? m.Groups[2].Value[0] - '0' : 1;
                if (name == "body") continue;
                string v;
                if (st.per != null && st.per.TryGetValue(name, out v) && !string.IsNullOrEmpty(v)) continue;
                if (st.values != null && st.values.TryGetValue(name, out v) && !string.IsNullOrEmpty(v)) continue;
                if (st.chosen.ContainsKey(name + ":" + n)) continue;
                if (Usable(name, depth, st).Count < n) return false;
            }
            return true;
        }

        // The versions of a line that may be picked now: those whose conditions hold, else the plain ones; and of
        // those only the ones that can be filled. Each entry is the index in Versions and the text without [ ].
        static List<KeyValuePair<int, string>> FillableVersions(PhraseEntry e, FillState st)
        {
            List<string> vs = Versions(e.English);
            var matching = new List<KeyValuePair<int, string>>();
            var plain = new List<KeyValuePair<int, string>>();
            for (int k = 0; k < vs.Count; k++)
            {
                var conds = new List<string>();
                string text;
                SplitConditions(vs[k], conds, out text);
                if (!TextFillable(text, 0, st)) continue;
                // a number the recording cannot say: only with its runtime Spanish
                if (HasRuntimeNumber(text) && string.IsNullOrEmpty(e.SpanishRtVersion(k))) continue;
                if (conds.Count == 0) plain.Add(new KeyValuePair<int, string>(k, text));
                else if (Holds(conds, st.conditions)) matching.Add(new KeyValuePair<int, string>(k, text));
            }
            return matching.Count > 0 ? matching : plain;
        }

        /// <summary>The text says a name: a person slot ({other}, {self}, {kerbal}) or {vessel}.</summary>
        public static bool NamesSomeone(string text)
        {
            if (string.IsNullOrEmpty(text) || text.IndexOf('{') < 0) return false;
            foreach (Match m in SlotRe.Matches(text))
            {
                string n = m.Groups[1].Value;
                if (Slots.IsPerson(n) || n == "vessel") return true;
            }
            return false;
        }

        /// <summary>The text uses a number slot that only the runtime voice can say ({fuel}, {ec} ...).</summary>
        public static bool HasRuntimeNumber(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            foreach (Match m in SlotRe.Matches(text)) if (Slots.IsRuntimeNumber(m.Groups[1].Value)) return true;
            return false;
        }

        /// <summary>The number values of the scene, for the singular noun after a 1 ("1 minute").</summary>
        static Dictionary<string, double> Numbers(FillState st)
        {
            var d = new Dictionary<string, double>();
            if (st.values == null) return d;
            foreach (var kv in st.values)
            {
                double v;
                if (Slots.IsNumber(kv.Key) && double.TryParse(kv.Value, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out v)) d[kv.Key] = v;
            }
            return d;
        }

        static string Plurals(string text, FillState st, bool spanish)
        {
            return text.IndexOf('{') < 0 ? text : RuntimeLines.Singular(text, Numbers(st), spanish);
        }

        static string PickVersion(PhraseEntry e, FillState st, out int index)
        {
            index = -1;
            List<KeyValuePair<int, string>> ok = FillableVersions(e, st);
            if (ok.Count == 0)
            {
                // CanFill said no: the director should not have picked the scene. Take the first version, its
                // slots show "..." and go to unresolved.
                string text;
                SplitConditions(Versions(e.English)[0], null, out text);
                return text;
            }
            if (st.fewNames && ok.Count > 1)
            {
                List<KeyValuePair<int, string>> bare = ok.FindAll(kv => !NamesSomeone(kv.Value));
                if (bare.Count > 0) ok = bare;
            }
            index = ok[0].Key;
            if (ok.Count == 1 || st.pick == null) return ok[0].Value;
            var idx = new List<string>();
            foreach (var kv in ok) idx.Add(kv.Key.ToString(System.Globalization.CultureInfo.InvariantCulture));
            string choice = st.pick(SceneMemory.VersionPool + e.ClipKey, idx);
            foreach (var kv in ok)
                if (kv.Key.ToString(System.Globalization.CultureInfo.InvariantCulture) == choice) { index = kv.Key; return kv.Value; }
            return ok[0].Value;
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
            if (st.values != null && st.values.TryGetValue(name, out v) && !string.IsNullOrEmpty(v)) return ShownValue(name, v);
            string key = name + ":" + n;
            if (st.chosen.TryGetValue(key, out v)) return v;
            List<string> pool = depth < MaxDepth ? Usable(name, depth, st) : new List<string>();
            if (pool.Count == 0)
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
            if (string.IsNullOrEmpty(raw) || !pool.Contains(raw)) raw = from[0];
            taken.Add(raw);
            string text;
            SplitConditions(raw, null, out text);
            st.chosenRaw[key] = text;
            // filled as if mid-sentence, the caller adds the capital when the slot starts a sentence
            v = HasSlots(text) ? Expand(text, false, depth + 1, st) : text;
            st.chosen[key] = v;
            return v;
        }

        // ---------------- the Spanish for the runtime voice ----------------

        /// <summary>The Spanish of the picked version with the same values, marked for RuntimeSpeech (names in ‹ ›,
        /// bodies in ⟪ ⟫). Null when there is none, when a value has no Spanish, or when it is the recorded sentence.</summary>
        static string SpanishFor(PhraseEntry e, int version, FillState st)
        {
            if (!st.spoken || version < 0) return null;
            string src = e.SpanishRtVersion(version);
            if (string.IsNullOrEmpty(src)) return null;
            bool ok = true;
            string t = ExpandEs(Plurals(src, st, true), true, 0, st, ref ok);
            if (!ok || string.IsNullOrEmpty(t) || t == e.Spanish) return null;
            return t;
        }

        static string ExpandEs(string src, bool atStart, int depth, FillState st, ref bool ok)
        {
            var sb = new System.Text.StringBuilder(src.Length + 16);
            int at = 0;
            foreach (Match m in EsSlotRe.Matches(src))
            {
                sb.Append(src, at, m.Index - at);
                string v = ResolveEs(m, depth, st, ref ok);
                if (!ok) return null;
                sb.Append(AtSentenceStart(src, m.Index, atStart) ? CapFirst(v) : v);
                at = m.Index + m.Length;
            }
            sb.Append(src, at, src.Length - at);
            return sb.ToString();
        }

        static string ResolveEs(Match m, int depth, FillState st, ref bool ok)
        {
            string art = m.Groups[1].Success ? m.Groups[1].Value : null;
            string name = m.Groups[2].Value, n = m.Groups[3].Success ? m.Groups[3].Value : "1";
            string v;
            if ((st.per != null && st.per.TryGetValue(name, out v) && !string.IsNullOrEmpty(v))
                || (st.values != null && st.values.TryGetValue(name, out v) && !string.IsNullOrEmpty(v)))
            {
                string said = art == null ? SpokenValue(name, v) : null;
                if (said == null) ok = false;
                return said ?? "";
            }
            string raw, g, es;
            if (depth >= MaxDepth || !st.chosenRaw.TryGetValue(name + ":" + n, out raw) || st.pools == null || !st.pools.Spanish(name, raw, out g, out es))
            {
                ok = false;
                return "";
            }
            string val = EsSlotRe.IsMatch(es) ? ExpandEs(es, false, depth + 1, st, ref ok) : es;
            if (!ok) return "";
            if (art == null) return val;
            string a = Article(art, g);
            if (a == null) { ok = false; return ""; }
            return a + " " + val;
        }

        /// <summary>A flight or person value as the voice says it: a name in a name mark, a body in a raw mark (its
        /// bodies.tsv spelling), a number in Spanish words. Null when it cannot be said.</summary>
        static string SpokenValue(string name, string v)
        {
            switch (name)
            {
                case "other":
                case "self":
                    return RuntimeSpeech.MarkName(RuntimeLines.CleanName(v));
                case "vessel":
                    string say = v.StartsWith("#", StringComparison.Ordinal) ? null : RuntimeLines.VesselSpeakable(v);
                    return say == null ? null : RuntimeSpeech.MarkName(say);
                case "here":
                    string body = v.StartsWith("the ", StringComparison.OrdinalIgnoreCase) ? v.Substring(4) : v;
                    return RuntimeSpeech.MarkRaw(RuntimeLines.BodySpoken(body));
                default:
                    double d;
                    if (!Slots.IsNumber(name) || !double.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out d)) return null;
                    return RuntimeLines.Words(name, d);
            }
        }

        /// <summary>A flight value as the subtitle shows it: a number as RuntimeLines.Digits ("1,234", "7.5"), the
        /// rest as it is.</summary>
        static string ShownValue(string name, string v)
        {
            double d;
            if (Slots.IsNumber(name) && double.TryParse(v, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out d)) return RuntimeLines.Digits(name, d);
            return v;
        }

        /// <summary>The article for {el x} {un x} {del x} {al x} by the gender of the value. Null without a gender.</summary>
        public static string Article(string art, string gender)
        {
            int g = Array.IndexOf(SlotPools.Genders, gender ?? "");
            if (g < 0) return null;
            switch (art)
            {
                case "el": return new[] { "el", "la", "los", "las" }[g];
                case "un": return new[] { "un", "una", "unos", "unas" }[g];
                case "del": return new[] { "del", "de la", "de los", "de las" }[g];
                case "al": return new[] { "al", "a la", "a los", "a las" }[g];
                default: return null;
            }
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

        static PhraseEntry CopyWithEnglish(PhraseEntry e, string english, string runtimeSpanish)
        {
            return new PhraseEntry
            {
                ClipKey = e.ClipKey, Event = e.Event, Severity = e.Severity, Band = e.Band, Role = e.Role, Scene = e.Scene,
                Seq = e.Seq, Gender = e.Gender, Phase = e.Phase, Cause = e.Cause, Trait = e.Trait, Persona = e.Persona,
                Topic = e.Topic, Core = e.Core, Beep = e.Beep, Body = e.Body, Spanish = e.Spanish,
                LonapseText = e.LonapseText, English = english, Folder = e.Folder, RuntimeSpanish = runtimeSpanish,
                SpanishRt = e.SpanishRt
            };
        }
    }
}
