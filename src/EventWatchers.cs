// Game events: pure watchers, tested offline. No UnityEngine, no KSP types.
// GameEventHub feeds them from GameEvents and the FlightSample and hands the EventRequests to the director.
// Every watcher takes the time in seconds ("now", any monotonic clock) and the tuning values as arguments,
// so a changed setting applies at once and the tests need no Settings object.
using System;
using System.Collections.Generic;

namespace Lonapse
{
    /// <summary>One event for ChatterDirector.PlayEvent. Empty strings mean "any".</summary>
    public struct EventRequest
    {
        public string ev;        // soi_enter science part_lost power_low power_ok signal_lost signal_back crew_lost
        public string band;      // calm tense panic
        public string topic;     // science: experiment id (EventWatchers.ExperimentId). soi_enter: "home". Else "any".
        public string body;      // FlightPhase.BodyKey of the body the line is about ({body}), "" when none
        public string trait;     // required speaker trait, "" = any (the director applies traitPreference itself)
        public bool crewOnly;    // no Mission Control line (signal_lost: MC cannot hear the crew)
        public float maxAge;     // s the director may keep it pending before it drops it
        public int priority;     // higher wins: EventPriority

        public override string ToString()
        {
            return ev + " band=" + band + " topic=" + topic + " body=" + body
                   + (string.IsNullOrEmpty(trait) ? "" : " trait=" + trait)
                   + (crewOnly ? " crewOnly" : "") + " maxAge=" + maxAge.ToString("0") + " prio=" + priority;
        }
    }

    /// <summary>Pending-list priority (Director API): crew_lost > part_lost > power > signal > science > soi.</summary>
    public static class EventPriority
    {
        public const int Soi = 1, Science = 2, Signal = 3, Power = 4, PartLost = 5, CrewLost = 6;
    }

    public static class EventWatchers
    {
        public const string Any = "any";
        public const string Home = "home";
        public const string Other = "other";

        public const string SoiEnter = "soi_enter", Science = "science", PartLost = "part_lost",
                            PowerLow = "power_low", PowerOk = "power_ok", SignalLost = "signal_lost",
                            SignalBack = "signal_back", CrewLost = "crew_lost";

        // Max ages for the events without a tuning key. Crew loss uses eventMaxAge, SOI and science eventMaxAge capped below.
        public const float PartLostMaxAge = 8f;     // only a fresh explosion is worth a call
        public const float PowerMaxAge = 120f;
        public const float SignalMaxAge = 30f;      // a stale "signal back" sounds wrong
        public const float ScienceMaxAge = 600f;    // was 90 s, a science line now waits through an ascent or reentry
        public const float SoiMaxAge = 120f;        // a "welcome to" long after the SOI change sounds wrong

        /// <summary>
        /// Max age the director keeps an event pending. A request without one (0 or less) gets eventMaxAge.
        /// Science is capped at ScienceMaxAge and soi_enter at SoiMaxAge. Crew loss and the rest keep the request.
        /// </summary>
        public static float MaxAgeFor(string ev, float requested, float eventMaxAge)
        {
            float a = requested > 0f ? requested : eventMaxAge;
            if (ev == Science) return Math.Min(a, ScienceMaxAge);
            if (ev == SoiEnter) return Math.Min(a, SoiMaxAge);
            return a;
        }

        /// <summary>Experiment ids that have their own science topic (lowercase).</summary>
        public static readonly string[] KnownExperiments =
        {
            "crewreport", "evareport", "mysterygoo", "surfacesample", "mobilematerialslab", "temperaturescan",
            "barometerscan", "seismicscan", "gravityscan", "atmosphereanalysis", "asteroidsample", "cometsample",
            "infraredtelescope", "magnetometer"
        };

        /// <summary>
        /// Science topic from a ScienceData.subjectID like "crewReport@KerbinSrfLandedLaunchPad".
        /// Lowercase id before '@' when known, "roc" for any id starting with "rocscience" (Breaking Ground
        /// surface features), "cometsample" for the comet variants (cometSample_short and so on), else "other".
        /// </summary>
        public static string ExperimentId(string subjectID)
        {
            if (string.IsNullOrEmpty(subjectID)) return Other;
            int at = subjectID.IndexOf('@');
            string id = (at >= 0 ? subjectID.Substring(0, at) : subjectID).Trim().ToLowerInvariant();
            if (id.Length == 0) return Other;
            if (id.StartsWith("rocscience", StringComparison.Ordinal)) return "roc";
            if (id.StartsWith("cometsample", StringComparison.Ordinal)) return "cometsample";
            for (int i = 0; i < KnownExperiments.Length; i++)
                if (KnownExperiments[i] == id) return id;
            return Other;
        }

        public static EventRequest Make(string ev, string band, string topic, string body, bool crewOnly, float maxAge, int priority)
        {
            EventRequest r;
            r.ev = ev;
            r.band = band;
            r.topic = string.IsNullOrEmpty(topic) ? Any : topic;
            r.body = body ?? "";
            r.trait = "";
            r.crewOnly = crewOnly;
            r.maxAge = maxAge < 0f ? 0f : maxAge;
            r.priority = priority;
            return r;
        }
    }

    /// <summary>Feature 4: SOI change. The same body again within soiCooldown is a duplicate (a flyby that
    /// clips an SOI edge, or the event firing twice). A different body always fires.</summary>
    public class SoiWatcher
    {
        private string lastBody;
        private double lastTime = double.NegativeInfinity;

        public void Reset() { lastBody = null; lastTime = double.NegativeInfinity; }

        /// <param name="bodyName">raw body name, used for the dedupe (planet packs all map to "generic")</param>
        /// <param name="bodyKey">FlightPhase.BodyKey(bodyName), goes into the request</param>
        public bool OnSoiChange(double now, string bodyName, string bodyKey, bool isHome, double cooldown, double maxAge,
                                out EventRequest r)
        {
            r = default(EventRequest);
            if (string.IsNullOrEmpty(bodyName)) return false;
            if (bodyName == lastBody && now - lastTime < cooldown) return false;
            lastBody = bodyName;
            lastTime = now;
            r = EventWatchers.Make(EventWatchers.SoiEnter, "calm", isHome ? EventWatchers.Home : EventWatchers.Any,
                                   bodyKey, false, (float)maxAge, EventPriority.Soi);
            return true;
        }
    }

    /// <summary>Feature 5: science result. Cooldown overall (scienceCooldown) and per topic (scienceTopicCooldown).
    /// A blocked result is dropped, not queued.</summary>
    public class ScienceWatcher
    {
        /// <summary>Experiments that come during the overall cooldown wait here, one per topic, and come out one per
        /// cooldown. Without it, experiments run right after each other got no line.</summary>
        public const int MaxQueued = 3;
        /// <summary>A queued experiment older than this (s) is dropped: a remark long after it sounds wrong.</summary>
        public const double MaxWait = 120.0;

        private struct Queued { public string topic; public string body; public double since; public double maxAge; }

        private double lastAny = double.NegativeInfinity;
        private readonly Dictionary<string, double> lastTopic = new Dictionary<string, double>();
        private readonly List<Queued> queue = new List<Queued>(MaxQueued);

        public int QueuedCount { get { return queue.Count; } }

        public void Reset() { lastAny = double.NegativeInfinity; lastTopic.Clear(); queue.Clear(); }

        /// <summary>
        /// An experiment result. True with a request when it may play now. During the overall cooldown it is queued
        /// (false, Tick gives it later). The same topic within topicCooldown, or already queued, is ignored.
        /// </summary>
        public bool OnExperiment(double now, string subjectID, string bodyKey, double cooldown, double topicCooldown,
                                 double maxAge, out EventRequest r)
        {
            r = default(EventRequest);
            string id = EventWatchers.ExperimentId(subjectID);
            // unknown experiments use the "any" science rows
            string topic = id == EventWatchers.Other ? EventWatchers.Any : id;
            double t;
            if (lastTopic.TryGetValue(topic, out t) && now - t < topicCooldown) return false;
            for (int i = 0; i < queue.Count; i++) if (queue[i].topic == topic) return false;
            if (now - lastAny < cooldown || queue.Count > 0)
            {
                if (queue.Count < MaxQueued) queue.Add(new Queued { topic = topic, body = bodyKey, since = now, maxAge = maxAge });
                return false;
            }
            r = Fire(now, topic, bodyKey, maxAge);
            return true;
        }

        /// <summary>Per frame: the oldest queued experiment once the overall cooldown is over.</summary>
        public bool Tick(double now, double cooldown, out EventRequest r)
        {
            r = default(EventRequest);
            for (int i = queue.Count - 1; i >= 0; i--) if (now - queue[i].since > MaxWait) queue.RemoveAt(i);
            if (queue.Count == 0 || now - lastAny < cooldown) return false;
            Queued q = queue[0];
            queue.RemoveAt(0);
            r = Fire(now, q.topic, q.body, q.maxAge);
            return true;
        }

        private EventRequest Fire(double now, string topic, string bodyKey, double maxAge)
        {
            lastAny = now;
            lastTopic[topic] = now;
            return EventWatchers.Make(EventWatchers.Science, "calm", topic, bodyKey, false, (float)maxAge, EventPriority.Science);
        }
    }

    /// <summary>Feature 6: part loss. Losses are summed over partLostWindow from the first one, then one request:
    /// tense, or panic at 3 or more parts or when the crew already panics. Losses during the cooldown are ignored.</summary>
    public class PartLossWatcher
    {
        public const int PanicParts = 3;
        public int pending;              // parts lost in the open window
        private double windowStart;
        private double cooldownUntil = double.NegativeInfinity;
        public int lastCount;            // parts of the last fired request (log, overlay)

        public void Reset() { pending = 0; cooldownUntil = double.NegativeInfinity; }

        public void OnPartLost(double now, int count)
        {
            if (count <= 0 || now < cooldownUntil) return;
            if (pending == 0) windowStart = now;
            pending += count;
        }

        public bool Tick(double now, double window, double cooldown, bool crewPanics, out EventRequest r)
        {
            r = default(EventRequest);
            if (pending <= 0 || now - windowStart < window) return false;
            lastCount = pending;
            bool panic = pending >= PanicParts || crewPanics;
            pending = 0;
            cooldownUntil = now + cooldown;
            r = EventWatchers.Make(EventWatchers.PartLost, panic ? "panic" : "tense", EventWatchers.Any, "", false,
                                   EventWatchers.PartLostMaxAge, EventPriority.PartLost);
            return true;
        }
    }

    /// <summary>Feature 7: electric charge with hysteresis. power_low below lowFrac, power_ok above okFrac after a
    /// power_low. Only when the capacity is at least minMax, never on EVA (the state is kept, nothing fires).
    /// The first reading counts: a ship that starts with flat batteries gets its power_low.</summary>
    public class PowerWatcher
    {
        public bool low;

        public void Reset() { low = false; }

        public bool Update(double ecFrac, double ecMax, bool eva, double lowFrac, double okFrac, double minMax,
                           out EventRequest r)
        {
            r = default(EventRequest);
            if (eva || !(ecMax >= minMax) || ecMax <= 0 || double.IsNaN(ecFrac)) return false;
            if (okFrac < lowFrac) okFrac = lowFrac;   // a broken setting must not make it flap
            if (!low && ecFrac < lowFrac)
            {
                low = true;
                r = EventWatchers.Make(EventWatchers.PowerLow, "tense", EventWatchers.Any, "", false,
                                       EventWatchers.PowerMaxAge, EventPriority.Power);
                return true;
            }
            if (low && ecFrac > okFrac)
            {
                low = false;
                r = EventWatchers.Make(EventWatchers.PowerOk, "calm", EventWatchers.Any, "", false,
                                       EventWatchers.PowerMaxAge, EventPriority.Power);
                return true;
            }
            return false;
        }
    }

    /// <summary>Feature 8: signal lost and back. A new state must hold for signalHold seconds. signal_lost is
    /// crew only and has a cooldown (signalCooldown since the last signal_lost). signal_back fires only after an
    /// announced signal_lost, so the pair always matches. While suppressed (plasma blackout) the candidate is
    /// dropped and nothing changes. The first reading sets the state without a call.</summary>
    public class SignalWatcher
    {
        public bool hasState;
        public bool connected = true;      // stable state
        public bool announcedLost;
        private bool candidate;
        private bool hasCandidate;
        private double candidateSince;
        private double lastLost = double.NegativeInfinity;

        public void Reset()
        {
            hasState = false; connected = true; announcedLost = false; hasCandidate = false;
        }

        public bool Update(double now, bool isConnected, bool suppress, double hold, double cooldown, out EventRequest r)
        {
            r = default(EventRequest);
            if (!hasState)
            {
                if (suppress) return false;
                hasState = true;
                connected = isConnected;
                return false;
            }
            if (suppress || isConnected == connected) { hasCandidate = false; return false; }
            if (!hasCandidate || candidate != isConnected)
            {
                hasCandidate = true;
                candidate = isConnected;
                candidateSince = now;
            }
            if (now - candidateSince < hold) return false;
            hasCandidate = false;
            connected = isConnected;
            if (!connected)
            {
                if (now - lastLost < cooldown) return false;
                lastLost = now;
                announcedLost = true;
                r = EventWatchers.Make(EventWatchers.SignalLost, "tense", EventWatchers.Any, "", true,
                                       EventWatchers.SignalMaxAge, EventPriority.Signal);
                return true;
            }
            if (!announcedLost) return false;
            announcedLost = false;
            r = EventWatchers.Make(EventWatchers.SignalBack, "calm", EventWatchers.Any, "", false,
                                   EventWatchers.SignalMaxAge, EventPriority.Signal);
            return true;
        }
    }

    /// <summary>Feature 10: a kerbal of this flight is dead or missing. The first loss opens a crewLostDelay wait,
    /// every loss inside it joins the same call. Each kerbal is reported once. Unknown kerbals are ignored.</summary>
    public class CrewLossWatcher
    {
        private readonly HashSet<string> known = new HashSet<string>();
        private readonly HashSet<string> reported = new HashSet<string>();
        public readonly List<string> pending = new List<string>();
        public readonly List<string> lastNames = new List<string>();   // names of the last fired call
        private double firstAt;

        public int KnownCount { get { return known.Count; } }

        /// <summary>New flight scene: forget everyone.</summary>
        public void Reset() { known.Clear(); reported.Clear(); pending.Clear(); lastNames.Clear(); }

        public void Know(string name) { if (!string.IsNullOrEmpty(name)) known.Add(name); }
        public bool IsKnown(string name) { return !string.IsNullOrEmpty(name) && known.Contains(name); }

        /// <summary>Returns true when the loss was accepted (known, not reported yet).</summary>
        public bool OnLost(double now, string name)
        {
            if (!IsKnown(name) || reported.Contains(name)) return false;
            reported.Add(name);
            if (pending.Count == 0) firstAt = now;
            pending.Add(name);
            return true;
        }

        public bool Tick(double now, double delay, double maxAge, out EventRequest r)
        {
            r = default(EventRequest);
            if (pending.Count == 0 || now - firstAt < delay) return false;
            lastNames.Clear();
            lastNames.AddRange(pending);
            pending.Clear();
            r = EventWatchers.Make(EventWatchers.CrewLost, "tense", EventWatchers.Any, "", false, (float)maxAge,
                                   EventPriority.CrewLost);
            return true;
        }
    }
}
