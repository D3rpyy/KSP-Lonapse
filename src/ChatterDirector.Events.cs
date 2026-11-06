// Director events: persona and trait in scene picking, the somber mood after a crew loss,
// and PlayEvent with a small pending list for the game events (soi_enter science part_lost power_low power_ok
// signal_lost signal_back crew_lost). Everything goes through the existing queue, so the radio/intercom
// arbitration (AirRules, IntercomTurnOpen, the deferred callout) and the blackout rules stay in charge.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Lonapse
{
    public partial class ChatterDirector
    {
        // ---------------- persona and trait ----------------

        /// <summary>Body key for {body} lines while an event or the debug tab plays a scene. Null: the sample body.</summary>
        public string bodyOverride;

        private bool Effects { get { return host.settings.personalityEffects; } }

        private static bool IsAnyKey(string k) { return string.IsNullOrEmpty(k) || k == PhraseTable.Any; }

        /// <summary>A persona scene needs personalityEffects and a speaker with that flag. Any-persona scenes fit everyone.</summary>
        private bool PersonaOk(PhraseScene sc, KerbalVoice k)
        {
            if (IsAnyKey(sc.Persona)) return true;
            return Effects && k != null && k.persona.Matches(sc.Persona);
        }

        /// <summary>A badass kerbal never plays a scream scene (only with personalityEffects on).</summary>
        private bool BadassNoScream(PhraseScene sc, KerbalVoice k)
        {
            return k != null && Effects && k.persona.badass && PacingRules.IsScream(sc.Id);
        }

        /// <summary>The scene fits this crew speaker: trait, persona and the badass scream rule. Null speaker: true.</summary>
        private bool FitsSpeaker(PhraseScene sc, KerbalVoice k)
        {
            if (sc == null) return false;
            if (k == null) return true;
            return k.persona.TraitMatches(sc.Trait) && PersonaOk(sc, k) && !BadassNoScream(sc, k);
        }

        /// <summary>Somebody conscious aboard can voice the scene's trait and persona. Scenes nobody fits are skipped.</summary>
        private bool CrewCanVoice(PhraseScene sc)
        {
            if (IsAnyKey(sc.Trait) && IsAnyKey(sc.Persona)) return true;
            if (!IsAnyKey(sc.Persona) && !Effects) return false;
            return host.roster.AnyWithTrait(sc.Trait, k => PersonaOk(sc, k));
        }

        /// <summary>A crew speaker for the scene with its trait and persona, null when nobody fits. A scene for anyone
        /// at a job event (staging, a burn, low power) mostly goes to the kerbal with that job, so the pilot calls the
        /// staging and not one of two scientists at random.</summary>
        private KerbalVoice PickFitting(PhraseScene sc)
        {
            Predicate<KerbalVoice> ok = k => PersonaOk(sc, k) && !BadassNoScream(sc, k);
            string job = IsAnyKey(sc.Trait) ? Persona.JobFor(sc.Event) : null;
            if (job != null && rng.NextDouble() < Persona.JobShare && host.roster.AnyWithTrait(job, ok))
                return host.roster.PickWithTrait(job, rng, ok);
            return host.roster.PickWithTrait(sc.Trait, rng, ok);
        }

        /// <summary>
        /// Persona tier for a known speaker: with chance personaPreference (doubled for a badass in the tense band)
        /// a scene with a persona the speaker has. A badass in the tense band gets badass scenes in that tier.
        /// </summary>
        private Predicate<PhraseScene> PersonaTier(KerbalVoice sp, string band)
        {
            if (sp == null || !Effects || !sp.persona.AnyFlag) return null;
            bool badassTense = sp.persona.badass && band == "tense";
            double p = badassTense ? Math.Min(1.0, 2.0 * Tu.personaPreference) : Tu.personaPreference;
            if (rng.NextDouble() >= p) return null;
            if (badassTense) return x => x.Persona == "badass";
            return x => !IsAnyKey(x.Persona) && sp.persona.Matches(x.Persona);
        }

        /// <summary>
        /// A persona or trait tier is a small list (2 to 8 scenes in orbit), so its own shuffle
        /// bag repeats it every few picks. A tier may only take a scene the whole list would also give now: one never
        /// heard, or in the quarter heard longest ago (SceneMemory.Freshest of the whole list).
        /// </summary>
        private Predicate<PhraseScene> Rested(IList<PhraseScene> all)
        {
            var ids = new HashSet<string>();
            foreach (PhraseScene x in Mem.Freshest(all)) ids.Add(x.Id);
            return x => ids.Contains(x.Id);
        }

        /// <summary>
        /// Scene choice for a known speaker, shared by radio and intercom. Tiers: persona scene (personaPreference),
        /// trait scene (traitPreference), plain any-persona any-trait scene, then any scene that fits. Never a misfit.
        /// </summary>
        private PhraseScene PreferFor(KerbalVoice sp, string band, Func<Predicate<PhraseScene>, PhraseScene> pick,
                                      Func<Predicate<PhraseScene>, List<PhraseScene>> list, Predicate<PhraseScene> filter)
        {
            Predicate<PhraseScene> fit = x => FitsSpeaker(x, sp) && (filter == null || filter(x));
            if (sp == null) return pick(fit);
            PhraseScene sc;
            Predicate<PhraseScene> pt = PersonaTier(sp, band);
            bool tt = sp.persona.trait != Persona.Any && rng.NextDouble() < Tu.traitPreference;
            Predicate<PhraseScene> rested = (pt != null || tt) ? Rested(list(fit)) : null;
            if (pt != null && (sc = pick(x => pt(x) && fit(x) && rested(x))) != null) return sc;
            if (tt && (sc = pick(x => !IsAnyKey(x.Trait) && fit(x) && rested(x))) != null) return sc;
            if ((sc = pick(x => IsAnyKey(x.Persona) && IsAnyKey(x.Trait) && fit(x))) != null) return sc;
            return pick(fit);
        }

        /// <summary>Radio scene of the event for this crew speaker (PickScene plus the persona and trait tiers).</summary>
        private PhraseScene PickSceneFor(string ev, string band, KerbalVoice sp, Predicate<PhraseScene> filter)
        {
            return PreferFor(sp, band, f => PickScene(ev, band, f), f => CoreList(ev, band, SceneCause(), false, f), filter);
        }

        /// <summary>Intercom scene for speaker A (PickIntercomScene plus the persona and trait tiers).</summary>
        private PhraseScene PickIntercomSceneFor(string band, KerbalVoice a, Predicate<PhraseScene> filter)
        {
            return PreferFor(a, band, f => PickIntercomScene(band, f), f => CoreList("chatter", band, DominantCause ?? NoCause, true, f), filter);
        }

        /// <summary>
        /// Intercom pair. Speaker A by roster.PickIntercomA (dim kerbals start more chats) when personalityEffects is on,
        /// else the old weighted pick. A forced (debug or event) scene gets an A that fits it when there is one.
        /// </summary>
        private bool PickChatPair(PhraseScene forced, out KerbalVoice a, out KerbalVoice b)
        {
            a = b = null;
            List<KerbalVoice> list = host.roster.voices;
            Predicate<KerbalVoice> okA = null;
            if (forced != null) okA = k => FitsSpeaker(forced, k);
            if (Effects) a = host.roster.PickIntercomA(rng, null, okA);
            else a = KerbalVoice.PickWithTrait(list, forced != null ? forced.Trait : null, null, rng, okA);
            if (a == null && forced != null) a = KerbalVoice.Pick(list, null, rng, null); // debug: play it anyway
            if (a == null) return false;
            b = KerbalVoice.Pick(list, null, rng, a);
            return b != null;
        }

        // ---------------- somber mood ----------------

        /// <summary>Clock until which the crew mourns: no goofy radio scenes, no MC jokes, no intercom banter.</summary>
        public float somberUntil = -1f;

        public bool Somber { get { return clock < somberUntil; } }

        /// <summary>Seconds of somber mood left, 0 or less when off (DebugOverlay).</summary>
        public float SomberLeft { get { return somberUntil - clock; } }

        private void StartSomber(string why)
        {
            float t = Tu.somberTime;
            if (t <= 0f) return;
            somberUntil = Math.Max(somberUntil, clock + t);
            if (HasChatItems()) DropChat("somber");
            Log.Info("Somber for " + t.ToString("0") + " s (" + why + ")");
        }

        /// <summary>MC joke: a mj_ scene (mc_jokes.tsv) or a calm goofy chatter scene that Mission Control opens.</summary>
        private static bool IsMcJoke(PhraseScene sc)
        {
            if (sc.Id.StartsWith("mj_", StringComparison.Ordinal)) return true;
            if (sc.Lines.Count > 0 && sc.Lines[0].ClipKey != null && sc.Lines[0].ClipKey.StartsWith("mj_", StringComparison.Ordinal)) return true;
            return sc.Event == "chatter" && sc.Band == "calm" && sc.StartsWithMc && IsGoofyRow(sc.Phase);
        }

        private static bool IsGoofyRow(string phase) { return phase == FlightPhase.Goofy || FlightPhase.IsGoofy(phase); }

        /// <summary>While somber: chatter scenes that are goofy (goofy phase rows) or MC jokes are not played.</summary>
        private bool SomberBlocks(PhraseScene sc)
        {
            if (!Somber || sc.Event != "chatter") return false;
            return IsGoofyRow(sc.Phase) || IsMcJoke(sc);
        }

        // ---------------- events (Director API D2) ----------------

        public const int PendingCapacity = 4;

        private sealed class PendingEvent
        {
            public EventRequest r;
            public float since;    // real time (Time.realtimeSinceStartup): the director clock stops with the gate closed
            public string why;     // last reason it waited
        }

        private readonly List<PendingEvent> pendingEvents = new List<PendingEvent>(PendingCapacity);
        private int airEventPrio;            // priority of the event scene in the queue or on the air, 0 = none
        private float nextEventTry;
        public string lastEventResult = "";  // overlay and log: what the last event did

        private enum EvGo { Played, Wait, Drop }

        private static float RealNow
        {
            get
            {
                try { return Time.realtimeSinceStartup; }
                catch (Exception) { return 0f; }
            }
        }

        public int PendingEventCount { get { return pendingEvents.Count; } }

        /// <summary>Pending list summary for the DebugOverlay, for example "crew_lost 12s (gate) | science 3s (serious phase)".</summary>
        public string EventStatus
        {
            get
            {
                if (pendingEvents.Count == 0) return "none pending" + (lastEventResult.Length > 0 ? ", last: " + lastEventResult : "");
                float now = RealNow;
                string s = "";
                for (int i = 0; i < pendingEvents.Count; i++)
                {
                    PendingEvent p = pendingEvents[i];
                    s += (i > 0 ? " | " : "") + p.r.ev + " " + (now - p.since).ToString("0") + "s/" + p.r.maxAge.ToString("0")
                         + (string.IsNullOrEmpty(p.why) ? "" : " (" + p.why + ")");
                }
                return s;
            }
        }

        private bool EventEnabled(string ev)
        {
            Settings c = host.settings;
            switch (ev)
            {
                case EventWatchers.SoiEnter: return c.eventSoi;
                case EventWatchers.Science: return c.eventScience;
                case EventWatchers.PartLost: return c.eventPartLoss;
                case EventWatchers.PowerLow: case EventWatchers.PowerOk: return c.eventPower;
                case EventWatchers.SignalLost: case EventWatchers.SignalBack: return c.eventSignal;
                case EventWatchers.CrewLost: return c.eventCrewLoss;
            }
            return Array.IndexOf(PhraseTable.Events, ev) >= 0;
        }

        private static string OppositeOf(string ev)
        {
            switch (ev)
            {
                case EventWatchers.PowerLow: return EventWatchers.PowerOk;
                case EventWatchers.PowerOk: return EventWatchers.PowerLow;
                case EventWatchers.SignalLost: return EventWatchers.SignalBack;
                case EventWatchers.SignalBack: return EventWatchers.SignalLost;
            }
            return null;
        }

        /// <summary>
        /// A game event (GameEventHub). True when it was queued now or kept in the pending list (4 slots by priority,
        /// each dropped after r.maxAge s of real time). Works with the gate closed: the event waits for it.
        /// </summary>
        public bool PlayEvent(EventRequest r)
        {
            if (string.IsNullOrEmpty(r.ev)) return false;
            r.maxAge = EventWatchers.MaxAgeFor(r.ev, r.maxAge, Tu.eventMaxAge); // science 600 s, soi 120 s at most
            RemovePending(OppositeOf(r.ev), "superseded by " + r.ev);
            RemovePending(r.ev, null); // a newer request of the same event replaces the waiting one
            string why;
            EvGo go = TryEvent(r, out why);
            if (go == EvGo.Played) return true;
            if (go == EvGo.Drop)
            {
                lastEventResult = r.ev + " dropped: " + why;
                Log.Info("Event " + lastEventResult);
                return false;
            }
            return AddPending(r, why);
        }

        private bool AddPending(EventRequest r, string why)
        {
            PendingEvent p = new PendingEvent { r = r, since = RealNow, why = why };
            if (pendingEvents.Count >= PendingCapacity)
            {
                int low = 0;
                for (int i = 1; i < pendingEvents.Count; i++)
                    if (pendingEvents[i].r.priority < pendingEvents[low].r.priority
                        || (pendingEvents[i].r.priority == pendingEvents[low].r.priority && pendingEvents[i].since < pendingEvents[low].since))
                        low = i;
                if (r.priority <= pendingEvents[low].r.priority)
                {
                    lastEventResult = r.ev + " refused: pending list full";
                    Log.Info("Event " + lastEventResult);
                    return false;
                }
                Log.Info("Pending event " + pendingEvents[low].r.ev + " dropped for " + r.ev + " (higher priority)");
                pendingEvents.RemoveAt(low);
            }
            pendingEvents.Add(p);
            lastEventResult = r.ev + " pending: " + why;
            Log.Info("Event " + lastEventResult + " (up to " + r.maxAge.ToString("0") + " s)");
            return true;
        }

        private void RemovePending(string ev, string why)
        {
            if (ev == null) return;
            for (int i = pendingEvents.Count - 1; i >= 0; i--)
            {
                if (pendingEvents[i].r.ev != ev) continue;
                if (why != null) Log.Info("Pending event " + ev + " dropped: " + why);
                pendingEvents.RemoveAt(i);
            }
        }

        /// <summary>Vessel switch: pending events of the old vessel go, a crew loss call stays (the kerbal is still gone).</summary>
        private void ResetEventsForVessel(bool evaHop = false)
        {
            for (int i = pendingEvents.Count - 1; i >= 0; i--)
            {
                string ev = pendingEvents[i].r.ev;
                if (ev == EventWatchers.CrewLost || (evaHop && ev == EventWatchers.Science)) continue;
                Log.Info("Pending event " + ev + " dropped: vessel switch");
                pendingEvents.RemoveAt(i);
            }
            airEventPrio = 0;
        }

        /// <summary>Per frame (gate open): expire pending events, play the best one that may play now.</summary>
        private void UpdateEvents()
        {
            if (airEventPrio > 0 && !HasRadioItems() && !RadioAudible && !Holding) airEventPrio = 0;
            if (pendingEvents.Count == 0) return;
            float now = RealNow;
            for (int i = pendingEvents.Count - 1; i >= 0; i--)
            {
                PendingEvent p = pendingEvents[i];
                if (now - p.since <= p.r.maxAge) continue;
                lastEventResult = p.r.ev + " expired after " + p.r.maxAge.ToString("0") + " s (" + p.why + ")";
                Log.Info("Event " + lastEventResult);
                pendingEvents.RemoveAt(i);
            }
            if (pendingEvents.Count == 0 || clock < nextEventTry) return;
            nextEventTry = clock + 0.25f;
            // highest priority first, the older one on a tie. At most one plays per try.
            pendingEvents.Sort((x, y) => x.r.priority != y.r.priority ? y.r.priority.CompareTo(x.r.priority) : x.since.CompareTo(y.since));
            for (int i = 0; i < pendingEvents.Count; i++)
            {
                PendingEvent p = pendingEvents[i];
                string why;
                EvGo go = TryEvent(p.r, out why);
                if (go == EvGo.Wait) { p.why = why; continue; }
                pendingEvents.RemoveAt(i);
                if (go == EvGo.Drop)
                {
                    lastEventResult = p.r.ev + " dropped: " + why;
                    Log.Info("Pending event " + lastEventResult);
                }
                return;
            }
        }

        // ---------------- crew_lost with the gate closed (the whole crew is gone) ----------------
        // When the last kerbal dies the gate closes (no crew, vessel destroyed, no vessel) and nothing else may play.
        // A pending crew_lost then plays its MC-only call here: quindar in, one MC line, quindar out on the control
        // channel with the radio profile, and its subtitle. LonapseAddon lets only the control channel through for it
        // (FinalCallActive) while the gate stays closed. Then the somber mood starts.

        private enum FinalStep { None, QuindarIn, Line, QuindarOut }
        private FinalStep finalStep = FinalStep.None;
        private PhraseEntry finalEntry;

        /// <summary>The closed-gate crew_lost call is playing: the control channel stays on.</summary>
        public bool FinalCallActive { get { return finalStep != FinalStep.None; } }

        /// <summary>Time.unscaledTime until which the subtitles are drawn with the gate closed (the final call's line).</summary>
        public float finalSubtitleUntil = -1f;

        /// <summary>
        /// Per frame while the gate is closed (not while paused). <paramref name="allowed"/>: the gate is closed only
        /// because the crew or the vessel is gone and the hard silence has run. Anything else (warp, scene change,
        /// mute, disabled) stops a running call and keeps the pending event waiting for the gate.
        /// </summary>
        public void UpdateClosedGate(bool allowed)
        {
            if (finalStep != FinalStep.None) { StepFinalCall(allowed); return; }
            if (!allowed || pendingEvents.Count == 0) return;
            Settings c = host.settings;
            if (c == null || !c.enabled || c.muted || !c.radioEnabled || !c.eventCrewLoss || !PhraseClips.Loaded) return;
            if (host.control == null || host.control.Busy) return;
            int idx = -1;
            float now = RealNow;
            for (int i = pendingEvents.Count - 1; i >= 0; i--)
            {
                PendingEvent p = pendingEvents[i];
                if (p.r.ev != EventWatchers.CrewLost) continue;
                if (now - p.since > p.r.maxAge)
                {
                    lastEventResult = p.r.ev + " expired after " + p.r.maxAge.ToString("0") + " s (" + p.why + ")";
                    Log.Info("Event " + lastEventResult);
                    pendingEvents.RemoveAt(i);
                    continue;
                }
                idx = i;
            }
            if (idx < 0) return;
            pendingEvents.RemoveAt(idx);
            PhraseScene sc = PickFinalScene();
            PhraseEntry e = sc != null ? FirstLine(sc) : null;
            if (e == null || e.Role != "mc")
            {
                lastEventResult = "crew_lost dropped: no MC-only line for the closed gate";
                Log.Info("Event " + lastEventResult);
                return;
            }
            finalEntry = e;
            Log.Info("Event crew_lost: final call with the gate closed (" + host.gateReason + "), scene " + sc.Id);
            if (!PlayFinalFx("beeps/quindar_in")) PlayFinalLine();
            else finalStep = FinalStep.QuindarIn;
        }

        /// <summary>A one-line MC crew_lost scene (tense first, then any band). Phase is not checked: the flight is over.</summary>
        private PhraseScene PickFinalScene()
        {
            // a real clip: the final call plays it directly, not through the runtime voice
            Predicate<PhraseScene> one = x => PhraseClips.Playable(x) && !x.IsProbe && x.Lines.Count == 1 && x.McLines == 1
                                              && PhraseClips.Clip(x.Lines[0], false) != null;
            PhraseScene sc = Mem.Take(PhraseClips.table.ScenesFor(EventWatchers.CrewLost, "tense", one), rng, lastSceneId);
            if (sc == null) sc = Mem.Take(PhraseClips.table.ScenesFor(EventWatchers.CrewLost, null, one), rng, lastSceneId);
            if (sc != null) lastSceneId = sc.Id;
            return sc;
        }

        private void StepFinalCall(bool allowed)
        {
            VoiceChannel ch = host.control;
            if (!allowed || ch == null || !host.settings.radioEnabled)
            {
                if (ch != null && ch.Busy) ch.FadeOutOnce(0.2f);
                EndFinalCall("stopped: gate " + host.gateReason);
                return;
            }
            if (ch.Busy) return;
            switch (finalStep)
            {
                case FinalStep.QuindarIn: PlayFinalLine(); break;
                case FinalStep.Line:
                    if (PlayFinalFx("beeps/quindar_out")) finalStep = FinalStep.QuindarOut;
                    else EndFinalCall("done");
                    break;
                default: EndFinalCall("done"); break;
            }
        }

        private bool PlayFinalFx(string folder)
        {
            AudioClip clip = ClipLibrary.CountOf(folder) > 0 ? ClipLibrary.Next(folder, rng) : null;
            if (clip == null) return false;
            VoiceChannel c = host.control;
            c.Play(clip, 0.45f, 1f, false, 0f);
            c.baseDistortion = 0.05f;
            c.dropout = 0f;
            return true;
        }

        /// <summary>The MC line itself, set up like a ControlLine (radio profile band, boost, compressor) plus subtitle.</summary>
        private void PlayFinalLine()
        {
            PhraseEntry e = finalEntry;
            AudioClip clip = e != null ? PhraseClips.Clip(e, false) : null;
            if (clip == null) { EndFinalCall("no clip"); return; }
            VoiceChannel c = host.control;
            Tuning tu = Tu;
            RadioProfile p = Profile;
            c.SetBand(p.HighPassHz, p.LowPassHz);
            c.Play(clip, 0.8f, 1f, false, 0f);
            c.boost = VoiceBoost;
            c.compressor = host.settings.voiceCompressor;
            c.phraseKey = e.ClipKey;
            c.baseDistortion = Math.Max(tu.controlDistortion, p.Distortion);
            c.dropout = 0f;
            ShowSubtitle(e, "MC", c.duration);
            try { finalSubtitleUntil = Time.unscaledTime + c.duration + 5f; } catch (Exception) { }
            lastLine = "MC: " + e.ClipKey + " (final call)";
            Audit.Add(lastLine);
            finalStep = FinalStep.Line;
            StartSomber("crew lost");
            lastEventResult = "crew_lost played (gate closed)";
        }

        private void EndFinalCall(string why)
        {
            if (finalStep == FinalStep.None) return;
            finalStep = FinalStep.None;
            finalEntry = null;
            Log.Info("Crew lost final call " + why);
        }

        /// <summary>HardReset: a hard silence or a reopened gate ends the closed-gate call.</summary>
        private void ResetFinalCall()
        {
            finalStep = FinalStep.None;
            finalEntry = null;
        }

        private static bool Preempts(string ev) { return ev == EventWatchers.PartLost || ev == EventWatchers.CrewLost; }

        /// <summary>Wait or drop reasons first (cheap), then the scene choice and the queueing.</summary>
        private EvGo TryEvent(EventRequest r, out string why)
        {
            why = null;
            string ev = r.ev;
            bool soi = ev == EventWatchers.SoiEnter;
            bool upbeat = soi || ev == EventWatchers.Science;
            if (!EventEnabled(ev)) { why = "disabled"; return EvGo.Drop; }
            // time warp chatter: an event waits for the end of the warp, as it did with the gate closed
            if (host.warpChatter) { why = "time warp"; return EvGo.Wait; }
            if (host.probeMode) return TryProbeEvent(r, out why); // probe scenes, crew_lost dropped
            if (!host.settings.radioEnabled) { why = "radio disabled"; return EvGo.Drop; }
            if (!host.CanStartAudio) { why = "gate " + host.gateReason; return EvGo.Wait; }
            if (!PhraseClips.Loaded) { why = "Lonapse table not loaded"; return EvGo.Drop; }
            // with crew aboard the crew still talks in a blackout (MC lines are dropped), so radio events go on
            bool blackout = radioSilent || (InBlackoutPhase && !mcBlackout);
            // no welcome in a panic or in plasma
            if (soi && (r.band == "panic" || KerbalVoice.AnyPanic(host.roster.voices))) { why = "panic"; return EvGo.Drop; }
            if (soi && blackout) { why = "blackout"; return EvGo.Drop; }
            // radio events wait for the blackout to end. A crew-only event may still use the intercom (below).
            if (blackout && !r.crewOnly) { why = "blackout"; return EvGo.Wait; }
            if (upbeat && FlightPhase.IsSerious(CurrentPhase)) { why = "serious phase"; return EvGo.Wait; }
            if (upbeat && Somber) { why = "somber"; return EvGo.Wait; }
            if (calloutPending) { why = "callout waiting"; return EvGo.Wait; }
            bool preempt = Preempts(ev);
            if (preempt)
            {
                // never cut an event of the same or higher priority that is still queued or on the air
                if (airEventPrio >= r.priority) { why = "event on the air"; return EvGo.Wait; }
            }
            else if (HasRadioItems() || Holding || airEventPrio > 0) { why = "radio busy"; return EvGo.Wait; }
            // a started intercom chat holds the air: the event waits in the pending list and plays right after it
            else if (ChatHoldsAir) { why = "intercom chat"; return EvGo.Wait; }
            if (!host.roster.AnyoneCanSpeak() && ev != EventWatchers.CrewLost) { why = "no conscious crew"; return EvGo.Wait; }

            string saved = bodyOverride;
            bodyOverride = string.IsNullOrEmpty(r.body) ? null : r.body;
            try
            {
                why = QueueEvent(r, blackout, preempt);
            }
            finally { bodyOverride = saved; }
            if (why != null && why.StartsWith("wait:", StringComparison.Ordinal)) { why = why.Substring(5); return EvGo.Wait; }
            if (why != null) return EvGo.Drop;
            airEventPrio = Math.Max(airEventPrio, Math.Max(1, r.priority));
            // give the event room before the scheduled chatter fires
            if (progress > target * 0.5f) progress = target * 0.5f;
            if (ev == EventWatchers.CrewLost) StartSomber("crew lost");
            lastEventResult = ev + " played";
            return EvGo.Played;
        }

        /// <summary>
        /// Event scene: exact topic first, then topic any. The requested band first, then any band. Trait and persona
        /// tiers like PreferFor, but the speaker is chosen after the scene (PickFitting). Shape filters the scene form.
        /// </summary>
        private PhraseScene PickEventScene(EventRequest r, Predicate<PhraseScene> shape)
        {
            string reqTrait = (!IsAnyKey(r.trait) && host.roster.AnyWithTrait(r.trait, null)) ? r.trait : null;
            Predicate<PhraseScene> f = x => shape(x) && (reqTrait == null || PhraseTable.TraitMatches(x.Trait, reqTrait));
            string topic = IsAnyKey(r.topic) ? PhraseTable.Any : r.topic;
            string[] topics = topic == PhraseTable.Any ? new[] { PhraseTable.Any } : new[] { topic, PhraseTable.Any };
            string[] bands = string.IsNullOrEmpty(r.band) ? new string[] { null } : new[] { r.band, null };
            bool badassTense = r.band == "tense" && Effects && host.roster.AnyWithTrait(null, k => k.persona.badass);
            bool personaRoll = Effects && rng.NextDouble() < (badassTense ? Math.Min(1.0, 2.0 * Tu.personaPreference) : Tu.personaPreference);
            bool traitRoll = rng.NextDouble() < Tu.traitPreference;
            foreach (string t in topics)
                foreach (string b in bands)
                {
                    PhraseScene sc;
                    Predicate<PhraseScene> rested = (personaRoll || traitRoll) ? Rested(EventCoreList(r.ev, b, t, f)) : null;
                    if (personaRoll)
                    {
                        Predicate<PhraseScene> pt = badassTense ? (Predicate<PhraseScene>)(x => x.Persona == "badass") : (x => !IsAnyKey(x.Persona));
                        if ((sc = PickEventCore(r.ev, b, t, x => pt(x) && f(x) && rested(x))) != null) return sc;
                    }
                    if (traitRoll && (sc = PickEventCore(r.ev, b, t, x => !IsAnyKey(x.Trait) && f(x) && rested(x))) != null) return sc;
                    if ((sc = PickEventCore(r.ev, b, t, x => IsAnyKey(x.Persona) && IsAnyKey(x.Trait) && f(x))) != null) return sc;
                    if ((sc = PickEventCore(r.ev, b, t, f)) != null) return sc;
                }
            return null;
        }

        private PhraseScene PickEventCore(string ev, string band, string topic, Predicate<PhraseScene> filter)
        {
            PhraseScene p = Mem.Take(EventCoreList(ev, band, topic, filter), rng, lastSceneId);
            if (p != null) lastSceneId = p.Id;
            return p;
        }

        private List<PhraseScene> EventCoreList(string ev, string band, string topic, Predicate<PhraseScene> filter)
        {
            string phase = CurrentPhase;
            return PhraseClips.table.ScenesFor(ev, band, null, null, null, null, topic, true,
                sc => PhraseClips.Playable(sc) && !sc.IsProbe && RadioPhaseOk(sc, phase) && CrewCanVoice(sc) && PartsOk(sc)
                      && (sc.IsIntercom || BlackoutOk(sc)) && sc.PlaysAt(Body) && filter(sc) && SlotsOk(sc));
        }

        /// <summary>One-line MC reply of the event (band, then tense, then any). Null when the event has none.</summary>
        private PhraseScene PickEventMcReply(string ev, string band)
        {
            Predicate<PhraseScene> one = x => x.Lines.Count == 1 && x.McLines == 1;
            string b = band == "panic" ? "tense" : band; // in a panic MC stays calm-ish (tense rows)
            PhraseScene sc = null;
            if (!string.IsNullOrEmpty(b)) sc = PickEventCore(ev, b, PhraseTable.Any, one);
            if (sc == null && b != "tense") sc = PickEventCore(ev, "tense", PhraseTable.Any, one);
            return sc ?? PickEventCore(ev, null, PhraseTable.Any, one);
        }

        private void EnqueueReply(PhraseScene sc, float delay)
        {
            PhraseEntry e = FirstLine(sc);
            if (e != null) EnqueueMc(e.Folder, delay, e);
        }

        /// <summary>Picks and queues the event. Null on success, "wait:reason" to keep it pending, else a drop reason.</summary>
        private string QueueEvent(EventRequest r, bool blackout, bool preempt)
        {
            string ev = r.ev;
            float s = host.stress.stress;
            float delay = U(0.2f, 0.6f);
            if (count + 16 > Capacity && !preempt) return "wait:queue full";

            bool chatOk = host.settings.intercomEnabled && host.roster.SpeakableCount() >= 2 && !host.evaMode;
            PhraseScene sc;
            if (r.crewOnly)
            {
                // signal_lost: no MC line. Two-line crew scenes go on the intercom. In blackout only those.
                // Only on a free intercom: FireIntercom would overwrite a running chat (chatA, chatB, chatStarted).
                // A busy intercom falls through to the one-line crew radio scene (in blackout: wait).
                bool intercomFree = !HasChatItems() && !IntercomAudible && IntercomMayStartNow;
                if (chatOk && intercomFree && (blackout || rng.NextDouble() < 0.4))
                {
                    sc = PickEventScene(r, x => x.McLines == 0 && x.CrewLines >= 2);
                    if (sc != null)
                    {
                        if (count + 2 * sc.Lines.Count + 1 > Capacity || !FireIntercom(false, sc)) return "wait:queue full";
                        Log.Info("Event " + ev + ": intercom scene " + sc.Id);
                        return null;
                    }
                }
                if (blackout) return "wait:blackout"; // a crew radio line would be cut by the blackout: wait for it to end
                sc = PickEventScene(r, x => x.McLines == 0 && x.CrewLines == 1);
                if (sc == null) return "no crew-only scene";
                if (!EnqueueRadioScene(sc, null, delay)) return "nobody fits " + sc.Id;
                Log.Info("Event " + ev + ": crew-only scene " + sc.Id);
                return null;
            }

            if (ev == EventWatchers.CrewLost)
            {
                // Mission Control speaks first (no crew-first scene). A short crew reaction may follow.
                sc = PickEventScene(r, x => x.StartsWithMc && !x.IsIntercom);
                if (sc == null) return "no MC-first crew_lost scene";
                if (preempt) { PreemptChatter(); if (HasChatItems()) DropChat("crew lost"); }
                if (!EnqueueRadioScene(sc, null, delay)) return "scene " + sc.Id + " not queued";
                if (sc.CrewLines == 0 && host.roster.AnyoneCanSpeak() && rng.NextDouble() < 0.5)
                {
                    PhraseScene crew = PickEventScene(r, x => x.McLines == 0 && x.CrewLines == 1);
                    if (crew != null) EnqueueRadioScene(crew, null, ResponseDelay(s) + U(0.5f, 1.5f));
                }
                Log.Info("Event crew_lost: scene " + sc.Id);
                return null;
            }

            bool crewFirst = ev == EventWatchers.SignalBack;
            sc = null;
            if (crewFirst) sc = PickEventScene(r, x => !x.IsIntercom && !x.StartsWithMc);
            if (sc == null) sc = PickEventScene(r, x => !x.IsIntercom);
            if (sc == null) return "no scene for " + ev + " band " + r.band + " topic " + r.topic;
            if (preempt)
            {
                // part_lost: radio chatter fades, a playing intercom line ends first (AirRules), the chat is dropped
                PreemptChatter();
                if (HasChatItems()) DropChat(ev);
            }
            if (!EnqueueRadioScene(sc, null, preempt ? 0.25f : delay)) return "scene " + sc.Id + " not queued (scream cooldown or nobody fits)";
            if (sc.McLines == 0)
            {
                // crew-only line: Mission Control answers (always for part loss and signal back)
                bool always = ev == EventWatchers.PartLost || ev == EventWatchers.SignalBack;
                if (always || rng.NextDouble() < McP(0.6))
                {
                    PhraseScene reply = PickEventMcReply(ev, r.band);
                    if (reply != null) EnqueueReply(reply, ResponseDelay(s));
                }
            }
            Log.Info("Event " + ev + ": scene " + sc.Id + " (" + sc.Band + ", topic " + sc.Topic + ")"
                     + (bodyOverride != null ? " body " + bodyOverride : ""));
            return null;
        }
    }
}
