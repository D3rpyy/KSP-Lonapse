// Microphone and receiver processing shared by recorded and runtime voices.
// State belongs to one audio source. Process runs on the audio thread without allocating.
using System;

namespace Lonapse
{
    public enum RadioVoice { Off, Headset, Helmet, Control }

    public sealed class RadioDsp
    {
        private sealed class Filter
        {
            private double b0 = 1, b1, b2, a1, a2;
            private readonly double[] z1 = new double[8], z2 = new double[8];

            public void Pass(double hz, double rate, bool high)
            {
                double w = 2 * Math.PI * Math.Min(hz, rate * 0.45) / rate;
                double c = Math.Cos(w), alpha = Math.Sin(w) / Math.Sqrt(2);
                double a0 = 1 + alpha;
                b0 = (high ? 1 + c : 1 - c) * 0.5 / a0;
                b1 = (high ? -(1 + c) : 1 - c) / a0;
                b2 = b0; a1 = -2 * c / a0; a2 = (1 - alpha) / a0;
            }

            public void Peak(double hz, double db, double q, double rate)
            {
                double a = Math.Pow(10, db / 40), w = 2 * Math.PI * Math.Min(hz, rate * 0.45) / rate;
                double c = Math.Cos(w), alpha = Math.Sin(w) / (2 * q), a0 = 1 + alpha / a;
                b0 = (1 + alpha * a) / a0; b1 = -2 * c / a0; b2 = (1 - alpha * a) / a0;
                a1 = -2 * c / a0; a2 = (1 - alpha / a) / a0;
            }

            public double Step(double x, int ch)
            {
                double y = b0 * x + z1[ch];
                z1[ch] = b1 * x - a1 * y + z2[ch];
                z2[ch] = b2 * x - a2 * y;
                if (Math.Abs(z1[ch]) < 1e-20) z1[ch] = 0;
                if (Math.Abs(z2[ch]) < 1e-20) z2[ch] = 0;
                return y;
            }

            public void Reset() { Array.Clear(z1, 0, 8); Array.Clear(z2, 0, 8); }
        }

        private readonly Filter[] band = new Filter[8];
        private readonly Filter helmet = new Filter(), output = new Filter();
        private const int DelayFrames = 4096;
        private readonly float[] delay = new float[DelayFrames * 8];
        private int delayAt, tap1, tap2, tap3;
        private RadioVoice lastVoice;
        private float lastRate, lastWeak = -1, lastCutoff = -1;
        private int lastChannels;
        private double envelope, heldGain = 1, ambienceAt;
        private double attack, release, threshold, ratio, makeup, drive;
        private float weak;
        private int frame;

        public float ReductionDb { get; private set; }

        public RadioDsp()
        {
            for (int i = 0; i < band.Length; i++) band[i] = new Filter();
        }

        public void Reset()
        {
            for (int i = 0; i < band.Length; i++) band[i].Reset();
            helmet.Reset(); output.Reset();
            Array.Clear(delay, 0, delay.Length);
            envelope = 0; heldGain = 1; frame = delayAt = 0;
            ReductionDb = 0;
        }

        private static float Unit(float x)
        {
            return float.IsNaN(x) ? 0 : Math.Max(0, Math.Min(1, x));
        }

        private void Configure(RadioVoice voice, float rate, float lowPass)
        {
            bool mc = voice == RadioVoice.Control;
            double hp = mc ? 300 + 100 * weak : 320;
            double lp = mc ? 3000 - 500 * weak : Math.Min(3000, lowPass);
            band[0].Peak(mc ? 250 : 1800, mc ? 2 : 3, mc ? 1 : 1.2, rate);
            for (int i = 1; i <= 3; i++) band[i].Pass(hp, rate, true);
            band[4].Pass(lp, rate, false);
            band[5].Pass(lp, rate, false);
            band[6].Pass(mc ? 3200 - 500 * weak : Math.Min(3200, lowPass + 200), rate, false);
            band[7].Peak(mc ? 2200 - 400 * weak : 500, mc ? 2 + weak : -3, mc ? 1 + 0.2 * weak : 1, rate);
            helmet.Peak(750, 5, 3, rate);
            output.Pass(mc ? 3400 - 500 * weak : Math.Min(3400, lowPass + 400), rate, false);
            threshold = mc ? -26 - 4 * weak : -28;
            ratio = mc ? 6 + 4 * weak : 8;
            makeup = MixMath.DbToLin((float)(mc ? 10 + 4 * weak : 12));
            drive = MixMath.DbToLin((float)(mc ? 3 + 3 * weak : 1));
            attack = 1 - Math.Exp(-1 / ((mc ? 0.003 - 0.001 * weak : 0.002) * rate));
            release = 1 - Math.Exp(-1 / ((mc ? 0.150 - 0.040 * weak : 0.090) * rate));
            tap1 = Math.Min(DelayFrames - 1, Math.Max(1, (int)(rate * 2.5 / 1000)));
            tap2 = Math.Min(DelayFrames - 1, Math.Max(1, (int)(rate * 5.5 / 1000)));
            tap3 = Math.Min(DelayFrames - 1, Math.Max(1, (int)(rate * 9.0 / 1000)));
            lastVoice = voice; lastRate = rate; lastWeak = weak; lastCutoff = lowPass;
        }

        private double Helmet(double x, int ch)
        {
            int a = ((delayAt - tap1 + DelayFrames) % DelayFrames) * 8 + ch;
            int b = ((delayAt - tap2 + DelayFrames) % DelayFrames) * 8 + ch;
            int c = ((delayAt - tap3 + DelayFrames) % DelayFrames) * 8 + ch;
            double y = 0.56 * x + 0.7 * (0.35 * delay[a] + 0.22 * delay[b] + 0.12 * delay[c]);
            delay[delayAt * 8 + ch] = (float)x;
            return helmet.Step(y, ch);
        }

        private static float Limit(double x)
        {
            double a = Math.Abs(x);
            if (a <= 0.8) return (float)x;
            double y = 0.8 + 0.2 * Math.Tanh((a - 0.8) / 0.2);
            return (float)(x < 0 ? -y : y);
        }

        public void Process(float[] data, int channels, float rate, RadioVoice voice, float signalWeakness,
                            float lowPass, bool compress, float gain, float[] ambience, int ambienceRate, float grit = 0f, float pickupGain = 1f)
        {
            if (data == null || channels < 1 || channels > 8 || data.Length % channels != 0 || voice == RadioVoice.Off) return;
            if (float.IsNaN(rate) || float.IsInfinity(rate) || rate < 8000) rate = 48000;
            if (float.IsNaN(lowPass) || float.IsInfinity(lowPass) || lowPass < 100) lowPass = 3000;
            if (float.IsNaN(gain) || float.IsInfinity(gain)) gain = 1;
            gain = Math.Max(0, Math.Min(16, gain));
            float targetWeak = voice == RadioVoice.Control ? Unit(signalWeakness) : 0;
            bool changed = voice != lastVoice || rate != lastRate || channels != lastChannels;
            if (changed) { Reset(); weak = targetWeak; }
            else weak += (targetWeak - weak) * (float)(1 - Math.Exp(-data.Length / (channels * rate * 0.15)));
            if (changed || Math.Abs(weak - lastWeak) > 0.001f || Math.Abs(lowPass - lastCutoff) > 1)
                Configure(voice, rate, lowPass);
            lastChannels = channels;
            bool hasAmbience = ambience != null && ambience.Length > 1 && ambienceRate > 0 && voice != RadioVoice.Helmet;
            double step = hasAmbience ? ambienceRate / (double)rate : 0;
            double drivenGain = drive * MixMath.DbToLin((voice == RadioVoice.Control ? 6f : 3f) * Unit(grit));
            // Fixed makeup keeps the processed voice near the level of the listening previews.
            double level = MixMath.DbToLin(voice == RadioVoice.Control ? 3.3f + 4.74f * weak : 4f);
            float reduction = 0;
            for (int i = 0; i < data.Length; i += channels)
            {
                double bed = 0;
                if (hasAmbience)
                {
                    ambienceAt %= ambience.Length;
                    int at = (int)ambienceAt, next = (at + 1) % ambience.Length;
                    bed = ambience[at] + (ambience[next] - ambience[at]) * (ambienceAt - at);
                    // Leave room for the compressor's makeup gain between words.
                    bed *= voice == RadioVoice.Headset ? 0.0316228 : 0.2511886;
                    bed *= Unit(pickupGain);
                    ambienceAt += step;
                }
                double peak = 0;
                for (int ch = 0; ch < channels; ch++)
                {
                    double x = data[i + ch];
                    if (double.IsNaN(x) || double.IsInfinity(x)) x = 0;
                    if (voice == RadioVoice.Helmet) x = Helmet(x, ch);
                    x += bed;
                    for (int k = 0; k < band.Length; k++) x = band[k].Step(x, ch);
                    data[i + ch] = (float)x;
                    peak = Math.Max(peak, Math.Abs(x));
                }
                double energy = peak * peak;
                envelope += (energy > envelope ? attack : release) * (energy - envelope);
                if ((frame++ & 15) == 0)
                {
                    double db = compress ? MixMath.CompressorGainDb(MixMath.LinToDb((float)Math.Sqrt(envelope)), (float)threshold, (float)ratio) : 0;
                    reduction = Math.Min(reduction, (float)db);
                    heldGain = compress ? MixMath.DbToLin((float)db) * makeup : 1;
                }
                for (int ch = 0; ch < channels; ch++)
                {
                    double x = data[i + ch] * (compress ? heldGain : 1);
                    double driven = voice == RadioVoice.Control
                        ? (1 - weak) * Math.Tanh(x * drivenGain) + weak * (2 / Math.PI) * Math.Atan(x * drivenGain)
                        : Math.Tanh(x * drivenGain);
                    x = output.Step(driven / drivenGain, ch);
                    data[i + ch] = Limit(x * gain * level);
                }
                delayAt = (delayAt + 1) % DelayFrames;
            }
            ReductionDb = reduction;
        }
    }
}
