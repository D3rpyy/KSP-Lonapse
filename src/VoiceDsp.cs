// Voice clarity DSP on the audio thread: presence EQ (+3 dB peaking around 2.5 kHz) and a
// feed-forward peak compressor (threshold -18 dB, ratio 3, makeup). Coefficients come from MixMath.
// Runs as the last filter on crew and control sources. No allocation in OnAudioFilterRead.
using System;
using UnityEngine;

namespace Lonapse
{
    public class VoiceDsp : MonoBehaviour
    {
        // set from the main thread, read on the audio thread
        public volatile bool compressorOn = true;
        public volatile bool eqOn = true;
        public float thresholdDb = -18f, ratio = 3f, makeupDb = 4f;
        public float attack = 0.005f, release = 0.12f;
        public float sampleRate = 48000f;
        public float outGain = 1f;   // voice boost and callout volume, linear (soft clipped above 0.8)
        public float dropout;        // 0..1: weak signal, short gaps and bit crunch (MC channel)

        private uint rngState = 2463534242u;
        private int gapLeft, gapLen;     // samples left in the current dropout, its length
        private float gapGain = 1f;
        private int holdCount;
        private float heldSample;

        private double b0, b1, b2, a1, a2;
        private double[] x1 = new double[8], x2 = new double[8], y1 = new double[8], y2 = new double[8];
        private float env;          // peak follower, linear
        private float coefSr = -1f;
        private int frame;
        private float gHeld = 1f;

        /// <summary>Last gain reduction in dB (0 or negative), for the debug overlay.</summary>
        public float lastReductionDb;

        private void Coefficients()
        {
            double[] c = MixMath.PeakingBiquad(2500.0, 3.0, 0.9, sampleRate);
            b0 = c[0]; b1 = c[1]; b2 = c[2]; a1 = c[3]; a2 = c[4];
            coefSr = sampleRate;
            Array.Clear(x1, 0, 8); Array.Clear(x2, 0, 8); Array.Clear(y1, 0, 8); Array.Clear(y2, 0, 8);
        }

        /// <summary>Linear up to 0.8, then a smooth knee that never passes 1.</summary>
        private static float SoftClip(float v)
        {
            float a = v < 0f ? -v : v;
            if (a <= 0.8f) return v;
            float y = 0.8f + 0.2f * (float)Math.Tanh((a - 0.8f) / 0.2f);
            return v < 0f ? -y : y;
        }

        private float Rand()
        {
            // xorshift32: no allocation on the audio thread
            uint x = rngState;
            x ^= x << 13; x ^= x >> 17; x ^= x << 5;
            rngState = x;
            return (x & 0xFFFFFF) / 16777216f;
        }

        /// <summary>
        /// Weak signal: up to about two short gaps per second (30 to 120 ms, 3 ms ramps), and above 0.3 a bit
        /// crunch (fewer levels, sample-and-hold). Strength 0..1.
        /// </summary>
        private void Dropouts(float[] data, int channels, float dr)
        {
            float pStart = dr * 2f / sampleRate;
            float ramp = 1f / Math.Max(1f, 0.003f * sampleRate);
            bool crush = dr > 0.3f;
            float levels = crush ? (float)Math.Pow(2.0, 12.0 - 8.0 * dr) : 0f;
            int hold = crush ? 1 + (int)(3f * dr) : 1;
            for (int i = 0; i < data.Length; i += channels)
            {
                if (gapLeft <= 0 && Rand() < pStart)
                {
                    gapLen = (int)((0.03f + 0.09f * Rand()) * sampleRate);
                    gapLeft = gapLen;
                }
                float target = gapLeft > 0 ? 0.04f : 1f;
                if (gapLeft > 0) gapLeft--;
                gapGain += Math.Max(-ramp, Math.Min(ramp, target - gapGain));
                for (int ch = 0; ch < channels; ch++)
                {
                    float v = data[i + ch];
                    if (crush)
                    {
                        if (ch == 0) { if (holdCount <= 0) { heldSample = v; holdCount = hold; } holdCount--; }
                        v = channels == 1 ? heldSample : v;
                        v = (float)Math.Round(v * levels) / levels;
                    }
                    data[i + ch] = v * gapGain;
                }
            }
        }

        private void OnAudioFilterRead(float[] data, int channels)
        {
            if (channels <= 0 || channels > 8) return;
            if (coefSr != sampleRate) Coefficients();
            bool eq = eqOn, comp = compressorOn;
            float og = outGain;
            float dr = dropout;
            if (dr > 0.001f) Dropouts(data, channels, dr);
            if (!eq && !comp && Math.Abs(og - 1f) < 1e-3f) return;

            float atk = 1f - (float)Math.Exp(-1.0 / (Math.Max(1e-4f, attack) * sampleRate));
            float rel = 1f - (float)Math.Exp(-1.0 / (Math.Max(1e-3f, release) * sampleRate));
            float makeup = MixMath.DbToLin(makeupDb);
            float minGainDb = 0f;

            for (int i = 0; i < data.Length; i += channels)
            {
                float peak = 0f;
                for (int ch = 0; ch < channels; ch++)
                {
                    double x = data[i + ch];
                    if (eq)
                    {
                        double y = b0 * x + b1 * x1[ch] + b2 * x2[ch] - a1 * y1[ch] - a2 * y2[ch];
                        x2[ch] = x1[ch]; x1[ch] = x;
                        y2[ch] = y1[ch]; y1[ch] = y;
                        x = y;
                        data[i + ch] = (float)x;
                    }
                    float ax = (float)Math.Abs(x);
                    if (ax > peak) peak = ax;
                }
                if (!comp)
                {
                    if (Math.Abs(og - 1f) >= 1e-3f)
                        for (int ch = 0; ch < channels; ch++) data[i + ch] = SoftClip(data[i + ch] * og);
                    continue;
                }
                env += (peak > env ? atk : rel) * (peak - env);
                // the gain curve (log and pow) is evaluated every 16 frames, the envelope every frame
                if ((frame++ & 15) == 0)
                {
                    float gDb = MixMath.CompressorGainDb(MixMath.LinToDb(env), thresholdDb, ratio);
                    if (gDb < minGainDb) minGainDb = gDb;
                    gHeld = MixMath.DbToLin(gDb) * makeup;
                }
                float g = gHeld * og;
                for (int ch = 0; ch < channels; ch++) data[i + ch] = SoftClip(data[i + ch] * g);
            }
            lastReductionDb = minGainDb;
            // denormal guard on the filter state
            for (int ch = 0; ch < channels; ch++)
            {
                if (Math.Abs(y1[ch]) < 1e-20) y1[ch] = 0.0;
                if (Math.Abs(y2[ch]) < 1e-20) y2[ch] = 0.0;
            }
        }
    }
}
