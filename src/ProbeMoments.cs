// Pure watchers for probe mode. Milestones (mission days, first orbit and first landing per body), rover
// moments (fast, air time, tilt, idle) and the data beeps while an antenna sends. No Unity or KSP references, so they
// compile under DC_OFFLINE_TEST and the tests cover them. LonapseAddon feeds them, the director plays what they return.
using System;
using System.Collections.Generic;

namespace Lonapse
{
    /// <summary>
    /// Milestones per vessel, remembered for the session only (a restart of KSP forgets them). The first time a vessel
    /// is seen, everything it already passed is marked done, so loading a save replays nothing.
    /// </summary>
    public class ProbeMilestones
    {
        /// <summary>Full mission days that count, with their topic. Ascending.</summary>
        public static readonly int[] Days = { 1, 10, 100, 365 };
        public static readonly string[] DayTopics = { "day1", "day10", "day100", "day365" };
        public const string Orbit = "orbit", Landing = "landing";

        private readonly Dictionary<string, HashSet<string>> done = new Dictionary<string, HashSet<string>>();

        /// <summary>Full days (not the 1-based mission day): 0 right after launch.</summary>
        public static int FullDays(double missionTime, double dayLength)
        {
            return ProbeRules.MissionDay(missionTime, dayLength) - 1;
        }

        private static string BodyKey(string kind, string body) { return kind + ":" + (body ?? ""); }

        private HashSet<string> For(string vesselId, double missionTime, double dayLength, bool orbiting, bool landed, string body,
                                    out bool fresh)
        {
            HashSet<string> set;
            fresh = !done.TryGetValue(vesselId, out set);
            if (!fresh) return set;
            set = new HashSet<string>();
            int full = FullDays(missionTime, dayLength);
            for (int i = 0; i < Days.Length; i++) if (full >= Days[i]) set.Add(DayTopics[i]);
            if (orbiting) set.Add(BodyKey(Orbit, body));
            if (landed) set.Add(BodyKey(Landing, body));
            done[vesselId] = set;
            return set;
        }

        /// <summary>
        /// The milestone topic reached now (day1 to day365 or orbit), or null. One per call. Several days passed at
        /// once (time warp) give only the highest, the lower ones are marked done. Landings come from TakeLanding.
        /// </summary>
        public string Update(string vesselId, double missionTime, double dayLength, bool orbiting, bool landed, string body)
        {
            if (string.IsNullOrEmpty(vesselId)) return null;
            bool fresh;
            HashSet<string> set = For(vesselId, missionTime, dayLength, orbiting, landed, body, out fresh);
            if (fresh) return null;
            int full = FullDays(missionTime, dayLength);
            string hit = null;
            for (int i = 0; i < Days.Length; i++)
                if (full >= Days[i] && set.Add(DayTopics[i])) hit = DayTopics[i];
            if (hit != null) return hit;
            if (orbiting && set.Add(BodyKey(Orbit, body))) return Orbit;
            return null;
        }

        /// <summary>True once per vessel and body: the first landing there this session (call it at touchdown).</summary>
        public bool TakeLanding(string vesselId, string body)
        {
            if (string.IsNullOrEmpty(vesselId)) return false;
            HashSet<string> set;
            if (!done.TryGetValue(vesselId, out set)) { set = new HashSet<string>(); done[vesselId] = set; }
            return set.Add(BodyKey(Landing, body));
        }
    }

    /// <summary>Rover moments of a probe rover: fast, air, tilt or idle (ProbeRover topics). One shared cooldown.</summary>
    public class RoverMoments
    {
        public float fastTime, idleTime, cooldown;
        private bool airDone, tiltDone;

        public void Reset() { fastTime = idleTime = cooldown = 0f; airDone = tiltDone = false; }

        /// <summary>Per frame. Returns fast, air, tilt, idle or null.</summary>
        public string Update(float dt, bool rover, bool landed, float speed, float airTime, float tiltDeg, Tuning tu)
        {
            if (float.IsNaN(dt) || dt < 0f) dt = 0f;
            if (cooldown > 0f) cooldown -= dt;
            if (!rover) { fastTime = idleTime = 0f; airDone = tiltDone = false; return null; }
            fastTime = speed > tu.roverFastSpeed ? fastTime + dt : 0f;
            idleTime = landed && speed < 0.3f ? idleTime + dt : 0f;
            // air and tilt count once per jump or per tip-over
            if (airTime < tu.roverAirSec * 0.5f) airDone = false;
            if (tiltDeg < tu.roverTiltDeg * 0.6f) tiltDone = false;
            string hit = null;
            if (!airDone && airTime >= tu.roverAirSec) { airDone = true; hit = "air"; }
            else if (!tiltDone && landed && tiltDeg >= tu.roverTiltDeg) { tiltDone = true; hit = "tilt"; }
            else if (fastTime >= tu.roverFastSec) { fastTime = 0f; hit = "fast"; }
            else if (idleTime >= tu.roverIdleSec) { idleTime = 0f; hit = "idle"; }
            if (hit == null || cooldown > 0f) return null;
            cooldown = tu.roverMomentCooldown;
            return hit;
        }
    }

    /// <summary>Data beeps while an antenna sends: one at the start, then one per gap, at most max per send.</summary>
    public class DataBeeps
    {
        private bool busy;
        private float since;
        private int count;

        public void Reset() { busy = false; since = 0f; count = 0; }

        /// <summary>Per poll. True when a data beep is due now. first is true for the beep that starts a send.</summary>
        public bool Update(float dt, bool sending, float gap, int max, out bool first)
        {
            first = false;
            if (float.IsNaN(dt) || dt < 0f) dt = 0f;
            if (!sending) { busy = false; return false; }
            if (!busy) { busy = true; since = 0f; count = 1; first = true; return true; }
            since += dt;
            if (count >= max || since < gap) return false;
            since = 0f;
            count++;
            return true;
        }
    }
}
