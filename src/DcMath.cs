// Pure math helpers. No Unity or KSP dependencies so the offline test harness can compile this file.
using System;
using System.Collections.Generic;

namespace Lonapse
{
    public static class DcMath
    {
        public static float Clamp01(float x)
        {
            if (float.IsNaN(x)) return 0f;
            return x < 0f ? 0f : (x > 1f ? 1f : x);
        }

        public static float Clamp(float x, float lo, float hi)
        {
            if (float.IsNaN(x)) return lo;
            return x < lo ? lo : (x > hi ? hi : x);
        }

        public static float Lerp(float a, float b, float t)
        {
            return a + (b - a) * Clamp01(t);
        }

        /// <summary>clamp01((x - lo) / (hi - lo)), safe for hi == lo.</summary>
        public static float Ramp(double x, double lo, double hi)
        {
            if (double.IsNaN(x)) return 0f;
            double span = hi - lo;
            if (Math.Abs(span) < 1e-12) return x >= hi ? 1f : 0f;
            double t = (x - lo) / span;
            return t <= 0 ? 0f : (t >= 1 ? 1f : (float)t);
        }

        /// <summary>Smoothstep of Ramp.</summary>
        public static float Smooth(double x, double lo, double hi)
        {
            float t = Ramp(x, lo, hi);
            return t * t * (3f - 2f * t);
        }

        /// <summary>Blend factor for an exponential smoother: 1 - exp(-dt/tau).</summary>
        public static float ExpAlpha(float dt, float tau)
        {
            if (tau <= 1e-6f) return 1f;
            if (dt <= 0f) return 0f;
            return 1f - (float)Math.Exp(-dt / tau);
        }

        /// <summary>NaN or Infinity become 0.</summary>
        public static double Finite(double x)
        {
            return (double.IsNaN(x) || double.IsInfinity(x)) ? 0.0 : x;
        }

        public static float Finite(float x)
        {
            return (float.IsNaN(x) || float.IsInfinity(x)) ? 0f : x;
        }

        /// <summary>
        /// Scheduler mean interval: exp(lerp(ln tQuiet, ln tManic, s)) / rateMultiplier.
        /// </summary>
        public static float MeanInterval(float stress, float tQuiet, float tManic, float rateMultiplier)
        {
            if (tQuiet < 0.01f) tQuiet = 0.01f;
            if (tManic < 0.01f) tManic = 0.01f;
            if (rateMultiplier < 0.01f) rateMultiplier = 0.01f;
            double lq = Math.Log(tQuiet), lm = Math.Log(tManic);
            double s = Clamp01(stress);
            return (float)(Math.Exp(lq + (lm - lq) * s) / rateMultiplier);
        }

        /// <summary>The point p (relative to the body center) is in the shadow of a body with radius r, with the sun in
        /// direction s (relative to the body center, any length): behind the body and inside its shadow cylinder.</summary>
        public static bool InShadow(double px, double py, double pz, double sx, double sy, double sz, double r)
        {
            double sl = Math.Sqrt(sx * sx + sy * sy + sz * sz);
            if (sl <= 0 || r <= 0) return false;
            double t = (px * sx + py * sy + pz * sz) / sl;
            if (t >= 0) return false;
            double perp2 = px * px + py * py + pz * pz - t * t;
            return perp2 < r * r;
        }

        /// <summary>On-rails ("HIGH") warp above 1x silences everything. Physics ("LOW") warp does not.</summary>
        public static bool IsRailsWarp(bool modeIsHigh, float rate)
        {
            return modeIsHigh && rate > 1.0001f;
        }

        /// <summary>
        /// Time warp factor: 1 + gain x log10(rate), from 1 up to max. 1 at rate 1 or below. Used for the talk rate
        /// (more chats in a long warp) and the voice pitch (the kerbals sound sped up).
        /// </summary>
        public static float WarpFactor(float warpRate, float gain, float max)
        {
            if (!(warpRate > 1f) || gain <= 0f) return 1f;
            return Clamp(1f + gain * (float)Math.Log10(warpRate), 1f, Math.Max(1f, max));
        }

        /// <summary>
        /// Mean interval between intercom chats: more frequent on stations/bases and on long quiet coasts.
        /// Deliberately independent of the radio rate multiplier and of the crew count.
        /// </summary>
        public static float IntercomInterval(float baseInterval, bool station, bool quietCoast, float stationBoost, float quietBoost)
        {
            float div = 1f;
            if (station) div *= Math.Max(0.1f, stationBoost);
            if (quietCoast) div *= Math.Max(0.1f, quietBoost);
            return Math.Max(1f, baseInterval) / div;
        }

        /// <summary>
        /// Stress-driven intercom interval: log-linear from the low-stress interval (base with station / quiet-coast
        /// boosts) at stress 0 down to <paramref name="minInterval"/> at <paramref name="peakStress"/>, flat above.
        /// The stress range where chats may start is limited separately (intercomMaxStress).
        /// </summary>
        public static float IntercomIntervalStress(float lowStressInterval, float stress, float minInterval, float peakStress)
        {
            float lo = Math.Max(1f, lowStressInterval);
            float hi = Math.Max(1f, minInterval);
            if (hi >= lo) return lo;
            float t = Ramp(stress, 0.0, Math.Max(0.01f, peakStress));
            return (float)Math.Exp(Math.Log(lo) + (Math.Log(hi) - Math.Log(lo)) * t);
        }

        /// <summary>
        /// Lowest playback pitch of a crew voice (about -1.4 semitones). The clips are already shifted up into the
        /// Kerbal register (+5 st male, +4 st female) and a pitch under 1 slows them down again. The old floor of 0.75
        /// undid the whole male shift, so a low kerbal sounded like the plain, slow Piper voice.
        /// </summary>
        public const float CrewPitchMin = 0.92f;

        /// <summary>Highest pitch the crew spread uses: the default crewPitchMax, above it the voices clamp together.</summary>
        public const float CrewSpreadMax = 1.12f;

        /// <summary>
        /// Crew pitch: base pitch, raised by up to <paramref name="stressSemitones"/> at sk = 1, lowered up to 6% by
        /// G strain, clamped to [CrewPitchMin, maxPitch].
        /// </summary>
        public static float CrewPitch(float basePitch, float sk, float gStrain, float stressSemitones, float maxPitch)
        {
            float raise = (float)Math.Pow(2.0, Clamp01(sk) * stressSemitones / 12.0);
            float p = basePitch * raise * (1f - 0.06f * Clamp01(gStrain));
            return Clamp(p, CrewPitchMin, Math.Max(CrewPitchMin + 0.01f, maxPitch));
        }

        /// <summary>
        /// Crew pitch for a clip. A recorded panic clip (crew/panic) already has its panic in the recording: no
        /// stress raise on top, only the kerbal's own pitch and the G strain drop (panic sounded too high).
        /// </summary>
        public static float CrewPitch(float basePitch, float sk, float gStrain, float stressSemitones, float maxPitch,
                                      bool recordedPanic)
        {
            return CrewPitch(basePitch, recordedPanic ? 0f : sk, gStrain, stressSemitones, maxPitch);
        }

        /// <summary>
        /// Radio static slider (0..1) to a level (0..1). Loudness is heard on a log scale, so a straight line was only
        /// audible near 100 %. The square root makes 25 % half the full level.
        /// </summary>
        public static float StaticCurve(float amount)
        {
            return (float)Math.Sqrt(Clamp01(amount));
        }

        /// <summary>EVA running: 0 at or below the walk speed, 1 at the run speed. Unknown speeds
        /// (0 or less, or run not above walk) fall back to 1.0 and 2.5 m/s.</summary>
        public static float EvaRunLevel(float speed, float walkSpeed, float runSpeed)
        {
            if (float.IsNaN(speed)) return 0f;
            if (!(walkSpeed > 0f) || !(runSpeed > walkSpeed)) { walkSpeed = 1.0f; runSpeed = 2.5f; }
            return Clamp01((speed - walkSpeed) / (runSpeed - walkSpeed));
        }

        /// <summary>EVA exertion: eases toward the run level, slowly up (riseTau s) and slower down (fallTau s).</summary>
        public static float Exertion(float cur, float target, float dt, float riseTau, float fallTau)
        {
            if (float.IsNaN(cur)) cur = 0f;
            target = Clamp01(target);
            float tau = target > cur ? riseTau : fallTau;
            if (!(dt > 0f)) return Clamp01(cur);
            float a = tau > 0.001f ? 1f - (float)Math.Exp(-dt / tau) : 1f;
            return Clamp01(cur + (target - cur) * a);
        }

        /// <summary>Descent blip interval: slow at d = 0 down to fast at d = 1, never below fast.</summary>
        public static float DescentInterval(float d, float slow, float fast)
        {
            return Math.Max(fast, Lerp(slow, fast, (float)Math.Pow(Clamp01(d), 0.7)));
        }

        /// <summary>Kept-length fraction for clipped lines under stress: 1 below truncStart, down to minFrac at sk = 1.</summary>
        public static float TruncFraction(float sk, float truncStart, float minFrac)
        {
            if (sk <= truncStart) return 1f;
            return Lerp(1f, minFrac, Ramp(sk, truncStart, 1.0));
        }

        /// <summary>Same, but a recorded panic clip is never cut: it is already broken into pieces, so a cut only
        /// loses the last word.</summary>
        public static float TruncFraction(float sk, float truncStart, float minFrac, bool recordedPanic)
        {
            return recordedPanic ? 1f : TruncFraction(sk, truncStart, minFrac);
        }

        /// <summary>Crew line volume by the kerbal's stress: 0.70 calm to 0.90 at full stress (about 2 dB). Was
        /// 0.55 to 1.0 (5 dB), which made panic lines much louder than the rest.</summary>
        public static float CrewVolume(float sk) { return Lerp(0.70f, 0.90f, Clamp01(sk)); }

        /// <summary>Extra gain on a recorded panic clip: 2 dB down. It is much denser than a calm clip at the same
        /// loudness (peak to RMS 11 dB against 15 dB), so it hit the soft clipper behind the voice boost and sounded harsh.</summary>
        public const float PanicClipGain = 0.8f;

        /// <summary>Radio distortion on a crew line: up to 0.3 with stress (was 0.5) and 0.1 with G strain. A recorded
        /// panic clip already has its drive in the recording, so it only gets the G part.</summary>
        public static float CrewDistortion(float sk, float gStrain, bool recordedPanic)
        {
            float g = 0.1f * Clamp01(gStrain);
            return recordedPanic ? g : 0.3f * Smooth(sk, 0.5, 1.0) + g;
        }

        /// <summary>Crew callout volume: the same 2 dB range, a little higher (was 0.65 to 1.0).</summary>
        public static float CalloutVolume(float sk) { return Lerp(0.75f, 0.95f, Clamp01(sk)); }

        /// <summary>Mission Control volume: steady. Was 0.80 calm down to 0.60 under stress, which left MC far below
        /// a panicking crew.</summary>
        public const float McVolume = 0.75f;

        /// <summary>
        /// Intercom pair: pitch multiplier for the second voice so the two base pitches differ by at least minGap.
        /// Returns 1 when they already differ enough.
        /// </summary>
        /// <summary>
        /// Two kerbals aboard sounded the same (pitch 1.005 and 0.948). Multipliers for the
        /// pitches of one crew (one voice model) so every two are at least minGap apart: sorted, pushed up from the
        /// lowest, then all shifted back so the average stays, kept in lo..hi. A big crew (a station) that does not
        /// fit at minGap gets a smaller gap: the range wins, because a pitch under lo undoes the Kerbal shift.
        /// One pitch or none: all 1.
        /// </summary>
        public static float[] SpreadPitches(float[] pitch, float minGap, float lo, float hi)
        {
            int n = pitch == null ? 0 : pitch.Length;
            var mul = new float[n];
            for (int i = 0; i < n; i++) mul[i] = 1f;
            if (n < 2) return mul;
            if (hi > lo) minGap = Math.Min(minGap, (hi - lo) / (n - 1));
            var order = new int[n];
            for (int i = 0; i < n; i++) order[i] = i;
            Array.Sort(order, (a, b) => pitch[a] != pitch[b] ? pitch[a].CompareTo(pitch[b]) : a.CompareTo(b));
            var p = new float[n];
            float before = 0f, after = 0f;
            for (int k = 0; k < n; k++)
            {
                p[k] = pitch[order[k]];
                if (k > 0 && p[k] < p[k - 1] + minGap) p[k] = p[k - 1] + minGap;
                before += pitch[order[k]];
                after += p[k];
            }
            // base pitches that alone span more than lo..hi: squeeze them in, the order stays
            float span = p[n - 1] - p[0];
            if (hi > lo && span > hi - lo)
            {
                after = 0f;
                for (int k = 0; k < n; k++) { p[k] = p[0] + (p[k] - p[0]) * (hi - lo) / span; after += p[k]; }
            }
            float shift = (before - after) / n;
            // the whole set must fit in lo..hi (the gap and the squeeze above make sure it can): shift into the range
            float low = p[0] + shift, high = p[n - 1] + shift;
            if (low < lo) shift += lo - low;
            else if (high > hi) shift -= Math.Min(high - hi, low - lo);
            for (int k = 0; k < n; k++)
            {
                int i = order[k];
                mul[i] = pitch[i] > 0.01f ? (p[k] + shift) / pitch[i] : 1f;
            }
            return mul;
        }

        public static float PairPitchNudge(float pitchA, float pitchB, float minGap)
        {
            return PairPitchNudge(pitchA, pitchB, minGap, 0f, float.MaxValue);
        }

        /// <summary>
        /// PairPitchNudge with the crew range: the second voice goes the other way when its side would leave lo..hi.
        /// Pushed down under CrewPitchMin it was clamped back to the first voice, and it undid the Kerbal shift.
        /// </summary>
        public static float PairPitchNudge(float pitchA, float pitchB, float minGap, float lo, float hi)
        {
            if (pitchB <= 0.01f) return 1f;
            float diff = pitchB - pitchA;
            if (Math.Abs(diff) >= minGap) return 1f;
            float up = pitchA + minGap, down = pitchA - minGap;
            bool goUp = diff >= 0f;
            if (goUp && up > hi && down >= lo) goUp = false;
            else if (!goUp && down < lo && up <= hi) goUp = true;
            return (goUp ? up : down) / pitchB;
        }

        /// <summary>Warp-end ping only when the previous one is at least cooldown seconds ago.</summary>
        public static bool WarpPingAllowed(float now, float lastPing, float cooldown)
        {
            return now - lastPing >= cooldown;
        }

        public static float Uniform(Random rng, float lo, float hi)
        {
            return lo + (hi - lo) * (float)rng.NextDouble();
        }
    }

    /// <summary>
    /// Shuffle bag over indices 0..count-1. Never returns any of the last min(3, count-1) picks.
    /// </summary>
    public class ShuffleBag
    {
        private readonly int count;
        private readonly List<int> remaining = new List<int>();
        private readonly int[] recent;
        private int recentLen;
        private int recentHead;

        public ShuffleBag(int count)
        {
            this.count = count < 0 ? 0 : count;
            int r = Math.Min(3, this.count - 1);
            recent = new int[r < 0 ? 0 : r];
        }

        public int Count { get { return count; } }

        private bool IsRecent(int idx)
        {
            for (int i = 0; i < recentLen; i++)
                if (recent[i] == idx) return true;
            return false;
        }

        /// <summary>Returns -1 when empty.</summary>
        public int Next(Random rng)
        {
            if (count <= 0) return -1;
            if (count == 1) return 0;
            if (remaining.Count == 0)
                for (int i = 0; i < count; i++) remaining.Add(i);

            // Pick randomly among remaining entries that are not recent. If all remaining are recent
            // (possible right after a refill), refill with every non-recent index instead.
            int eligible = 0;
            for (int i = 0; i < remaining.Count; i++)
                if (!IsRecent(remaining[i])) eligible++;
            if (eligible == 0)
            {
                remaining.Clear();
                for (int i = 0; i < count; i++)
                    if (!IsRecent(i)) remaining.Add(i);
                eligible = remaining.Count;
                if (eligible == 0) return 0;
            }

            int k = rng.Next(eligible);
            int chosenPos = -1;
            for (int i = 0; i < remaining.Count; i++)
            {
                if (IsRecent(remaining[i])) continue;
                if (k == 0) { chosenPos = i; break; }
                k--;
            }
            int chosen = remaining[chosenPos];
            remaining.RemoveAt(chosenPos);

            if (recent.Length > 0)
            {
                recent[recentHead] = chosen;
                recentHead = (recentHead + 1) % recent.Length;
                if (recentLen < recent.Length) recentLen++;
            }
            return chosen;
        }
    }

    /// <summary>
    /// Push-to-talk click: about 25 ms of band-limited noise with a 1 ms attack and exponential decay, plus a short
    /// low "thump". Deterministic for a given seed. Pure, tested offline.
    /// </summary>
    public static class ClickSynth
    {
        public static float[] Generate(int rate, int seed, float lengthSec = 0.025f)
        {
            if (rate < 8000) rate = 44100;
            int n = Math.Max(16, (int)(rate * lengthSec));
            float[] d = new float[n];
            Random r = new Random(seed);
            double dt = 1.0 / rate;
            // one-pole high-pass (~700 Hz) then low-pass (~5 kHz) on white noise: a dry "tsk"
            double hpA = Math.Exp(-2.0 * Math.PI * 700.0 * dt), lpA = Math.Exp(-2.0 * Math.PI * 5000.0 * dt);
            double hpPrevIn = 0, hpPrevOut = 0, lp = 0;
            double attack = 0.001, decay = lengthSec / 4.0;
            float peak = 0f;
            for (int i = 0; i < n; i++)
            {
                double t = i * dt;
                double x = r.NextDouble() * 2.0 - 1.0;
                double hp = hpA * (hpPrevOut + x - hpPrevIn);
                hpPrevIn = x; hpPrevOut = hp;
                lp = lp * lpA + hp * (1.0 - lpA);
                double env = (t < attack ? t / attack : 1.0) * Math.Exp(-Math.Max(0.0, t - attack) / decay);
                double thump = 0.35 * Math.Sin(2.0 * Math.PI * 180.0 * t) * Math.Exp(-t / 0.004);
                float v = (float)(env * lp * 3.0 + thump * (t < attack ? t / attack : 1.0));
                d[i] = v;
                float a = Math.Abs(v);
                if (a > peak) peak = a;
            }
            // normalize to 0.8 peak, 2 ms fade-out so the clip ends at zero
            float g = peak > 1e-6f ? 0.8f / peak : 0f;
            int fade = Math.Min(n, (int)(rate * 0.002));
            for (int i = 0; i < n; i++)
            {
                float f = i >= n - fade ? (n - 1 - i) / (float)Math.Max(1, fade) : 1f;
                d[i] *= g * f;
            }
            return d;
        }
    }
}
