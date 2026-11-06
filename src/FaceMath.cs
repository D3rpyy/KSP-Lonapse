// Pure math for kerbal faces: the mood of a voice line, the expression blend, the head-turn angle and
// the smoothing. No Unity or KSP dependencies so the offline test harness can compile this file. FaceDriver feeds it
// the playing line and the bone positions and applies the result to the Animator and to bn_headPivot_b01.
//
// Expression sign (stock kerbalExpressionSystem.IVAUpdate, read in the IL): targetExpression = (whee * 0.5 -
// panic * 0.5) * 2 = whee - panic, with whee and panic clamped to 0..1. The stock value is the moving average of that
// target, lerped into `expression`. So positive is the grin side ("whee") and negative is the fear side ("panic").
// The moods below use the same convention: fear is negative, grin is positive.
using System;

namespace Lonapse
{
    public static class FaceMath
    {
        public const float MoodPanic = -1f;     // panic line: full fear
        public const float MoodTense = -0.5f;   // tense line: mild fear
        public const float MoodGrin = 0.8f;     // calm goofy line, calm chatter, relief or intercom: grin
        public const float MoodBadass = 0.4f;   // badass kerbal on a tense line: mild grin
        public const float MoodCalm = 0.2f;     // other calm lines (plain callouts): a hint of a smile
        public const float ListenerScale = 0.5f; // listeners in the same vessel react at half strength
        public const float GlanceScale = 0.5f;  // the speaker glances at a listener at half the angle
        public const float MaxTurnLimit = 90f;  // headTurnMaxDeg is clamped to 0..this

        /// <summary>
        /// Mood target of a line, -1 (fear) .. +1 (grin). band: calm, tense, panic (anything else counts as calm).
        /// ev and phase are the table columns of the line (may be null). intercom: the line plays on the intercom
        /// preset. badass: the speaking kerbal is badass (never scared on a tense line, a little scared at panic).
        /// </summary>
        public static float Mood(string band, string ev, string phase, bool intercom, bool badass)
        {
            band = band ?? "calm";
            if (band == "panic") return badass ? MoodTense : MoodPanic;
            if (band == "tense") return badass ? MoodBadass : MoodTense;
            bool goofy = intercom || ev == "chatter" || ev == "relief" || IsGoofyPhase(phase);
            if (goofy) return MoodGrin;
            return badass ? MoodBadass : MoodCalm;
        }

        /// <summary>Below this the stock face counts as fearful (KSP's own panic, high g).</summary>
        public const float FearFace = -0.15f;

        /// <summary>The face at the kerbal's own panic threshold: clearly scared.</summary>
        public const float FaceAtPanic = -0.5f;

        /// <summary>
        /// How happy a face may look at this personal stress (sk, 0..1), anchored to the kerbal's own thresholds, so
        /// the face matches the voice: free below tenseAt, then down to clearly scared (FaceAtPanic) at
        /// panicAt, then on to full fear at 1. A brave or badass kerbal has later thresholds, so it stays calm longer.
        /// </summary>
        public static float StressCap(float stress, float tenseAt, float panicAt)
        {
            if (float.IsNaN(stress) || float.IsInfinity(stress)) stress = 0f;
            if (float.IsNaN(tenseAt) || float.IsInfinity(tenseAt)) tenseAt = 0.3f;
            if (float.IsNaN(panicAt) || float.IsInfinity(panicAt)) panicAt = 0.7f;
            tenseAt = Clamp(tenseAt, 0f, 0.98f);
            panicAt = Clamp(panicAt, tenseAt + 0.01f, 0.999f);
            if (stress <= tenseAt) return 1f;
            if (stress <= panicAt) return 1f + (FaceAtPanic - 1f) * (stress - tenseAt) / (panicAt - tenseAt);
            return Clamp(FaceAtPanic + (-1f - FaceAtPanic) * (stress - panicAt) / (1f - panicAt), -1f, 1f);
        }

        /// <summary>
        /// The mood a talking or listening face moves to. The line mood, but a line never makes the face happier than
        /// it is: a fearful stock face (stockMood below FearFace) keeps its fear on a calm line, a fearful line may
        /// deepen it. The stress caps how happy the face can look (StressCap).
        /// </summary>
        public static float HeldMood(float lineMood, float stockMood, float stress, float tenseAt, float panicAt)
        {
            if (float.IsNaN(lineMood)) lineMood = 0f;
            if (float.IsNaN(stockMood)) stockMood = 0f;
            float m = lineMood;
            if (stockMood < FearFace && m > stockMood) m = stockMood;
            float cap = StressCap(stress, tenseAt, panicAt);
            if (m > cap) m = cap;
            return Clamp(m, -1f, 1f);
        }

        /// <summary>
        /// The mood of a face between lines: the stock face, but never happier than the stress allows. So a stressed
        /// kerbal no longer grins between two scared lines (stock KSP grins at speed and G). Below tenseAt it is the
        /// stock face.
        /// </summary>
        public static float IdleMood(float stockMood, float stress, float tenseAt, float panicAt)
        {
            if (float.IsNaN(stockMood) || float.IsInfinity(stockMood)) stockMood = 0f;
            return Clamp(Math.Min(stockMood, StressCap(stress, tenseAt, panicAt)), -1f, 1f);
        }

        /// <summary>
        /// The mood written to the face: the idle mood, moved toward the held line mood by the line weight (0..1). With
        /// no stress the idle mood is the stock face, so this is the old blend of stock face and line.
        /// </summary>
        public static float FaceMood(float lineMood, float lineWeight, float stockMood, float stress, float tenseAt, float panicAt)
        {
            float idle = IdleMood(stockMood, stress, tenseAt, panicAt);
            float line = HeldMood(lineMood, stockMood, stress, tenseAt, panicAt);
            return Clamp(idle + (line - idle) * Clamp01(lineWeight), -1f, 1f);
        }

        /// <summary>Same as PhraseTable.IsGoofyPhase, repeated here so this file stays free of the table.</summary>
        public static bool IsGoofyPhase(string p)
        {
            return p == "goofy" || p == "prelaunch" || p == "orbit" || p == "coast" || p == "landed";
        }

        /// <summary>Band name for the enum value of KerbalVoice.band (0 calm, 1 tense, 2 panic).</summary>
        public static string BandName(int band)
        {
            return band >= 2 ? "panic" : (band == 1 ? "tense" : "calm");
        }

        /// <summary>Strongest of two moods by magnitude (a listener of two speakers reacts to the stronger line).</summary>
        public static float Stronger(float a, float b)
        {
            return Math.Abs(b) > Math.Abs(a) ? b : a;
        }

        /// <summary>
        /// Expression value written to the Animator: the stock value moved toward sign * mood by weight. Weight is
        /// clamped to 0..1, the result to -1..1. sign below 0 flips the mood (faceExpressionSign).
        /// </summary>
        public static float Blend(float stock, float mood, float weight, float sign)
        {
            if (float.IsNaN(stock) || float.IsInfinity(stock)) stock = 0f;
            if (float.IsNaN(mood) || float.IsInfinity(mood)) mood = 0f;
            float w = Clamp01(weight);
            float target = Clamp(sign < 0f ? -mood : mood, -1f, 1f);
            return Clamp(stock + (target - stock) * w, -1f, 1f);
        }

        /// <summary>Weight ramp with separate rise and fall times in seconds. Result 0..1.</summary>
        public static float Ramp(float w, bool on, float dt, float rise, float fall)
        {
            if (float.IsNaN(w) || float.IsInfinity(w)) w = 0f;
            if (float.IsNaN(dt) || dt < 0f) dt = 0f;
            float t = on ? rise : fall;
            float step = t <= 1e-4f ? 1f : dt / t;
            w = on ? w + step : w - step;
            return Clamp01(w);
        }

        /// <summary>Smoothstep 0..1.</summary>
        public static float Smooth01(float t)
        {
            if (float.IsNaN(t) || t <= 0f) return 0f;
            if (t >= 1f) return 1f;
            return t * t * (3f - 2f * t);
        }

        /// <summary>Exponential follow with a rate in 1/s (headTurnSpeed). Rate 0 or less holds the value.</summary>
        public static float Follow(float cur, float target, float dt, float rate)
        {
            if (float.IsNaN(cur) || float.IsInfinity(cur)) cur = 0f;
            if (float.IsNaN(target) || float.IsInfinity(target)) return cur;
            if (float.IsNaN(dt) || dt <= 0f || float.IsNaN(rate) || rate <= 0f) return cur;
            float a = 1f - (float)Math.Exp(-rate * dt);
            return cur + (target - cur) * a;
        }

        /// <summary>
        /// Stress of the voice for a head nod, 0..1: a fast follow of the open amount minus a slow one, so the head
        /// dips when the voice gets louder than its own average (a stressed syllable) and not on a steady voice.
        /// fast and slow are the follower states, kept per kerbal.
        /// </summary>
        public static float Accent(ref float fast, ref float slow, float open, float dt)
        {
            if (float.IsNaN(open)) open = 0f;
            fast = Follow(fast, open, dt, 12f);
            slow = Follow(slow, open, dt, 1.6f);
            float a = (fast - slow) * 2.5f;
            return a < 0f ? 0f : (a > 1f ? 1f : a);
        }

        /// <summary>A listener's single nod, t seconds after the line ended: half a sine over dur seconds, 0 outside.</summary>
        public static float ListenerNod(float t, float dur)
        {
            if (float.IsNaN(t) || t < 0f || dur <= 0f || t > dur) return 0f;
            return (float)Math.Sin(Math.PI * t / dur);
        }

        /// <summary>Wraps to -180..180 degrees.</summary>
        public static float WrapDeg(float a)
        {
            if (float.IsNaN(a) || float.IsInfinity(a)) return 0f;
            a = a % 360f;
            if (a > 180f) a -= 360f;
            else if (a < -180f) a += 360f;
            return a;
        }

        /// <summary>Angle wrapped to -180..180 and limited to +-max (max clamped to 0..MaxTurnLimit).</summary>
        public static float ClampAngle(float a, float max)
        {
            if (float.IsNaN(max) || max < 0f) max = 0f;
            if (max > MaxTurnLimit) max = MaxTurnLimit;
            return Clamp(WrapDeg(a), -max, max);
        }

        /// <summary>
        /// Signed angle in degrees around axis k from f to t, both projected onto the plane normal to k. Rotating f by
        /// this angle around k (Rodrigues, same as Quaternion.AngleAxis) points it at the projection of t. Returns 0
        /// when k is zero or either projection is too short (target straight above or below).
        /// </summary>
        public static float SignedAngleAround(float kx, float ky, float kz, float fx, float fy, float fz,
                                              float tx, float ty, float tz)
        {
            float kl = (float)Math.Sqrt(kx * kx + ky * ky + kz * kz);
            if (!(kl > 1e-6f)) return 0f;
            kx /= kl; ky /= kl; kz /= kl;
            float fd = fx * kx + fy * ky + fz * kz;
            fx -= fd * kx; fy -= fd * ky; fz -= fd * kz;
            float td = tx * kx + ty * ky + tz * kz;
            tx -= td * kx; ty -= td * ky; tz -= td * kz;
            float fl = (float)Math.Sqrt(fx * fx + fy * fy + fz * fz);
            float tl = (float)Math.Sqrt(tx * tx + ty * ty + tz * tz);
            if (!(fl > 1e-5f) || !(tl > 1e-5f)) return 0f;
            float cx = fy * tz - fz * ty, cy = fz * tx - fx * tz, cz = fx * ty - fy * tx;
            float sin = cx * kx + cy * ky + cz * kz;
            float cos = fx * tx + fy * ty + fz * tz;
            return (float)(Math.Atan2(sin, cos) * 180.0 / Math.PI);
        }

        /// <summary>Head-turn target: the angle to the speaker limited to +-max, times scale (GlanceScale for the speaker).</summary>
        public static float TurnTarget(float angleDeg, float maxDeg, float scale)
        {
            return ClampAngle(angleDeg, maxDeg) * Clamp01(scale);
        }

        /// <summary>Axis index 0 (X), 1 (Y) or 2 (Z) from the tuning value (rounded, clamped).</summary>
        public static int AxisIndex(float axis)
        {
            if (float.IsNaN(axis)) return 1;
            int i = (int)Math.Round(axis);
            return i < 0 ? 0 : (i > 2 ? 2 : i);
        }

        /// <summary>Test mood: a 4 s sine from grin to fear and back, -1..1.</summary>
        public static float TestMood(float t)
        {
            if (float.IsNaN(t)) return 0f;
            return (float)Math.Sin(t * 2.0 * Math.PI / 4.0);
        }

        /// <summary>Test head turn: a 3 s sine between -max and +max (max clamped like ClampAngle).</summary>
        public static float TestYaw(float t, float max)
        {
            if (float.IsNaN(t)) return 0f;
            return ClampAngle((float)Math.Sin(t * 2.0 * Math.PI / 3.0) * max, max);
        }

        /// <summary>Override check: the parameter read back after the Animator still equals our write (NaN: no).</summary>
        public static bool OverrideHeld(float read, float written)
        {
            if (float.IsNaN(read) || float.IsNaN(written)) return false;
            return Math.Abs(read - written) <= OverrideEps;
        }

        public const float OverrideEps = 1e-4f;

        public static float Clamp01(float v)
        {
            if (float.IsNaN(v)) return 0f;
            return v < 0f ? 0f : (v > 1f ? 1f : v);
        }

        public static float Clamp(float v, float lo, float hi)
        {
            if (float.IsNaN(v)) return lo > 0f ? lo : (hi < 0f ? hi : 0f);
            return v < lo ? lo : (v > hi ? hi : v);
        }
    }
}
