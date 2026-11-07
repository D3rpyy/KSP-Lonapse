// Probe mode: pure rules of the director in probe mode. Beep pitch and mood, subtitle
// speakers, scene shapes and the event plan. No Unity or KSP references, so it compiles under
// DC_OFFLINE_TEST and the tests cover it.
using System;
using System.Collections.Generic;

namespace Lonapse
{
    /// <summary>What the director does with a game event in probe mode.</summary>
    public enum ProbeEventPlan { Drop = 0, Radio = 1 }

    /// <summary>Roles that occur in a probe scene, counted on its generic lines.</summary>
    public struct ProbeShape
    {
        public int mc, probe, crew;

        /// <summary>Needs the radio link: an MC line or a probe beep.</summary>
        public bool NeedsSignal { get { return mc > 0 || probe > 0; } }
    }

    public static class ProbeRules
    {
        // Flight events the probe reacts to (probe variants in probe_events.tsv)
        public const string Touchdown = "touchdown", Splashdown = "splashdown", Docked = "docked", BurnSoon = "burn_soon";
        public const string HailEvent = "probe_hail";
        public const string StressEvent = "probe_stress", SayAgainEvent = "probe_say_again", MilestoneEvent = "probe_milestone",
                            RoverEvent = "probe_rover", DataEvent = "probe_data";
        /// <summary>Subtitle of the data stream beep (beeps/probe_data).</summary>
        public const string DataSubtitle = "[beeps a data stream]";

        public const float DrowsyPitch = 0.8f;
        public const float PitchPerStress = 0.25f;
        public const string ScaredMood = "scared", HappyMood = "happy";

        // Subtitle speaker name of Mission Control
        public const string McName = "MC";

        /// <summary>Beep pitch: 1 + 0.25 x stress, capped at pitchMax (at least 1). Drowsy: 0.8.</summary>
        public static float BeepPitch(float stress, bool drowsy, float pitchMax)
        {
            if (drowsy) return DrowsyPitch;
            if (float.IsNaN(stress) || stress < 0f) stress = 0f;
            if (stress > 1f) stress = 1f;
            if (float.IsNaN(pitchMax) || pitchMax < 1f) pitchMax = 1f;
            return Math.Min(1f + PitchPerStress * stress, pitchMax);
        }

        /// <summary>Beep mood that plays for a probe row. Tense or panic stress turns every beep scared.
        /// An empty or unknown mood gives happy.</summary>
        public static string BeepMood(string rowMood, bool tense)
        {
            if (tense) return ScaredMood;
            if (string.IsNullOrEmpty(rowMood) || Array.IndexOf(ProbeCores.Moods, rowMood) < 0) return HappyMood;
            return rowMood;
        }

        /// <summary>Subtitle of a beep without a row (or a row whose mood was overridden), for example "[beeps nervously]".
        /// The same short form as the probe rows in the tables (PhraseTable.ProbeSubtitleOk).</summary>
        public static string BeepSubtitle(string mood)
        {
            return "[beeps " + MoodAdverb(mood) + "]";
        }

        /// <summary>Adverb of a beep mood for the subtitle. Unknown or empty gives the happy one.</summary>
        public static string MoodAdverb(string mood)
        {
            switch (mood)
            {
                case "offended": return "indignantly";
                case "curious": return "curiously";
                case "scared": return "nervously";
                case "sleepy": return "sleepily";
                case "proud": return "proudly";
                case "bored": return "flatly";
                default: return "happily";
            }
        }

        /// <summary>Subtitle speaker name for a role in probe mode.</summary>
        public static string SpeakerName(string role, string core)
        {
            switch (role)
            {
                case PhraseTable.RoleProbe: return ProbeCores.DisplayName(core);
                default: return McName;
            }
        }

        /// <summary>The probe is forced asleep when any of its cores has no power (ModuleState NotEnoughResources = 1)
        /// or hibernates. states and hibernating are per core, same length.</summary>
        public static bool ForcedAsleep(IList<int> states, IList<bool> hibernating)
        {
            if (states != null)
                for (int i = 0; i < states.Count; i++) if (states[i] == 1) return true;
            if (hibernating != null)
                for (int i = 0; i < hibernating.Count; i++) if (hibernating[i]) return true;
            return false;
        }

        /// <summary>Probe events: crew_lost and signal_lost are dropped (Mission Control cannot reach the probe), the
        /// rest plays on the radio.</summary>
        public static ProbeEventPlan PlanFor(string ev)
        {
            if (ev == EventWatchers.CrewLost) return ProbeEventPlan.Drop;
            if (ev == EventWatchers.SignalLost) return ProbeEventPlan.Drop;
            return ProbeEventPlan.Radio;
        }

        /// <summary>Beep mood of the probe's own reaction to an event when there is no probe scene for it.</summary>
        public static string FallbackMood(string ev)
        {
            switch (ev)
            {
                case EventWatchers.Science: return "proud";
                case EventWatchers.PowerLow: return "sleepy";
                case EventWatchers.PowerOk: return HappyMood;
                case EventWatchers.SoiEnter: return "curious";
                case EventWatchers.PartLost: return ScaredMood;
                case EventWatchers.SignalBack: return HappyMood;
                // Flight events
                case Touchdown: return HappyMood;
                case Splashdown: return "curious";
                case Docked: return HappyMood;
                case BurnSoon: return "curious";
                // Docking: the probe notices the target, gets nervous when it closes fast, and counts down the distance
                case "dock_approach": return "curious";
                case "undocked": return HappyMood;
                case "dock_close": return "curious";
                case "dock_close_tense": return ScaredMood;
                case "dock_callout_far": return "curious";
                case "dock_callout_near": return "proud";
                case "staging": return HappyMood;
                case "chute_semi": case "chute_full": return HappyMood;
                case "throttle_full": return "proud";
                case "throttle_cut": return "curious";
                case "plan_node": return "curious";
                case "probe_stress": return ScaredMood;
                case "probe_milestone": return "proud";
                case "probe_rover": return HappyMood;
            }
            return null;
        }

        /// <summary>Counts the roles of a scene's lines.</summary>
        public static ProbeShape ShapeOf(IList<PhraseEntry> lines)
        {
            ProbeShape s = default(ProbeShape);
            if (lines == null) return s;
            for (int i = 0; i < lines.Count; i++)
            {
                PhraseEntry e = lines[i];
                if (e == null) continue;
                switch (e.Role)
                {
                    case "mc": s.mc++; break;
                    case PhraseTable.RoleProbe: s.probe++; break;
                    default: s.crew++; break;
                }
            }
            return s;
        }

        public static ProbeShape ShapeOf(PhraseScene sc) { return sc == null ? default(ProbeShape) : ShapeOf(sc.Lines); }

        /// <summary>
        /// A probe scene fits the current state. Crew rows never fit. Asleep: no probe beeps. No link: nothing fits
        /// (every probe scene is on the radio).
        /// </summary>
        public static bool SceneFits(ProbeShape s, bool asleep, bool signal)
        {
            if (s.crew > 0) return false;
            if (asleep && s.probe > 0) return false;
            if (!signal && s.NeedsSignal) return false;
            return true;
        }

        /// <summary>A hail scene (probe_hail) opens with the probe, so it needs a probe row first.</summary>
        public static bool StartsWithProbe(IList<PhraseEntry> lines)
        {
            return lines != null && lines.Count > 0 && lines[0] != null && lines[0].Role == PhraseTable.RoleProbe;
        }

        /// <summary>Pick weight of a probe scene for the current core: 2 for a scene of exactly that core, 1 for core any.</summary>
        public static int CoreWeight(string sceneCore, string core)
        {
            bool any = string.IsNullOrEmpty(sceneCore) || sceneCore == PhraseTable.Any;
            return !any && sceneCore == core ? 2 : 1;
        }

        /// <summary>
        /// Weighted pick from the pool of exact-core and core-any scenes (exact core counts twice). Avoids
        /// <paramref name="avoidId"/> when there is another choice. Null for an empty list.
        /// </summary>
        public static PhraseScene PickWeighted(IList<PhraseScene> list, string core, Random rng, string avoidId)
        {
            if (list == null || list.Count == 0) return null;
            if (list.Count == 1) return list[0];
            int total = 0;
            for (int i = 0; i < list.Count; i++)
                if (list[i].Id != avoidId) total += CoreWeight(list[i].Core, core);
            if (total == 0) return list[0];
            int r = rng.Next(total);
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].Id == avoidId) continue;
                r -= CoreWeight(list[i].Core, core);
                if (r < 0) return list[i];
            }
            return list[list.Count - 1];
        }

        /// <summary>Callouts: Mission Control answers a probe callout beep with this chance, and never within
        /// gap s of its last answer (staging comes in bursts). roll is 0..1.</summary>
        public static bool CalloutMcAllowed(double roll, float chance, float sinceLastMc, float gap)
        {
            return sinceLastMc >= gap && roll < chance;
        }

        /// <summary>"Say again?": only below the signal threshold (but with a link), with the chance per scene
        /// and at most once per cooldown s. roll is 0..1.</summary>
        public static bool SayAgainDue(float signal, bool connected, float threshold, double roll, float chance,
                                       float sinceLast, float cooldown)
        {
            if (!connected || float.IsNaN(signal) || signal >= threshold) return false;
            return sinceLast >= cooldown && roll < chance;
        }

        /// <summary>The mood a probe repeats a beep in after "say again?". Proud and bored cores get annoyed.</summary>
        public static string RepeatMood(string mood)
        {
            if (mood == "proud" || mood == "bored") return "offended";
            return string.IsNullOrEmpty(mood) ? HappyMood : mood;
        }

        /// <summary>A stock Kerbin day and an Earth day, in seconds.</summary>
        public const double KerbinDay = 6.0 * 3600.0, EarthDay = 24.0 * 3600.0;

        /// <summary>The day length to count with. A missing or broken value gives the Kerbin day.</summary>
        public static double SaneDay(double dayLength)
        {
            return double.IsNaN(dayLength) || double.IsInfinity(dayLength) || dayLength < 60.0 ? KerbinDay : dayLength;
        }

        /// <summary>1-based mission day for a day of dayLength seconds (the game clock, so mods like Kronometer
        /// count). Negative or NaN time gives day 1.</summary>
        public static int MissionDay(double missionTime, double dayLength)
        {
            if (double.IsNaN(missionTime) || missionTime <= 0) return 1;
            double d = Math.Floor(missionTime / SaneDay(dayLength));
            if (d > int.MaxValue - 1) return int.MaxValue;
            return (int)d + 1;
        }

        /// <summary>The probe calls Mission Control on its own (probe_hail) only when it is awake, the link
        /// works and the flight is not in a serious phase.</summary>
        public static bool HailMayStart(string phase, bool link, bool asleep)
        {
            return link && !asleep && !FlightPhase.IsSerious(phase);
        }
    }
}
