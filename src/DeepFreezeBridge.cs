// DeepFreeze (DeepFreeze Updated): its freeze, thaw and comatose events become Lonapse mod events, so the lines in
// Phrases/Runtime/deepfreeze.tsv play on them. DeepFreeze is found through reflection, there is no dependency.
// Without DeepFreeze nothing is subscribed.
#if !DC_OFFLINE_TEST
using System;
using System.Collections.Generic;
using System.Reflection;

namespace Lonapse
{
    public sealed class DeepFreezeBridge : IDisposable
    {
        public const string Frozen = "deepfreeze.frozen", Thawed = "deepfreeze.thawed", Comatose = "deepfreeze.comatose";

        private LonapseAddon host;
        private EventData<Part, ProtoCrewMember> onFrozen, onThaw, onComatose;
        private EventData<ProtoCrewMember> onDied;

        public bool Active { get { return onFrozen != null || onThaw != null; } }

        public void Init(LonapseAddon owner)
        {
            host = owner;
            try
            {
                Type t = FindEvents();
                if (t == null) return;
                onFrozen = Field<EventData<Part, ProtoCrewMember>>(t, "onKerbalFrozen");
                onThaw = Field<EventData<Part, ProtoCrewMember>>(t, "onKerbalThaw");
                onComatose = Field<EventData<Part, ProtoCrewMember>>(t, "onKerbalSetComatose");
                onDied = Field<EventData<ProtoCrewMember>>(t, "onFrozenKerbalDied");
                if (onFrozen != null) onFrozen.Add(OnFrozen);
                if (onThaw != null) onThaw.Add(OnThaw);
                if (onComatose != null) onComatose.Add(OnComatose);
                if (onDied != null) onDied.Add(OnDied);
                Log.Info("DeepFreeze found: freeze " + (onFrozen != null) + ", thaw " + (onThaw != null)
                         + ", comatose " + (onComatose != null) + ", died frozen " + (onDied != null));
            }
            catch (Exception e) { Log.Exception("DeepFreezeBridge.Init", e); }
        }

        public void Dispose()
        {
            try
            {
                if (onFrozen != null) onFrozen.Remove(OnFrozen);
                if (onThaw != null) onThaw.Remove(OnThaw);
                if (onComatose != null) onComatose.Remove(OnComatose);
                if (onDied != null) onDied.Remove(OnDied);
            }
            catch (Exception e) { Log.Exception("DeepFreezeBridge.Dispose", e); }
            onFrozen = onThaw = onComatose = null;
            thawed.Clear();
            onDied = null;
        }

        /// <summary>DF.DFGameEvents in the loaded DeepFreeze assembly, or null.</summary>
        private static Type FindEvents()
        {
            foreach (AssemblyLoader.LoadedAssembly la in AssemblyLoader.loadedAssemblies)
            {
                // la.name is the KSPAssembly name ("DeepFreeze Continued..."), the file name is DeepFreeze
                if (la == null || la.assembly == null || la.assembly.GetName().Name != "DeepFreeze") continue;
                Type t = la.assembly.GetType("DF.DFGameEvents", false);
                if (t != null) return t;
            }
            return null;
        }

        private static T Field<T>(Type t, string name) where T : class
        {
            FieldInfo f = t.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            return f != null ? f.GetValue(null) as T : null;
        }

        private sealed class Waiting { public string name; public Part part; public float left; }

        /// <summary>Seconds a thawed kerbal may take to show up in the crew list.</summary>
        private const float ThawWait = 3f;
        private readonly List<Waiting> thawed = new List<Waiting>();

        private void OnFrozen(Part p, ProtoCrewMember k) { Offer(Frozen, p, k, false); }
        private void OnComatose(Part p, ProtoCrewMember k) { Offer(Comatose, p, k, false); }

        // the thawed kerbal says line 1, once the crew list has them again (the next rebuild)
        private void OnThaw(Part p, ProtoCrewMember k)
        {
            if (k != null) thawed.Add(new Waiting { name = k.name, part = p, left = ThawWait });
        }

        /// <summary>Every frame: offers a waiting thaw when the kerbal is back aboard, or drops it after ThawWait.</summary>
        public void Tick(float dt)
        {
            for (int i = thawed.Count - 1; i >= 0; i--)
            {
                Waiting w = thawed[i];
                w.left -= dt;
                bool back = host != null && host.roster != null && host.roster.Find(w.name) != null;
                if (!back && w.left > 0f) continue;
                thawed.RemoveAt(i);
                ProtoCrewMember k = null;
                try { k = HighLogic.CurrentGame.CrewRoster[w.name]; } catch (Exception) { }
                if (back && k != null) Offer(Thawed, w.part, k, true);
                else Log.Info("DeepFreeze " + Thawed + ": " + Names.Localize(w.name) + " not in the crew list, no line");
            }
        }

        private void OnDied(ProtoCrewMember k)
        {
            try { if (k != null && host != null && host.events != null) host.events.OnFrozenKerbalDied(k.name); }
            catch (Exception e) { Log.Exception("DeepFreezeBridge.OnDied", e); }
        }

        /// <summary>The event goes to the director with the kerbal's first name as x_frozen. speaks: the kerbal says
        /// line 1 (a thaw). Else somebody else aboard does (the frozen one is in the freezer, the comatose one out).</summary>
        private void Offer(string id, Part p, ProtoCrewMember k, bool speaks)
        {
            try
            {
                if (host == null || host.director == null || k == null) return;
                string first = RuntimeLines.CleanName(SubtitleSlots.FirstName(Names.Localize(k.name)));
                if (string.IsNullOrEmpty(first)) return;
                var names = new Dictionary<string, string> { { "x_frozen", first } };
                string why = host.director.OfferModEvent(id, p != null ? p.vessel : null, speaks ? k : null, null, names);
                Log.Info("DeepFreeze " + id + ": " + first + (why == null ? "" : " (not played: " + why + ")"));
            }
            catch (Exception e) { Log.Exception("DeepFreezeBridge.Offer", e); }
        }
    }
}
#endif
