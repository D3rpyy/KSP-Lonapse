// Experimental (runtime voice): lines that only exist at runtime, because they hold real values. Mission Control
// reports (altitude, orbit, speed, fuel, mission day), science (experiment, body, science points) and contracts
// (funds). Also the Spanish words for numbers and the spoken names of bodies and vessels.
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
        public string Id;
        /// <summary>When it fits: orbit, flight (flying or sub-orbital), suborbital, landed, any, science, science_pts,
        /// contract_done, contract_funds, contract_failed.</summary>
        public string When;
        public string Spanish, English;

        public RuntimeTemplate(string id, string when, string spanish, string english)
        {
            Id = id; When = when; Spanish = spanish; English = english;
        }
    }

    public static class RuntimeLines
    {
        // Slots: {alt_km} {pe_km} {ap_km} {speed} {fuel} {day} {sci} {funds} numbers, {vessel} {body} {exp} names.
        public static readonly RuntimeTemplate[] Reports = {
            new RuntimeTemplate("rt_orbit_1", "orbit", "Órbita estable. Periapsis {pe_km} kilómetros, apoapsis {ap_km} kilómetros.",
                                "Stable orbit. Periapsis {pe_km} km, apoapsis {ap_km} km."),
            new RuntimeTemplate("rt_orbit_2", "orbit", "Os vemos a {alt_km} kilómetros sobre {body}.",
                                "We see you {alt_km} km above {body}."),
            new RuntimeTemplate("rt_orbit_3", "orbit", "Seguimos vuestra órbita. Vais a {speed} metros por segundo. Nada mal.",
                                "Tracking your orbit. You're doing {speed} m/s. Not bad."),
            new RuntimeTemplate("rt_flight_1", "flight", "Altitud {alt_km} kilómetros, velocidad {speed} metros por segundo. Todo bien.",
                                "Altitude {alt_km} km, speed {speed} m/s. All good."),
            new RuntimeTemplate("rt_sub_1", "suborbital", "Apoapsis a {ap_km} kilómetros. Todavía no es una órbita.",
                                "Apoapsis at {ap_km} km. Not an orbit yet."),
            new RuntimeTemplate("rt_landed_1", "landed", "Os tenemos en la superficie de {body}. Buen sitio.",
                                "We have you on the surface of {body}. Nice spot."),
            new RuntimeTemplate("rt_fuel_1", "fuel", "Según nuestras pantallas os queda el {fuel} por ciento de combustible.",
                                "Our screens say you have {fuel} percent fuel left."),
            new RuntimeTemplate("rt_fuel_low", "fuel_low", "Ojo con el combustible. Solo queda el {fuel} por ciento.",
                                "Watch the fuel. Only {fuel} percent left."),
            new RuntimeTemplate("rt_day_1", "any", "Día {day} de la misión. Aquí abajo todo en orden.",
                                "Mission day {day}. All fine down here."),
            new RuntimeTemplate("rt_vessel_1", "any", "{vessel}, aquí Control. Os seguimos en pantalla.",
                                "{vessel}, Control here. We have you on screen."),
        };

        public static readonly RuntimeTemplate[] Science = {
            new RuntimeTemplate("rt_sci_1", "science_pts", "Datos de {exp} recibidos desde {body}. Son {sci} puntos de ciencia.",
                                "Got the {exp} data from {body}. That's {sci} science points."),
            new RuntimeTemplate("rt_sci_2", "science_pts", "Los científicos dan las gracias por los datos de {exp}. {sci} puntos.",
                                "The scientists say thanks for the {exp} data. {sci} points."),
            new RuntimeTemplate("rt_sci_3", "science", "Datos de {exp} recibidos desde {body}.",
                                "Got the {exp} data from {body}."),
        };

        public static readonly RuntimeTemplate[] Contracts = {
            new RuntimeTemplate("rt_con_1", "contract_funds", "Contrato cumplido. {funds} fondos para el programa.",
                                "Contract complete. {funds} funds for the program."),
            new RuntimeTemplate("rt_con_2", "contract_funds", "Buenas noticias de contabilidad: {funds} fondos más.",
                                "Good news from accounting: {funds} more funds."),
            new RuntimeTemplate("rt_con_3", "contract_done", "Contrato cumplido. Buen trabajo, equipo.",
                                "Contract complete. Good work, team."),
            new RuntimeTemplate("rt_con_fail", "contract_failed", "Hemos perdido un contrato. Contabilidad no está contenta.",
                                "We lost a contract. Accounting is not happy."),
        };

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
        /// 10 for keys with decimals (km, science points), else whole numbers. 1234 gives "1,234".</summary>
        public static double Rounded(string key, double v)
        {
            if (HasDecimals(key) && Math.Abs(v) < 10) return Math.Round(v, 1, MidpointRounding.AwayFromZero);
            return Math.Round(v, 0, MidpointRounding.AwayFromZero);
        }

        static bool HasDecimals(string key) { return key.EndsWith("_km", StringComparison.Ordinal) || key == "sci"; }

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
            spanish = Slot.Replace(t.Spanish, m =>
            {
                string k = m.Groups[1].Value;
                double v;
                string[] nm;
                if (nums != null && nums.TryGetValue(k, out v) && !double.IsNaN(v) && !double.IsInfinity(v)) return Words(k, v);
                if (names != null && names.TryGetValue(k, out nm) && nm != null && !string.IsNullOrEmpty(nm[0])) return RuntimeSpeech.MarkRaw(nm[0]);
                ok = false;
                return m.Value;
            });
            english = Slot.Replace(t.English, m =>
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

        /// <summary>The report templates that fit a situation (orbit, flight, suborbital, landed or other) and the values
        /// there are. Fuel templates only with a fuel value, the low one below 25 percent.</summary>
        public static List<RuntimeTemplate> ReportsFor(string situation, Dictionary<string, double> nums, Dictionary<string, string[]> names)
        {
            var r = new List<RuntimeTemplate>();
            double fuel = double.NaN;
            bool hasFuel = nums != null && nums.TryGetValue("fuel", out fuel) && !double.IsNaN(fuel);
            foreach (RuntimeTemplate t in Reports)
            {
                bool fits;
                switch (t.When)
                {
                    case "any": fits = true; break;
                    case "fuel": fits = hasFuel && fuel >= 25; break;
                    case "fuel_low": fits = hasFuel && fuel < 25 && fuel > 0; break;
                    case "flight": fits = situation == "flight" || situation == "suborbital"; break;
                    default: fits = t.When == situation; break;
                }
                string s, e;
                if (fits && Fill(t, nums, names, out s, out e)) r.Add(t);
            }
            return r;
        }
    }
}
