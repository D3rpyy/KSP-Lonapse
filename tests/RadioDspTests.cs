using System;
using Lonapse;

public static class RadioDspTests
{
    private static float[] Tone(int rate, double hz, double amplitude, int channels = 1)
    {
        float[] data = new float[rate * channels];
        for (int i = 0; i < rate; i++)
            for (int ch = 0; ch < channels; ch++) data[i * channels + ch] = (float)(amplitude * Math.Sin(2 * Math.PI * hz * i / rate));
        return data;
    }

    private static double Rms(float[] data, int from)
    {
        double sum = 0;
        for (int i = from; i < data.Length; i++) sum += data[i] * data[i];
        return Math.Sqrt(sum / Math.Max(1, data.Length - from));
    }

    private static double Response(RadioVoice voice, int rate, double hz, float weak = 0, float cutoff = 3000)
    {
        float[] data = Tone(rate, hz, 0.001);
        new RadioDsp().Process(data, 1, rate, voice, weak, cutoff, false, 1, null, 0);
        return Rms(data, rate / 2);
    }

    private static double Difference(float[] a, float[] b)
    {
        double max = 0;
        for (int i = 0; i < a.Length; i++) max = Math.Max(max, Math.Abs(a[i] - b[i]));
        return max;
    }

    private static double Harmonic(float[] data, int rate, double hz)
    {
        double sine = 0, cosine = 0;
        int from = data.Length / 2;
        for (int i = from; i < data.Length; i++)
        {
            double phase = 2 * Math.PI * hz * i / rate;
            sine += data[i] * Math.Sin(phase);
            cosine += data[i] * Math.Cos(phase);
        }
        return 2 * Math.Sqrt(sine * sine + cosine * cosine) / (data.Length - from);
    }

    private static void Noise(Action<bool, string, string> check)
    {
        check(RadioNoise.Gain(0) == 0 && RadioNoise.Gain(0.5f) == 1 && RadioNoise.Gain(1) == 2,
            "radio static: one volume mutes at zero and keeps the default bed level at saved gain 0.5", null);
        check(RadioNoise.Gain(0) * RadioNoise.ControlLevel(0.12f, 1, 1) == 0,
            "radio static: zero also mutes Mission Control on the weakest link", null);
        check(Math.Abs(RadioNoise.CrewBed * 0.5 - 0.6 * Math.Sqrt(0.5) * 0.5) < 1e-6,
            "radio static: default crew hiss keeps its level", null);
        check(Math.Abs(RadioNoise.ControlLevel(0.12f, 0, 0.4f) - (0.12 + 0.3 * Math.Sqrt(0.5))) < 1e-6,
            "radio static: default Mission Control hiss keeps its level", null);
        Settings old = new Settings { staticVolume = 0.3f, radioStatic = 0.8f, tuningVersion = 13 };
        old.MigrateValues(13);
        double previous = 0.6 * Math.Sqrt(0.8) * 0.3;
        double merged = RadioNoise.CrewBed * RadioNoise.DefaultVolume * RadioNoise.Gain(old.radioStatic);
        check(Math.Abs(merged - previous) < 1e-6 && old.tuningVersion == Tuning.CurrentVersion,
            "radio static: migration combines saved volume and amount into the new slider", null);
        float once = old.radioStatic;
        check(old.MigrateValues(Tuning.CurrentVersion).Count == 0 && old.radioStatic == once,
            "radio static: the saved levels are merged only once", null);
        check(RadioNoise.FromLegacy(0, 1) == 0 && RadioNoise.FromLegacy(1, 0) == 0 && RadioNoise.FromLegacy(1, 1) == 1,
            "radio static: migration preserves zero and clamps the combined maximum", null);
        check(RadioNoise.Gain(float.NaN) == 0 && RadioNoise.FromLegacy(float.NaN, 1) == 0,
            "radio static: invalid saved levels do not produce noise", null);
    }

    private static void Reference(Action<bool, string, string> check)
    {
        string here = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
        string path = System.IO.Path.GetFullPath(System.IO.Path.Combine(here, "../../tests/radio_reference.tsv"));
        string[] lines = System.IO.File.ReadAllLines(path), header = lines[0].Split('\t');
        var culture = System.Globalization.CultureInfo.InvariantCulture;
        for (int row = 1; row < lines.Length; row++)
        {
            if (lines[row].StartsWith("#") || lines[row].Length == 0) continue;
            string[] fields = lines[row].Split('\t');
            RadioVoice voice = (RadioVoice)Enum.Parse(typeof(RadioVoice), fields[0]);
            int rate = int.Parse(fields[1], culture);
            float weak = float.Parse(fields[2], culture);
            float[] data = new float[1024]; data[0] = 0.001f;
            new RadioDsp().Process(data, 1, rate, voice, weak, 3000, false, 1, null, 0);
            double peak = 0, difference = 0;
            for (int i = 0; i < data.Length; i++) peak = Math.Max(peak, Math.Abs(data[i]));
            for (int col = 3; col < header.Length; col++)
            {
                int at = int.Parse(header[col].Substring(1), culture);
                difference = Math.Max(difference, Math.Abs(data[at] / peak - double.Parse(fields[col], culture)));
            }
            check(difference < 0.002, "radio: response matches the FFmpeg preview (" + voice + ", " + rate + ", weak " + weak + ")", "max error " + difference);
        }
    }

    public static void Run(Action<bool, string, string> check)
    {
        Reference(check);
        Noise(check);
        foreach (int rate in new[] { 22050, 44100, 48000, 96000 })
        {
            foreach (RadioVoice voice in new[] { RadioVoice.Headset, RadioVoice.Helmet, RadioVoice.Control })
            {
                double mid = Response(voice, rate, 1000);
                double low = Response(voice, rate, 80);
                double high = Response(voice, rate, 8000);
                check(mid > 0.0001 && low < mid * 0.01 && high < mid * 0.01,
                    "radio: steep transmit band at " + rate + " Hz, " + voice, "mid " + mid + ", low " + low + ", high " + high);
            }
        }
        double near = Response(RadioVoice.Control, 48000, 2800) / Response(RadioVoice.Control, 48000, 1000);
        double far = Response(RadioVoice.Control, 48000, 2800, 1) / Response(RadioVoice.Control, 48000, 1000, 1);
        check(far < near * 0.6, "radio: a weak MC link narrows the receiver band relative to the speech band", "good " + near + ", weak " + far);
        check(Response(RadioVoice.Headset, 48000, 2500, 0, 1400) < Response(RadioVoice.Headset, 48000, 2500) * 0.1,
            "radio: G-strain low-pass still muffles the headset", null);
        float[] stressed = Tone(48000, 500, 1);
        new RadioDsp().Process(stressed, 1, 48000, RadioVoice.Headset, 0, 3000, true, 1, null, 0, 1);
        double third = Harmonic(stressed, 48000, 1500) / Harmonic(stressed, 48000, 500);
        check(third < 0.03, "radio: loud stressed crew keep a gentle saturation", "third harmonic " + (100 * third).ToString("0.00") + "%");

        float[] source = Tone(48000, 700, 0.2f, 2);
        float[] full = (float[])source.Clone(), split = new float[source.Length];
        var one = new RadioDsp();
        var chunks = new RadioDsp();
        one.Process(full, 2, 48000, RadioVoice.Helmet, 0, 3000, true, 2, null, 0);
        for (int at = 0; at < source.Length; at += 512)
        {
            int count = Math.Min(512, source.Length - at);
            float[] block = new float[count];
            Array.Copy(source, at, block, 0, count);
            chunks.Process(block, 2, 48000, RadioVoice.Helmet, 0, 3000, true, 2, null, 0);
            Array.Copy(block, 0, split, at, count);
        }
        check(Difference(full, split) < 1e-6, "radio: filter, compressor and helmet state survive audio block boundaries", null);
        bool stereo = true;
        for (int i = 0; i < full.Length; i += 2) stereo &= full[i] == full[i + 1];
        check(stereo, "radio: identical stereo channels stay identical", null);
        float[] left = Tone(48000, 900, 0.1, 2);
        for (int i = 1; i < left.Length; i += 2) left[i] = 0;
        new RadioDsp().Process(left, 2, 48000, RadioVoice.Helmet, 0, 3000, true, 1, null, 0);
        bool isolated = true;
        for (int i = 1; i < left.Length; i += 2) isolated &= left[i] == 0;
        check(isolated, "radio: helmet delay and filters never leak between channels", null);

        float[] reset = (float[])source.Clone();
        one.Reset();
        one.Process(reset, 2, 48000, RadioVoice.Helmet, 0, 3000, true, 2, null, 0);
        check(Difference(full, reset) < 1e-6, "radio: a new transmission clears old speech and helmet tails", null);
        float[] bed = Tone(22050, 600, 0.001);
        foreach (RadioVoice voice in new[] { RadioVoice.Headset, RadioVoice.Control, RadioVoice.Helmet })
        {
            float[] silence = new float[48000];
            new RadioDsp().Process(silence, 1, 48000, voice, 0, 3000, true, 1, bed, 22050);
            double level = Rms(silence, 24000);
            check(voice == RadioVoice.Helmet ? level == 0 : level > 0.00001,
                "radio: microphone pickup is resampled, and stays outside the helmet (" + voice + ")", "rms " + level);
        }
        float[] muted = (float[])source.Clone();
        float[] fan = Tone(48000, 600, Math.Sqrt(2) * Math.Pow(10, -26.0 / 20));
        float[] fanOnly = new float[48000];
        float[] speech = Tone(48000, 1000, Math.Sqrt(2) * 0.1);
        new RadioDsp().Process(fanOnly, 1, 48000, RadioVoice.Headset, 0, 3000, true, 1, fan, 48000);
        new RadioDsp().Process(speech, 1, 48000, RadioVoice.Headset, 0, 3000, true, 1, null, 0);
        double fanRatio = Rms(fanOnly, 24000) / Rms(speech, 24000);
        check(fanRatio < Math.Pow(10, -18.0 / 20), "radio: fan pickup stays at least 18 dB behind speech even between words",
            "fan relative to speech " + (20 * Math.Log10(fanRatio)).ToString("0.0") + " dB");
        new RadioDsp().Process(muted, 2, 48000, RadioVoice.Control, 0, 3000, true, 0, bed, 22050);
        check(Rms(muted, 0) == 0, "radio: zero gain also silences microphone pickup", null);
        float[] unpowered = new float[48000];
        new RadioDsp().Process(unpowered, 1, 48000, RadioVoice.Headset, 0, 3000, true, 1, bed, 22050, 0, 0);
        check(Rms(unpowered, 0) == 0, "radio: a cabin without power contributes no fan pickup", null);
        float[] off = (float[])source.Clone();
        new RadioDsp().Process(off, 2, 48000, RadioVoice.Off, 1, 3000, true, 4, bed, 22050);
        check(Difference(off, source) == 0, "radio: Off leaves key tones and other non-voice audio untouched", null);

        bool finite = true, bounded = true;
        var changing = new RadioDsp();
        foreach (int rate in new[] { 8000, 22050, 48000, 192000 })
        {
            float[] loud = Tone(rate, 800, 20);
            loud[0] = float.NaN; loud[1] = float.PositiveInfinity;
            changing.Process(loud, 1, rate, RadioVoice.Control, float.NaN, float.NaN, true, 16, null, 0);
            for (int i = 0; i < loud.Length; i++)
            {
                finite &= !float.IsNaN(loud[i]) && !float.IsInfinity(loud[i]);
                bounded &= Math.Abs(loud[i]) <= 1;
            }
        }
        check(finite && bounded, "radio: loud/invalid input and sample-rate changes remain finite and peak limited", null);
    }
}
