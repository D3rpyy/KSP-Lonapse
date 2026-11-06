// Pacing primitives for the chatter scheduler: priority, token bucket, per-key cooldowns,
// escalation level and a time-weighted rolling statistic. Pure C#, no Unity, tested offline.
using System;
using System.Collections.Generic;

namespace Lonapse
{
    // Higher value means higher priority. Callouts preempt warnings, warnings preempt chatter.
    public enum Priority { Chatter, Warning, Callout }

    // Global rate limiter. Holds up to `capacity` tokens and refills at `perMinute` tokens per minute.
    public class TokenBucket
    {
        readonly float capacity;
        readonly float perMinute;
        float tokens;
        float last;
        bool started;

        public TokenBucket(float capacity, float perMinute)
        {
            this.capacity = Math.Max(0f, capacity);
            this.perMinute = Math.Max(0f, perMinute);
            tokens = this.capacity;
        }

        // Takes one token if available. `now` is in seconds and must not go backwards.
        public bool TryTake(float now)
        {
            if (!started) { started = true; last = now; }
            float elapsed = now - last;
            if (elapsed > 0f)
            {
                tokens = Math.Min(capacity, tokens + elapsed * perMinute / 60f);
                last = now;
            }
            if (tokens >= 1f) { tokens -= 1f; return true; }
            return false;
        }
    }

    // Cooldown per key. A key is ready when `cd` seconds have passed since its last Stamp.
    public class Cooldowns
    {
        readonly Dictionary<string, float> stamps = new Dictionary<string, float>();

        public bool Ready(string key, float now, float cd)
        {
            float last;
            if (!stamps.TryGetValue(key, out last)) return true;
            return now - last >= cd;
        }

        public void Stamp(string key, float now)
        {
            stamps[key] = now;
        }

        public void Clear()
        {
            stamps.Clear();
        }
    }

    // Panic-style escalation level. Bump adds one step (up to cap), Decay is exponential with time constant tau.
    public class Escalation
    {
        public float Level { get; private set; }

        public void Bump(float cap)
        {
            Level = Math.Min(cap, Level + 1f);
        }

        public void Decay(float dt, float tau)
        {
            if (dt <= 0f) return;
            if (tau <= 0f) { Level = 0f; return; }
            Level *= (float)Math.Exp(-dt / tau);
        }

        // Returns Level * step, limited to cap.
        public float Offset(float step, float cap)
        {
            return Math.Min(cap, Level * step);
        }
    }

    // Mean and standard deviation over roughly the last `window` seconds. Each sample is weighted by its dt.
    public class RollingStat
    {
        struct Sample { public float X; public float Dt; }

        readonly float window;
        readonly List<Sample> samples = new List<Sample>();
        float total;

        public RollingStat(float window)
        {
            this.window = Math.Max(0f, window);
        }

        public void Add(float x, float dt)
        {
            if (dt <= 0f || float.IsNaN(x)) return;
            samples.Add(new Sample { X = x, Dt = dt });
            total += dt;
            // Drop the oldest samples while the remaining ones still cover the window.
            while (samples.Count > 1 && total - samples[0].Dt >= window)
            {
                total -= samples[0].Dt;
                samples.RemoveAt(0);
            }
        }

        public float StdDev
        {
            get
            {
                if (samples.Count == 0 || total <= 0f) return 0f;
                double mean = 0;
                foreach (Sample s in samples) mean += s.X * s.Dt;
                mean /= total;
                double var = 0;
                foreach (Sample s in samples) { double d = s.X - mean; var += s.Dt * d * d; }
                var /= total;
                return (float)Math.Sqrt(Math.Max(0.0, var));
            }
        }
    }

    public enum StationAction { Check, Exchange }

    // Helpers for the scheduler. Pure, tested offline.
    /// <summary>What a stage holds, for the staging callout.</summary>
    public enum StageKind { Separation, Ignition, Any }

    public static class PacingRules
    {
        /// <summary>A decoupler or fairing makes it a separation (also with an engine), an engine alone an ignition.</summary>
        public static StageKind ClassifyStage(bool hasDecoupler, bool hasEngine)
        {
            if (hasDecoupler) return StageKind.Separation;
            if (hasEngine) return StageKind.Ignition;
            return StageKind.Any;
        }

        /// <summary>Kind of a staging scene by its id: staging_ign_* ignition, staging_any_* neutral, the rest separation.</summary>
        public static StageKind StagingSceneKind(string sceneId)
        {
            if (sceneId == null) return StageKind.Any;
            if (sceneId.StartsWith("staging_ign", StringComparison.Ordinal)) return StageKind.Ignition;
            if (sceneId.StartsWith("staging_any", StringComparison.Ordinal)) return StageKind.Any;
            return StageKind.Separation;
        }

        /// <summary>Scream scenes (scene id scream_*) get their own per-kerbal cooldown and never go on the intercom.</summary>
        public static bool IsScream(string sceneId)
        {
            return sceneId != null && sceneId.StartsWith("scream", StringComparison.Ordinal);
        }

        // Req 9: stations and bases use one mechanism. "check": MC check-ins only. "exchange": regular exchanges only.
        // "auto": check-ins below burstBand, regular exchanges at or above it.
        public static StationAction StationChoice(string style, float stress, float burstBand)
        {
            if (style == "check") return StationAction.Check;
            if (style == "exchange") return StationAction.Exchange;
            return stress < burstBand ? StationAction.Check : StationAction.Exchange;
        }

        // Signature of the maneuver plan. Count, the sum of node UTs rounded to 0.5 s, and the summed
        // delta-v magnitude rounded to 0.1 m/s. Dragging a node changes it, a plain clock tick does not.
        public static string PlanSignature(int count, double[] uts, double[] dvs)
        {
            double ut = 0, dv = 0;
            if (uts != null) for (int i = 0; i < uts.Length; i++) ut += Math.Round(uts[i] * 2.0) / 2.0;
            if (dvs != null) for (int i = 0; i < dvs.Length; i++) dv += dvs[i];
            return count + "|" + ut.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)
                   + "|" + Math.Round(dv, 1).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
        }

        // Req 11: may a panic line start now? Global minimum gap and the per-speaker cooldown.
        public static bool PanicAllowed(float now, float lastPanic, float minGap, bool speakerReady)
        {
            return speakerReady && now - lastPanic >= minGap;
        }
    }

    /// <summary>
    /// Who has the air: the radio (crew and MC transmissions, callouts, countdown, keying, quindar, static bursts)
    /// or the intercom (crew-to-crew chat). Pure, tested offline. The radio never starts inside an intercom turn,
    /// an intercom chat never starts while the radio is busy, queued or pending, or within IntercomStartGap s after it.
    /// A running chat yields between turns and resumes ChatResumeGap s after the radio, or is dropped after ChatMaxPause s.
    /// </summary>
    public static class AirRules
    {
        public const float IntercomStartGap = 2f;   // quiet radio before a new chat starts
        public const float ChatResumeGap = 1.5f;    // quiet radio before a paused chat takes its next turn
        public const float ChatMaxPause = 20f;      // a chat paused longer than this is dropped
        public const float CalloutMaxDefer = 4f;    // a callout that waited this long for an intercom line is dropped
        public const float DockCalloutMaxDefer = 2f; // docking distance callouts go stale faster (the distance moved on)
        public const float IntercomRadioLead = 15f;  // no new chat when the radio scheduler fires within this many s
        public const float IntercomLeadMaxWait = 45f; // a chat that waited this long for the radio lead starts anyway

        /// <summary>
        /// Priority of a callout for the single "waits for the intercom" slot. ev is the callout event name, null for
        /// a docking distance callout (the only caller without an event). Staging and parachutes 2, the rest 1.
        /// </summary>
        public static int CalloutRank(string ev)
        {
            if (ev == "staging" || ev == "chute_semi" || ev == "chute_full") return 2;
            return 1;
        }

        /// <summary>A new callout takes the waiting slot when it ranks at least as high (a newer docking distance wins).</summary>
        public static bool CalloutReplacesPending(string pendingEv, string newEv)
        {
            return CalloutRank(newEv) >= CalloutRank(pendingEv);
        }

        /// <summary>Longest wait for an intercom line before the callout is dropped.</summary>
        public static float CalloutMaxDeferFor(string ev)
        {
            return ev == null ? DockCalloutMaxDefer : CalloutMaxDefer;
        }

        /// <summary>The radio is quiet enough for an intercom turn. started: the chat already had a turn (resume gap).</summary>
        public static bool ChatTurnMayStart(bool started, bool radioAudible, bool radioQueued, bool radioPending, float sinceRadio)
        {
            if (radioAudible || radioQueued || radioPending) return false;
            return sinceRadio >= (started ? ChatResumeGap : IntercomStartGap);
        }

        /// <summary>A new intercom chat may be queued (not yet started).</summary>
        public static bool IntercomMayStart(bool radioAudible, bool radioQueued, bool radioPending, float sinceRadio)
        {
            return ChatTurnMayStart(false, radioAudible, radioQueued, radioPending, sinceRadio);
        }

        /// <summary>
        /// A new intercom chat may be queued, with a look ahead (radio cut into a running chat).
        /// radioDueIn: seconds until the radio scheduler fires, below 0 when it is off. A chat does not start when the
        /// radio is due within IntercomRadioLead s, unless the chat already waited IntercomLeadMaxWait s for it.
        /// </summary>
        public static bool IntercomMayStart(bool radioAudible, bool radioQueued, bool radioPending, float sinceRadio,
                                            float radioDueIn, float waitedFor)
        {
            if (!IntercomMayStart(radioAudible, radioQueued, radioPending, sinceRadio)) return false;
            if (radioDueIn >= 0f && radioDueIn < IntercomRadioLead && waitedFor < IntercomLeadMaxWait) return false;
            return true;
        }

        /// <summary>
        /// A started chat holds the air until its last item played: radio items queue behind it and wait.
        /// started: the chat had its first turn. chatQueued: chat items are left. intercomAudible: a line or click plays.
        /// </summary>
        public static bool ChatHoldsAir(bool started, bool chatQueued, bool intercomAudible)
        {
            return intercomAudible || (started && chatQueued);
        }

        /// <summary>A radio item may start: never inside an intercom turn, and never while a started chat holds the air.</summary>
        public static bool RadioMayStart(bool intercomTurnOpen, bool chatHoldsAir)
        {
            return !intercomTurnOpen && !chatHoldsAir;
        }

        /// <summary>A radio item may start: never inside an intercom turn (a line playing, or its click just played).</summary>
        public static bool RadioMayStart(bool intercomTurnOpen)
        {
            return !intercomTurnOpen;
        }

        /// <summary>The chat must yield now: the next chat item opens a new turn and radio is waiting behind it.</summary>
        public static bool ChatShouldYield(bool headOpensTurn, bool radioQueuedBehind)
        {
            return headOpensTurn && radioQueuedBehind;
        }

        /// <summary>The paused chat waited too long for the radio: drop the rest of it.</summary>
        public static bool ChatPausedTooLong(float pausedFor)
        {
            return pausedFor > ChatMaxPause;
        }

        /// <summary>
        /// Stable partition of a ring buffer slice: items for which <paramref name="first"/> holds move to the front,
        /// both groups keep their order. <paramref name="scratch"/> needs at least <paramref name="count"/> slots.
        /// Returns the number of items in the front group.
        /// </summary>
        public static int StablePartitionRing<T>(T[] ring, int head, int count, Predicate<T> first, T[] scratch)
        {
            int cap = ring.Length, n = 0;
            for (int i = 0; i < count; i++) { T x = ring[(head + i) % cap]; if (first(x)) scratch[n++] = x; }
            int front = n;
            for (int i = 0; i < count; i++) { T x = ring[(head + i) % cap]; if (!first(x)) scratch[n++] = x; }
            for (int i = 0; i < count; i++) { ring[(head + i) % cap] = scratch[i]; scratch[i] = default(T); }
            return front;
        }
    }

    // Largest value seen over roughly the last `window` seconds (closing speed just before contact).
    public class RecentMax
    {
        readonly float window;
        readonly List<float> xs = new List<float>();
        readonly List<float> ts = new List<float>();
        float clock;

        public RecentMax(float window) { this.window = Math.Max(0f, window); }

        public void Add(float x, float dt)
        {
            if (dt > 0f) clock += dt;
            if (float.IsNaN(x)) return;
            xs.Add(x); ts.Add(clock);
            while (ts.Count > 1 && clock - ts[0] > window) { ts.RemoveAt(0); xs.RemoveAt(0); }
        }

        public float Max
        {
            get
            {
                float m = 0f;
                for (int i = 0; i < xs.Count; i++) if (xs[i] > m) m = xs[i];
                return m;
            }
        }

        public void Clear() { xs.Clear(); ts.Clear(); }
    }
}
