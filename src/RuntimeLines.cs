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
                                                  "contract_funds", "contract_done", "contract_failed" };
        public static readonly string[] Situations = { "orbit", "flight", "suborbital", "landed", "escape", "other" };
        public static readonly string[] NumberSlots = { "alt_km", "pe_km", "ap_km", "speed", "fuel", "day", "sci", "funds",
            "inc_deg", "period_min", "t_ap_min", "t_pe_min", "ec", "mono", "parts", "crew", "node_dv", "node_min",
            "funds_total", "sci_total", "rep", "contracts", "dist_m", "close_ms", "height_m", "vspeed_ms" };
        public static readonly string[] NameSlots = { "vessel", "body", "exp", "target" };

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
            return r;
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

        /// <summary>Digits for the English subtitle and the value that is spoken, rounded the same way: one decimal below
        /// 10 for keys with decimals (km, min, deg, ms, science points), else whole numbers. 1234 gives "1,234".</summary>
        public static double Rounded(string key, double v)
        {
            if (HasDecimals(key) && Math.Abs(v) < 10) return Math.Round(v, 1, MidpointRounding.AwayFromZero);
            return Math.Round(v, 0, MidpointRounding.AwayFromZero);
        }

        static bool HasDecimals(string key)
        {
            return key.EndsWith("_km", StringComparison.Ordinal) || key.EndsWith("_min", StringComparison.Ordinal)
                   || key.EndsWith("_deg", StringComparison.Ordinal) || key.EndsWith("_ms", StringComparison.Ordinal) || key == "sci";
        }

        public static string Digits(string key, double v)
        {
            double r = Rounded(key, v);
            return r == Math.Floor(r) ? r.ToString("#,0", CultureInfo.InvariantCulture) : r.ToString("0.0", CultureInfo.InvariantCulture);
        }

        /// <summary>The spoken Spanish of a value: 12.5 gives "doce coma cinco". The day is a count after "día", the
        /// other values come before a noun (apocope).</summary>
        public static string Words(string key, double v)
        {
            double r = Rounded(key, v);
            bool apo = key != "day";
            long whole = (long)Math.Floor(Math.Abs(r));
            int tenth = (int)Math.Round((Math.Abs(r) - whole) * 10);
            string w = Number(whole, apo && tenth == 0);
            if (tenth > 0) w += " coma " + Units[tenth];
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
        /// "1 minute". Applied to the template before it is filled.</summary>
        static string Singular(string template, Dictionary<string, double> nums, Dictionary<string, string> plurals)
        {
            if (nums == null || string.IsNullOrEmpty(template)) return template;
            return SlotNoun.Replace(template, m =>
            {
                double v;
                string k = m.Groups[1].Value, noun = m.Groups[2].Value, one;
                if (nums.TryGetValue(k, out v) && !double.IsNaN(v) && Math.Abs(Rounded(k, v)) == 1 && plurals.TryGetValue(noun, out one))
                    return "{" + k + "} " + one;
                return m.Value;
            });
        }
    }
}
