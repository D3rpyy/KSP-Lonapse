// Events of other mods (LonapseApi.Trigger): the pending list, the cooldowns, and what plays for an event: an event
// chat (on=mod.event), or a Mission Control event line (kind event) with a crew reply. Without the runtime voice
// the same lines are shown as subtitles with the radio keying.
using System;
using System.Collections.Generic;

namespace Lonapse
{
    public partial class ChatterDirector
    {
        readonly ModEventQueue modQueue = new ModEventQueue();
        /// <summary>The last mod events and what became of them, newest first (Mods tab).</summary>
        public readonly List<string> modLog = new List<string>();
        /// <summary>QueueRuntimeOnly shows the line as a subtitle instead of speaking it.</summary>
        bool runtimeTextOnly;
        readonly HashSet<string> modDropSaid = new HashSet<string>();

        sealed class ModData
        {
            public string kerbal;
            public Dictionary<string, double> nums = new Dictionary<string, double>();
            public Dictionary<string, string[]> names = new Dictionary<string, string[]>();
        }

        /// <summary>Chance of the event chat when both the chat and the Mission Control line fit (who=any).</summary>
        const double ModChatShare = 0.6;
        const double ModReplyChance = 0.8;

        void NoteMod(string line)
        {
            modLog.Insert(0, line);
            while (modLog.Count > 5) modLog.RemoveAt(modLog.Count - 1);
        }

        /// <summary>A drop reason, logged once per event and reason.</summary>
        string ModDrop(string id, string why)
        {
            if (modDropSaid.Add(id + "|" + why)) Log.Info("Mod event " + id + " dropped: " + why);
            NoteMod(id + ": dropped, " + why);
            return why;
        }

        /// <summary>
        /// LonapseApi.Trigger: accepts a mod event into the pending list. Null when accepted, else why not. vessel:
        /// the vessel it happened on (null: the active one). kerbal: who did it (null: Lonapse picks). numbers: the
        /// x_ values. names: x_ values as "english" or "spanish|english".
        /// </summary>
        public string OfferModEvent(string id, Vessel vessel, ProtoCrewMember kerbal, IDictionary<string, double> numbers,
                                    IDictionary<string, string> names)
        {
            if (!ModEvents.ValidId(id)) return ModDrop(id ?? "(null)", "the id is not mod.event (a-z 0-9 _)");
            RuntimeTemplate reg = RuntimeLines.RegisteredTopic(PhraseClips.runtimeTemplates, id);
            if (reg != null)
                return ModDrop(id, "it is the topic of " + (reg.Kind == "experiment" ? "an " : "a ") + reg.Kind + " row, its lines play on that moment itself");
            if (PhraseClips.MutedEvent(id)) return ModDrop(id, "the player muted the lines of this mod");
            ModEventInfo info = ModEvents.Info(PhraseClips.runtimeTemplates, id);
            if (!info.HasLines) return ModDrop(id, "no lines for it are loaded");
            Vessel av = ActiveVessel;
            if (av == null) return ModDrop(id, "no active vessel");
            var d = new ModData();
            if (kerbal != null)
            {
                KerbalVoice k = host.roster.Find(kerbal.name);
                if (k == null) return ModDrop(id, "the kerbal is not aboard or nearby");
                d.kerbal = k.name;
            }
            else if (vessel != null && vessel.id != av.id
                     && !host.roster.voices.Exists(k => k.vesselId == vessel.id) && !host.roster.radio.Exists(k => k.vesselId == vessel.id))
                return ModDrop(id, "the vessel is not the active one or nearby");
            if (numbers != null)
                foreach (var kv in numbers)
                {
                    if (!Slots.IsModSlot(kv.Key)) { IgnoredValue(id, kv.Key); continue; }
                    if (double.IsNaN(kv.Value) || double.IsInfinity(kv.Value)) continue;
                    d.nums[kv.Key] = kv.Value;
                }
            if (names != null)
                foreach (var kv in names)
                {
                    if (!Slots.IsModSlot(kv.Key)) { IgnoredValue(id, kv.Key); continue; }
                    string[] n = ModEvents.SplitName(kv.Value);
                    if (n == null) continue;
                    // the spoken form like a vessel name: respelled for the voice, left out when it cannot be said
                    string say = RuntimeLines.VesselSpeakable(n[0]);
                    if (say == null) continue;
                    d.names[kv.Key] = new[] { RuntimeSpeech.Respell(say), RuntimeLines.CleanName(n[1]) };
                }
            string why = modQueue.Offer(id, info.Priority, info.Cooldown, clock, d);
            if (why != null) return ModDrop(id, why);
            Log.Info("Mod event " + id + " accepted (" + modQueue.Count + " waiting)");
            return null;
        }

        void IgnoredValue(string id, string key)
        {
            if (modDropSaid.Add(id + "|value " + key)) Log.Info("Mod event " + id + ": value " + key + " ignored, a mod value starts with x_");
        }

        /// <summary>LonapseApi.Cancel: drops a waiting mod event that has not played yet. True when one was waiting.</summary>
        /// <summary>A mod event whose id starts with prefix waits to play.</summary>
        public bool ModWaiting(string prefix) { return modQueue.Waiting(prefix); }

        public bool CancelModEvent(string id)
        {
            if (id == null || !modQueue.Cancel(id)) return false;
            Log.Info("Mod event " + id + " cancelled by its mod");
            NoteMod(id + ": cancelled");
            return true;
        }

        /// <summary>Every frame: plays one waiting mod event when the air is free.</summary>
        void ModTick()
        {
            if (modQueue.Count == 0 || !host.CanStartAudio || radioSilent || Holding || runtimeChatPending) return;
            if (HasRadioItems() || HasChatItems() || !Idle) return;
            float s = host.stress.stress;
            Tuning tu = Tu;
            // low and normal only when it is calm enough for chatter, high up to a panic
            int min = s < tu.burstBand ? 0 : s < 0.85f ? 2 : 3;
            double before = modQueue.LastAny;
            ModEventQueue.Entry e = modQueue.Take(clock, min);
            if (e == null) return;
            string result = PlayMod(e.Id, (ModData)e.Data);
            if (result == null) { modQueue.Undo(e, before); ModDrop(e.Id, "nothing fits now"); return; }
            Log.Info("Mod event " + e.Id + ": " + result);
            NoteMod(e.Id + ": " + result);
        }

        /// <summary>Queues what plays for a mod event. Returns what it was ("event chat", "Mission Control line"),
        /// null when nothing fits.</summary>
        string PlayMod(string id, ModData d)
        {
            ModEventInfo info = ModEvents.Info(PhraseClips.runtimeTemplates, id);
            KerbalVoice k = d.kerbal != null ? host.roster.Find(d.kerbal) : null;
            if (k == null || !CanChat(k)) k = PickRested();
            bool reach = !McUnreachable && host.signal.connected && host.settings.radioEnabled;
            bool text = !RuntimeOn;
            bool chatOk = info.HasChat && info.Who != "mc" && k != null;
            bool mcOk = info.HasMc && info.Who != "crew" && reach;
            bool chatFirst = chatOk && (!mcOk || rng.NextDouble() < ModChatShare);
            string tag = text ? " (subtitles, the runtime voice is off)" : "";
            if (chatFirst && ModChat(id, k, d, text)) return "event chat" + tag;
            if (mcOk && ModMc(id, k, d, text)) return "Mission Control line" + tag;
            if (!chatFirst && chatOk && ModChat(id, k, d, text)) return "event chat" + tag;
            return null;
        }

        bool ModChat(string id, KerbalVoice k, ModData d, bool text)
        {
            KerbalVoice partner = PartnerOf(k);
            return EventChat(id, k, partner, d.names, null, d.nums, text)
                   || (partner != null && EventChat(id, k, null, d.names, null, d.nums, text));
        }

        bool ModMc(string id, KerbalVoice k, ModData d, bool text)
        {
            Vessel v = ActiveVessel;
            if (v == null) return false;
            string sit = SituationOf(v) ?? "other";
            var nums = new Dictionary<string, double>();
            var names = new Dictionary<string, string[]>();
            CollectValues(v, sit, nums, names, false);
            foreach (var kv in d.nums) nums[kv.Key] = kv.Value;
            foreach (var kv in d.names) names[kv.Key] = kv.Value;
            string[] kn = k != null ? FirstNames(k) : null;
            if (kn != null) { names["kerbal"] = kn; reportKerbal = k; }
            runtimeTopic = id;
            runtimeTextOnly = text;
            try { return QueueKind("event", sit, nums, names, U(0.2f, 0.5f), false, ModReplyChance); }
            finally { runtimeTopic = null; runtimeTextOnly = false; reportKerbal = null; }
        }

        /// <summary>A subtitle-only line: radio keying around it for a kerbal, a short static click for Mission Control.</summary>
        void QueueSubtitle(KerbalVoice sp, PhraseEntry e, float delay)
        {
            if (count + 4 > Capacity) return;
            Enqueue(UttKind.RadioOpen, null, delay, 0f, null);
            Utterance u = Enqueue(UttKind.SubtitleLine, null, 0.05f, 0f, sp);
            if (u != null) u.entry = e;
            if (sp != null && host.settings.rogerBeep) Enqueue(UttKind.RogerBeep, null, 0.04f, 0f, null);
            Enqueue(UttKind.RadioClose, null, 0.02f, 0f, null);
        }

        /// <summary>A chat shown as subtitles: the two kerbals in turn, each line keyed like a radio line. False when
        /// the queue has no room.</summary>
        bool QueueSubtitleChat(KerbalVoice a, KerbalVoice b, List<PhraseEntry> entries, bool radio)
        {
            if (count + 4 * entries.Count > Capacity) return false;
            for (int i = 0; i < entries.Count; i++)
                QueueSubtitle(i % 2 == 0 ? a : b, entries[i], i == 0 ? U(0.2f, 0.6f) : ResponseDelay(host.stress.stress));
            return true;
        }

        /// <summary>Mods tab: plays a mod event now with the active kerbal and test values for its x_ slots, past the
        /// cooldown. The result for the overlay.</summary>
        public string TestModEvent(string id)
        {
            var d = new ModData { kerbal = host.roster.Own != null ? host.roster.Own.name : null };
            foreach (RuntimeTemplate t in PhraseClips.runtimeTemplates)
            {
                if (!t.On.Contains(id) && !(t.Kind == "reply" && t.To.Count > 0)) continue;
                foreach (string[] ver in t.Versions)
                    foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(ver[1], @"\{(x_[a-z0-9_]+)\}"))
                    {
                        string key = m.Groups[1].Value;
                        if (!d.nums.ContainsKey(key) && !d.names.ContainsKey(key))
                        {
                            // a slot in a check is a number, the rest a name
                            if (t.Checks.Exists(c => c.Key == key)) d.nums[key] = 3;
                            else d.names[key] = new[] { RuntimeSpeech.Respell("Test"), "Test" };
                        }
                    }
            }
            string r = PlayMod(id, d);
            NoteMod(id + ": test, " + (r ?? "nothing fits now"));
            Log.Info("Mod event " + id + " test: " + (r ?? "nothing fits now"));
            return r ?? "nothing fits now";
        }
    }
}
