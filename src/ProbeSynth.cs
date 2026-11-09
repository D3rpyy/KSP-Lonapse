// Probe beep synth: a port of make_probe_beep and normalize from audio/build_clips.py, so probe voices can be
// made in game from a few values (base pitch, wave, rhythm, glide and more) instead of prebuilt WAV files.
// Speed, vibrato, buzz and spread on top of the Python values, and mood patterns of a voice's own.
// Pure code: no Unity or KSP references, so it compiles under DC_OFFLINE_TEST.
// tests/probe_reference.tsv (from audio/probe_reference.py) checks that both give the same samples.
using System;
using System.Collections.Generic;
using System.Globalization;

namespace Lonapse
{
    /// <summary>The timbre of one probe voice. Same fields as audio/probe/cores.tsv.</summary>
    public sealed class ProbeVoiceSpec
    {
        public string Name = ProbeCores.Generic;
        public string DisplayName = "Probe";
        /// <summary>The stock core whose phrase table lines this voice uses (the table's core column).</summary>
        public string Lines = ProbeCores.Generic;
        public double BaseHz = 940.0;
        public string Wave = "pulse";
        public string Rhythm = "even";
        public double Glide = 0.15;
        // extras (not in cores.tsv). The defaults change nothing, so stock voices sound as built by Python.
        public double Speed = 1.0;     // 0.5 .. 2: notes and gaps this many times faster
        public double Vibrato = 0.0;   // 0 .. 1 semitone of 6 Hz vibrato on every note
        public double Buzz = 0.0;      // 0 .. 1 grit: soft drive and a 38 Hz buzz (the offended mood uses 0.55)
        public double Spread = 1.0;    // 0.25 .. 2: the jumps between notes this many times wider
        /// <summary>Mood patterns of this voice, by mood name. A mood that is not here uses the stock pattern.</summary>
        public Dictionary<string, ProbeMoodSpec> Moods = new Dictionary<string, ProbeMoodSpec>();

        public const double MinHz = 200.0, MaxHz = 3000.0;
        public const double MinSpeed = 0.5, MaxSpeed = 2.0, MaxVibrato = 1.0, MaxBuzz = 1.0, MinSpread = 0.25, MaxSpread = 2.0;
        public static readonly string[] Waves = { "sine", "triangle", "square", "saw", "pulse", "fm" };
        public static readonly string[] Rhythms = { "even", "swing", "stutter", "lazy" };

        public ProbeVoiceSpec() { }

        public ProbeVoiceSpec(string name, double baseHz, string wave, string rhythm, double glide)
        {
            Name = name; BaseHz = baseHz; Wave = wave; Rhythm = rhythm; Glide = glide;
        }

        /// <summary>
        /// Voice from key/value pairs (a LONAPSE_PROBE_VOICE node or a settings entry). Keys: name, displayName, lines,
        /// baseHz, wave, rhythm, glide. A missing or bad value keeps the default and adds a message to
        /// <paramref name="errors"/>. A voice without a name is rejected (null).
        /// </summary>
        public static ProbeVoiceSpec FromValues(IDictionary<string, string> values, List<string> errors)
        {
            ProbeVoiceSpec v = new ProbeVoiceSpec();
            string s;
            if (values == null || !values.TryGetValue("name", out s) || string.IsNullOrEmpty(s = s.Trim()))
            {
                if (errors != null) errors.Add("probe voice without a name");
                return null;
            }
            v.Name = s.ToLowerInvariant();
            // a voice that replaces a stock voice keeps that core's name and lines unless it says otherwise
            bool stockName = Array.IndexOf(ProbeCores.Keys, v.Name) >= 0;
            if (stockName) v.Lines = ProbeCores.DefaultLines(v.Name);
            v.DisplayName = values.TryGetValue("displayName", out s) && !string.IsNullOrEmpty(s.Trim()) ? s.Trim()
                          : stockName ? ProbeCores.DisplayName(v.Name) : "Probe";
            if (values.TryGetValue("lines", out s))
            {
                string l = s.Trim().ToLowerInvariant();
                if (ProbeCores.IsLinesKey(l)) v.Lines = l;
                else if (errors != null) errors.Add(v.Name + ": lines '" + s + "' is not a stock core or a registered lineset, using generic");
            }
            if (values.TryGetValue("baseHz", out s))
            {
                double hz;
                if (double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out hz) && hz >= MinHz && hz <= MaxHz) v.BaseHz = hz;
                else if (errors != null) errors.Add(v.Name + ": baseHz '" + s + "' must be " + MinHz + " to " + MaxHz);
            }
            if (values.TryGetValue("wave", out s))
            {
                string w = s.Trim().ToLowerInvariant();
                if (Array.IndexOf(Waves, w) >= 0) v.Wave = w;
                else if (errors != null) errors.Add(v.Name + ": wave '" + s + "' must be one of " + string.Join(" ", Waves));
            }
            if (values.TryGetValue("rhythm", out s))
            {
                string r = s.Trim().ToLowerInvariant();
                if (Array.IndexOf(Rhythms, r) >= 0) v.Rhythm = r;
                else if (errors != null) errors.Add(v.Name + ": rhythm '" + s + "' must be one of " + string.Join(" ", Rhythms));
            }
            if (values.TryGetValue("glide", out s))
            {
                double g;
                if (double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out g) && g >= 0.0 && g <= 1.0) v.Glide = g;
                else if (errors != null) errors.Add(v.Name + ": glide '" + s + "' must be 0 to 1");
            }
            Num(values, "speed", MinSpeed, MaxSpeed, ref v.Speed, v.Name, errors);
            Num(values, "vibrato", 0.0, MaxVibrato, ref v.Vibrato, v.Name, errors);
            Num(values, "buzz", 0.0, MaxBuzz, ref v.Buzz, v.Name, errors);
            Num(values, "spread", MinSpread, MaxSpread, ref v.Spread, v.Name, errors);
            return v;
        }

        /// <summary>One optional number: kept when missing, reported and kept when bad or out of range.</summary>
        internal static void Num(IDictionary<string, string> values, string key, double min, double max, ref double field,
                                 string owner, List<string> errors)
        {
            string s;
            if (values == null || !values.TryGetValue(key, out s)) return;
            double d;
            if (double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out d) && d >= min && d <= max) field = d;
            else if (errors != null)
                errors.Add(owner + ": " + key + " '" + s + "' must be " + min.ToString(CultureInfo.InvariantCulture) + " to " + max.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>Adds or replaces a mood pattern.</summary>
        public void SetMood(ProbeMoodSpec m)
        {
            if (m != null) Moods[m.Name] = m;
        }

        /// <summary>The values that shape the sound. Two voices with the same signature sound the same.</summary>
        public string Signature()
        {
            string sig = R(BaseHz) + "|" + Wave + "|" + Rhythm + "|" + R(Glide) + "|" + R(Speed) + "|" + R(Vibrato) + "|" + R(Buzz) + "|" + R(Spread);
            if (Moods != null && Moods.Count > 0)
            {
                List<string> keys = new List<string>(Moods.Keys);
                keys.Sort(StringComparer.Ordinal);
                foreach (string k in keys) sig += "|" + Moods[k].Canonical();
            }
            return sig;
        }

        internal static string R(double d) { return d.ToString("R", CultureInfo.InvariantCulture); }

        public bool SameSound(ProbeVoiceSpec o) { return o != null && Signature() == o.Signature(); }

        public ProbeVoiceSpec Clone(string newName)
        {
            ProbeVoiceSpec c = new ProbeVoiceSpec(newName ?? Name, BaseHz, Wave, Rhythm, Glide)
            {
                DisplayName = DisplayName, Lines = Lines, Speed = Speed, Vibrato = Vibrato, Buzz = Buzz, Spread = Spread,
            };
            // mood patterns are never changed after parsing, so the copy can share them
            if (Moods != null) foreach (var kv in Moods) c.Moods[kv.Key] = kv.Value;
            return c;
        }

        /// <summary>Key/value pairs for a settings node. FromValues reads them back.</summary>
        public List<KeyValuePair<string, string>> ToValues()
        {
            return new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("name", Name),
                new KeyValuePair<string, string>("displayName", DisplayName),
                new KeyValuePair<string, string>("lines", Lines),
                new KeyValuePair<string, string>("baseHz", BaseHz.ToString("R", CultureInfo.InvariantCulture)),
                new KeyValuePair<string, string>("wave", Wave),
                new KeyValuePair<string, string>("rhythm", Rhythm),
                new KeyValuePair<string, string>("glide", Glide.ToString("R", CultureInfo.InvariantCulture)),
                new KeyValuePair<string, string>("speed", R(Speed)),
                new KeyValuePair<string, string>("vibrato", R(Vibrato)),
                new KeyValuePair<string, string>("buzz", R(Buzz)),
                new KeyValuePair<string, string>("spread", R(Spread)),
            };
        }

        /// <summary>The stock voices, one per ProbeCores.Keys value, same values as audio/probe/cores.tsv.</summary>
        public static ProbeVoiceSpec[] Stock()
        {
            return new[]
            {
                Named(new ProbeVoiceSpec(ProbeCores.Stayputnik, 1320, "sine", "even", 0.0)),
                Named(new ProbeVoiceSpec(ProbeCores.Okto, 880, "triangle", "swing", 0.35)),
                Named(new ProbeVoiceSpec(ProbeCores.Hecs, 700, "square", "stutter", 0.1)),
                Named(new ProbeVoiceSpec(ProbeCores.RoveMate, 520, "saw", "lazy", 0.6)),
                Named(new ProbeVoiceSpec(ProbeCores.Snob, 1050, "fm", "even", 0.2)),
                Named(new ProbeVoiceSpec(ProbeCores.Qbe, 1650, "triangle", "stutter", 0.05)),
                Named(new ProbeVoiceSpec(ProbeCores.Mk2Drone, 1180, "saw", "swing", 0.25)),
                Named(new ProbeVoiceSpec(ProbeCores.Mpo, 820, "sine", "swing", 0.45)),
                Named(new ProbeVoiceSpec(ProbeCores.Mtm, 440, "square", "lazy", 0.3)),
                Named(new ProbeVoiceSpec(ProbeCores.Generic, 940, "pulse", "even", 0.15)),
            };
        }

        private static ProbeVoiceSpec Named(ProbeVoiceSpec v)
        {
            v.DisplayName = ProbeCores.DisplayName(v.Name);
            v.Lines = ProbeCores.DefaultLines(v.Name);
            return v;
        }
    }

    /// <summary>One note of a beep phrase: pitch from st0 to st1 (semitones from the base pitch), length, pause after, level.</summary>
    public struct ProbeNote
    {
        public double st0, st1, dur, gap, amp;
        public ProbeNote(double a, double b, double d, double g, double amp) { st0 = a; st1 = b; dur = d; gap = g; this.amp = amp; }
    }

    /// <summary>
    /// A mood pattern of a voice's own (a MOOD node inside LONAPSE_PROBE_VOICE or PROBE_VOICE). Every notes value
    /// is one variant. With one notes value the three variants are that pattern at 0, +2 and -2 semitones, like the
    /// stock moods. A note is start[>end][:length][@level], for example 0>3:0.08@0.9. "rest:0.1" adds a pause.
    /// </summary>
    public sealed class ProbeMoodSpec
    {
        public string Name;
        public double Length = 0.1;      // note length in s when a note has none
        public double Gap = 0.04;        // pause after each note in s
        public double Vibrato = 0.0;     // semitones, on top of the voice vibrato
        public double Buzz = 0.0;        // grit, the larger of this and the voice buzz is used
        public bool UseRhythm = true;    // false: the voice rhythm does not change this pattern (like the scared trill)
        public readonly List<string> Notes = new List<string>();              // as written, one per variant
        public readonly List<List<ProbeNote>> Variants = new List<List<ProbeNote>>();

        public const int MaxNotes = 32, MaxVariants = 3;
        public const double MaxSemitones = 36.0, MinLength = 0.01, MaxLength = 1.0, MaxGap = 1.0;

        /// <summary>
        /// Mood from its values and notes lines. Returns null (with a message) for an unknown mood name or when no
        /// notes line could be read. A bad note is reported and left out.
        /// </summary>
        public static ProbeMoodSpec FromValues(IDictionary<string, string> values, IList<string> notes, string owner, List<string> errors)
        {
            string s;
            string who = (owner ?? "voice") + " MOOD";
            if (values == null || !values.TryGetValue("name", out s) || Array.IndexOf(ProbeCores.Moods, s.Trim().ToLowerInvariant()) < 0)
            {
                if (errors != null) errors.Add(who + ": name must be one of " + string.Join(" ", ProbeCores.Moods));
                return null;
            }
            ProbeMoodSpec m = new ProbeMoodSpec { Name = s.Trim().ToLowerInvariant() };
            who = (owner ?? "voice") + " " + m.Name;
            ProbeVoiceSpec.Num(values, "length", MinLength, MaxLength, ref m.Length, who, errors);
            ProbeVoiceSpec.Num(values, "gap", 0.0, MaxGap, ref m.Gap, who, errors);
            ProbeVoiceSpec.Num(values, "vibrato", 0.0, ProbeVoiceSpec.MaxVibrato, ref m.Vibrato, who, errors);
            ProbeVoiceSpec.Num(values, "buzz", 0.0, ProbeVoiceSpec.MaxBuzz, ref m.Buzz, who, errors);
            if (values.TryGetValue("rhythm", out s))
            {
                string r = s.Trim().ToLowerInvariant();
                if (r == "true" || r == "yes") m.UseRhythm = true;
                else if (r == "false" || r == "no") m.UseRhythm = false;
                else if (errors != null) errors.Add(who + ": rhythm '" + s + "' must be true or false");
            }
            if (notes != null)
                foreach (string line in notes)
                {
                    if (m.Variants.Count >= MaxVariants) { if (errors != null) errors.Add(who + ": more than " + MaxVariants + " notes lines, the rest is left out"); break; }
                    List<ProbeNote> list = m.ParseLine(line, who, errors);
                    if (list.Count == 0) continue;
                    m.Variants.Add(list);
                    m.Notes.Add(line.Trim());
                }
            if (m.Variants.Count == 0)
            {
                if (errors != null) errors.Add(who + ": no notes, the stock pattern stays");
                return null;
            }
            return m;
        }

        private List<ProbeNote> ParseLine(string line, string who, List<string> errors)
        {
            List<ProbeNote> r = new List<ProbeNote>();
            if (line == null) return r;
            foreach (string raw in line.Split(new[] { ' ', '\t', ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string tok = raw.Trim();
                if (tok.StartsWith("rest:", StringComparison.OrdinalIgnoreCase))
                {
                    double rest;
                    if (r.Count > 0 && Parse(tok.Substring(5), out rest) && rest >= 0.0 && rest <= MaxGap)
                    {
                        ProbeNote last = r[r.Count - 1];
                        last.gap += rest;
                        r[r.Count - 1] = last;
                    }
                    else if (errors != null) errors.Add(who + ": bad rest '" + tok + "' (it needs a note before it and 0 to " + MaxGap + " s)");
                    continue;
                }
                ProbeNote n;
                if (!ParseNote(tok, out n)) { if (errors != null) errors.Add(who + ": bad note '" + tok + "', use start>end:length@level"); continue; }
                if (r.Count >= MaxNotes) { if (errors != null) errors.Add(who + ": more than " + MaxNotes + " notes, the rest is left out"); break; }
                r.Add(n);
            }
            return r;
        }

        private bool ParseNote(string tok, out ProbeNote n)
        {
            n = new ProbeNote();
            double amp = 1.0, dur = Length;
            int at = tok.IndexOf('@');
            if (at >= 0)
            {
                if (!Parse(tok.Substring(at + 1), out amp) || amp < 0.0 || amp > 1.0) return false;
                tok = tok.Substring(0, at);
            }
            int colon = tok.IndexOf(':');
            if (colon >= 0)
            {
                if (!Parse(tok.Substring(colon + 1), out dur) || dur < MinLength || dur > MaxLength) return false;
                tok = tok.Substring(0, colon);
            }
            double a, b;
            int gt = tok.IndexOf('>');
            if (gt >= 0)
            {
                if (!Parse(tok.Substring(0, gt), out a) || !Parse(tok.Substring(gt + 1), out b)) return false;
            }
            else
            {
                if (!Parse(tok, out a)) return false;
                b = a;
            }
            if (Math.Abs(a) > MaxSemitones || Math.Abs(b) > MaxSemitones) return false;
            n = new ProbeNote(a, b, dur, Gap, amp);
            return true;
        }

        private static bool Parse(string s, out double d)
        {
            return double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out d)
                   && !double.IsNaN(d) && !double.IsInfinity(d);
        }

        /// <summary>Notes of a variant (0, 1, 2). The last note never has a pause after it.</summary>
        public List<ProbeNote> NotesFor(int variant)
        {
            List<ProbeNote> r = new List<ProbeNote>();
            if (Variants.Count == 0) return r;
            int v = ((variant % 3) + 3) % 3;
            double tr = 0.0;
            List<ProbeNote> src;
            if (Variants.Count == 1) { src = Variants[0]; tr = new[] { 0.0, 2.0, -2.0 }[v]; }
            else src = Variants[v % Variants.Count];
            for (int i = 0; i < src.Count; i++)
            {
                ProbeNote n = src[i];
                r.Add(new ProbeNote(n.st0 + tr, n.st1 + tr, n.dur, i == src.Count - 1 ? 0.0 : n.gap, n.amp));
            }
            return r;
        }

        /// <summary>Values for a settings node, without the notes lines (those are in Notes).</summary>
        public List<KeyValuePair<string, string>> ToValues()
        {
            return new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("name", Name),
                new KeyValuePair<string, string>("length", ProbeVoiceSpec.R(Length)),
                new KeyValuePair<string, string>("gap", ProbeVoiceSpec.R(Gap)),
                new KeyValuePair<string, string>("vibrato", ProbeVoiceSpec.R(Vibrato)),
                new KeyValuePair<string, string>("buzz", ProbeVoiceSpec.R(Buzz)),
                new KeyValuePair<string, string>("rhythm", UseRhythm ? "true" : "false"),
            };
        }

        /// <summary>Everything that changes the sound, for the voice signature.</summary>
        public string Canonical()
        {
            return Name + "{" + ProbeVoiceSpec.R(Length) + "," + ProbeVoiceSpec.R(Gap) + "," + ProbeVoiceSpec.R(Vibrato) + ","
                   + ProbeVoiceSpec.R(Buzz) + "," + (UseRhythm ? "r" : "n") + ":" + string.Join(";", Notes.ToArray()) + "}";
        }
    }

    /// <summary>
    /// Beep phrases per voice and mood. Every function mirrors the Python function of the same name in
    /// audio/build_clips.py, in the same order of operations, so the samples match to rounding.
    /// </summary>
    public static class ProbeSynth
    {
        public const int SampleRate = 44100;
        public const int VariantsPerMood = 3;           // FOLDERS count of every probe folder
        public const double TargetRmsDb = -22.0;        // FOLDERS rms of every probe folder
        public const double PeakLimitDb = -1.3;         // PEAK_LIMIT_DB
        private const int Frame = 441;                  // FRAME, 10 ms
        private const double SilenceDb = -50.0;         // SILENCE_DB

        /// <summary>A random number from a to b. The game passes a real RNG, the reference test the midpoint.</summary>
        public delegate double Uniform(double a, double b);

        /// <summary>One finished clip: beep phrase, normalised to -22 dBFS gated RMS with a -1.3 dBFS peak limit.</summary>
        public static float[] Render(ProbeVoiceSpec v, string mood, int variant, Uniform uniform)
        {
            double[] y = Normalize(Beep(v, mood, variant, uniform), TargetRmsDb);
            float[] r = new float[y.Length];
            for (int i = 0; i < y.Length; i++) r[i] = (float)y[i];
            return r;
        }

        // ------------------------------------------------ make_probe_beep


        private sealed class MoodOpt
        {
            public double buzz, vibrato, vibratoLast;
            public bool noRhythm;
        }

        /// <summary>make_probe_beep: one beep phrase before normalising.</summary>
        public static double[] Beep(ProbeVoiceSpec v, string mood, int variant, Uniform uniform)
        {
            if (v == null) throw new ArgumentNullException("v");
            if (uniform == null) uniform = (a, b) => (a + b) * 0.5;
            MoodOpt opt;
            List<ProbeNote> notes;
            ProbeMoodSpec own;
            if (v.Moods != null && mood != null && v.Moods.TryGetValue(mood, out own))
            {
                notes = own.NotesFor(variant);
                opt = new MoodOpt { buzz = own.Buzz, vibrato = own.Vibrato, noRhythm = !own.UseRhythm };
            }
            else notes = MoodNotes(mood, variant, out opt);
            // the extras: at their defaults (spread 1, speed 1, vibrato 0, buzz 0) the math is the Python math exactly
            if (v.Spread != 1.0)
                for (int k = 0; k < notes.Count; k++)
                {
                    ProbeNote n = notes[k];
                    notes[k] = new ProbeNote(n.st0 * v.Spread, n.st1 * v.Spread, n.dur, n.gap, n.amp);
                }
            if (!opt.noRhythm) notes = ApplyRhythm(notes, v.Rhythm);
            double speed = v.Speed > 0.0 ? v.Speed : 1.0;
            double buzz = Math.Max(opt.buzz, v.Buzz);
            List<double> x = new List<double>();
            double? prev = null;
            for (int k = 0; k < notes.Count; k++)
            {
                ProbeNote n = notes[k];
                double d = n.dur / speed * uniform(0.92, 1.08);
                double vib = opt.vibrato;
                if (k == notes.Count - 1) vib = Math.Max(vib, opt.vibratoLast);
                vib += v.Vibrato;
                x.AddRange(SynthNote(v, n.st0, n.st1, d, n.amp, prev, vib, buzz));
                prev = n.st1;
                if (k < notes.Count - 1 && n.gap > 0)
                    x.AddRange(Silence(n.gap / speed * uniform(0.9, 1.1)));
            }
            double[] r = x.ToArray();
            FadeEdges(r, (int)(0.003 * SampleRate), (int)(0.004 * SampleRate));
            return r;
        }

        /// <summary>probe_mood_notes: the note shape of a mood. Offsets in semitones from the voice base pitch.</summary>
        private static List<ProbeNote> MoodNotes(string mood, int i, out MoodOpt opt)
        {
            opt = new MoodOpt();
            double tr = new[] { 0.0, 2.0, -2.0 }[Mod(i, 3)];
            List<ProbeNote> r = new List<ProbeNote>();
            switch (mood)
            {
                case "happy":
                {
                    int[][] all = { new[] { 0, 4, 7 }, new[] { 0, 4, 7, 12 }, new[] { 0, 5, 9 } };
                    foreach (int s in all[Mod(i, 3)]) r.Add(new ProbeNote(tr + s, tr + s + 3, 0.065, 0.04, 1.0));
                    return r;
                }
                case "offended":
                {
                    double lo = -12.0 + tr;
                    if (i == 1) r.Add(new ProbeNote(lo + 2, lo, 0.10, 0.03, 0.9));
                    r.Add(new ProbeNote(lo, lo - 2, 0.16, 0.035, 1.0));
                    r.Add(new ProbeNote(lo - 2, lo - 6, 0.30, 0.0, 1.0));
                    opt.buzz = 0.55;
                    return r;
                }
                case "curious":
                    if (i == 2) r.Add(new ProbeNote(tr - 3, tr - 3, 0.06, 0.04, 0.8));
                    r.Add(new ProbeNote(tr, tr, 0.08, 0.05, 0.9));
                    r.Add(new ProbeNote(tr, tr + 7, 0.26, 0.0, 1.0));
                    return r;
                case "scared":
                {
                    int count = new[] { 9, 11, 8 }[Mod(i, 3)];
                    double up = new[] { 3.0, 2.0, 4.0 }[Mod(i, 3)];
                    for (int k = 0; k < count; k++)
                    {
                        double st = 5 + tr + (k % 2 != 0 ? up : 0);
                        r.Add(new ProbeNote(st, st, 0.034, 0.010, 1.0));
                    }
                    opt.noRhythm = true;
                    return r;
                }
                case "sleepy":
                {
                    double b = -5.0 + tr;
                    if (i != 1) r.Add(new ProbeNote(b, b - 1, 0.20, 0.07, 1.0));
                    r.Add(new ProbeNote(b - 3, b - 4, 0.22, 0.08, 0.75));
                    r.Add(new ProbeNote(b - 7, b - 10, 0.36, 0.0, 0.55));
                    opt.vibrato = 0.15;
                    return r;
                }
                case "proud":
                {
                    double top = new[] { 12, 12, 7 }[Mod(i, 3)];
                    double mid = new[] { 5, 4, 4 }[Mod(i, 3)];
                    r.Add(new ProbeNote(tr, tr, 0.075, 0.035, 0.9));
                    r.Add(new ProbeNote(tr + mid, tr + mid, 0.075, 0.035, 0.95));
                    r.Add(new ProbeNote(tr + top, tr + top, 0.30, 0.0, 1.0));
                    opt.vibratoLast = 0.25;
                    return r;
                }
                case "bored":
                {
                    double b = -3.0 + tr;
                    int n = new[] { 3, 2, 4 }[Mod(i, 3)];
                    for (int k = 0; k < n - 1; k++) r.Add(new ProbeNote(b, b, 0.15, 0.09, 0.8));
                    r.Add(new ProbeNote(b, b - 1, 0.26, 0.0, 0.7));
                    return r;
                }
            }
            throw new ArgumentException("unknown probe mood: " + mood);
        }

        private static int Mod(int a, int m) { int r = a % m; return r < 0 ? r + m : r; }

        /// <summary>apply_rhythm: even as is, swing long short, stutter every note twice quickly, lazy longer.</summary>
        private static List<ProbeNote> ApplyRhythm(List<ProbeNote> notes, string rhythm)
        {
            List<ProbeNote> r = new List<ProbeNote>(notes.Count * 2);
            switch (rhythm)
            {
                case "swing":
                    for (int k = 0; k < notes.Count; k++)
                    {
                        ProbeNote n = notes[k];
                        r.Add(new ProbeNote(n.st0, n.st1, n.dur * (k % 2 == 0 ? 1.25 : 0.75), n.gap, n.amp));
                    }
                    return r;
                case "stutter":
                    foreach (ProbeNote n in notes)
                    {
                        double mid = n.st0 + (n.st1 - n.st0) * 0.5;
                        r.Add(new ProbeNote(n.st0, mid, n.dur * 0.45, 0.014, n.amp));
                        r.Add(new ProbeNote(mid, n.st1, n.dur * 0.5, n.gap, n.amp * 0.9));
                    }
                    return r;
                case "lazy":
                    foreach (ProbeNote n in notes) r.Add(new ProbeNote(n.st0, n.st1, n.dur * 1.2, n.gap * 1.35, n.amp));
                    return r;
            }
            return notes;
        }

        // ------------------------------------------------ synth_note

        private struct Partial
        {
            public int h; public double a; public bool cosine;
            public Partial(int h, double a, bool cosine) { this.h = h; this.a = a; this.cosine = cosine; }
        }

        /// <summary>_wave_partials: band-limited partials of a wave at f Hz (harmonics under 9 kHz).</summary>
        private static List<Partial> WavePartials(string wave, double f)
        {
            int nmax = Math.Max(1, (int)(9000.0 / Math.Max(f, 1.0)));
            List<Partial> r = new List<Partial>();
            switch (wave)
            {
                case "sine":
                case "fm":
                    r.Add(new Partial(1, 1.0, false));
                    return r;
                case "triangle":
                    for (int n = 1; n <= Math.Min(nmax, 9); n += 2)
                        r.Add(new Partial(n, (8 / Math.Pow(Math.PI, 2)) * ((n / 2) % 2 == 0 ? 1 : -1) / (double)(n * n), false));
                    return r;
                case "square":
                    for (int n = 1; n <= Math.Min(nmax, 9); n += 2) r.Add(new Partial(n, (4 / Math.PI) / n, false));
                    return r;
                case "saw":
                    for (int n = 1; n <= Math.Min(nmax, 10); n++) r.Add(new Partial(n, (2 / Math.PI) / n, false));
                    return r;
                case "pulse":   // 25 % duty pulse
                    for (int n = 1; n <= Math.Min(nmax, 10); n++)
                        r.Add(new Partial(n, (2 / (n * Math.PI)) * Math.Sin(n * Math.PI * 0.25), true));
                    return r;
            }
            throw new ArgumentException("unknown probe wave: " + wave);
        }

        /// <summary>synth_note: one note, pitch gliding from st0 to st1, sliding in from the note before.</summary>
        private static double[] SynthNote(ProbeVoiceSpec v, double st0, double st1, double dur, double amp, double? fromSt,
                                          double vibrato, double buzz)
        {
            int n = Math.Max(1, (int)(dur * SampleRate));
            double bas = v.BaseHz;
            string wave = v.Wave;
            List<Partial> partials = WavePartials(wave, bas * Math.Pow(2, Math.Max(st0, st1) / 12.0));
            double port = fromSt.HasValue ? (fromSt.Value - st0) * v.Glide : 0.0;
            double tau = Math.Max(0.01, 0.3 * dur);
            int na = Math.Max(1, (int)(0.005 * SampleRate));
            int nr = Math.Max(1, Math.Min(n / 2, (int)(0.018 * SampleRate)));
            double[] outp = new double[n];
            double ph = 0.0;
            bool fm = wave == "fm";
            double tanh25 = Math.Tanh(2.5);
            for (int k = 0; k < n; k++)
            {
                double t = k / (double)SampleRate;
                double st = st0 + (st1 - st0) * (k / (double)n) + port * Math.Exp(-t / tau);
                if (vibrato != 0.0) st += vibrato * Math.Sin(2 * Math.PI * 6.0 * t);
                ph += 2 * Math.PI * bas * Math.Pow(2, st / 12.0) / SampleRate;
                double val;
                if (fm)
                {
                    double idx = 1.8 * Math.Exp(-t / 0.12) + 0.4;
                    val = Math.Sin(ph + idx * Math.Sin(2 * ph));
                }
                else
                {
                    val = 0.0;
                    for (int p = 0; p < partials.Count; p++)
                    {
                        Partial q = partials[p];
                        val += q.a * (q.cosine ? Math.Cos(q.h * ph) : Math.Sin(q.h * ph));
                    }
                }
                if (buzz != 0.0)
                    val = Math.Tanh(2.5 * val) / tanh25 * (1.0 - buzz * (0.5 + 0.5 * Math.Sin(2 * Math.PI * 38.0 * t)));
                double env = 1.0;
                if (k < na) env = 0.5 - 0.5 * Math.Cos(Math.PI * k / na);
                if (k >= n - nr) env *= 0.5 - 0.5 * Math.Cos(Math.PI * (n - 1 - k) / nr);
                outp[k] = val * env * amp;
            }
            return outp;
        }

        // ------------------------------------------------ helpers and normalize

        /// <summary>silence: Python round() rounds half to even, so does Math.Round.</summary>
        private static double[] Silence(double dur)
        {
            return new double[(int)Math.Round(dur * SampleRate, MidpointRounding.ToEven)];
        }

        public static void FadeEdges(double[] x, int nIn, int nOut)
        {
            nIn = Math.Min(nIn, x.Length);
            nOut = Math.Min(nOut, x.Length);
            for (int k = 0; k < nIn; k++) x[k] *= 0.5 - 0.5 * Math.Cos(Math.PI * k / nIn);
            int L = x.Length;
            for (int k = 0; k < nOut; k++) x[L - 1 - k] *= 0.5 - 0.5 * Math.Cos(Math.PI * k / nOut);
        }

        private static double Db(double x) { return x > 1e-12 ? 20.0 * Math.Log10(x) : -240.0; }

        /// <summary>gated_rms_db: RMS over 10 ms frames above max(-50 dBFS, loudest - 30 dB).</summary>
        public static double GatedRmsDb(double[] x)
        {
            List<double> lv = new List<double>();
            for (int i = 0; i <= x.Length - Frame; i += Frame)
            {
                double sum = 0.0;
                for (int j = i; j < i + Frame; j++) sum += x[j] * x[j];
                lv.Add(sum / Frame);
            }
            if (lv.Count == 0)
            {
                if (x.Length == 0) return -240.0;
                double s2 = 0.0;
                for (int j = 0; j < x.Length; j++) s2 += x[j] * x[j];
                return Db(Math.Sqrt(s2 / x.Length));
            }
            double mx = double.MinValue;
            foreach (double v in lv) if (v > mx) mx = v;
            double gate = Math.Max(Math.Pow(10, SilenceDb / 10), mx * Math.Pow(10, -3.0));
            double sel = 0.0;
            int cnt = 0;
            foreach (double v in lv) if (v >= gate) { sel += v; cnt++; }
            return cnt > 0 ? 10 * Math.Log10(sel / cnt) : -240.0;
        }

        /// <summary>limit: block-based lookahead peak limiter (64-sample blocks, fast release), then a hard clamp.</summary>
        public static double[] Limit(double[] x, double ceilingDb)
        {
            double t = Math.Pow(10, ceilingDb / 20.0);
            const int B = 64;
            int nb = (x.Length + B - 1) / B;
            double[] req = new double[nb];
            for (int b = 0; b < nb; b++)
            {
                double pk = 0.0;
                for (int i = b * B; i < Math.Min(x.Length, (b + 1) * B); i++) pk = Math.Max(pk, Math.Abs(x[i]));
                req[b] = pk <= t ? 1.0 : t / pk;
            }
            double[] env = new double[nb];
            for (int b = 0; b < nb; b++) env[b] = Math.Min(Math.Min(req[Math.Max(0, b - 1)], req[b]), req[Math.Min(nb - 1, b + 1)]);
            const double rel = 0.02;
            for (int b = 1; b < nb; b++) env[b] = Math.Min(env[b], env[b - 1] + rel);
            for (int b = nb - 2; b >= 0; b--) env[b] = Math.Min(env[b], env[b + 1] + rel);
            double[] y = (double[])x.Clone();
            for (int b = 0; b < nb; b++)
            {
                double g0 = env[b];
                double g1 = b + 1 < nb ? env[b + 1] : env[b];
                g1 = Math.Min(g1, req[b]);
                int bas = b * B;
                for (int k = 0; k < B; k++)
                {
                    int i = bas + k;
                    if (i >= y.Length) break;
                    y[i] *= g0 + (g1 - g0) * k / B;
                }
            }
            for (int i = 0; i < y.Length; i++) y[i] = Math.Max(-t, Math.Min(t, y[i]));
            return y;
        }

        /// <summary>normalize (fades=False, as for every probe folder): gain to the target gated RMS through the limiter.</summary>
        public static double[] Normalize(double[] x, double targetDb)
        {
            double gDb = targetDb - GatedRmsDb(x);
            double[] y = x;
            for (int it = 0; it < 8; it++)
            {
                double g = Math.Pow(10, gDb / 20.0);
                double[] s = new double[x.Length];
                for (int i = 0; i < x.Length; i++) s[i] = x[i] * g;
                y = Limit(s, PeakLimitDb);
                double err = targetDb - GatedRmsDb(y);
                if (Math.Abs(err) < 0.25) break;
                gDb += err;
            }
            return y;
        }
    }
}
