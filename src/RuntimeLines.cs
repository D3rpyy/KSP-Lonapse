// Experimental (runtime voice): lines that only exist at runtime, because they hold real values. The templates are in
// GameData/Lonapse/Phrases/runtime.tsv (reports, docking, landing, science, contracts). This file parses them, picks
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
        public string Id, Kind, Spanish, English;
        /// <summary>Situations of the "when" column (orbit flight suborbital landed escape), empty: any.</summary>
        public readonly List<string> Situations = new List<string>();
        /// <summary>Checks of the "when" column: value key, operator (&lt; &lt;= &gt; &gt;= =), number.</summary>
        public readonly List<KeyValuePair<string, KeyValuePair<string, double>>> Checks =
            new List<KeyValuePair<string, KeyValuePair<string, double>>>();

        public RuntimeTemplate(string id, string kind, string spanish, string english)
        {
            Id = id; Kind = kind; Spanish = spanish; English = english;
        }
    }

    public static class RuntimeLines
    {
        public const string FileRel = "GameData/Lonapse/Phrases/runtime.tsv";
        public static readonly string[] Kinds = { "report", "dock", "land", "science", "science_pts",
                                                  "contract_funds", "contract_done", "contract_failed", "chat" };
        public static readonly string[] Situations = { "orbit", "flight", "suborbital", "landed", "escape", "other" };
        public static readonly string[] NumberSlots = { "alt_km", "pe_km", "ap_km", "speed", "fuel", "day", "sci", "funds",
            "inc_deg", "period_min", "t_ap_min", "t_pe_min", "ec", "mono", "parts", "crew", "node_dv", "node_min",
            "funds_total", "sci_total", "rep", "contracts", "dist_m", "close_ms", "height_m", "vspeed_ms" };
        public static readonly string[] NameSlots = { "vessel", "body", "exp", "target", "self", "other" };
        /// <summary>A chat line: rt_chat_&lt;name&gt;_&lt;seq&gt;. The lines of one name form one chat, seq from 1.</summary>
        static readonly Regex ChatId = new Regex(@"^(rt_chat_[a-z0-9_]+?)_([0-9]+)$");

        static readonly Regex Check = new Regex(@"^([a-z_]+)(<=|>=|<|>|=)(-?[0-9]+(?:\.[0-9]+)?)$");
        static readonly Regex IdRe = new Regex(@"^rt_[a-z0-9_]+$");

        /// <summary>Parses runtime.tsv: header "id kind when spanish english", # comments. Bad rows go to errors and
        /// are skipped.</summary>
        public static List<RuntimeTemplate> Parse(IList<string> lines, List<string> errors)
        {
            var r = new List<RuntimeTemplate>();
            var seen = new HashSet<string>();
            bool header = false;
            for (int i = 0; i < lines.Count; i++)
            {
                string line = lines[i];
                if (line == null || line.Trim().Length == 0 || line.TrimStart().StartsWith("#", StringComparison.Ordinal)) continue;
                string[] f = line.Split('\t');
                if (!header)
                {
                    header = true;
                    if (f.Length != 5 || f[0] != "id" || f[1] != "kind" || f[2] != "when" || f[3] != "spanish" || f[4] != "english")
                    {
                        if (errors != null) errors.Add("runtime line " + (i + 1) + ": header must be id, kind, when, spanish, english");
                        return r;
                    }
                    continue;
                }
                var bad = new List<string>();
                if (f.Length != 5) { if (errors != null) errors.Add("runtime line " + (i + 1) + ": expected 5 columns, got " + f.Length); continue; }
                var t = new RuntimeTemplate(f[0].Trim(), f[1].Trim(), f[3].Trim(), f[4].Trim());
                if (!IdRe.IsMatch(t.Id)) bad.Add("id '" + t.Id + "' must match rt_[a-z0-9_]+");
                else if (!seen.Add(t.Id)) bad.Add("duplicate id " + t.Id);
                if (Array.IndexOf(Kinds, t.Kind) < 0) bad.Add("unknown kind '" + t.Kind + "'");
                foreach (string tok in f[2].Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (tok == "any") continue;
                    if (Array.IndexOf(Situations, tok) >= 0) { t.Situations.Add(tok); continue; }
                    Match m = Check.Match(tok);
                    if (m.Success && Array.IndexOf(NumberSlots, m.Groups[1].Value) >= 0)
                    {
                        t.Checks.Add(new KeyValuePair<string, KeyValuePair<string, double>>(m.Groups[1].Value,
                            new KeyValuePair<string, double>(m.Groups[2].Value, double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture))));
                        continue;
                    }
                    bad.Add("bad condition '" + tok + "'");
                }
                if (t.Spanish.Length == 0 || t.English.Length == 0) bad.Add("spanish and english must not be empty");
                foreach (string text in new[] { t.Spanish, t.English })
                    foreach (Match m in Slot.Matches(text))
                        if (Array.IndexOf(NumberSlots, m.Groups[1].Value) < 0 && Array.IndexOf(NameSlots, m.Groups[1].Value) < 0)
                            bad.Add("unknown slot {" + m.Groups[1].Value + "}");
                if (SlotSet(t.Spanish) != SlotSet(t.English)) bad.Add("spanish and english must use the same slots");
                if (bad.Count > 0) { if (errors != null) errors.Add("runtime line " + (i + 1) + " (" + t.Id + "): " + string.Join("; ", bad.ToArray())); continue; }
                r.Add(t);
            }
            if (!header && errors != null) errors.Add("runtime: no header");
            // chats: ids rt_chat_<name>_<seq>, seq 1, 2, 3 ... without gaps, at least 2 lines, conditions on line 1 only
            var groups = new Dictionary<string, List<RuntimeTemplate>>();
            foreach (RuntimeTemplate t in r)
            {
                if (t.Kind != "chat") continue;
                Match m = ChatId.Match(t.Id);
                if (!m.Success) { if (errors != null) errors.Add("runtime (" + t.Id + "): a chat id is rt_chat_<name>_<seq>"); continue; }
                List<RuntimeTemplate> g;
                if (!groups.TryGetValue(m.Groups[1].Value, out g)) groups[m.Groups[1].Value] = g = new List<RuntimeTemplate>();
                g.Add(t);
            }
            var drop = new HashSet<RuntimeTemplate>();
            foreach (var kv in groups)
            {
                kv.Value.Sort((x, y) => ChatSeq(x).CompareTo(ChatSeq(y)));
                string why = null;
                if (kv.Value.Count < 2) why = "a chat needs at least 2 lines";
                for (int i = 0; i < kv.Value.Count && why == null; i++)
                {
                    if (ChatSeq(kv.Value[i]) != i + 1) why = "seq must go 1, 2, 3 without gaps";
                    else if (i > 0 && (kv.Value[i].Checks.Count > 0 || kv.Value[i].Situations.Count > 0)) why = "only line 1 has a when";
                }
                if (why != null) { if (errors != null) errors.Add("runtime chat " + kv.Key + ": " + why); foreach (var t in kv.Value) drop.Add(t); }
            }
            foreach (RuntimeTemplate t in r)
                if (t.Kind == "chat" && !ChatId.IsMatch(t.Id)) drop.Add(t);
            r.RemoveAll(t => drop.Contains(t));
            return r;
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

        /// <summary>Line 1 of a chat fits (situation and checks), and every line fills with the names of its speaker:
        /// line i is spoken by A when i is even. namesA and namesB hold self and other for each speaker.</summary>
        public static bool ChatFits(List<RuntimeTemplate> chat, string situation, Dictionary<string, double> nums,
                                    Dictionary<string, string[]> namesA, Dictionary<string, string[]> namesB)
        {
            if (chat == null || chat.Count == 0) return false;
            var head = new List<RuntimeTemplate> { new RuntimeTemplate(chat[0].Id, "chat", chat[0].Spanish, chat[0].English) };
            head[0].Situations.AddRange(chat[0].Situations);
            head[0].Checks.AddRange(chat[0].Checks);
            if (Fitting(head, "chat", situation, nums, namesA).Count == 0) return false;
            for (int i = 1; i < chat.Count; i++)
            {
                string s, e;
                if (!Fill(chat[i], nums, i % 2 == 0 ? namesA : namesB, out s, out e)) return false;
            }
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
        /// holds and every slot has a value.</summary>
        public static List<RuntimeTemplate> Fitting(IList<RuntimeTemplate> all, string kind, string situation,
                                                    Dictionary<string, double> nums, Dictionary<string, string[]> names)
        {
            var r = new List<RuntimeTemplate>();
            if (all == null) return r;
            foreach (RuntimeTemplate t in all)
            {
                if (t.Kind != kind) continue;
                if (t.Situations.Count > 0 && !t.Situations.Contains(situation ?? "")) continue;
                bool ok = true;
                foreach (var c in t.Checks)
                {
                    double v;
                    if (nums == null || !nums.TryGetValue(c.Key, out v) || double.IsNaN(v)) { ok = false; break; }
                    double rv = Rounded(c.Key, v), n = c.Value.Value;
                    switch (c.Value.Key)
                    {
                        case "<": ok = rv < n; break;
                        case "<=": ok = rv <= n; break;
                        case ">": ok = rv > n; break;
                        case ">=": ok = rv >= n; break;
                        default: ok = rv == n; break;
                    }
                    if (!ok) break;
                }
                string s, e;
                if (ok && Fill(t, nums, names, out s, out e)) r.Add(t);
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

        static bool HasDecimals(string key)
        {
            return key.EndsWith("_km", StringComparison.Ordinal) || key.EndsWith("_min", StringComparison.Ordinal)
                   || key.EndsWith("_deg", StringComparison.Ordinal) || key.EndsWith("_ms", StringComparison.Ordinal) || key == "sci";
        }

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
        /// the voice), " Debris" and brackets go. Null for a name too long to say in a line (over 4 words or 28 letters).</summary>
        public static string VesselSpeakable(string vesselName)
        {
            string n = CleanName(vesselName);
            if (string.IsNullOrEmpty(n)) return null;
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
            spanish = null; english = null;
            if (t == null) return false;
            bool ok = true;
            spanish = Slot.Replace(Singular(t.Spanish, nums, SpanishPlurals), m =>
            {
                string k = m.Groups[1].Value;
                double v;
                string[] nm;
                if (nums != null && nums.TryGetValue(k, out v) && !double.IsNaN(v) && !double.IsInfinity(v)) return Words(k, v);
                if (names != null && names.TryGetValue(k, out nm) && nm != null && !string.IsNullOrEmpty(nm[0])) return RuntimeSpeech.MarkRaw(nm[0]);
                ok = false;
                return m.Value;
            });
            english = Slot.Replace(Singular(t.English, nums, EnglishPlurals), m =>
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
        };
        static readonly Dictionary<string, string> EnglishPlurals = new Dictionary<string, string> {
            { "meters", "meter" }, { "minutes", "minute" }, { "degrees", "degree" }, { "points", "point" }, { "funds", "fund" },
            { "parts", "part" }, { "contracts", "contract" }, { "kerbals", "kerbal" },
        };
        static readonly Regex SlotNoun = new Regex(@"\{([a-z_]+)\} (\p{L}+)");

        /// <summary>A value that is 1 (after rounding) takes the singular noun right after its slot: "un kilómetro",
        /// "1 minute". In Spanish a value in millions gets "de" before its noun. Applied to the template before it is filled.</summary>
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
