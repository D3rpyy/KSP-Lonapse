// DeepFreeze (DeepFreeze Updated): its freeze, thaw and comatose events become Lonapse mod events, so the lines in
// Compat/DeepFreeze/deepfreeze.tsv play on them. Once a second it reads DeepFreeze's public list of frozen kerbals
// (DeepFreeze.Instance.FrozenKerbals, the API other mods use too): who is frozen aboard and who is comatose.
// DeepFreeze is found through reflection, there is no dependency. Without DeepFreeze nothing is subscribed.
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
        private EventData<Part, ProtoCrewMember> onFrozen, onThaw, onComatose, onAwake;
        private EventData<ProtoCrewMember> onDied;

        public bool Active { get { return onFrozen != null || onThaw != null; } }

        public void Init(LonapseAddon owner)
        {
            host = owner;
            try
            {
                if (!PhraseClips.AssemblyLoaded("DeepFreeze")) return;
                Type t = FindEvents();
                if (t == null) { Log.Warn("DeepFreeze is installed but DF.DFGameEvents is missing: no freeze or thaw lines (a new DeepFreeze version?)"); return; }
                onFrozen = Field<EventData<Part, ProtoCrewMember>>(t, "onKerbalFrozen");
                onThaw = Field<EventData<Part, ProtoCrewMember>>(t, "onKerbalThaw");
                onComatose = Field<EventData<Part, ProtoCrewMember>>(t, "onKerbalSetComatose");
                onAwake = Field<EventData<Part, ProtoCrewMember>>(t, "onKerbalUnSetComatose");
                onDied = Field<EventData<ProtoCrewMember>>(t, "onFrozenKerbalDied");
                if (onFrozen != null) onFrozen.Add(OnFrozen);
                if (onThaw != null) onThaw.Add(OnThaw);
                if (onComatose != null) onComatose.Add(OnComatose);
                if (onDied != null) onDied.Add(OnDied);
                if (onAwake != null) onAwake.Add(OnAwake);
                FindList(t.Assembly);
                if (onFrozen == null || onThaw == null || onComatose == null || onDied == null)
                    Log.Warn("DeepFreeze: not every event was found, those get no lines (a new DeepFreeze version?)");
                Log.Info("DeepFreeze found: freeze " + (onFrozen != null) + ", thaw " + (onThaw != null)
                         + ", comatose " + (onComatose != null) + ", died frozen " + (onDied != null) + ", frozen list " + (frozenProp != null));
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
                if (onAwake != null) onAwake.Remove(OnAwake);
            }
            catch (Exception e) { Log.Exception("DeepFreezeBridge.Dispose", e); }
            onFrozen = onThaw = onComatose = onAwake = null;
            thawed.Clear();
            frozenAboard.Clear();
            CrewRoster.comatose.Clear();
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

        // ---- the list of frozen kerbals ----
        private FieldInfo instanceField;      // DF.DeepFreeze.Instance
        private PropertyInfo frozenProp;      // its FrozenKerbals: name to KerbalInfo
        private FieldInfo infoVessel, infoType;   // KerbalInfo.vesselID and type
        private float listLeft;
        private bool listFailed;
        /// <summary>The kerbals frozen in the active vessel at the last read. DeepFreeze takes a kerbal off its list
        /// before it reports the death, so the last read says where they were.</summary>
        private readonly HashSet<string> frozenAboard = new HashSet<string>(StringComparer.Ordinal);

        private void FindList(Assembly a)
        {
            Type dt = a.GetType("DF.DeepFreeze", false), it = a.GetType("DF.KerbalInfo", false);
            if (dt != null) instanceField = dt.GetField("Instance", BindingFlags.Public | BindingFlags.Static);
            if (dt != null) frozenProp = dt.GetProperty("FrozenKerbals", BindingFlags.Public | BindingFlags.Instance);
            if (it != null) { infoVessel = it.GetField("vesselID"); infoType = it.GetField("type"); }
            if (instanceField == null || frozenProp == null || infoVessel == null || infoType == null)
            {
                frozenProp = null;
                Log.Warn("DeepFreeze: the list of frozen kerbals was not found, a death in the freezer of a ship loaded frozen is not reported and comatose kerbals keep talking (a new DeepFreeze version?)");
            }
        }

        /// <summary>Once a second: who is frozen aboard the active vessel and who is comatose (anywhere).</summary>
        private void ReadList()
        {
            if (frozenProp == null || listFailed) return;
            try
            {
                object df = instanceField.GetValue(null);
                if (df == null) return;
                var list = frozenProp.GetValue(df, null) as System.Collections.IDictionary;
                if (list == null) return;
                Vessel av = FlightGlobals.ActiveVessel;
                Guid id = av != null ? av.id : Guid.Empty;
                frozenAboard.Clear();
                CrewRoster.comatose.Clear();
                foreach (System.Collections.DictionaryEntry e in list)
                {
                    string name = e.Key as string;
                    if (name == null || e.Value == null) continue;
                    object vt = infoType.GetValue(e.Value);
                    if (vt is ProtoCrewMember.KerbalType && (ProtoCrewMember.KerbalType)vt == ProtoCrewMember.KerbalType.Tourist)
                        CrewRoster.comatose.Add(name);   // DeepFreeze keeps comatose kerbals on the list as Tourist
                    else if (id != Guid.Empty && infoVessel.GetValue(e.Value) is Guid && (Guid)infoVessel.GetValue(e.Value) == id)
                        frozenAboard.Add(name);
                }
            }
            catch (Exception e)
            {
                listFailed = true;
                Log.Warn("DeepFreeze: reading the list of frozen kerbals failed, stopped: " + e.Message);
            }
        }

        private void OnAwake(Part p, ProtoCrewMember k) { if (k != null) CrewRoster.comatose.Remove(k.name); }

        private sealed class Waiting { public string name; public Part part; public float left; }

        /// <summary>Seconds a thawed kerbal may take to show up in the crew list.</summary>
        private const float ThawWait = 3f;
        private readonly List<Waiting> thawed = new List<Waiting>();

        private void OnFrozen(Part p, ProtoCrewMember k) { Offer(Frozen, p, k, false); }
        private void OnComatose(Part p, ProtoCrewMember k)
        {
            if (k != null) CrewRoster.comatose.Add(k.name);
            Offer(Comatose, p, k, false);
        }

        // the thawed kerbal says line 1, once the crew list has them again (the next rebuild)
        private void OnThaw(Part p, ProtoCrewMember k)
        {
            if (k != null) thawed.Add(new Waiting { name = k.name, part = p, left = ThawWait });
        }

        /// <summary>Every frame: offers a waiting thaw when the kerbal is back aboard, or drops it after ThawWait. Once
        /// a second the list of frozen kerbals.</summary>
        public void Tick(float dt)
        {
            listLeft -= dt;
            if (listLeft <= 0f) { listLeft = 1f; ReadList(); }
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
            try { if (k != null && host != null && host.events != null) host.events.OnFrozenKerbalDied(k.name, frozenAboard.Contains(k.name)); }
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
