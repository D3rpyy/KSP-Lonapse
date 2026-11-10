// Experimental (runtime voice): the pure part of speaking a line at runtime with Piper. Text: loñapsE with real
// names respelled for a Spanish voice. Sound: the Kerbal pitch shift and the same level shaping as
// audio/build_lonapse.py (trim, fade, pad, gated RMS target with a peak limiter), so a runtime line sounds like a
// pre-rendered one. Pure code: no Unity or KSP references, so it compiles under DC_OFFLINE_TEST.
using System;
using System.Collections.Generic;
using System.Text;

namespace Lonapse
{
    public static class RuntimeSpeech
    {
        /// <summary>Marks a name in a Spanish template: "‹Jebediah›, aquí Control." The name is spoken as itself,
        /// the rest becomes loñapsE.</summary>
        public const char NameOpen = '‹', NameClose = '›';

        public static string MarkName(string name) { return NameOpen + (name ?? "") + NameClose; }

        /// <summary>Marks a name that is already spelled for the voice (a stock body from bodies.tsv, a respelled
        /// name): spoken as it is, not reversed and not respelled again.</summary>
        public const char RawOpen = '⟪', RawClose = '⟫';

        public static string MarkRaw(string name) { return RawOpen + (name ?? "") + RawClose; }

        /// <summary>The template with the marks removed (the Spanish as text, for the table entry).</summary>
        public static string Unmarked(string template)
        {
            if (string.IsNullOrEmpty(template)) return template ?? "";
            var sb = new StringBuilder(template.Length);
            foreach (char c in template) if (c != NameOpen && c != NameClose && c != RawOpen && c != RawClose) sb.Append(c);
            return sb.ToString();
        }

        /// <summary>
        /// The text Piper reads: every word outside the marks reversed (LonapseText.ReverseText), every name
        /// respelled for a Spanish voice (Respell) and not reversed, a raw mark as it is. The marks are removed.
        /// </summary>
        public static string Spoken(string template)
        {
            if (string.IsNullOrEmpty(template)) return "";
            var sb = new StringBuilder(template.Length + 8);
            int i = 0;
            while (i < template.Length)
            {
                int a = template.IndexOf(NameOpen, i), b = template.IndexOf(RawOpen, i);
                int open = a < 0 ? b : (b < 0 ? a : Math.Min(a, b));
                if (open < 0) { sb.Append(LonapseText.ReverseText(template.Substring(i))); break; }
                if (open > i) sb.Append(LonapseText.ReverseText(template.Substring(i, open - i)));
                bool raw = template[open] == RawOpen;
                int close = template.IndexOf(raw ? RawClose : NameClose, open + 1);
                if (close < 0) close = template.Length;
                string inner = template.Substring(open + 1, close - open - 1);
                sb.Append(raw ? inner : Respell(inner));
                i = close + 1;
            }
            return sb.ToString();
        }

        /// <summary>
        /// A generic {body} line ("el planeta", "the planet", see bodies.tsv) with a real body name instead, for a
        /// body without its own clip (a modded planet). spanishName goes in as it is (put it in a mark), englishName
        /// replaces "the planet" (a capital when it starts the sentence stays a capital name anyway). False unless
        /// both texts hold the generic words exactly once.
        /// </summary>
        public static bool SwapBody(string spanish, string english, string spanishName, string englishName,
                                    out string spanishOut, out string englishOut)
        {
            spanishOut = ReplaceOnce(spanish, "el planeta", spanishName);
            englishOut = ReplaceOnce(english, "the planet", englishName);
            if (spanishOut == null || englishOut == null) { spanishOut = null; englishOut = null; return false; }
            return true;
        }

        static string ReplaceOnce(string text, string what, string with)
        {
            if (string.IsNullOrEmpty(text)) return null;
            int i = text.IndexOf(what, StringComparison.OrdinalIgnoreCase);
            if (i < 0 || text.IndexOf(what, i + what.Length, StringComparison.OrdinalIgnoreCase) >= 0) return null;
            // whole words only ("el planetario" is not the planet)
            if (i > 0 && char.IsLetter(text[i - 1])) return null;
            int end = i + what.Length;
            if (end < text.Length && char.IsLetter(text[end])) return null;
            return text.Substring(0, i) + with + text.Substring(end);
        }

        static bool IsVowel(char c) { return "aeiouáéíóú".IndexOf(c) >= 0; }

        /// <summary>
        /// An English, Russian or Chinese (KSP) name spelled so a Spanish voice says it about right: "Jebediah Kerman" becomes
        /// "Yebedia Kerman", "Gilly" "Guili", "Союз" "Soyus". Only letters change, word by word. Each word keeps a
        /// capital only at its start (espeak-ng reads a capital inside a word as a separate letter).
        /// Han characters use bundled Mandarin readings, with one Spanish syllable per character.
        /// </summary>
        public static string Respell(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            var sb = new StringBuilder(name.Length + 4);
            int i = 0;
            while (i < name.Length)
            {
                int code = char.IsHighSurrogate(name[i]) && i + 1 < name.Length && char.IsLowSurrogate(name[i + 1])
                           ? char.ConvertToUtf32(name[i], name[i + 1]) : name[i];
                string chinese = ChinesePronunciation.Spell(code);
                if (chinese != null)
                {
                    // Keep syllables and adjacent Latin words separate, so Spanish digraphs cannot form across them.
                    if (sb.Length > 0 && char.IsLetterOrDigit(sb[sb.Length - 1])) sb.Append(' ');
                    sb.Append(chinese);
                    i += code > 0xFFFF ? 2 : 1;
                    if (i < name.Length && (char.IsLetterOrDigit(name[i]) || char.IsHighSurrogate(name[i]))) sb.Append(' ');
                    continue;
                }
                if (!char.IsLetter(name[i])) { sb.Append(name[i]); i++; continue; }
                int j = i;
                while (j < name.Length && char.IsLetter(name[j]) && !ChinesePronunciation.IsHan(name[j])) j++;
                // An unknown Han character stays visible, so validation can reject unsupported names.
                if (j == i) { sb.Append(name[i]); i++; continue; }
                sb.Append(RespellWord(name.Substring(i, j - i)));
                i = j;
            }
            return sb.ToString();
        }

        static string RespellWord(string word)
        {
            string w = word.ToLowerInvariant();
            var o = new StringBuilder(w.Length + 2);
            for (int i = 0; i < w.Length; i++)
            {
                char c = w[i];
                char n = i + 1 < w.Length ? w[i + 1] : '\0';
                char p = o.Length > 0 ? o[o.Length - 1] : '\0';
                // These are final Spanish spellings. Do not feed them through the English rules below
                // (which would turn y into i, or drop an h). Sounds absent from Spanish stay approximate.
                string russian = RussianSpelling(c, n, i == 0 || "аеёиоуыэюяьъй".IndexOf(w[i - 1]) >= 0);
                if (russian != null)
                {
                    // Spanish ll is a different sound, just as with Bill below.
                    if (c != 'л' || p != 'l') o.Append(russian);
                    continue;
                }
                if (c == 'e' && n == 'e') { o.Append('i'); i++; continue; }          // Eeloo, Jeeb: i
                if (c == 'o' && n == 'o') { o.Append('u'); i++; continue; }          // Jool, Eeloo: u
                if (c == 'p' && n == 'h') { o.Append('f'); i++; continue; }
                if (c == 't' && n == 'h') { o.Append('z'); i++; continue; }
                if (c == 's' && n == 'h') { o.Append("ch"); i++; continue; }
                if (c == 'c' && n == 'k') { o.Append('k'); i++; continue; }
                if (c == n && c != 'e' && c != 'o') { continue; }                    // Bill: Bil (Spanish ll is y)
                switch (c)
                {
                    case 'j': o.Append('y'); break;                                  // Jebediah: Yebedia
                    case 'w': o.Append('u'); break;
                    case 'y': o.Append(i == 0 && IsVowel(n) ? 'y' : 'i'); break;      // Gilly: Guili, Yuri: Yuri
                    case 'h':
                        // a spoken English h (at the start, between vowels) is the Spanish j. After a vowel it is silent.
                        if (i == 0 || (IsVowel(p) && IsVowel(n))) o.Append('j');
                        break;
                    case 'g': o.Append(n == 'e' || n == 'i' || n == 'y' ? "gu" : "g"); break;  // a hard g like Gilly
                    default: o.Append(c); break;
                }
            }
            if (o.Length == 0) return word;
            o[0] = char.ToUpperInvariant(o[0]);
            return o.ToString();
        }

        static string RussianSpelling(char c, char next, bool vowelStart)
        {
            switch (c)
            {
                case 'а': return "a";
                case 'б': return "b";
                case 'в': return "v";
                case 'г': return "еиэ".IndexOf(next) >= 0 ? "gu" : "g";
                case 'д': return "d";
                case 'е': return vowelStart ? "ye" : "e";
                case 'ё': return "yo";
                case 'ж': case 'ш': case 'щ': return "ch";
                case 'з': return "s";
                case 'и': case 'й': case 'ы': return "i";
                case 'к': return "k";
                case 'л': return "l";
                case 'м': return "m";
                case 'н': return "n";
                case 'о': return "o";
                case 'п': return "p";
                case 'р': return "r";
                case 'с': return "s";
                case 'т': return "t";
                case 'у': return "u";
                case 'ф': return "f";
                case 'х': return "j";
                case 'ц': return "ts";
                case 'ч': return "ch";
                case 'ъ': case 'ь': return "";
                case 'э': return "e";
                case 'ю': return "yu";
                case 'я': return "ya";
                default: return null;
            }
        }

        // a line that already ends on a radio word gets no name after it ("Copy, over, Jebediah.")
        static readonly string[] EndWords = { "over", "out", "cambio", "corto", "fuera" };

        /// <summary>
        /// The line with a name added at the end of its last sentence: "Good work." becomes "Good work, Jebediah.",
        /// "¿Todo bien?" becomes "¿Todo bien, Jebediah?". At the end, so no capital has to change. Null when the line
        /// does not take a name: empty, a slot, versions or quotes left, it ends on a symbol or a radio word, or the
        /// name is already in it.
        /// </summary>
        public static string AddName(string text, string name)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(name)) return null;
            string t = text.TrimEnd();
            if (t.IndexOfAny(new[] { '"', '{', '}', '|', '“', '”', '«', '»' }) >= 0) return null;
            string bare = name.Trim(NameOpen, NameClose, RawOpen, RawClose);
            if (bare.Length > 0 && t.IndexOf(bare, StringComparison.OrdinalIgnoreCase) >= 0) return null;
            int end = t.Length;
            while (end > 0 && ".!?…".IndexOf(t[end - 1]) >= 0) end--;
            if (end == 0 || !char.IsLetterOrDigit(t[end - 1])) return null;
            int w = end;
            while (w > 0 && char.IsLetter(t[w - 1])) w--;
            string last = t.Substring(w, end - w).ToLowerInvariant();
            if (Array.IndexOf(EndWords, last) >= 0) return null;
            return t.Substring(0, end) + ", " + name + t.Substring(end);
        }

        /// <summary>JSON string literal for Piper's --json-input (quotes, backslashes and control characters).</summary>
        public static string Json(string s)
        {
            var sb = new StringBuilder(s.Length + 2);
            sb.Append('"');
            foreach (char c in s)
            {
                if (c == '"') sb.Append("\\\"");
                else if (c == '\\') sb.Append("\\\\");
                else if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4"));
                else sb.Append(c);
            }
            return sb.Append('"').ToString();
        }

        // ------------------------------------------------------------------------------------------------------
        // Sound
        // ------------------------------------------------------------------------------------------------------
        public const int OutRate = 44100;
        const int Frame = 441;                // 10 ms
        const double SilenceDb = -50.0, RmsTargetDb = -20.0, PeakBuildDb = -1.9, FadeS = 0.008, PadS = 0.015;

        /// <summary>16-bit mono PCM of a WAV file as floats, and its rate. Null when it is not 16-bit PCM.</summary>
        public static float[] ReadWav(byte[] b, out int rate)
        {
            rate = 0;
            if (b == null || b.Length < 44 || Encoding.ASCII.GetString(b, 0, 4) != "RIFF") return null;
            int pos = 12, channels = 1, bits = 16;
            while (pos + 8 <= b.Length)
            {
                string id = Encoding.ASCII.GetString(b, pos, 4);
                int len = BitConverter.ToInt32(b, pos + 4);
                int body = pos + 8;
                if (id == "fmt " && body + 16 <= b.Length)
                {
                    channels = BitConverter.ToInt16(b, body + 2);
                    rate = BitConverter.ToInt32(b, body + 4);
                    bits = BitConverter.ToInt16(b, body + 14);
                }
                else if (id == "data")
                {
                    if (bits != 16 || channels < 1) return null;
                    int end = Math.Min(b.Length, body + Math.Max(0, len));
                    int n = (end - body) / (2 * channels);
                    var x = new float[n];
                    for (int i = 0; i < n; i++) x[i] = BitConverter.ToInt16(b, body + i * 2 * channels) / 32768f;
                    return x;
                }
                pos = body + Math.Max(0, len) + (len & 1);
            }
            return null;
        }

        /// <summary>
        /// The Kerbal shift of the build (ffmpeg asetrate=rate*ratio, aresample=44100): the samples are played
        /// ratio times faster, which raises pitch and tempo together, then resampled to 44.1 kHz (cubic Hermite).
        /// </summary>
        public static float[] Shift(float[] x, int rate, double ratio)
        {
            if (x == null || x.Length == 0 || rate <= 0 || ratio <= 0) return new float[0];
            double step = rate * ratio / OutRate;      // input samples per output sample
            int n = (int)Math.Floor((x.Length - 1) / step);
            var y = new float[Math.Max(0, n)];
            for (int k = 0; k < y.Length; k++)
            {
                double t = k * step;
                int i = (int)t;
                double f = t - i;
                double xm1 = x[Math.Max(0, i - 1)], x0 = x[i], x1 = x[Math.Min(x.Length - 1, i + 1)], x2 = x[Math.Min(x.Length - 1, i + 2)];
                double c1 = 0.5 * (x1 - xm1), c2 = xm1 - 2.5 * x0 + 2 * x1 - 0.5 * x2, c3 = 0.5 * (x2 - xm1) + 1.5 * (x0 - x1);
                y[k] = (float)(((c3 * f + c2) * f + c1) * f + x0);
            }
            return y;
        }

        static double Db(double v) { return 20.0 * Math.Log10(Math.Max(v, 1e-12)); }

        static double[] FrameDb(float[] x)
        {
            int nf = x.Length / Frame;
            var r = new double[nf];
            for (int f = 0; f < nf; f++)
            {
                double s = 0;
                for (int j = 0; j < Frame; j++) { double v = x[f * Frame + j]; s += v * v; }
                r[f] = Db(Math.Sqrt(s / Frame));
            }
            return r;
        }

        /// <summary>Gated RMS in dB: 10 ms frames above max(-50 dBFS, loudest frame - 30 dB), as gated_rms_db.</summary>
        public static double GatedRmsDb(float[] x)
        {
            int nf = x.Length / Frame;
            if (nf == 0) return -240.0;
            var p = new double[nf];
            double max = 0;
            for (int f = 0; f < nf; f++)
            {
                double s = 0;
                for (int j = 0; j < Frame; j++) { double v = x[f * Frame + j]; s += v * v; }
                p[f] = s / Frame;
                if (p[f] > max) max = p[f];
            }
            double gate = Math.Max(Math.Pow(10, SilenceDb / 10.0), max * 1e-3), sum = 0;
            int c = 0;
            for (int f = 0; f < nf; f++) if (p[f] >= gate) { sum += p[f]; c++; }
            return c > 0 ? 10.0 * Math.Log10(sum / c) : -240.0;
        }

        /// <summary>Block lookahead peak limiter (64-sample blocks, fast release), as limit() in the build.</summary>
        public static float[] Limit(float[] x, double ceilingDb)
        {
            double t = Math.Pow(10, ceilingDb / 20.0);
            const int B = 64;
            int nb = (x.Length + B - 1) / B;
            var req = new double[nb];
            for (int b = 0; b < nb; b++)
            {
                double pk = 0;
                for (int i = b * B; i < Math.Min(x.Length, (b + 1) * B); i++) pk = Math.Max(pk, Math.Abs(x[i]));
                req[b] = pk <= t ? 1.0 : t / pk;
            }
            var env = new double[nb];
            for (int k = 0; k < nb; k++) env[k] = Math.Min(req[Math.Max(0, k - 1)], Math.Min(req[k], req[Math.Min(nb - 1, k + 1)]));
            const double rel = 0.02;
            for (int k = 1; k < nb; k++) env[k] = Math.Min(env[k], env[k - 1] + rel);
            for (int k = nb - 2; k >= 0; k--) env[k] = Math.Min(env[k], env[k + 1] + rel);
            var y = new float[x.Length];
            for (int k = 0; k < nb; k++)
            {
                double g0 = env[k], g1 = Math.Min(k + 1 < nb ? env[k + 1] : env[k], req[k]);
                for (int j = 0; j < B; j++)
                {
                    int i = k * B + j;
                    if (i >= x.Length) break;
                    double v = x[i] * (g0 + (g1 - g0) * j / B);
                    y[i] = (float)Math.Max(-t, Math.Min(t, v));
                }
            }
            return y;
        }

        /// <summary>
        /// shape() of the build: trim the quiet ends (30 dB under the loudest frame), 8 ms fades, 15 ms silence on
        /// both ends, gain to -20 dB gated RMS with the peak under -1.9 dBFS, then cut long quiet edges. Null when
        /// there is no speech.
        /// </summary>
        public static float[] Shape(float[] x)
        {
            if (x == null) return null;
            double[] fdb = FrameDb(x);
            if (fdb.Length == 0) return null;
            double loud = double.MinValue;
            foreach (double d in fdb) loud = Math.Max(loud, d);
            double cut = Math.Max(SilenceDb + 5.0, loud - 30.0);
            int a = -1, z = -1;
            for (int f = 0; f < fdb.Length; f++) if (fdb[f] > cut) { if (a < 0) a = f; z = f; }
            if (a < 0) return null;
            int len = (z - a + 1) * Frame;
            int fade = (int)Math.Round(FadeS * OutRate), pad = (int)Math.Round(PadS * OutRate);
            var s = new float[len + 2 * pad];
            Array.Copy(x, a * Frame, s, pad, len);
            for (int i = 0; i < Math.Min(fade, len); i++)
            {
                float g = i / (float)fade;
                s[pad + i] *= g;
                s[pad + len - 1 - i] *= g;
            }
            double gDb = RmsTargetDb - GatedRmsDb(s);
            float[] y = s;
            for (int it = 0; it < 12; it++)
            {
                float g = (float)Math.Pow(10, gDb / 20.0);
                var t = new float[s.Length];
                for (int i = 0; i < s.Length; i++) t[i] = s[i] * g;
                y = Limit(t, PeakBuildDb);
                double err = RmsTargetDb - GatedRmsDb(y);
                if (Math.Abs(err) < 0.2) break;
                gDb += err;
            }
            return EdgeTrim(y, 3);
        }

        static float[] EdgeTrim(float[] y, int keep)
        {
            double[] fdb = FrameDb(y);
            int nf = fdb.Length;
            if (nf <= 2 * keep) return y;
            int lead = 0, trail = 0;
            while (lead < nf && fdb[lead] <= SilenceDb) lead++;
            while (trail < nf && fdb[nf - 1 - trail] <= SilenceDb) trail++;
            if (lead >= nf) return y;
            int start = Math.Max(0, lead - keep) * Frame, end = y.Length - Math.Max(0, trail - keep) * Frame;
            var r = new float[end - start];
            Array.Copy(y, start, r, 0, r.Length);
            return r;
        }

        /// <summary>The whole chain for one Piper WAV: read, Kerbal shift, shape. Null when it fails.</summary>
        public static float[] FromPiperWav(byte[] wav, double pitchSemitones)
        {
            int rate;
            float[] x = ReadWav(wav, out rate);
            if (x == null || rate <= 0) return null;
            return Shape(Shift(x, rate, Math.Pow(2.0, pitchSemitones / 12.0)));
        }
    }

    /// <summary>Experimental (runtime voice): how one speaker kind sounds, from DELIVERY and BAND_DELIVERY in
    /// audio/build_lonapse.py for the calm band. Piper renders slower by the pitch ratio, so after the shift the
    /// tempo is the natural one times Tempo.</summary>
    public sealed class RuntimeDelivery
    {
        public string Kind, Voice;
        public double PitchSt, Tempo = 1.0, NoiseScale = 0.667, NoiseW = 0.8;

        public double LengthScale { get { return Math.Pow(2.0, PitchSt / 12.0) / Tempo; } }

        /// <summary>DELIVERY in build_lonapse.py: the voice and the Kerbal shift per speaker.</summary>
        public static readonly RuntimeDelivery[] All = {
            new RuntimeDelivery { Kind = "m", Voice = "es_ES-davefx-medium", PitchSt = 5.0 },
            new RuntimeDelivery { Kind = "f", Voice = "es_MX-claude-high", PitchSt = 4.0 },
            new RuntimeDelivery { Kind = "mc", Voice = "es_MX-ald-medium", PitchSt = 3.0 },
        };

        // BAND_DELIVERY, MC_LIVELY_* and PANIC_NOISE_* in build_lonapse.py: a runtime line must sound like the
        // recorded lines of the same band. Tempo factor and extra semitones per delivery band.
        static readonly Dictionary<string, double[]> Bands = new Dictionary<string, double[]> {
            { "calm", new[] { 1.00, 0.0 } }, { "tense", new[] { 1.10, 0.0 } }, { "panic", new[] { 1.10, 0.0 } },
            { "mc_panic", new[] { 0.95, 0.3 } }, { "callout", new[] { 1.08, 0.5 } },
        };
        public const double McLivelyTempo = 1.04, McLivelyNoise = 0.80, McLivelyNoiseW = 0.90;
        public const double PanicNoise = 0.90, PanicNoiseW = 1.10;

        /// <summary>
        /// The kind for one line, like delivery() in build_lonapse.py: "m", "f" or "mc" with the delivery band after a
        /// dot ("m.tense", "mc.mc_panic", "f.callout"). Calm and none give the plain kind.
        /// The countdown has fixed recordings and does not request runtime synthesis.
        /// </summary>
        public static string KindFor(string speaker, string band, string severity, string ev)
        {
            if (ev == "countdown") return null;
            bool mc = speaker == "mc";
            string b;
            if (severity == "callout") b = "callout";
            else if (band == "panic" && mc) b = "mc_panic";
            else if (band == "tense" || band == "panic") b = band;
            else b = "calm";
            return b == "calm" ? speaker : speaker + "." + b;
        }

        /// <summary>The base kind of a kind: "m.tense" gives "m".</summary>
        public static string BaseKind(string kind)
        {
            if (kind == null) return null;
            int dot = kind.IndexOf('.');
            return dot < 0 ? kind : kind.Substring(0, dot);
        }

        /// <summary>The delivery of a kind (see KindFor). Null for an unknown kind.</summary>
        public static RuntimeDelivery For(string kind)
        {
            string b = BaseKind(kind);
            RuntimeDelivery basis = null;
            foreach (RuntimeDelivery d in All) if (d.Kind == b) basis = d;
            if (basis == null) return null;
            string band = b == kind ? "calm" : kind.Substring(b.Length + 1);
            double[] bd;
            if (!Bands.TryGetValue(band, out bd)) return null;
            bool mc = b == "mc";
            var r = new RuntimeDelivery { Kind = kind, Voice = basis.Voice, PitchSt = basis.PitchSt + bd[1], Tempo = bd[0] };
            if (mc && band != "mc_panic") { r.Tempo *= McLivelyTempo; r.NoiseScale = McLivelyNoise; r.NoiseW = McLivelyNoiseW; }
            else if (!mc && band == "panic") { r.NoiseScale = PanicNoise; r.NoiseW = PanicNoiseW; }
            return r;
        }
    }
}
