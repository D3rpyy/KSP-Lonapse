// Experimental (runtime voice): a test from the Debug tab. Mission Control greets a kerbal aboard by name, spoken at
// runtime by Piper: the name as itself, the rest loñapsE. The clip goes through the normal MC path (quindar, radio
// sound, subtitle), so it can be compared with a pre-rendered line. The time from request to clip is logged.
using System;

namespace Lonapse
{
    public partial class ChatterDirector
    {
        static readonly string[][] RuntimeGreetings = {
            new[] { "{n}, aquí Control. ¿Qué tal va todo por ahí arriba?", "{N}, Control here. How is everything up there?" },
            new[] { "Hola, {n}. Te oímos perfectamente.", "Hello, {N}. We hear you perfectly." },
            new[] { "{n}, buen trabajo hoy. Sigue así.", "{N}, good work today. Keep it up." },
            new[] { "{n}, ¿nos copias? Cambio.", "{N}, do you copy? Over." },
        };
        int runtimeTests, runtimeAddressed, runtimeUsed, runtimeLate, runtimeFailed;
        KerbalVoice lastCrewVoice;
        float lastCrewVoiceClock = -999f;

        /// <summary>Share of Mission Control lines that get a crew name, when the setting is on.</summary>
        const double RuntimeNameChance = 0.35;
        /// <summary>How long a named line may hold the radio after its turn came (the quindar already played), in s.
        /// Later than that, the pre-rendered clip plays without the name.</summary>
        const float RuntimeMaxWait = 1.0f;
        /// <summary>The kerbal who spoke in the last minute is the one Mission Control answers.</summary>
        const float RuntimeAnswerWindow = 60f;

        /// <summary>A named MC line on its way: Piper renders it while the line waits in the queue.</summary>
        public sealed class RuntimeLine
        {
            public PhraseEntry entry;  // the named copy: its own clip key, English and Spanish with the name
            public UnityEngine.AudioClip clip;
            public bool failed;
            public float waitFrom = -1f;
        }

        /// <summary>Counters for the Debug tab: lines with a name asked, played, too late, failed.</summary>
        public string RuntimeNameStats
        {
            get { return runtimeAddressed + " asked, " + runtimeUsed + " played, " + runtimeLate + " too late, " + runtimeFailed + " failed"; }
        }

        /// <summary>
        /// Setting "Mission Control says crew names": sometimes an MC line gets the name of a kerbal aboard at its end,
        /// spoken at runtime. Null when not (setting off, no Piper, no crew, the roll, or a line that takes no name).
        /// The kerbal who spoke last is the one addressed, else one at random.
        /// </summary>
        RuntimeLine MaybeAddressed(PhraseEntry e)
        {
            if (e == null || e.Role != "mc" || e.SubtitleOnly || !host.settings.runtimeNames) return null;
            if (host.roster.voices.Count == 0 || rng.NextDouble() >= RuntimeNameChance) return null;
            if (RuntimeVoice.Missing("mc") != null) return null;
            KerbalVoice k = lastCrewVoice != null && clock - lastCrewVoiceClock < RuntimeAnswerWindow
                            && host.roster.voices.Contains(lastCrewVoice) ? lastCrewVoice
                            : host.roster.voices[rng.Next(host.roster.voices.Count)];
            string name = k != null ? SubtitleSlots.FirstName(k.displayName) : null;
            if (string.IsNullOrEmpty(name)) return null;
            string english = RuntimeSpeech.AddName(e.English, name);
            string spanish = RuntimeSpeech.AddName(e.Spanish, RuntimeSpeech.MarkName(name));
            if (english == null || spanish == null) return null;
            if (host.runtimeVoice == null) host.runtimeVoice = new RuntimeVoice();
            var n = new PhraseEntry
            {
                ClipKey = e.ClipKey + "_rt" + (++runtimeAddressed), Event = e.Event, Severity = e.Severity, Band = e.Band,
                Role = e.Role, Scene = e.Scene, Seq = e.Seq, Gender = e.Gender, Phase = e.Phase, Cause = e.Cause,
                Trait = e.Trait, Persona = e.Persona, Topic = e.Topic, Core = e.Core, Beep = e.Beep, Body = e.Body,
                Spanish = spanish.Replace(RuntimeSpeech.NameOpen.ToString(), "").Replace(RuntimeSpeech.NameClose.ToString(), ""),
                LonapseText = RuntimeSpeech.Spoken(spanish), English = english, Folder = e.Folder,
            };
            var rt = new RuntimeLine { entry = n };
            host.runtimeVoice.Request("mc", spanish, r =>
            {
                if (r.clip == null) { rt.failed = true; runtimeFailed++; return; }
                PhraseClips.SetRuntimeClip(n, r.clip);
                rt.clip = r.clip;
                Log.Info("Runtime voice: " + n.ClipKey + " \"" + r.spoken + "\" ready in " + r.queueToReadyMs.ToString("0")
                         + " ms (Piper " + r.piperMs.ToString("0") + " ms)");
            });
            return rt;
        }

        /// <summary>
        /// At the turn of an MC line: false means wait a frame (Piper is not done and the wait is still short).
        /// True with the named clip and entry when it is ready, true with nulls to play the pre-rendered line.
        /// </summary>
        bool RuntimeTurn(Utterance u, out UnityEngine.AudioClip clip, out PhraseEntry shown)
        {
            clip = null; shown = null;
            RuntimeLine rt = u.rt;
            if (rt == null) return true;
            if (rt.clip != null) { clip = rt.clip; shown = rt.entry; runtimeUsed++; return true; }
            if (rt.failed) return true;
            if (rt.waitFrom < 0f) rt.waitFrom = clock;
            if (clock - rt.waitFrom < RuntimeMaxWait) return false;
            runtimeLate++;
            Log.Info("Runtime voice: " + rt.entry.ClipKey + " too late, the line plays without the name");
            u.rt = null;
            return true;
        }

        /// <summary>Called when a crew line starts: the kerbal Mission Control answers next.</summary>
        void NoteCrewSpeaker(KerbalVoice sp)
        {
            lastCrewVoice = sp;
            lastCrewVoiceClock = clock;
        }

        /// <summary>Status line of the last runtime voice test, for the Debug tab.</summary>
        public string runtimeStatus;

        /// <summary>Debug tab: MC greets a kerbal aboard by name with a runtime clip. Returns a status line.</summary>
        public string DebugRuntimeGreeting()
        {
            if (host.runtimeVoice == null) host.runtimeVoice = new RuntimeVoice();
            string why = RuntimeVoice.Missing("mc");
            if (why != null) return runtimeStatus = "runtime voice: " + why;
            string full = null;
            if (host.roster.voices.Count > 0)
            {
                KerbalVoice k = host.roster.voices[rng.Next(host.roster.voices.Count)];
                full = k != null ? k.displayName : null;
            }
            if (string.IsNullOrEmpty(full)) full = "Jebediah Kerman";
            string name = SubtitleSlots.FirstName(full);
            string[] g = RuntimeGreetings[runtimeTests++ % RuntimeGreetings.Length];
            string spanish = g[0].Replace("{n}", RuntimeSpeech.MarkName(name));
            var e = new PhraseEntry
            {
                ClipKey = "rt_greet_" + runtimeTests, Event = "chatter", Severity = "chatter", Band = "calm", Role = "mc",
                Scene = "rt_greet", Seq = 1, Gender = "m", Phase = "any", Cause = "any",
                Spanish = g[0].Replace("{n}", name), English = g[1].Replace("{N}", name),
            };
            e.Folder = PhraseTable.FolderFor(e.Role, e.Severity, e.Band, e.Event);
            e.LonapseText = RuntimeSpeech.Spoken(spanish);
            runtimeStatus = "runtime voice: rendering \"" + e.LonapseText + "\"";
            host.runtimeVoice.Request("mc", spanish, r =>
            {
                if (r.clip == null) { runtimeStatus = "runtime voice failed: " + r.error; return; }
                PhraseClips.SetRuntimeClip(e, r.clip);
                EnqueueMc(e.Folder, 0.2f, e);
                runtimeStatus = string.Format("runtime voice: \"{0}\" ready in {1:0} ms (Piper {2:0} ms), {3:0.00} s of speech",
                                              r.spoken, r.queueToReadyMs, r.piperMs, r.clip.length);
                Log.Info("Runtime voice: " + e.ClipKey + " \"" + r.spoken + "\" ready in " + r.queueToReadyMs.ToString("0")
                         + " ms (Piper " + r.piperMs.ToString("0") + " ms), " + r.clip.length.ToString("0.00") + " s");
            });
            return runtimeStatus;
        }
    }
}
