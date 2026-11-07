// Events of other mods (the public API, LonapseApi): what a mod's lines say about an event (meta rows), and the
// pending list with its cooldowns. Pure code: no Unity or KSP references, so it compiles under DC_OFFLINE_TEST.
using System;
using System.Collections.Generic;

namespace Lonapse
{
    /// <summary>The settings of a mod event, from its meta row (or the defaults).</summary>
    public sealed class ModEventInfo
    {
        public string Id;
        public double Cooldown = ModEvents.DefaultCooldown;
        /// <summary>0 low, 1 normal, 2 high.</summary>
        public int Priority = 1;
        /// <summary>crew (event chats only), mc (event lines only) or any.</summary>
        public string Who = "any";
        /// <summary>The event has an event chat (on=id) and/or a Mission Control event line.</summary>
        public bool HasChat, HasMc;
        public bool HasLines { get { return (HasChat && Who != "mc") || (HasMc && Who != "crew"); } }
    }

    public static class ModEvents
    {
        public const double DefaultCooldown = 120.0;   // s, per event id
        public const double GlobalGap = 30.0;          // s between two mod events, high priority skips it
        public const double MaxAge = 90.0;             // s, an event that waited longer is dropped
        public const int MaxPending = 6;

        /// <summary>A valid event id: mod.event, a-z 0-9 _ on both sides of the one dot.</summary>
        public static bool ValidId(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            int dot = id.IndexOf('.');
            if (dot <= 0 || dot == id.Length - 1 || id.IndexOf('.', dot + 1) >= 0) return false;
            for (int i = 0; i < id.Length; i++)
            {
                char c = id[i];
                if (i != dot && !(c >= 'a' && c <= 'z') && !(c >= '0' && c <= '9') && c != '_') return false;
            }
            return true;
        }

        public static int PriorityOf(string p) { return p == "low" ? 0 : p == "high" ? 2 : 1; }

        /// <summary>The topic belongs to a mod's experiment, device or contract row: it plays on that moment
        /// (the experiment, the deployed part, the contract), not by Trigger.</summary>
        public static bool IsRegisteredTopic(IList<RuntimeTemplate> all, string id)
        {
            return RuntimeLines.RegisteredTopic(all, id) != null;
        }

        /// <summary>The settings and lines of event id in the loaded templates.</summary>
        public static ModEventInfo Info(IList<RuntimeTemplate> all, string id)
        {
            var info = new ModEventInfo { Id = id };
            if (all == null || id == null) return info;
            foreach (RuntimeTemplate t in all)
            {
                if (!t.On.Contains(id)) continue;
                if (t.Kind == "meta")
                {
                    if (t.Cooldown >= 0) info.Cooldown = t.Cooldown;
                    if (t.Priority != null) info.Priority = PriorityOf(t.Priority);
                    if (t.Who != null) info.Who = t.Who;
                }
                else if (t.Kind == "chat") info.HasChat = true;
                else if (t.Kind == "event") info.HasMc = true;
            }
            return info;
        }

        /// <summary>The mod events with lines in the templates, sorted (for the Mods tab).</summary>
        public static List<string> Ids(IList<RuntimeTemplate> all)
        {
            var r = new List<string>();
            if (all != null)
                foreach (RuntimeTemplate t in all)
                    foreach (string o in t.On)
                        if (RuntimeLines.IsModEvent(o) && !r.Contains(o)) r.Add(o);
            // a registered topic (experiment, device, contract) plays on its own moment, never by Trigger
            if (all != null)
                foreach (RuntimeTemplate t in all)
                    if (RuntimeLines.IsRegKind(t.Kind)) r.Remove(t.On[0]);
            r.Sort(StringComparer.Ordinal);
            return r;
        }

        /// <summary>A name value from a mod: "english", or "spanish|english". Null when empty.</summary>
        public static string[] SplitName(string value)
        {
            if (string.IsNullOrEmpty(value)) return null;
            int bar = value.IndexOf('|');
            string es = bar >= 0 ? value.Substring(0, bar).Trim() : value.Trim();
            string en = bar >= 0 ? value.Substring(bar + 1).Trim() : value.Trim();
            if (es.Length == 0 || en.Length == 0) return null;
            return new[] { es, en };
        }
    }

    /// <summary>The pending mod events: at most MaxPending, one per id, each dropped after MaxAge s. An id plays at
    /// most once per its cooldown, and two mod events keep GlobalGap s apart unless the second one is high.</summary>
    public sealed class ModEventQueue
    {
        public sealed class Entry
        {
            public string Id;
            public int Priority;
            public double Cooldown, At;
            public object Data;
        }

        readonly List<Entry> pending = new List<Entry>();
        readonly Dictionary<string, double> lastPlayed = new Dictionary<string, double>();
        double lastAny = double.NegativeInfinity;

        public int Count { get { return pending.Count; } }

        /// <summary>Adds an event. Null when it is accepted, else why not: "cooldown", "already waiting", "queue full".</summary>
        public string Offer(string id, int priority, double cooldown, double now, object data)
        {
            Expire(now);
            double t;
            if (lastPlayed.TryGetValue(id, out t) && now - t < cooldown) return "cooldown";
            if (pending.Exists(e => e.Id == id)) return "already waiting";
            if (pending.Count >= ModEvents.MaxPending) return "queue full";
            pending.Add(new Entry { Id = id, Priority = priority, Cooldown = cooldown, At = now, Data = data });
            return null;
        }

        /// <summary>The next event to play now, or null: the highest priority first, then the oldest. minPriority:
        /// the lowest priority the air allows now (0 all, 2 only high, 3 none). Taking it starts its cooldown.</summary>
        /// <summary>An event whose id starts with prefix is waiting.</summary>
        public bool Waiting(string prefix)
        {
            foreach (Entry e in pending) if (e.Id.StartsWith(prefix, StringComparison.Ordinal)) return true;
            return false;
        }

        public Entry Take(double now, int minPriority)
        {
            Expire(now);
            Entry best = null;
            foreach (Entry e in pending)
            {
                if (e.Priority < minPriority) continue;
                if (e.Priority < 2 && now - lastAny < ModEvents.GlobalGap) continue;
                if (best == null || e.Priority > best.Priority || (e.Priority == best.Priority && e.At < best.At)) best = e;
            }
            if (best == null) return null;
            pending.Remove(best);
            lastPlayed[best.Id] = now;
            lastAny = now;
            return best;
        }

        /// <summary>Puts a taken event back as if it never played (nothing could be queued for it).</summary>
        public void Undo(Entry e, double previousAny)
        {
            if (e == null) return;
            lastPlayed.Remove(e.Id);
            lastAny = previousAny;
        }

        public double LastAny { get { return lastAny; } }

        /// <summary>Removes a waiting event (it has not played yet). True when one was waiting.</summary>
        public bool Cancel(string id)
        {
            return pending.RemoveAll(e => e.Id == id) > 0;
        }

        void Expire(double now)
        {
            pending.RemoveAll(e => now - e.At > ModEvents.MaxAge);
        }

        public void Clear() { pending.Clear(); }
    }
}
