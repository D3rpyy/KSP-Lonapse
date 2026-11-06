// Pure math for the talking-kerbal mouth (req 9). No Unity or KSP dependencies so the offline test harness can
// compile this file. MouthDriver feeds it the output samples of a voice channel and applies the result to the jaw.
using System;
using System.Collections.Generic;

namespace Lonapse
{
    public static class MouthMath
    {
        /// <summary>Root mean square of the whole buffer. 0 for null or empty buffers, NaN samples count as 0.</summary>
        public static float Rms(float[] buf)
        {
            return buf == null ? 0f : Rms(buf, buf.Length);
        }

        /// <summary>Root mean square of the first n samples.</summary>
        public static float Rms(float[] buf, int n)
        {
            if (buf == null || n <= 0) return 0f;
            if (n > buf.Length) n = buf.Length;
            double sum = 0.0;
            for (int i = 0; i < n; i++)
            {
                float s = buf[i];
                if (float.IsNaN(s) || float.IsInfinity(s)) continue;
                sum += (double)s * s;
            }
            return (float)Math.Sqrt(sum / n);
        }

        /// <summary>Largest absolute sample of the first n samples (digital silence check).</summary>
        public static float Peak(float[] buf, int n)
        {
            if (buf == null || n <= 0) return 0f;
            if (n > buf.Length) n = buf.Length;
            float p = 0f;
            for (int i = 0; i < n; i++)
            {
                float a = Math.Abs(buf[i]);
                if (a > p && !float.IsNaN(a) && !float.IsInfinity(a)) p = a;
            }
            return p;
        }

        /// <summary>
        /// One-pole envelope follower: rises toward x with time constant attack, falls with release (seconds).
        /// </summary>
        public static float Follow(float env, float x, float dt, float attack, float release)
        {
            if (float.IsNaN(env) || float.IsInfinity(env)) env = 0f;
            if (float.IsNaN(x) || float.IsInfinity(x)) x = 0f;
            if (dt <= 0f || float.IsNaN(dt)) return env;
            float tau = x > env ? attack : release;
            if (tau <= 1e-4f) return x;
            float a = 1f - (float)Math.Exp(-dt / tau);
            return env + (x - env) * a;
        }

        /// <summary>Jaw open amount 0..1 from the envelope: nothing below the gate, then linear with gain.</summary>
        public static float Open(float env, float gate, float gain)
        {
            if (float.IsNaN(env) || env <= gate) return 0f;
            float o = (env - gate) * gain;
            if (float.IsNaN(o)) return 0f;
            return o < 0f ? 0f : (o > 1f ? 1f : o);
        }

        /// <summary>A rig is warm (its closed reference may be tracked) after minFrames frames and minAge seconds.</summary>
        public static bool RefWarm(int frames, float age, int minFrames, float minAge)
        {
            if (float.IsNaN(age)) return false;
            return frames >= minFrames && age >= minAge;
        }

        /// <summary>One-pole step weight 0..1 for a time constant tau (seconds). 0 for dt &lt;= 0, 1 for tau &lt;= 0.</summary>
        public static float DecayAlpha(float dt, float tau)
        {
            if (float.IsNaN(dt) || dt <= 0f) return 0f;
            if (float.IsNaN(tau) || tau <= 1e-4f) return 1f;
            float a = 1f - (float)Math.Exp(-dt / tau);
            return a < 0f ? 0f : (a > 1f ? 1f : a);
        }

        /// <summary>Syllable rate of Flap for this seed, 6..8 Hz.</summary>
        public static float FlapHz(uint seed)
        {
            double u = (seed % 1000u) / 1000.0;
            return (float)(6.0 + 2.0 * u);
        }

        /// <summary>
        /// Procedural talking when no audio level is available: a 6..8 Hz syllable wave (rate from the seed)
        /// shaped by a slow phrase envelope, so the mouth does not flap like a metronome. Returns 0..1.
        /// </summary>
        public static float Flap(float t, uint seed)
        {
            if (float.IsNaN(t) || float.IsInfinity(t)) return 0f;
            double f = FlapHz(seed);
            double ph = ((seed >> 10) % 628u) / 100.0;
            double syl = 0.5 + 0.5 * Math.Sin(2.0 * Math.PI * f * t + ph);
            double phrase = 0.65 + 0.35 * Math.Sin(2.0 * Math.PI * 0.9 * t + 1.7 * ph);
            double v = syl * syl * phrase; // squared: short open peaks, longer closed parts
            return v < 0.0 ? 0f : (v > 1.0 ? 1f : (float)v);
        }

        /// <summary>
        /// Signed twist angle in degrees (-180..180) of the rotation quaternion (x, y, z, w) around the local axis
        /// with the given index (0 = X, 1 = Y, other = Z). For q = AngleAxis(a, axis) this returns a.
        /// The swing part (rotation around the other axes) is ignored. q and -q give the same result.
        /// </summary>
        public static float HingeAngleDeg(float x, float y, float z, float w, int axis)
        {
            if (float.IsNaN(x) || float.IsNaN(y) || float.IsNaN(z) || float.IsNaN(w)) return 0f;
            float c = axis == 0 ? x : (axis == 1 ? y : z);
            if (w < 0f) { c = -c; w = -w; }
            if (Math.Abs(c) < 1e-9f && w < 1e-9f) return 0f; // pure swing of 180 degrees: no defined twist
            double a = 2.0 * Math.Atan2(c, w) * (180.0 / Math.PI);
            if (a > 180.0) a -= 360.0;
            else if (a < -180.0) a += 360.0;
            return (float)a;
        }

        /// <summary>
        /// Linear ramp of the talk weight toward 1 while talking and toward 0 otherwise, full swing in fade seconds.
        /// </summary>
        public static float RampWeight(float w, bool talking, float dt, float fade)
        {
            if (float.IsNaN(w) || float.IsInfinity(w)) w = 0f;
            if (float.IsNaN(dt) || dt < 0f) dt = 0f;
            float step = fade <= 1e-4f ? 1f : dt / fade;
            w = talking ? w + step : w - step;
            return w < 0f ? 0f : (w > 1f ? 1f : w);
        }

        /// <summary>Smoothstep 0..1, used to ease the talk weight in and out.</summary>
        public static float Smooth01(float t)
        {
            if (float.IsNaN(t) || t <= 0f) return 0f;
            if (t >= 1f) return 1f;
            return t * t * (3f - 2f * t);
        }

        /// <summary>
        /// Open amount for one portrait render. Portraits refresh at 7 to 10 fps, so a smooth 6 to 8 Hz flap aliases
        /// into nothing. While the voice is audible the jaw alternates per portrait frame between a wide value
        /// (at least openFloor, more when the envelope is louder) and closedValue. Silence closes the mouth.
        /// </summary>
        public static float PortraitOpen(bool openPhase, bool voiced, float open, float openFloor, float closedValue)
        {
            if (!voiced) return 0f;
            if (float.IsNaN(open)) open = 0f;
            float v = openPhase ? Math.Max(open, openFloor) : closedValue;
            return v < 0f ? 0f : (v > 1f ? 1f : v);
        }

        /// <summary>Frame length of a loudness track in seconds (100 frames per second).</summary>
        public const float EnvelopeFrame = 0.01f;

        /// <summary>
        /// Loudness track of a clip from its samples (interleaved channels): the RMS of each frame of frame seconds,
        /// mixed to mono, scaled so the 95th percentile of the voiced frames is 1 and clamped to 0..1. A quiet and a
        /// loud recording move the mouth the same. Null for no data.
        /// </summary>
        public static float[] Envelope(float[] data, int channels, int frequency, float frame)
        {
            if (data == null || channels < 1 || frequency < 1 || frame <= 0f) return null;
            int per = Math.Max(1, (int)Math.Round(frequency * frame));
            int samples = data.Length / channels;
            int n = (samples + per - 1) / per;
            if (n == 0) return null;
            var env = new float[n];
            for (int f = 0; f < n; f++)
            {
                double sum = 0;
                int start = f * per, end = Math.Min(samples, start + per), cnt = 0;
                for (int i = start; i < end; i++)
                {
                    double m = 0;
                    for (int c = 0; c < channels; c++)
                    {
                        float x = data[i * channels + c];
                        if (!float.IsNaN(x)) m += x;
                    }
                    m /= channels;
                    sum += m * m;
                    cnt++;
                }
                env[f] = cnt > 0 ? (float)Math.Sqrt(sum / cnt) : 0f;
            }
            float max = 0f;
            for (int f = 0; f < n; f++) if (env[f] > max) max = env[f];
            if (max < 1e-5f) { for (int f = 0; f < n; f++) env[f] = 0f; return env; }
            var voiced = new List<float>();
            for (int f = 0; f < n; f++) if (env[f] > 0.05f * max) voiced.Add(env[f]);
            voiced.Sort();
            float top = voiced.Count > 0 ? voiced[Math.Min(voiced.Count - 1, (int)(voiced.Count * 0.95))] : max;
            if (top < 1e-5f) top = max;
            for (int f = 0; f < n; f++) { float v = env[f] / top; env[f] = v > 1f ? 1f : v; }
            return env;
        }

        /// <summary>The track at clip time t in seconds, linear between frames. 0 outside the clip.</summary>
        public static float EnvelopeAt(float[] env, float frame, float t)
        {
            if (env == null || env.Length == 0 || frame <= 0f || float.IsNaN(t) || t < 0f) return 0f;
            float x = t / frame;
            int i = (int)x;
            if (i >= env.Length) return 0f;
            float a = env[i], b = i + 1 < env.Length ? env[i + 1] : 0f;
            return a + (b - a) * (x - i);
        }

        /// <summary>Jaw open amount 0..1 from a loudness track value: closed below 0.12 (pauses and breath), fully
        /// open from 0.8.</summary>
        public static float EnvelopeOpen(float v)
        {
            if (float.IsNaN(v) || v <= 0.12f) return 0f;
            float o = (v - 0.12f) / 0.68f;
            return o > 1f ? 1f : o;
        }

        /// <summary>Hinge axis index from the tuning float: 0 = X, 1 = Y, anything else = Z.</summary>
        public static int AxisIndex(float axis)
        {
            if (float.IsNaN(axis)) return 2;
            int i = (int)Math.Round(axis);
            return i == 0 || i == 1 ? i : 2;
        }
    }
}
