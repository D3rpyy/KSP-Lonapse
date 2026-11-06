// Experimental (runtime voice): lines that only exist at runtime, because they hold real values. The templates are in
// GameData/Lonapse/Phrases/Runtime/*.tsv (reports, docking, landing, science, contracts, chats). This file parses them, picks
// the ones that fit and fills them. Also the Spanish words for numbers and the spoken names of bodies and vessels.
// Pure: no Unity, no KSP, so the offline tests cover it. The director fills the values and sends the Spanish to Piper.
// In the Spanish a number becomes words, and those words are reversed like every other word (loñapsE).
// Names stay names (RuntimeSpeech.MarkName). The English subtitle shows the number in digits.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Lonapse
{
    public sealed class RuntimeTemplate
    {
        public string Id, Kind;
        /// <summary>The versions of the line: { spanish, english } pairs, split by | in the file, in the same order.
        /// One is picked per play from the versions whose slots all have a value.</summary>
        public readonly List<string[]> Versions = new List<string[]>();
        /// <summary>Situations of the "when" column (orbit flight suborbital landed escape), empty: any.</summary>
        public readonly List<string> Situations = new List<string>();
        /// <summary>Checks of the "when" column: value key, operator (&lt; &lt;= &gt; &gt;= =), number.</summary>
        public readonly List<KeyValuePair<string, KeyValuePair<string, double>>> Checks =
            new List<KeyValuePair<string, KeyValuePair<string, double>>>();
        /// <summary>Chat only: the job kerbal A and kerbal B must have (a=pilot b=scientist in the when column).
        /// Null: any job.</summary>
        public string TraitA, TraitB;
        /// <summary>Reply only: the Mission Control lines it answers, ids or prefixes ending in * (to=rt_fuel* in the
        /// when column). Empty: any report or warp line.</summary>
        public readonly List<string> To = new List<string>();
        /// <summary>Where the row is: "reports.tsv:12".</summary>
        public string Where = "";

        /// <summary>A line about time warp: kind warp, or a check on in_warp (a warp chat).</summary>
        public bool AboutWarp
        {
            get
            {
                if (Kind == "warp") return true;
                foreach (var c in Checks) if (c.Key == "in_warp") return true;
                return false;
            }
        }

        /// <summary>The first version (the tests and the log use it).</summary>
        public string Spanish { get { return Versions.Count > 0 ? Versions[0][0] : ""; } }
        public string English { get { return Versions.Count > 0 ? Versions[0][1] : ""; } }

        public RuntimeTemplate(string id, string kind, string spanish, string english)
        {
            Id = id; Kind = kind;
            string[] es = (spanish ?? "").Split(SubtitleSlots.VersionSep), en = (english ?? "").Split(SubtitleSlots.VersionSep);
            for (int i = 0; i < Math.Max(es.Length, en.Length); i++)
                Versions.Add(new[] { i < es.Length ? es[i].Trim() : "", i < en.Length ? en[i].Trim() : "" });
        }
    }

    public static class RuntimeLines
    {
        public static readonly string[] Kinds = { "report", "dock", "land", "science", "science_pts",
                                                  "contract_funds", "contract_done", "contract_failed", "chat", "warp", "reply", "sunrise" };
        public static readonly string[] Situations = { "orbit", "flight", "suborbital", "landed", "escape", "other" };
        /// <summary>The number slots (Slots.Numbers) and the name slots: names and persons of Slots.</summary>
        public static readonly string[] NumberSlots = Slots.Numbers;
        public static readonly string[] NameSlots = Concat(Slots.Names, Slots.Persons);
        /// <summary>Columns every runtime file must have, found by name. Other columns are ignored.</summary>
        public static readonly string[] Columns = { "id", "kind", "when", "spanish", "english" };

        static string[] Concat(string[] a, string[] b)
        {
            var r = new string[a.Length + b.Length];
            a.CopyTo(r, 0);
            b.CopyTo(r, a.Length);
            return r;
        }

        /// <summary>Career log types that count as a visit to their target body.</summary>
        static readonly string[] VisitTypes = { "Flyby", "Orbit", "Suborbit", "Land", "Flight", "Escape" };

        /// <summary>
        /// Crew history (user request): from a kerbal's career log, entries { flight, type, target } of the flights
        /// before this one. missions: flights that ended with a recovery. flight_no: missions + 1, this flight.
        /// visits and landings: earlier flights that reached or landed on <paramref name="body"/>. stars: the
        /// experience level. Training entries do not count.
        /// </summary>
        public static void CrewHistory(IEnumerable<string[]> career, string body, int stars, Dictionary<string, double> nums)
        {
            if (nums == null) return;
            var recovered = new HashSet<string>();
            var visits = new HashSet<string>();
            var lands = new HashSet<string>();
            if (career != null)
                foreach (string[] e in career)
                {
                    if (e == null || e.Length < 3) continue;
                    string f = e[0] ?? "", type = e[1] ?? "", target = e[2] ?? "";
                    if (type.StartsWith("Training", StringComparison.Ordinal)) continue;
                    if (type == "Recover") recovered.Add(f);
                    bool here = !string.IsNullOrEmpty(body) && string.Equals(target, body, StringComparison.OrdinalIgnoreCase);
                    if (here && Array.IndexOf(VisitTypes, type) >= 0) visits.Add(f);
                    if (here && type == "Land") lands.Add(f);
                }
            nums["missions"] = recovered.Count;
            nums["flight_no"] = recovered.Count + 1;
            nums["visits"] = visits.Count;
            nums["landings"] = lands.Count;
            nums["stars"] = Math.Max(0, stars);
        }
        /// <summary>A chat line: rt_chat_&lt;name&gt;_&lt;seq&gt;. The lines of one name form one chat, seq from 1.</summary>
        static readonly Regex ChatId = new Regex(@"^(rt_chat_[a-z0-9_]+?)_([0-9]+)$");

        static readonly Regex Check = new Regex(@"^([a-z_]+)(<=|>=|<|>|=)(-?[0-9]+(?:\.[0-9]+)?)$");
        static readonly Regex TraitCheck = new Regex(@"^(a|b)=([a-z]+)$");
        static readonly Regex IdRe = new Regex(@"^rt_[a-z0-9_]+$");
        static readonly Regex ToRe = new Regex(@"^to=(rt_[a-z0-9_]*\*?)$");

        /// <summary>Parses one runtime file (the tests, a single table).</summary>
        public static List<RuntimeTemplate> Parse(IList<string> lines, List<string> errors)
        {
            return ParseAll(new[] { new KeyValuePair<string, IList<string>>("runtime", lines) }, errors);
        }

        /// <summary>
        /// Parses the files of Phrases/Runtime (name, lines) as one set: ids are unique over all files, and a chat may
        /// not be split over two files. Each file has its own header with at least id, kind, when, spanish, english
        /// (any order, other columns are ignored). # starts a comment. Bad rows go to errors and are skipped.
        /// </summary>
        public static List<RuntimeTemplate> ParseAll(IEnumerable<KeyValuePair<string, IList<string>>> files, List<string> errors)
        {
            var r = new List<RuntimeTemplate>();
            var seen = new HashSet<string>();
            foreach (var file in files)
                ParseFile(file.Key, file.Value, errors, seen, r);
            // chats: ids rt_chat_<name>_<seq>, seq 1, 2, 3 ... without gaps, at least 2 lines, conditions on line 1 only
            var groups = new Dictionary<string, List<RuntimeTemplate>>();
            var drop = new HashSet<RuntimeTemplate>();
            foreach (RuntimeTemplate t in r)
            {
                if (t.Kind != "chat") continue;
                Match m = ChatId.Match(t.Id);
                if (!m.Success) { if (errors != null) errors.Add(t.Where + " (" + t.Id + "): a chat id is rt_chat_<name>_<seq>"); drop.Add(t); continue; }
                List<RuntimeTemplate> g;
                if (!groups.TryGetValue(m.Groups[1].Value, out g)) groups[m.Groups[1].Value] = g = new List<RuntimeTemplate>();
                g.Add(t);
            }
            foreach (var kv in groups)
            {
                kv.Value.Sort((x, y) => ChatSeq(x).CompareTo(ChatSeq(y)));
                string why = null;
                if (kv.Value.Count < 2) why = "a chat needs at least 2 lines";
                string file0 = FileOf(kv.Value[0].Where);
                for (int i = 0; i < kv.Value.Count && why == null; i++)
                {
                    if (ChatSeq(kv.Value[i]) != i + 1) why = "seq must go 1, 2, 3 without gaps";
                    else if (FileOf(kv.Value[i].Where) != file0) why = "all lines of a chat go in one file";
                    else if (i > 0 && (kv.Value[i].Checks.Count > 0 || kv.Value[i].Situations.Count > 0
                                       || kv.Value[i].TraitA != null || kv.Value[i].TraitB != null)) why = "only line 1 has a when";
                }
                if (why != null) { if (errors != null) errors.Add("runtime chat " + kv.Key + ": " + why); foreach (var t in kv.Value) drop.Add(t); }
            }
            r.RemoveAll(t => drop.Contains(t));
            return r;
        }

        static string FileOf(string where)
        {
            int c = (where ?? "").LastIndexOf(':');
            return c > 0 ? where.Substring(0, c) : where ?? "";
        }

        static void ParseFile(string name, IList<string> lines, List<string> errors, HashSet<string> seen, List<RuntimeTemplate> r)
        {
            Dictionary<string, int> col = null;
            int width = 0;
            if (lines == null) return;
            for (int i = 0; i < lines.Count; i++)
            {
                string line = (lines[i] ?? "").TrimEnd('\r');
                if (line.Trim().Length == 0 || line.TrimStart().StartsWith("#", StringComparison.Ordinal)) continue;
                string[] f = line.Split('\t');
                for (int k = 0; k < f.Length; k++) f[k] = f[k].Trim();
                string where = name + ":" + (i + 1);
                if (col == null)
                {
                    col = new Dictionary<string, int>();
                    for (int k = 0; k < f.Length; k++) if (!col.ContainsKey(f[k])) col[f[k]] = k;
                    var missing = new List<string>();
                    foreach (string c in Columns) if (!col.ContainsKey(c)) missing.Add(c);
                    if (missing.Count > 0)
                    {
                        if (errors != null) errors.Add(where + ": header needs the columns " + string.Join(", ", Columns) + " (missing " + string.Join(" ", missing.ToArray()) + ")");
                        return;
                    }
                    width = f.Length;
                    continue;
                }
                int need = 0;
                foreach (string c in Columns) need = Math.Max(need, col[c] + 1);
                if (f.Length > width || f.Length < need)
                {
                    if (errors != null) errors.Add(where + ": expected " + width + " columns, got " + f.Length);
                    continue;
                }
                if (f.Length < width) { var pad = new string[width]; f.CopyTo(pad, 0); for (int k = f.Length; k < width; k++) pad[k] = ""; f = pad; }
                var bad = new List<string>();
                var t = new RuntimeTemplate(f[col["id"]], f[col["kind"]], f[col["spanish"]], f[col["english"]]);
                t.Where = where;
                if (!IdRe.IsMatch(t.Id)) bad.Add("id '" + t.Id + "' must match rt_[a-z0-9_]+");
                else if (!seen.Add(t.Id)) bad.Add("duplicate id " + t.Id);
                if (Array.IndexOf(Kinds, t.Kind) < 0) bad.Add("unknown kind '" + t.Kind + "'");
                foreach (string tok in f[col["when"]].Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (tok == "any") continue;
                    if (Array.IndexOf(Situations, tok) >= 0) { t.Situations.Add(tok); continue; }
                    Match to = ToRe.Match(tok);
                    if (to.Success)
                    {
                        if (t.Kind != "reply") bad.Add("'" + tok + "' only works in a reply");
                        else t.To.Add(to.Groups[1].Value);
                        continue;
                    }
                    Match tm = TraitCheck.Match(tok);
                    if (tm.Success)
                    {
                        string job = tm.Groups[2].Value;
                        if (t.Kind != "chat") bad.Add("'" + tok + "' only works in a chat");
                        else if (Array.IndexOf(Persona.TraitKeys, job) < 0 && Array.IndexOf(Persona.PersonaKeys, job) < 0)
                            bad.Add("unknown job or persona '" + job + "' (pilot engineer scientist tourist, badass brave nervous dim veteran)");
                        else if (tm.Groups[1].Value == "a") t.TraitA = t.TraitA == null ? job : t.TraitA + " " + job;
                        else t.TraitB = t.TraitB == null ? job : t.TraitB + " " + job;
                        continue;
                    }
                    Match m = Check.Match(tok);
                    if (m.Success && Array.IndexOf(NumberSlots, m.Groups[1].Value) >= 0)
                    {
                        t.Checks.Add(new KeyValuePair<string, KeyValuePair<string, double>>(m.Groups[1].Value,
                            new KeyValuePair<string, double>(m.Groups[2].Value, double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture))));
                        continue;
                    }
                    bad.Add("bad condition '" + tok + "'");
                }
                int nEs = f[col["spanish"]].Split(SubtitleSlots.VersionSep).Length, nEn = f[col["english"]].Split(SubtitleSlots.VersionSep).Length;
                if (nEs != nEn) bad.Add(nEs + " spanish versions for " + nEn + " english, split both by | in the same order");
                for (int v = 0; v < t.Versions.Count && bad.Count == 0; v++)
                {
                    string es = t.Versions[v][0], en = t.Versions[v][1], vn = t.Versions.Count > 1 ? " (version " + (v + 1) + ")" : "";
                    if (es.Length == 0 || en.Length == 0) { bad.Add("spanish and english must not be empty" + vn); continue; }
                    foreach (string text in new[] { es, en })
                        foreach (Match m in Slot.Matches(text))
                            if (Array.IndexOf(NumberSlots, m.Groups[1].Value) < 0 && Array.IndexOf(NameSlots, m.Groups[1].Value) < 0)
                                bad.Add("unknown slot {" + m.Groups[1].Value + "}" + vn);
                    if (SlotSet(es) != SlotSet(en)) bad.Add("spanish and english must use the same slots" + vn);
                }
                if (bad.Count > 0) { if (errors != null) errors.Add(where + " (" + t.Id + "): " + string.Join("; ", bad.ToArray())); continue; }
                r.Add(t);
            }
            if (col == null && errors != null) errors.Add(name + ": no header");
        }

        static int ChatSeq(RuntimeTemplate t)
        {
            Match m = ChatId.Match(t.Id);
            return m.Success ? int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) : 0;
        }

        /// <summary>The chats of the templates: lists of lines in seq order. Line 1 holds the when.</summary>
        public static List<List<RuntimeTemplate>> Chats(IList<RuntimeTemplate> all)
        {
            var map = new Dictionary<string, List<RuntimeTemplate>>();
            var order = new List<string>();
            if (all != null)
                foreach (RuntimeTemplate t in all)
                {
                    if (t.Kind != "chat") continue;
                    Match m = ChatId.Match(t.Id);
                    if (!m.Success) continue;
                    List<RuntimeTemplate> g;
                    if (!map.TryGetValue(m.Groups[1].Value, out g)) { map[m.Groups[1].Value] = g = new List<RuntimeTemplate>(); order.Add(m.Groups[1].Value); }
                    g.Add(t);
                }
            var r = new List<List<RuntimeTemplate>>();
            foreach (string k in order) { map[k].Sort((x, y) => ChatSeq(x).CompareTo(ChatSeq(y))); r.Add(map[k]); }
            return r;
        }

        /// <summary>Line 1 of a chat fits (situation, checks and the jobs of A and B), and every line has a version
        /// that fills with the names of its speaker: line i is spoken by A when i is even. namesA and namesB hold self
        /// and other for each speaker. jobA and jobB are the trait keys of the two kerbals (Persona.trait). Null: the
        /// job is not checked.</summary>
        public static bool ChatFits(List<RuntimeTemplate> chat, string situation, Dictionary<string, double> nums,
                                    Dictionary<string, string[]> namesA, Dictionary<string, string[]> namesB,
                                    string jobA = null, string jobB = null)
        {
            if (chat == null || chat.Count == 0) return false;
            if (!JobOk(chat[0].TraitA, jobA) || !JobOk(chat[0].TraitB, jobB)) return false;
            if (!Holds(chat[0], situation, nums) || FillableVersions(chat[0], nums, namesA).Count == 0) return false;
            for (int i = 1; i < chat.Count; i++)
                if (FillableVersions(chat[i], nums, i % 2 == 0 ? namesA : namesB).Count == 0) return false;
            return true;
        }

        /// <summary>The text holds the name as a whole word (not inside another word), ignoring case.</summary>
        public static bool HasWord(string text, string name)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(name)) return false;
            for (int i = text.IndexOf(name, StringComparison.OrdinalIgnoreCase); i >= 0;
                 i = text.IndexOf(name, i + 1, StringComparison.OrdinalIgnoreCase))
            {
                bool before = i == 0 || !char.IsLetterOrDigit(text[i - 1]);
                int j = i + name.Length;
                bool after = j >= text.Length || !char.IsLetterOrDigit(text[j]);
                if (before && after) return true;
            }
            return false;
        }

        /// <summary>The jobs and personas a chat asks for (null: any, else keys split by spaces, all must hold) and the
        /// keys of the kerbal (null: not checked), like "pilot brave dim" (Persona.ChatKeys). A kerbal with an unknown
        /// job (any) and no flags only fits a chat that asks for none.</summary>
        public static bool JobOk(string want, string have)
        {
            if (want == null || have == null) return true;
            string[] keys = have.Split(' ');
            foreach (string w in want.Split(' '))
                if (w.Length > 0 && Array.IndexOf(keys, w) < 0) return false;
            return true;
        }

        static string SlotSet(string text)
        {
            var l = new List<string>();
            foreach (Match m in Slot.Matches(text ?? "")) if (!l.Contains(m.Groups[1].Value)) l.Add(m.Groups[1].Value);
            l.Sort(StringComparer.Ordinal);
            return string.Join(",", l.ToArray());
        }

        /// <summary>The templates of a kind that fit: the situation matches (or the template has none), every check
        /// holds and at least one version has a value for every slot.</summary>
        public static List<RuntimeTemplate> Fitting(IList<RuntimeTemplate> all, string kind, string situation,
                                                    Dictionary<string, double> nums, Dictionary<string, string[]> names)
        {
            var r = new List<RuntimeTemplate>();
            if (all == null) return r;
            foreach (RuntimeTemplate t in all)
                if (t.Kind == kind && Holds(t, situation, nums) && FillableVersions(t, nums, names).Count > 0) r.Add(t);
            return r;
        }

        /// <summary>The kind of a recorded Mission Control line (a calm chatter scene that ends with MC) for Answers.</summary>
        public const string RecordedKind = "recorded";

        /// <summary>A reply answers this Mission Control line: one of its to= ids or prefixes matches, or it has none
        /// and the line is a report, a warp or sunrise line, or a recorded calm MC line.</summary>
        public static bool Answers(RuntimeTemplate reply, string lineId, string lineKind)
        {
            if (reply == null || reply.Kind != "reply" || string.IsNullOrEmpty(lineId)) return false;
            if (reply.To.Count == 0) return lineKind == "report" || lineKind == "warp" || lineKind == "sunrise" || lineKind == RecordedKind;
            foreach (string p in reply.To)
            {
                if (p.EndsWith("*", StringComparison.Ordinal) ? lineId.StartsWith(p.Substring(0, p.Length - 1), StringComparison.Ordinal)
                                                              : lineId == p) return true;
            }
            return false;
        }

        /// <summary>The replies that fit after a Mission Control line: they answer it (Answers), and their when and
        /// slots fit like any template (Fitting).</summary>
        public static List<RuntimeTemplate> Replies(IList<RuntimeTemplate> all, RuntimeTemplate line, string situation,
                                                    Dictionary<string, double> nums, Dictionary<string, string[]> names)
        {
            var r = Fitting(all, "reply", situation, nums, names);
            if (line == null) { r.Clear(); return r; }
            r.RemoveAll(t => !Answers(t, line.Id, line.Kind));
            return r;
        }

        /// <summary>The situation and the checks of the when column hold.</summary>
        static bool Holds(RuntimeTemplate t, string situation, Dictionary<string, double> nums)
        {
            if (t.Situations.Count > 0 && !t.Situations.Contains(situation ?? "")) return false;
            foreach (var c in t.Checks)
            {
                double v;
                if (nums == null || !nums.TryGetValue(c.Key, out v) || double.IsNaN(v)) return false;
                double rv = Rounded(c.Key, v), n = c.Value.Value;
                bool ok;
                switch (c.Value.Key)
                {
                    case "<": ok = rv < n; break;
                    case "<=": ok = rv <= n; break;
                    case ">": ok = rv > n; break;
                    case ">=": ok = rv >= n; break;
                    default: ok = rv == n; break;
                }
                if (!ok) return false;
            }
            return true;
        }

        /// <summary>The indices of the versions whose slots all have a value now.</summary>
        public static List<int> FillableVersions(RuntimeTemplate t, Dictionary<string, double> nums, Dictionary<string, string[]> names)
        {
            var r = new List<int>();
            if (t == null) return r;
            for (int v = 0; v < t.Versions.Count; v++)
            {
                string s, e;
                if (Fill(t, v, nums, names, out s, out e)) r.Add(v);
            }
            return r;
        }

        /// <summary>Stock experiment ids: Spanish words for "datos de ..." and the English for "the ... data".</summary>
        static readonly Dictionary<string, string[]> Experiments = new Dictionary<string, string[]> {
            { "crewReport", new[] { "informe de tripulación", "crew report" } },
            { "evaReport", new[] { "paseo espacial", "EVA report" } },
            { "surfaceSample", new[] { "muestra de suelo", "surface sample" } },
            { "mysteryGoo", new[] { "moco misterioso", "Mystery Goo" } },
            { "mobileMaterialsLab", new[] { "materiales", "materials" } },
            { "temperatureScan", new[] { "temperatura", "temperature" } },
            { "barometerScan", new[] { "presión", "pressure" } },
            { "seismicScan", new[] { "sismos", "seismic" } },
            { "gravityScan", new[] { "gravedad", "gravity" } },
            { "atmosphereAnalysis", new[] { "atmósfera", "atmosphere" } },
            { "asteroidSample", new[] { "asteroide", "asteroid sample" } },
            { "infraredTelescope", new[] { "telescopio", "telescope" } },
            { "magnetometer", new[] { "campo magnético", "magnetometer" } },
        };

        /// <summary>Experiment id from a subject id ("temperatureScan@KerbinSrfLanded" gives "temperatureScan").</summary>
        public static string ExperimentId(string subjectId)
        {
            if (string.IsNullOrEmpty(subjectId)) return "";
            int at = subjectId.IndexOf('@');
            return at > 0 ? subjectId.Substring(0, at) : subjectId;
        }

        /// <summary>Spanish and English words of an experiment. Unknown (modded) ids: "los datos" style generic words.
        /// Comet samples (cometSample_short ...) share one entry.</summary>
        public static string[] ExperimentWords(string experimentId)
        {
            string[] w;
            if (!string.IsNullOrEmpty(experimentId))
            {
                if (Experiments.TryGetValue(experimentId, out w)) return w;
                if (experimentId.StartsWith("cometSample", StringComparison.Ordinal)) return new[] { "cometa", "comet sample" };
            }
            return new[] { "experimento", "experiment" };
        }

        // ---------------- numbers ----------------

        static readonly string[] Units = { "cero", "uno", "dos", "tres", "cuatro", "cinco", "seis", "siete", "ocho", "nueve",
            "diez", "once", "doce", "trece", "catorce", "quince", "dieciséis", "diecisiete", "dieciocho", "diecinueve",
            "veinte", "veintiuno", "veintidós", "veintitrés", "veinticuatro", "veinticinco", "veintiséis", "veintisiete",
            "veintiocho", "veintinueve" };
        static readonly string[] Tens = { "", "", "", "treinta", "cuarenta", "cincuenta", "sesenta", "setenta", "ochenta", "noventa" };
        static readonly string[] Hundreds = { "", "ciento", "doscientos", "trescientos", "cuatrocientos", "quinientos",
            "seiscientos", "setecientos", "ochocientos", "novecientos" };

        /// <summary>
        /// A whole number in Spanish words, 0 to 999 999 999: 21 "veintiuno", 101 "ciento uno", 2500 "dos mil
        /// quinientos". apocope: before a masculine noun "uno" becomes "un" (veintiún kilómetros, un millón).
        /// Negative numbers get "menos".
        /// </summary>
        public static string Number(long n, bool apocope)
        {
            if (n < 0) return "menos " + Number(-n, apocope);
            if (n == 0) return Units[0];
            if (n > 999999999) n = 999999999;
            var parts = new List<string>();
            long millions = n / 1000000, thousands = (n / 1000) % 1000, rest = n % 1000;
            if (millions > 0) parts.Add(millions == 1 ? "un millón" : Below1000(millions, true) + " millones");
            if (thousands > 0) parts.Add(thousands == 1 ? "mil" : Below1000(thousands, true) + " mil");
            if (rest > 0) parts.Add(Below1000(rest, apocope));
            return string.Join(" ", parts.ToArray());
        }

        static string Below1000(long n, bool apocope)
        {
            if (n == 100) return "cien";
            var sb = new StringBuilder();
            long h = n / 100, r = n % 100;
            if (h > 0) sb.Append(Hundreds[h]);
            if (r > 0)
            {
                if (sb.Length > 0) sb.Append(' ');
                string w;
                if (r < 30) w = Units[r];
                else w = Tens[r / 10] + (r % 10 > 0 ? " y " + Units[r % 10] : "");
                if (apocope)
                {
                    if (w == "uno") w = "un";
                    else if (w == "veintiuno") w = "veintiún";
                    else if (w.EndsWith(" y uno", StringComparison.Ordinal)) w = w.Substring(0, w.Length - 3) + "un";
                }
                sb.Append(w);
            }
            return sb.ToString();
        }

        /// <summary>
        /// The value that is spoken and shown, rounded so it stays short (user report: "15 000 000 km" was a long string
        /// of words). From a billion: one decimal below 10 billion. From a million: whole millions, one decimal below
        /// 10 million (2.5 million). From 10 000: whole thousands. Below that: one decimal below 10 for keys with decimals (km, min, deg, ms, science points),
        /// else whole numbers. Spoken words and subtitle digits use this same value.
        /// </summary>
        public static double Rounded(string key, double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) return v;
            double a = Math.Abs(v), sign = v < 0 ? -1 : 1;
            if (a >= 999500000)
            {
                double g = a / 1e9;
                g = g < 9.95 ? Math.Round(g, 1, MidpointRounding.AwayFromZero) : Math.Round(g, 0, MidpointRounding.AwayFromZero);
                return sign * g * 1e9;
            }
            if (a >= 999500)
            {
                double m = a / 1000000.0;
                m = m < 9.95 ? Math.Round(m, 1, MidpointRounding.AwayFromZero) : Math.Round(m, 0, MidpointRounding.AwayFromZero);
                return sign * m * 1000000.0;
            }
            if (a >= 10000) return sign * Math.Round(a / 1000.0, 0, MidpointRounding.AwayFromZero) * 1000.0;
            if (HasDecimals(key) && a < 10) return Math.Round(v, 1, MidpointRounding.AwayFromZero);
            return Math.Round(v, 0, MidpointRounding.AwayFromZero);
        }

        /// <summary>True when the rounded value is said in millions ("quince millones"): Spanish then needs "de"
        /// before the noun ("quince millones de kilómetros").</summary>
        public static bool InMillions(string key, double v)
        {
            return !double.IsNaN(v) && Math.Abs(Rounded(key, v)) >= 1000000.0;
        }

        static bool HasDecimals(string key) { return Slots.HasDecimals(key); }

        /// <summary>Subtitle digits: "1,234", "7.5", "15 million", "2.5 million".</summary>
        public static string Digits(string key, double v)
        {
            double r = Rounded(key, v);
            if (Math.Abs(r) >= 1e9) return (r / 1e9).ToString("#,0.#", CultureInfo.InvariantCulture) + " billion";
            if (Math.Abs(r) >= 1000000.0)
                return (r / 1000000.0).ToString("0.#", CultureInfo.InvariantCulture) + " million";
            return r == Math.Floor(r) ? r.ToString("#,0", CultureInfo.InvariantCulture) : r.ToString("0.0", CultureInfo.InvariantCulture);
        }

        /// <summary>The spoken Spanish of a value: 12.5 gives "doce coma cinco", 15 million "quince millones", 2.5
        /// million "dos coma cinco millones", 1 million "un millón". The day is a count after "día", the other values
        /// come before a noun (apocope: "veintiún kilómetros").</summary>
        public static string Words(string key, double v)
        {
            double r = Rounded(key, v);
            string w;
            if (Math.Abs(r) >= 1e9)
            {
                // Spanish has no word for a billion: "mil millones"
                double g = Math.Abs(r) / 1e9;
                long whole = (long)Math.Floor(g);
                int tenth = (int)Math.Round((g - whole) * 10);
                w = (whole == 1 && tenth == 0 ? "" : Number(whole, true) + (tenth > 0 ? " coma " + Units[tenth] : "") + " ") + "mil millones";
            }
            else if (Math.Abs(r) >= 1000000.0)
            {
                double m = Math.Abs(r) / 1000000.0;
                long whole = (long)Math.Floor(m);
                int tenth = (int)Math.Round((m - whole) * 10);
                if (whole == 1 && tenth == 0) w = "un millón";
                else w = Number(whole, tenth == 0) + (tenth > 0 ? " coma " + Units[tenth] : "") + " millones";
            }
            else
            {
                bool apo = key != "day";
                long whole = (long)Math.Floor(Math.Abs(r));
                int tenth = (int)Math.Round((Math.Abs(r) - whole) * 10);
                w = Number(whole, apo && tenth == 0);
                if (tenth > 0) w += " coma " + Units[tenth];
            }
            return r < 0 ? "menos " + w : w;
        }

        // ---------------- names ----------------

        /// <summary>Spoken names of the stock bodies, as in bodies.tsv read forwards (the build reverses them back):
        /// Eve "Iv", Jool "Yul", the Mun "la Mun". The tests check them against that file.</summary>
        static readonly Dictionary<string, string> StockSpoken = new Dictionary<string, string> {
            { "kerbol", "Kérbol" }, { "moho", "Moho" }, { "eve", "Iv" }, { "gilly", "Guili" }, { "kerbin", "Kérbin" },
            { "mun", "la Mun" }, { "minmus", "Mínmus" }, { "duna", "Duna" }, { "ike", "Aik" }, { "dres", "Dres" },
            { "jool", "Yul" }, { "laythe", "Léiz" }, { "vall", "Val" }, { "tylo", "Tailo" }, { "bop", "Bop" },
            { "pol", "Pol" }, { "eeloo", "Ilu" },
        };

        /// <summary>Spoken Spanish of a body name: the bodies.tsv spelling for a stock body, else the name respelled for
        /// a Spanish voice. Already final: put it in a raw mark (RuntimeSpeech.MarkRaw), not respelled again.</summary>
        public static string BodySpoken(string bodyName)
        {
            if (string.IsNullOrEmpty(bodyName)) return "";
            string s;
            if (StockSpoken.TryGetValue(bodyName.Trim().ToLowerInvariant(), out s)) return s;
            return RuntimeSpeech.Respell(CleanName(bodyName));
        }

        /// <summary>English body name for a subtitle: "the Mun", else the name as KSP gives it (without a ^N suffix).</summary>
        public static string BodyEnglish(string bodyName)
        {
            string n = CleanName(bodyName);
            return n == "Mun" ? "the Mun" : n;
        }

        // two letters or more: a single I, V or X is a letter ("Kerbal X")
        static readonly Regex Roman = new Regex(@"^(II|III|IV|VI|VII|VIII|IX)$");
        static readonly string[] RomanValues = { "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X" };

        /// <summary>A vessel name a voice can say: "Mun Lander II" gives "Mun Lander 2" (a digit is said in Spanish by
        /// the voice), " Debris" and brackets go. Null for a name too long to say in a line (over 4 words or 28 letters)
        /// and for a name with letters outside the Latin alphabet: the Spanish voice cannot say those.</summary>
        public static string VesselSpeakable(string vesselName)
        {
            string n = CleanName(vesselName);
            if (string.IsNullOrEmpty(n) || !LatinOnly(n)) return null;
            n = Regex.Replace(n, @"\s*[\(\[].*?[\)\]]\s*", " ").Trim();
            if (n.EndsWith(" Debris", StringComparison.OrdinalIgnoreCase)) n = n.Substring(0, n.Length - 7).Trim();
            string[] words = n.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0 || words.Length > 4 || n.Length > 28) return null;
            for (int i = 1; i < words.Length; i++)
                if (Roman.IsMatch(words[i])) words[i] = (Array.IndexOf(RomanValues, words[i]) + 1).ToString(CultureInfo.InvariantCulture);
            var sb = new StringBuilder();
            foreach (string w in words)
            {
                string clean = Regex.Replace(w, @"[^\p{L}\p{Nd}\-']", "");
                if (clean.Length == 0) continue;
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(clean);
            }
            return sb.Length > 0 ? sb.ToString() : null;
        }

        /// <summary>A biome name a voice can say, as VesselSpeakable (so also not in another alphabet, as with KSP in
        /// Chinese, Japanese or Russian). Null for a localization tag a planet pack left untranslated (#LOC_...).</summary>
        public static string BiomeSpeakable(string biomeName)
        {
            string n = CleanName(biomeName);
            if (string.IsNullOrEmpty(n) || n.StartsWith("#", StringComparison.Ordinal)) return null;
            return VesselSpeakable(n);
        }

        /// <summary>Every letter is Latin (Basic Latin up to Latin Extended-B, so accents pass).</summary>
        public static bool LatinOnly(string text)
        {
            foreach (char c in text ?? "")
                if (char.IsLetter(c) && c > '\u024F') return false;
            return true;
        }

        /// <summary>KSP display names can end in a gender tag (^N, ^f). Trimmed, the tag removed.</summary>
        public static string CleanName(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;
            string t = name.Trim();
            int caret = t.IndexOf('^');
            return caret > 0 ? t.Substring(0, caret).Trim() : t;
        }

        // ---------------- filling ----------------

        static readonly Regex Slot = new Regex(@"\{([a-z_]+)\}");

        /// <summary>
        /// Fills a template. nums: numeric values by slot name. names: name slots by slot name, each { spoken, english }
        /// (the spoken one goes into a raw mark, it is final). False when a slot has no value.
        /// spanish: the Piper template (numbers in words, outside the marks, so they are reversed), english: the subtitle.
        /// </summary>
        public static bool Fill(RuntimeTemplate t, Dictionary<string, double> nums, Dictionary<string, string[]> names,
                                out string spanish, out string english)
        {
            return Fill(t, 0, nums, names, out spanish, out english);
        }

        /// <summary>Fill with one version of the template (an index into Versions).</summary>
        public static bool Fill(RuntimeTemplate t, int version, Dictionary<string, double> nums, Dictionary<string, string[]> names,
                                out string spanish, out string english)
        {
            spanish = null; english = null;
            if (t == null || version < 0 || version >= t.Versions.Count) return false;
            return FillText(t.Versions[version][0], t.Versions[version][1], nums, names, out spanish, out english);
        }

        /// <summary>Fills a Spanish and an English text with the same values (also used for recorded lines with
        /// number slots). False when a slot has no value.</summary>
        public static bool FillText(string es, string en, Dictionary<string, double> nums, Dictionary<string, string[]> names,
                                    out string spanish, out string english)
        {
            spanish = null; english = null;
            bool ok = true;
            spanish = Slot.Replace(Singular(es ?? "", nums, SpanishPlurals), m =>
            {
                string k = m.Groups[1].Value;
                double v;
                string[] nm;
                if (nums != null && nums.TryGetValue(k, out v) && !double.IsNaN(v) && !double.IsInfinity(v)) return Words(k, v);
                if (names != null && names.TryGetValue(k, out nm) && nm != null && !string.IsNullOrEmpty(nm[0])) return RuntimeSpeech.MarkRaw(nm[0]);
                ok = false;
                return m.Value;
            });
            english = Slot.Replace(Singular(en ?? "", nums, EnglishPlurals), m =>
            {
                string k = m.Groups[1].Value;
                double v;
                string[] nm;
                if (nums != null && nums.TryGetValue(k, out v) && !double.IsNaN(v) && !double.IsInfinity(v)) return Digits(k, v);
                if (names != null && names.TryGetValue(k, out nm) && nm != null && nm.Length > 1 && !string.IsNullOrEmpty(nm[1])) return nm[1];
                ok = false;
                return m.Value;
            });
            if (!ok) { spanish = null; english = null; }
            return ok;
        }

        static readonly Dictionary<string, string> SpanishPlurals = new Dictionary<string, string> {
            { "kilómetros", "kilómetro" }, { "metros", "metro" }, { "minutos", "minuto" }, { "grados", "grado" },
            { "puntos", "punto" }, { "fondos", "fondo" }, { "piezas", "pieza" }, { "contratos", "contrato" }, { "kerbals", "kerbal" },
            { "vuelos", "vuelo" }, { "viajes", "viaje" }, { "aterrizajes", "aterrizaje" },
        };
        static readonly Dictionary<string, string> EnglishPlurals = new Dictionary<string, string> {
            { "meters", "meter" }, { "minutes", "minute" }, { "degrees", "degree" }, { "points", "point" }, { "funds", "fund" },
            { "parts", "part" }, { "contracts", "contract" }, { "kerbals", "kerbal" },
            { "flights", "flight" }, { "trips", "trip" }, { "landings", "landing" },
        };
        static readonly Regex SlotNoun = new Regex(@"\{([a-z_]+)\} (\p{L}+)");

        /// <summary>A value that is 1 (after rounding) takes the singular noun right after its slot: "un kilómetro",
        /// "1 minute". In Spanish a value in millions gets "de" before its noun. Applied to the template before it is filled.</summary>
        public static string Singular(string template, Dictionary<string, double> nums, bool spanish)
        {
            return Singular(template, nums, spanish ? SpanishPlurals : EnglishPlurals);
        }

        static string Singular(string template, Dictionary<string, double> nums, Dictionary<string, string> plurals)
        {
            if (nums == null || string.IsNullOrEmpty(template)) return template;
            bool spanish = plurals == SpanishPlurals;
            return SlotNoun.Replace(template, m =>
            {
                double v;
                string k = m.Groups[1].Value, noun = m.Groups[2].Value, one;
                if (!nums.TryGetValue(k, out v) || double.IsNaN(v)) return m.Value;
                if (Math.Abs(Rounded(k, v)) == 1 && plurals.TryGetValue(noun, out one)) return "{" + k + "} " + one;
                // "quince millones de kilómetros", not before "por ciento" or a word that is already "de"
                if (spanish && InMillions(k, v) && noun != "de" && noun != "por") return "{" + k + "} de " + noun;
                return m.Value;
            });
        }
    }
}
