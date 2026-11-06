// The public API for other mods: give your own events a voice. A mod ships its lines in
// GameData/<YourMod>/Lonapse/*.tsv (the format of Phrases/Runtime, see docs/API.md) and calls Trigger when something
// happens. Lonapse picks who talks, queues it, and keeps the priorities and cooldowns.
// Call it through reflection (docs/api/LonapseWrapper.cs), so your mod has no dependency on Lonapse. Only KSP and
// .NET types are used here, and that will not change within ApiVersion 1. Main thread only. Nothing here throws.
using System;
using System.Collections.Generic;

namespace Lonapse
{
    public static class LonapseApi
    {
        /// <summary>The version of this API. A new version only adds, it never changes these calls.</summary>
        public static int ApiVersion { get { return 1; } }

        /// <summary>True in flight with Lonapse running and enabled. Trigger returns false otherwise.</summary>
        public static bool Available
        {
            get
            {
                try
                {
                    LonapseAddon a = LonapseAddon.Instance;
                    return a != null && a.director != null && a.settings != null && a.settings.enabled;
                }
                catch (Exception) { return false; }
            }
        }

        /// <summary>
        /// Something happened that your lines talk about. eventId: "yourmod.event" (a-z 0-9 _, one dot), the on= of
        /// your lines. vessel: where it happened (null: the active vessel). kerbal: who did it (null: Lonapse picks).
        /// numbers and names: the values of your x_ slots ({x_cups}). A name is "english" or "spanish|english".
        /// Returns true when the event is accepted. It then plays soon, when the radio is free, or is dropped after
        /// 90 s. False when it is not accepted: Lonapse is not running, no lines for the event are loaded, the
        /// vessel or kerbal is not the active one or near it, or the event is in its cooldown. KSP.log says why.
        /// </summary>
        public static bool Trigger(string eventId, Vessel vessel, ProtoCrewMember kerbal,
                                   IDictionary<string, double> numbers, IDictionary<string, string> names)
        {
            try
            {
                if (!Available) return false;
                return LonapseAddon.Instance.director.OfferModEvent(eventId, vessel, kerbal, numbers, names) == null;
            }
            catch (Exception e) { Log.Exception("LonapseApi.Trigger", e); return false; }
        }

        /// <summary>Trigger without values.</summary>
        public static bool Trigger(string eventId, Vessel vessel, ProtoCrewMember kerbal)
        {
            return Trigger(eventId, vessel, kerbal, null, null);
        }

        /// <summary>True when lines for the event are loaded (an event chat or a Mission Control event line). The
        /// tables load with the first flight scene, so before that it is false.</summary>
        public static bool HasLines(string eventId)
        {
            try { return ModEvents.Info(PhraseClips.runtimeTemplates, eventId).HasLines; }
            catch (Exception) { return false; }
        }
    }
}
