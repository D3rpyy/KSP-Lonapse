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
        int runtimeTests;

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
