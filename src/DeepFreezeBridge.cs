// DeepFreeze (DeepFreeze Updated): its freeze, thaw and comatose events become Lonapse mod events, so the lines in
// Compat/DeepFreeze/deepfreeze.tsv play on them. Kerbals frozen or thawed one after another become one group, so
// two get both names in one line and three or more a line for the group. Once a second it reads DeepFreeze's public list of frozen kerbals
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
        private EventData<Part, string> onFreezing, onThawing, onFreezeAbort, onThawAbort;   // starts and aborts, by name

        public bool Active { get { return onFrozen != null || onThaw != null; } }

        /// <summary>Kerbals DeepFreeze has frozen in the active vessel (the last read, once a second).</summary>
        public int FrozenAboard { get { return frozenAboard.Count; } }

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
                onFreezing = Field<EventData<Part, string>>(t, "onKerbalFreezing");
                onThawing = Field<EventData<Part, string>>(t, "onKerbalThawing");
                onFreezeAbort = Field<EventData<Part, string>>(t, "onKerbalFreezeAbort");
                onThawAbort = Field<EventData<Part, string>>(t, "onKerbalThawAbort");
                onDied = Field<EventData<ProtoCrewMember>>(t, "onFrozenKerbalDied");
                if (onFrozen != null) onFrozen.Add(OnFrozen);
                if (onThaw != null) onThaw.Add(OnThaw);
                if (onComatose != null) onComatose.Add(OnComatose);
                if (onDied != null) onDied.Add(OnDied);
                if (onAwake != null) onAwake.Add(OnAwake);
                if (onFreezing != null) onFreezing.Add(OnFreezing);
                if (onThawing != null) onThawing.Add(OnThawing);
                if (onFreezeAbort != null) onFreezeAbort.Add(OnFreezeAbort);
                if (onThawAbort != null) onThawAbort.Add(OnThawAbort);
                startsKnown = onFreezing != null && onThawing != null;
                FindList(t.Assembly);
                if (onFrozen == null || onThaw == null || onComatose == null || onDied == null)
                    Log.Warn("DeepFreeze: not every event was found, those get no lines (a new DeepFreeze version?)");
                Log.Info("DeepFreeze found: freeze " + (onFrozen != null) + ", thaw " + (onThaw != null)
                         + ", comatose " + (onComatose != null) + ", died frozen " + (onDied != null) + ", frozen list " + (frozenProp != null)
                         + ", starts " + startsKnown);
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
                if (onFreezing != null) onFreezing.Remove(OnFreezing);
                if (onThawing != null) onThawing.Remove(OnThawing);
                if (onFreezeAbort != null) onFreezeAbort.Remove(OnFreezeAbort);
                if (onThawAbort != null) onThawAbort.Remove(OnThawAbort);
            }
            catch (Exception e) { Log.Exception("DeepFreezeBridge.Dispose", e); }
            onFrozen = onThaw = onComatose = onAwake = null;
            onFreezing = onThawing = onFreezeAbort = onThawAbort = null;
            freezing.Clear(); thawing.Clear();
            frozenBatch.Clear(); thawedBatch.Clear(); comatoseBatch.Clear();
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

        // ---- the moments, grouped: DeepFreeze freezes and thaws one kerbal at a time ----
        private double clock;
        private readonly NameBatch frozenBatch = new NameBatch(), thawedBatch = new NameBatch(), comatoseBatch = new NameBatch();
        private Part frozenPart, thawedPart, comatosePart;

        // a freeze or thaw that started and has not ended: the group waits for it. Name to start time.
        private readonly Dictionary<string, double> freezing = new Dictionary<string, double>(), thawing = new Dictionary<string, double>();
        private bool startsKnown;
        /// <summary>A start without an end (a vessel switch, a DeepFreeze error) stops counting after this.</summary>
        private const double StuckAfter = 30.0;

        private void OnFreezing(Part p, string name) { if (name != null) { freezing[name] = clock; Changed(name, true); } }
        private void OnThawing(Part p, string name) { if (name != null) { thawing[name] = clock; Changed(name, false); } }

        /// <summary>A kerbal goes the other way (frozen again before the thaw line, or thawed before the freeze
        /// line): the old moment is dropped, from the open group and from the queue, so no stale line plays.</summary>
        private void Changed(string name, bool freezingNow)
        {
            NameBatch b = freezingNow ? thawedBatch : frozenBatch;
            b.Remove(name);
            if (freezingNow) comatoseBatch.Remove(name);
            if (host == null || host.director == null) return;
            string id = freezingNow ? Thawed : Frozen;
            foreach (string ev in new[] { id, id + "_two", id + "_group" })
                if (host.director.CancelModEvent(ev)) Log.Info("DeepFreeze " + ev + " dropped: " + Names.Localize(name) + (freezingNow ? " freezes again" : " thaws"));
        }
        private void OnFreezeAbort(Part p, string name) { if (name != null) freezing.Remove(name); }
        private void OnThawAbort(Part p, string name) { if (name != null) thawing.Remove(name); }

        private void OnFrozen(Part p, ProtoCrewMember k)
        {
            if (k == null) return;
            freezing.Remove(k.name);
            Changed(k.name, true);
            frozenBatch.Add(clock, k.name);
            frozenPart = p;
        }

        private void OnThaw(Part p, ProtoCrewMember k)
        {
            if (k == null) return;
            thawing.Remove(k.name);
            Changed(k.name, false);
            thawedBatch.Add(clock, k.name);
            thawedPart = p;
        }

        private bool Running(Dictionary<string, double> d)
        {
            if (d.Count == 0) return false;
            var old = new List<string>();
            foreach (var kv in d) if (clock - kv.Value > StuckAfter) old.Add(kv.Key);
            foreach (string n in old) d.Remove(n);
            return d.Count > 0;
        }

        /// <summary>A freeze or thaw is running, a group is open, or its line still waits: other talk holds off, so
        /// no ordinary radio call comes between the freezer and its line.</summary>
        public bool Busy
        {
            get
            {
                return freezing.Count > 0 || thawing.Count > 0 || frozenBatch.Count > 0 || thawedBatch.Count > 0 || comatoseBatch.Count > 0
                       || (host != null && host.director != null && host.director.ModWaiting("deepfreeze."));
            }
        }

        private void OnComatose(Part p, ProtoCrewMember k)
        {
            if (k == null) return;
            thawing.Remove(k.name);
            CrewRoster.comatose.Add(k.name);
            comatoseBatch.Add(clock, k.name);
            comatosePart = p;
        }

        /// <summary>Every frame: plays a group that is due. Once a second the list of frozen kerbals.</summary>
        public void Tick(float dt)
        {
            clock += dt;
            listLeft -= dt;
            if (listLeft <= 0f) { listLeft = 1f; ReadList(); }
            // with the starts known: Grace s after the last end while none runs. Else Quiet s after the last end.
            double wait = startsKnown ? NameBatch.Grace : NameBatch.Quiet;
            bool fz = startsKnown && Running(freezing), th = startsKnown && Running(thawing);
            List<string> g;
            if ((g = frozenBatch.Take(clock, fz, wait)) != null) Offer(Frozen, frozenPart, g, false);
            if ((g = thawedBatch.Take(clock, th, wait)) != null)
            {
                // an emergency thaw ends comatose: those get the comatose line, not the groggy hello
                g.RemoveAll(n => CrewRoster.comatose.Contains(n));
                if (g.Count > 0) Offer(Thawed, thawedPart, g, true);
            }
            if ((g = comatoseBatch.Take(clock, th, wait)) != null) Offer(Comatose, comatosePart, g, false);
        }

        private void OnDied(ProtoCrewMember k)
        {
            try { if (k != null && host != null && host.events != null) host.events.OnFrozenKerbalDied(k.name, frozenAboard.Contains(k.name)); }
            catch (Exception e) { Log.Exception("DeepFreezeBridge.OnDied", e); }
        }

        /// <summary>One line for the group: id for one kerbal (x_frozen), id_two for two (x_frozen and x_frozen2),
        /// id_group for three or more (x_count, x_frozen is one of them). thawedSpeaks: a thawed kerbal says line 1.
        /// Else somebody else aboard does (the frozen are in the freezer, the comatose out).</summary>
        private void Offer(string id, Part p, List<string> group, bool thawedSpeaks)
        {
            try
            {
                if (host == null || host.director == null || group.Count == 0) return;
                var firsts = new List<string>();
                foreach (string n in group)
                {
                    string f = RuntimeLines.CleanName(SubtitleSlots.FirstName(Names.Localize(n)));
                    if (!string.IsNullOrEmpty(f)) firsts.Add(f);
                }
                if (firsts.Count == 0) return;
                string ev = NameBatch.IdFor(id, group.Count);
                var names = new Dictionary<string, string> { { "x_frozen", firsts[0] } };
                if (firsts.Count > 1) names["x_frozen2"] = firsts[1];
                var nums = new Dictionary<string, double> { { "x_count", group.Count } };
                ProtoCrewMember speaker = null;
                if (thawedSpeaks)
                {
                    // the first thawed kerbal the crew list has, named first so {x_frozen2} is somebody else
                    for (int i = 0; i < group.Count && speaker == null; i++)
                    {
                        if (host.roster.Find(group[i]) == null) continue;
                        try { speaker = HighLogic.CurrentGame.CrewRoster[group[i]]; } catch (Exception) { }
                        if (speaker != null && i > 0 && i < firsts.Count)
                        {
                            string t = firsts[0]; firsts[0] = firsts[i]; firsts[i] = t;
                            names["x_frozen"] = firsts[0];
                            if (firsts.Count > 1) names["x_frozen2"] = firsts[1];
                        }
                    }
                    if (speaker == null)
                    {
                        Log.Info("DeepFreeze " + ev + ": " + string.Join(", ", firsts.ToArray()) + " not in the crew list, no line");
                        return;
                    }
                }
                string why = host.director.OfferModEvent(ev, p != null ? p.vessel : null, speaker, nums, names);
                Log.Info("DeepFreeze " + ev + ": " + string.Join(", ", firsts.ToArray()) + (why == null ? "" : " (not played: " + why + ")"));
            }
            catch (Exception e) { Log.Exception("DeepFreezeBridge.Offer", e); }
        }
    }
}
#endif
