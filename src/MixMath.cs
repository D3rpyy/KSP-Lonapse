// Mix math: dB and linear conversion, ducking smoother, compressor curve and RBJ peaking EQ.
// Pure C#, no Unity, tested offline.
using System;

namespace Lonapse
{
    public static class MixMath
    {
        // Floor used by LinToDb for zero or negative input.
        public const float MinDb = -120f;
        public const float MaxVolume = 2f;

        public static float ClampVolume(float value)
        {
            return DcMath.Clamp(DcMath.Finite(value), 0f, MaxVolume);
        }

        /// <summary>Retain gain above AudioSource's ceiling in the final sample filter.</summary>
        public static void SplitChannelGain(float level, float userGain, out float source, out float filter)
        {
            level = Math.Max(0f, DcMath.Finite(level));
            userGain = DcMath.Clamp(DcMath.Finite(userGain), 0f, 4f);
            float beforeFilter = level * Math.Min(userGain, 1f);
            source = Math.Min(beforeFilter, 1f);
            filter = Math.Max(userGain, 1f) * Math.Max(beforeFilter, 1f);
        }

        public static float DbToLin(float db)
        {
            return (float)Math.Pow(10.0, db / 20.0);
        }

        public static float LinToDb(float lin)
        {
            if (lin <= 0f) return MinDb;
            return 20f * (float)Math.Log10(lin);
        }

        // One-pole smoother toward target. attack applies when the value goes down, release when it goes up.
        // attack and release are time constants in seconds.
        public static float DuckStep(float cur, float target, float dt, float attack, float release)
        {
            if (dt <= 0f) return cur;
            float tau = target < cur ? attack : release;
            if (tau <= 0f) return target;
            float k = 1f - (float)Math.Exp(-dt / tau);
            return cur + (target - cur) * k;
        }

        // Static compressor curve. Returns the gain change in dB (zero or negative).
        public static float CompressorGainDb(float inDb, float thresholdDb, float ratio)
        {
            if (ratio <= 1f || inDb <= thresholdDb) return 0f;
            float outDb = thresholdDb + (inDb - thresholdDb) / ratio;
            return outDb - inDb;
        }

        // RBJ cookbook peaking EQ. Returns normalised coefficients in this order:
        // { b0, b1, b2, a1, a2 }, with a0 divided out. The filter is y = b0*x + b1*x1 + b2*x2 - a1*y1 - a2*y2
        // in the usual form, so H(z) = (b0 + b1 z^-1 + b2 z^-2) / (1 + a1 z^-1 + a2 z^-2).
        public static double[] PeakingBiquad(double f0, double gainDb, double q, double sr)
        {
            double A = Math.Pow(10.0, gainDb / 40.0);
            double w0 = 2.0 * Math.PI * f0 / sr;
            double cw = Math.Cos(w0);
            double alpha = Math.Sin(w0) / (2.0 * q);

            double b0 = 1.0 + alpha * A;
            double b1 = -2.0 * cw;
            double b2 = 1.0 - alpha * A;
            double a0 = 1.0 + alpha / A;
            double a1 = -2.0 * cw;
            double a2 = 1.0 - alpha / A;

            return new double[] { b0 / a0, b1 / a0, b2 / a0, a1 / a0, a2 / a0 };
        }
    }
}
