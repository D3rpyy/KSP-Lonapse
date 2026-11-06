// The director in probe mode (an uncrewed vessel with a probe core).
// Only Mission Control and the probe talk, always on the radio. Pet talk: Mission Control talks to the probe, the
// probe answers with beeps (beep[1], radio band) and a bracketed subtitle. Hails: the probe beeps first on its
// own timer, Mission Control answers. Events: probe scenes per
// event, also for touchdown, splashdown, docking and a burn. Everything audible goes through the queue, so the
// radio arbitration (AirRules) stays in charge.
// The pure rules live in ProbeRules.cs (tested offline).
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Lonapse
{
    public partial class ChatterDirector
    {
        public float hailLeft = -1f;          // s to the next probe hail, -1 = not in probe mode
        private bool wasProbeMode;
        private float lastProbeCalloutMc = -1e9f;   // clock of the last MC answer to a probe callout
        private float lastSayAgain = -1e9f;         // clock of the last "say again?"

        private string Core { get { return host.probeCoreKey ?? ProbeCores.Generic; } }
        private bool ProbeAsleep { get { return host.probeSleepState == ProbeSleepState.Asleep; } }
        private bool ProbeDrowsy { get { return host.probeSleepState == ProbeSleepState.Drowsy; } }
        private bool ProbeTense { get { return host.stress.stress >= Tu.mcTenseHi; } }
        /// <summary>The radio link to the probe works: signal, no blackout.</summary>
        private bool ProbeLink { get { return host.signal.connected && !radioSilent && !InBlackoutPhase; } }

        private static bool IsIntercomItem(UttKind k)
        {
            return k == UttKind.IntercomLine || k == UttKind.IntercomClick || k == UttKind.CabinBeep;
        }

        /// <summary>Overlay line, for example "okto awake, hail in 120 s".</summary>
        public string ProbeStatus
        {
            get
            {
                if (!host.probeMode) return "off";
                return Core + " " + host.probeSleepState.ToString().ToLowerInvariant()
                       + ", hail " + (hailLeft < 0f ? "-" : Math.Max(0f, hailLeft / RadioRate).ToString("0") + " s");
            }
        }

        private void ResetProbe()
        {
            hailLeft = NextHailGap() * 0.5f;
        }

        /// <summary>Gap to the next hail in base seconds. UpdateHail counts it down at RadioRate, so the "Radio amount"
        /// slider (rateMultiplier) scales the hails like the pet talk and the crew radio.</summary>
        private float NextHailGap() { return Math.Max(10f, Tu.hailInterval * U(0.6f, 1.4f)); }

        private float RadioRate { get { return DcMath.Clamp(host.settings.rateMultiplier, 0.25f, 4f); } }

        // ---------------- scene choice ----------------

        /// <summary>Probe scene of an event: exact-core and core-any scenes in one pool, exact core weighted 2x
        /// (ProbeRules.PickWeighted). With a topic: scenes of that topic first, then topic any.</summary>
        private PhraseScene PickProbe(string ev, Predicate<PhraseScene> filter, string topic = null)
        {
            if (!PhraseClips.Loaded) return null;
            List<PhraseScene> list = PhraseClips.table.ProbeScenesFor(ev, Core, CurrentPhase, false,
                sc => PhraseClips.Playable(sc) && (filter == null || filter(sc)) && SlotsOk(sc));
            if (list.Count == 0) return null;
            if (!IsAnyKey(topic))
            {
                List<PhraseScene> exact = list.FindAll(x => x.Topic == topic);
                list = exact.Count > 0 ? exact : list.FindAll(x => IsAnyKey(x.Topic));
            }
            // line memory first (the freshest scenes), then the core weighting among those
            PhraseScene p = ProbeRules.PickWeighted(Mem.Freshest(list), Core, rng, lastSceneId);
            if (p != null) { lastSceneId = p.Id; Mem.Stamp(p.Id); }
            return p;
        }

        private static bool HasProbeMood(PhraseScene sc, string mood)
        {
            for (int i = 0; i < sc.Lines.Count; i++)
                if (sc.Lines[i].Role == PhraseTable.RoleProbe && sc.Lines[i].Beep == mood) return true;
            return false;
        }

        // ---------------- queueing ----------------

        /// <summary>
        /// Queues a probe scene line by line: mc on the radio (quindar), probe as a beep on the radio.
        /// mcOnly: the probe rows are left out (a sleeping probe does not answer). firstOnly: only the first line (a
        /// callout without the MC answer). "Say again?": on a weak signal the first probe answer to an MC line
        /// may get an MC "say again?" and a repeated beep (ProbeRules.SayAgainDue). False when nothing was queued.
        /// </summary>
        private bool EnqueueProbeScene(PhraseScene sc, float delay, bool mcOnly = false, bool firstOnly = false)
        {
            if (sc == null) return false;
            List<PhraseEntry> lines = PlayLines(sc);
            if (lines.Count == 0) return false;
            ProbeShape shape = ProbeRules.ShapeOf(lines);
            if (shape.crew > 0) return false;
            int need = 8; // room for a "say again?" insert
            for (int i = 0; i < lines.Count; i++) need += lines[i].Role == "mc" ? 7 : 1;
            if (count + need > Capacity) return false;
            Tuning tu = Tu;
            PhraseScene sayAgain = null;
            if (host.settings.probeSayAgain && !mcOnly && !firstOnly && !ProbeAsleep && shape.mc > 0 && shape.probe > 0
                && ProbeRules.SayAgainDue(host.signal.signal, host.signal.connected, tu.sayAgainSignal, rng.NextDouble(),
                                          tu.sayAgainChance, clock - lastSayAgain, tu.sayAgainCooldown))
            {
                string keep = lastSceneId;
                sayAgain = PickProbe(ProbeRules.SayAgainEvent, x => x.Lines.Count == 2 && x.Lines[0].Role == "mc"
                                                                     && x.Lines[1].Role == PhraseTable.RoleProbe);
                lastSceneId = keep;
            }
            float s = host.stress.stress;
            bool first = true;
            string prevRole = null;
            int queued = 0;
            for (int i = 0; i < lines.Count; i++)
            {
                PhraseEntry e = lines[i];
                string role = e.Role;
                if (role == PhraseTable.RoleProbe && (mcOnly || ProbeAsleep)) continue;
                float d;
                if (first) d = delay;
                else if (role == "mc") d = ResponseDelay(s);
                else d = (prevRole == "mc" ? U(0.3f, 0.7f) + host.LightDelay : U(0.2f, 0.5f));
                if (role == "mc") EnqueueMc(e.Folder, d, e);
                else
                {
                    Utterance u = Enqueue(UttKind.ProbeBeep, null, d, 0f, null);
                    if (u != null) u.entry = e;
                    if (sayAgain != null && prevRole == "mc")
                    {
                        // the answer was lost in the static: MC asks again, the probe repeats (annoyed when proud)
                        EnqueueMc(sayAgain.Lines[0].Folder, ResponseDelay(s), sayAgain.Lines[0]);
                        Utterance rep = Enqueue(UttKind.ProbeBeep, ProbeRules.RepeatMood(e.Beep), U(0.3f, 0.7f) + host.LightDelay, 0f, null);
                        if (rep != null) rep.entry = sayAgain.Lines[1];
                        lastSayAgain = clock;
                        Log.Info("Say again inserted into " + sc.Id + " (" + sayAgain.Id + "), signal " + host.signal.signal.ToString("0.00"));
                        sayAgain = null;
                    }
                }
                first = false;
                prevRole = role;
                queued++;
                if (firstOnly) break;
            }
            if (queued == 0) return false;
            Log.Debug("Probe scene " + sc.Id + " (" + sc.Event + ", core " + sc.Core + ") queued for " + Core
                      + (mcOnly ? ", MC only" : ""));
            return true;
        }

        private void ShowProbeSubtitle(string who, string text, float seconds)
        {
            if (string.IsNullOrEmpty(text) || !host.settings.subtitles || host.subtitles == null) return;
            host.subtitles.Show(who, text, seconds);
        }

        // ---------------- playback (Start) ----------------

        /// <summary>Start of a probe utterance (ProbeBeep). Called from Start with the gate open.</summary>
        private StartResult StartProbe(Utterance u, out VoiceChannel ch)
        {
            ch = null;
            Settings cfg = host.settings;
            Tuning tu = Tu;
            PhraseEntry e = u.entry;
            // left over from a probe vessel after a switch to a crewed one (the queue survives a vessel switch)
            if (!host.probeMode) return StartResult.Skip;
            switch (u.who)
            {
                case UttKind.ProbeBeep:
                {
                    // the probe answers over the link: no link, blackout or a sleeping probe means no answer
                    if (!host.signal.connected || radioSilent || ProbeAsleep) return StartResult.Skip;
                    VoiceChannel b = host.beep[1];
                    if (b.Busy) return StartResult.Wait;
                    // u.folder: a mood set by the director (fallback beep, "say again?" repeat), else the row's mood
                    bool data = u.folder == ProbeCores.DataFolder || (e != null && e.Event == ProbeRules.DataEvent);
                    string rowMood = (u.folder != null && !data) ? u.folder : (e != null ? e.Beep : null);
                    string mood = data ? "happy" : ProbeRules.BeepMood(rowMood, ProbeTense);
                    string text = data ? (e != null ? PhraseClips.Subtitle(e) : ProbeRules.DataSubtitle)
                                : (e != null && mood == rowMood) ? PhraseClips.Subtitle(e) : ProbeRules.BeepSubtitle(mood);
                    string who = host.ProbeDisplayName;
                    AudioClip clip = null;
                    if (cfg.beepsEnabled && data) clip = ClipLibrary.Next(ProbeCores.DataFolder, rng);
                    if (cfg.beepsEnabled && clip == null) clip = VoiceClip(host.probeVoice, mood);
                    if (clip == null)
                    {
                        // beeps off or no clip: the subtitle still tells what the probe said
                        ShowProbeSubtitle(who, text, 2.5f);
                        return StartResult.Instant;
                    }
                    float pitch = ProbeRules.BeepPitch(host.stress.stress, ProbeDrowsy, tu.probeBeepPitchMax);
                    RadioProfile p = Profile;
                    b.SetBand(p.HighPassHz, p.LowPassHz);
                    b.Play(clip, 0.7f, pitch, false, 0f);
                    b.baseDistortion = Math.Max(0.08f, 0.5f * p.Distortion) + 0.3f * host.StaticLevel * cfg.weakSignalVolume;
                    b.dropout = host.Dropout; // a weak signal chops the beeps like the MC lines
                    if (staticChop) b.TruncateAt(U(tu.chopMin, tu.chopMax) * pitch);
                    ShowProbeSubtitle(who, text, Math.Max(2.5f, b.duration + 1f));
                    lastLine = who + ": " + mood + " beeps (" + b.clipName + ", pitch " + pitch.ToString("0.00") + ")";
                    Audit.Add("probe " + lastLine);
                    ch = b;
                    return StartResult.Started;
                }
            }
            return StartResult.Skip;
        }

        /// <summary>
        /// A beep of the voice in that mood: the prebuilt WAV clips for an unchanged stock voice, clips made in game
        /// (ProbeVoiceBank) for every other voice. Falls back to the stock clips of the voice's lines core.
        /// </summary>
        private AudioClip VoiceClip(ProbeVoiceSpec v, string mood)
        {
            if (v == null) return ClipLibrary.Next(ProbeCores.Folder(Core, mood), rng);
            if (host.probeVoices.UsesStockClips(v)) return ClipLibrary.Next(ProbeCores.Folder(v.Name, mood), rng);
            return ProbeVoiceBank.Next(v, mood, rng) ?? ClipLibrary.Next(ProbeCores.Folder(v.Lines, mood), rng);
        }

        /// <summary>Settings window: play one beep of the voice now, on the probe beep channel.</summary>
        public string PreviewProbeVoice(ProbeVoiceSpec v, string mood)
        {
            AudioClip clip = VoiceClip(v, mood);
            if (clip == null) return "no clip";
            VoiceChannel b = host.beep[1];
            if (b.Busy) b.Stop();
            RadioProfile p = Profile;
            b.SetBand(p.HighPassHz, p.LowPassHz);
            b.Play(clip, 0.7f, 1f, false, 0f);
            b.baseDistortion = Math.Max(0.08f, 0.5f * p.Distortion);
            b.dropout = 0f;
            // label for the player: nothing for an unchanged stock voice, "changed" for an edited stock voice,
            // "custom" for a new voice or one from a config
            string tag = v == null || host.probeVoices.UsesStockClips(v) ? ""
                       : host.probeVoices.IsStockName(v.Name) ? " (changed)" : " (custom)";
            return (v != null ? v.Name : Core) + " " + mood + tag;
        }

        // ---------------- scheduled talk ----------------

        /// <summary>Probe mode replacement of Fire: pet talk. Returns true (the radio timer restarts) unless rate limited.</summary>
        private bool FireProbe(bool appendOverlap)
        {
            if (appendOverlap) return true;           // no crew bursts in probe mode
            Settings cfg = host.settings;
            if (!cfg.probePetTalk || !host.signal.connected) return true;
            Predicate<PhraseScene> fits = x => ProbeRules.SceneFits(ProbeRules.ShapeOf(x), false, true);
            if (ProbeAsleep)
            {
                // the probe sleeps: now and then Mission Control talks anyway, nobody answers
                if (rng.NextDouble() < 0.25)
                {
                    Predicate<PhraseScene> mc = x => fits(x) && x.McLines > 0;
                    PhraseScene sl = PickProbe("probe_sleepy", mc) ?? PickProbe("probe_chat", mc);
                    if (EnqueueProbeScene(sl, U(0f, 0.3f), true)) Log.Info("Pet talk to a sleeping probe: " + sl.Id);
                }
                return true;
            }
            string ev = ProbeDrowsy ? "probe_sleepy" : "probe_chat";
            PhraseScene sc = null;
            // Under stress Mission Control calms the probe about the main cause (heat, spin, g, ...)
            if (ProbeTense && cfg.probeStressTalk) sc = PickProbeCause(ProbeRules.StressEvent, DominantCause, fits);
            if (sc == null && ProbeTense) sc = PickProbe(ev, x => fits(x) && HasProbeMood(x, ProbeRules.ScaredMood));
            if (sc == null) sc = PickProbe(ev, fits);
            if (sc == null && ProbeDrowsy) sc = PickProbe("probe_chat", fits);
            if (sc != null && EnqueueProbeScene(sc, U(0f, 0.3f)))
            {
                Log.Info("Pet talk " + sc.Id + " (" + CurrentPhase + ", core " + Core + ")");
                return true;
            }
            // no pet talk scene for this phase and core: a telemetry ping now and then
            Log.Info("Pet talk: no scene for " + ev + " in phase " + CurrentPhase + ", core " + Core
                     + (sc != null ? " (" + sc.Id + " not queued)" : ""));
            if (cfg.beepsEnabled && rng.NextDouble() < 0.5) Enqueue(UttKind.PingPattern, null, 0f, 0f, null);
            return true;
        }

        /// <summary>Per frame with the gate open, at the end of Update.</summary>
        private void UpdateProbe(float dt)
        {
            if (!host.probeMode) { wasProbeMode = false; hailLeft = -1f; return; }
            if (!wasProbeMode) { wasProbeMode = true; ResetProbe(); Log.Info("Probe mode: core " + Core); }
            UpdateHail(dt);
        }

        private bool AirFree { get { return count == 0 && Idle && !RadioAudible && !Holding && !calloutPending; } }

        /// <summary>A probe scene for the cause first (cause column), else one with cause any.</summary>
        private PhraseScene PickProbeCause(string ev, string cause, Predicate<PhraseScene> fits)
        {
            PhraseScene sc = null;
            if (!IsAnyKey(cause) && cause != NoCause) sc = PickProbe(ev, x => fits(x) && x.Cause == cause);
            return sc ?? PickProbe(ev, x => fits(x) && IsAnyKey(x.Cause));
        }

        private bool ProbeBeepPending()
        {
            if (host.beep[1].Busy) return true;
            for (int i = 0; i < count; i++) if (queue[(head + i) % Capacity].who == UttKind.ProbeBeep) return true;
            return false;
        }

        /// <summary>
        /// The probe version of a callout (staging, chutes, throttle). One short beep keyed on the radio, and
        /// now and then the MC answer of the scene (ProbeRules.CalloutMcAllowed). topic: separation or ignition for
        /// staging, null otherwise. Null when played, else the reason (staging then plays the stage tone).
        /// </summary>
        public string PlayProbeCallout(string ev, string topic, string what)
        {
            if (!host.CanStartAudio) return "gate " + host.gateReason;
            if (radioSilent) return "radio blackout";
            if (!host.settings.radioEnabled) return "radio disabled";
            if (!host.settings.probeCallouts) return "probe callouts off";
            if (string.IsNullOrEmpty(ev)) return "no probe callout for " + what;
            if (!ProbeLink) return "no link";
            if (ProbeAsleep) return "probe asleep";
            if (ProbeBeepPending()) return "probe busy";
            if (count + 16 > Capacity) return "queue full";
            Tuning tu = Tu;
            PhraseScene sc = PickProbe(ev, x => ProbeRules.StartsWithProbe(x.Lines)
                                                && ProbeRules.SceneFits(ProbeRules.ShapeOf(x), false, true), topic);
            // the callout cuts pet talk like a crew callout does, but never an event on the air
            if (airEventPrio == 0) PreemptChatter();
            if (sc != null)
            {
                bool mc = sc.McLines > 0 && ProbeRules.CalloutMcAllowed(rng.NextDouble(), tu.probeCalloutMcChance,
                                                                         clock - lastProbeCalloutMc, tu.probeCalloutMcGap);
                if (EnqueueProbeScene(sc, 0f, false, !mc))
                {
                    if (mc) lastProbeCalloutMc = clock;
                    Log.Info("Probe callout (" + what + "): " + sc.Id + (mc ? " with MC" : ""));
                    return null;
                }
            }
            string mood = ProbeRules.FallbackMood(ev);
            if (mood == null) return "no probe scene";
            Enqueue(UttKind.ProbeBeep, mood, 0f, 0f, null);
            Log.Info("Probe callout (" + what + "): " + mood + " beeps only");
            return null;
        }

        /// <summary>A milestone scene (probe_milestone, topic day1 to day365, orbit or landing). False when none
        /// was queued. Used for the first landing on a body, the rest arrives as an event.</summary>
        public bool EnqueueProbeMilestone(string topic, float delay)
        {
            if (!host.CanStartAudio || !host.settings.radioEnabled || !ProbeLink || ProbeAsleep) return false;
            PhraseScene sc = PickProbe(ProbeRules.MilestoneEvent, x => ProbeRules.SceneFits(ProbeRules.ShapeOf(x), false, true), topic);
            if (sc == null || sc.Topic != topic || !EnqueueProbeScene(sc, delay)) return false;
            Log.Info("Probe milestone " + topic + ": " + sc.Id);
            return true;
        }

        /// <summary>
        /// An antenna is sending science. The first beep of a send may be a probe_data scene (the probe
        /// beeps the data stream, MC confirms). Later beeps are the data stream beep alone (beeps/probe_data).
        /// </summary>
        public void PlayProbeData(bool first)
        {
            if (!host.CanStartAudio || radioSilent || !host.settings.radioEnabled || !host.settings.probeTransmit) return;
            if (!ProbeLink || ProbeAsleep || ProbeBeepPending() || count + 16 > Capacity) return;
            if (first && !HasRadioItems() && !Holding)
            {
                PhraseScene sc = PickProbe(ProbeRules.DataEvent, x => ProbeRules.StartsWithProbe(x.Lines)
                                                                      && ProbeRules.SceneFits(ProbeRules.ShapeOf(x), false, true));
                if (sc != null && EnqueueProbeScene(sc, 0.2f)) { Log.Info("Probe data: " + sc.Id); return; }
            }
            Enqueue(UttKind.ProbeBeep, ProbeCores.DataFolder, 0.1f, 0f, null);
            Log.Debug("Probe data beep" + (first ? " (send started)" : ""));
        }

        /// <summary>The probe calls Mission Control on its own (probe_hail), Mission Control answers.
        /// Own timer (hailInterval), only awake, with the link and outside a serious phase.</summary>
        private void UpdateHail(float dt)
        {
            if (!host.settings.probeHail) return;
            if (holdoff > 0f) return;
            if (hailLeft > 0f) { hailLeft -= dt * RadioRate; return; }
            if (!AirFree || clock - lastRadioClock < AirRules.IntercomStartGap) { hailLeft = 1f; return; }
            if (!ProbeRules.HailMayStart(CurrentPhase, ProbeLink, ProbeAsleep) || Somber) { hailLeft = 5f; return; }
            PhraseScene sc = PickProbe(ProbeRules.HailEvent, x => ProbeRules.StartsWithProbe(x.Lines)
                                                                  && ProbeRules.SceneFits(ProbeRules.ShapeOf(x), false, true));
            hailLeft = NextHailGap();
            if (sc == null || !EnqueueProbeScene(sc, U(0.2f, 0.6f))) return;
            // the hail is the radio turn: the pet talk timer gives it room
            if (progress > target * 0.5f) progress = target * 0.5f;
            Log.Info("Probe hail " + sc.Id + " queued, next in about " + (hailLeft / RadioRate).ToString("0") + " s");
        }

        /// <summary>
        /// The probe reacts to a flight event (touchdown, splashdown, docked, burn_soon) on the radio. A probe
        /// scene of the event, else the probe's own mood beep. Needs the link and an awake probe. preempt cuts the
        /// chatter first (warnings). False when nothing was queued.
        /// </summary>
        public bool EnqueueProbeReaction(string ev, float delay, bool preempt)
        {
            if (!host.CanStartAudio || !host.settings.radioEnabled) return false;
            if (!ProbeLink || ProbeAsleep) { Log.Debug("Probe reaction " + ev + " skipped: " + (ProbeAsleep ? "asleep" : "no link")); return false; }
            if (!preempt && (HasRadioItems() || Holding)) { Log.Debug("Probe reaction " + ev + " skipped: radio busy"); return false; }
            PhraseScene sc = PickProbe(ev, x => ProbeRules.SceneFits(ProbeRules.ShapeOf(x), false, true));
            if (preempt) PreemptChatter();
            if (sc != null && EnqueueProbeScene(sc, delay))
            {
                Log.Info("Probe reaction " + ev + ": scene " + sc.Id + " (core " + sc.Core + ")");
                return true;
            }
            string mood = ProbeRules.FallbackMood(ev);
            if (mood == null || count + 1 > Capacity) return false;
            Enqueue(UttKind.ProbeBeep, mood, delay, 0f, null);
            Log.Info("Probe reaction " + ev + ": " + mood + " beeps only");
            return true;
        }

        // ---------------- events ----------------

        /// <summary>Probe-mode version of TryEvent (the gate and enable checks are done by the caller where noted).</summary>
        private EvGo TryProbeEvent(EventRequest r, out string why)
        {
            why = null;
            string ev = r.ev;
            ProbeEventPlan plan = ProbeRules.PlanFor(ev);
            if (!host.CanStartAudio) { why = "gate " + host.gateReason; return EvGo.Wait; }
            if (!PhraseClips.Loaded) { why = "Lonapse table not loaded"; return EvGo.Drop; }
            if (plan == ProbeEventPlan.Drop)
            {
                // A pure probe flight never has a crew_lost (the watcher only knows kerbals of this flight). One only
                // arrives when the last kerbal of a ship with a probe core died and the gate reopened in probe mode:
                // then Mission Control still gets its one line, like the closed-gate final call.
                // signal_lost: Mission Control cannot reach the probe, so nobody talks.
                if (ev == EventWatchers.SignalLost) { why = "probe out of contact"; return EvGo.Drop; }
                if (ev != EventWatchers.CrewLost || !host.settings.radioEnabled) { why = "not in probe mode"; return EvGo.Drop; }
                if (HasRadioItems() || Holding || airEventPrio >= r.priority) { why = "radio busy"; return EvGo.Wait; }
                PhraseEntry fe = FirstLine(PickFinalScene());
                if (fe == null || fe.Role != "mc") { why = "no MC-only crew_lost line"; return EvGo.Drop; }
                PreemptChatter();
                EnqueueMc(fe.Folder, U(0.2f, 0.6f), fe);
                airEventPrio = Math.Max(airEventPrio, Math.Max(1, r.priority));
                StartSomber("crew lost");
                lastEventResult = ev + " played (probe mode, MC only)";
                return EvGo.Played;
            }
            bool soi = ev == EventWatchers.SoiEnter;
            bool upbeat = soi || ev == EventWatchers.Science || ev == ProbeRules.MilestoneEvent || ev == ProbeRules.RoverEvent;
            bool blackout = radioSilent || InBlackoutPhase;
            if (soi && blackout) { why = "blackout"; return EvGo.Drop; }
            if (upbeat && Somber) { why = "somber"; return EvGo.Wait; }
            if (upbeat && FlightPhase.IsSerious(CurrentPhase)) { why = "serious phase"; return EvGo.Wait; }
            if (calloutPending) { why = "callout waiting"; return EvGo.Wait; }
            bool preempt = Preempts(ev);
            if (preempt) { if (airEventPrio >= r.priority) { why = "event on the air"; return EvGo.Wait; } }
            else if (HasRadioItems() || Holding || airEventPrio > 0) { why = "radio busy"; return EvGo.Wait; }
            if (count + 16 > Capacity && !preempt) { why = "queue full"; return EvGo.Wait; }

            bool link = ProbeLink;
            bool asleep = ProbeAsleep;
            if (!link) { why = "no link"; return EvGo.Wait; }

            string saved = bodyOverride;
            bodyOverride = string.IsNullOrEmpty(r.body) ? null : r.body;
            try
            {
                // a sleeping probe does not answer: Mission Control still says its lines (power_low "nap time")
                PhraseScene sc = PickProbe(ev, x =>
                {
                    ProbeShape s = ProbeRules.ShapeOf(x);
                    return asleep ? s.crew == 0 && s.mc > 0 : ProbeRules.SceneFits(s, false, true);
                }, r.topic);

                if (sc == null)
                {
                    // no probe scene for this event: the probe reacts on its own with a mood beep
                    string mood = ProbeRules.FallbackMood(ev);
                    if (mood == null || asleep) { why = "no probe scene"; return EvGo.Drop; }
                    if (preempt) PreemptChatter();
                    Enqueue(UttKind.ProbeBeep, mood, preempt ? 0.25f : U(0.2f, 0.6f), 0f, null);
                    Log.Info("Event " + ev + ": no probe scene, " + mood + " beeps only");
                }
                else
                {
                    if (preempt) PreemptChatter();
                    if (!EnqueueProbeScene(sc, preempt ? 0.25f : U(0.2f, 0.6f), asleep)) { why = "wait"; return EvGo.Wait; }
                    Log.Info("Event " + ev + ": probe scene " + sc.Id + " (core " + sc.Core + ", topic " + sc.Topic + ")"
                             + (asleep ? ", MC only (asleep)" : ""));
                }
            }
            finally { bodyOverride = saved; }

            airEventPrio = Math.Max(airEventPrio, Math.Max(1, r.priority));
            if (progress > target * 0.5f) progress = target * 0.5f;
            lastEventResult = ev + " played (probe)";
            return EvGo.Played;
        }

        // ---------------- debug ----------------

        /// <summary>Debug tab in probe mode: a random probe scene of the event (any event when empty) for the current core.</summary>
        private string DebugPlayProbe(string evt, string phase, string topic)
        {
            var list = new List<PhraseScene>();
            foreach (PhraseScene sc in PhraseClips.table.Scenes)
            {
                if (!sc.IsProbe) continue;
                if (PhraseTable.IsProbeCrewEvent(sc.Event)) continue; // needs crew aboard
                if (debugSlotsOnly && !sc.HasSlots) continue;
                if (!string.IsNullOrEmpty(evt) && sc.Event != evt) continue;
                if (!string.IsNullOrEmpty(phase) && !PhraseTable.PhaseMatches(sc.Phase, phase)) continue;
                if (!string.IsNullOrEmpty(topic) && !PhraseTable.TopicMatches(sc.Topic, topic)) continue;
                if (!PhraseTable.CoreMatches(sc.Core, Core)) continue;
                if (!PhraseClips.Playable(sc)) continue;
                list.Add(sc);
            }
            PhraseScene pick = ProbeRules.PickWeighted(list, Core, rng, lastSceneId);
            string want = (string.IsNullOrEmpty(evt) ? "any" : evt) + "/core " + Core;
            if (pick == null) return "no probe scene matches " + want;
            lastSceneId = pick.Id;
            string tag = pick.Id + " (" + pick.Event + "/" + pick.Phase + "/core " + pick.Core + ")";
            ProbeShape shape = ProbeRules.ShapeOf(pick);
            PreemptChatter(false);
            holdUntil = -1f;
            if (!EnqueueProbeScene(pick, 0.2f)) return "not played: queue full or nothing playable now, " + tag;
            Log.Info("Debug play " + tag + " for " + want);
            return "played " + tag + (ProbeAsleep && shape.probe > 0 ? " (probe asleep: no beeps)" : "")
                   + (!host.signal.connected && shape.NeedsSignal ? " (no signal: radio lines skipped)" : "");
        }

        // ---------------- probe and crew on one vessel ----------------

        // cabin sound of the probe: a wide band and no grit (the radio band only with intercomRadioFx)
        private const float CabinBeepHighPass = 180f, CabinBeepLowPass = 9000f, CabinBeepVolume = 0.6f;
        private VoiceChannel cabinBeepCh;   // set while a cabin beep plays, so the radio waits for it (IntercomAudible)

        /// <summary>
        /// A probe and crew chat can happen: the setting is on, the vessel has crew and an awake probe core, and the
        /// phrase table is loaded. The intercom scheduler then also runs with only one kerbal aboard.
        /// </summary>
        private bool ProbeCrewPossible
        {
            get
            {
                return host.settings.probeCrewChats && !host.probeMode && host.probeCoreKey != null && !ProbeAsleep
                       && PhraseClips.Loaded;
            }
        }

        /// <summary>
        /// The intercom scheduler fired. With one kerbal it is always a probe and crew chat. With more crew it is one
        /// with chance probeCrewShare, else (or when no scene fits) a normal crew chat.
        /// </summary>
        private void FireChat(bool probeCrew, int speakable)
        {
            if (probeCrew && (speakable < 2 || rng.NextDouble() < Tu.probeCrewShare) && FireProbeCrew()) return;
            if (speakable >= 2) FireIntercom();
        }

        /// <summary>
        /// Queues a probe_crew scene: the probe beeps in the cabin (CabinBeep), one crew member speaks the crew rows
        /// on the intercom. The scene is picked for that kerbal (persona and trait tiers) in its band, else calm.
        /// It runs as an intercom chat with the kerbal as both partners, so the abort rules stay the same.
        /// </summary>
        private bool FireProbeCrew()
        {
            List<KerbalVoice> list = host.roster.voices;
            KerbalVoice a = Effects ? host.roster.PickIntercomA(rng, null, null) : KerbalVoice.Pick(list, null, rng, null);
            if (a == null) return false;
            string band = KerbalVoice.BandNames[(int)(a.band == Band.Panic ? Band.Tense : a.band)];
            Func<string, Func<Predicate<PhraseScene>, PhraseScene>> pick = b =>
                f => PickProbe(PhraseTable.ProbeCrewEvent, x => x.Band == b && f(x));
            Func<string, Func<Predicate<PhraseScene>, List<PhraseScene>>> all = b =>
                f => PhraseClips.Loaded ? PhraseClips.table.ProbeScenesFor(PhraseTable.ProbeCrewEvent, Core, CurrentPhase, false,
                         x => x.Band == b && PhraseClips.Playable(x) && f(x) && SlotsOk(x)) : new List<PhraseScene>();
            PhraseScene sc = PreferFor(a, band, pick(band), all(band), null);
            if (sc == null && band != "calm") sc = PreferFor(a, "calm", pick("calm"), all("calm"), null);
            if (sc == null) return false;
            List<PhraseEntry> lines = PlayLines(sc);
            if (lines.Count == 0 || count + 2 * lines.Count + 1 > Capacity) return false;
            chatA = a;
            chatB = a;
            chatForced = false;
            chatStarted = false;
            lastChatClock = clock;
            chatLimit = IntercomAbortLimit;
            Tuning tu = Tu;
            string prev = null;
            for (int i = 0; i < lines.Count; i++)
            {
                PhraseEntry e = lines[i];
                float d = i == 0 ? U(0f, 0.3f) : U(tu.intercomGapMin, tu.intercomGapMax);
                Utterance u;
                if (e.Role == PhraseTable.RoleProbe)
                {
                    // the probe is not on the intercom: no click before its beep
                    u = Enqueue(UttKind.CabinBeep, null, d, 0f, null);
                }
                else
                {
                    Enqueue(UttKind.IntercomClick, null, d, 0f, null);
                    u = Enqueue(UttKind.IntercomLine, null, 0.04f, 0f, a);
                }
                if (u != null) u.entry = e;
                prev = e.Role;
            }
            if (prev == PhraseTable.RoleCrew) Enqueue(UttKind.IntercomClick, null, 0.1f, 0f, null); // release click
            Log.Debug("Probe and crew chat queued: " + ProbeDisplayNameSafe + " and " + a.displayName + ", scene " + sc.Id
                      + " (" + sc.Band + ")");
            return true;
        }

        /// <summary>Debug tab, event probe_crew on a crewed vessel: a probe and crew chat now.</summary>
        private string DebugPlayProbeCrew()
        {
            if (host.probeCoreKey == null) return "not played: no probe core on this vessel";
            if (!host.settings.intercomEnabled) return "not played: the intercom is off";
            if (ProbeAsleep) return "not played: the probe is asleep (no power)";
            if (host.roster.SpeakableCount() < 1) return "not played: nobody aboard can talk";
            // a queued or playing chat stays: FireProbeCrew would take over its speakers (chatA, chatB)
            if (HasChatItems() || IntercomAudible) return "not played: a chat is queued or playing, try again after it";
            PreemptChatter(true);   // fades the radio, keeps the intercom
            holdUntil = -1f;
            return FireProbeCrew() ? "played a probe and crew chat (see KSP.log for the scene)" : "not played: no probe_crew scene fits this phase";
        }

        private string ProbeDisplayNameSafe { get { try { return host.ProbeDisplayName; } catch (Exception) { return Core; } } }

        /// <summary>Start of a cabin beep: the probe's own voice and mood, the cabin sound, its subtitle.</summary>
        private StartResult StartCabinBeep(Utterance u, out VoiceChannel ch)
        {
            ch = null;
            Settings cfg = host.settings;
            PhraseEntry e = u.entry;
            // the same abort rules as an intercom line, and the probe must still be there and awake
            if ((!chatForced && host.stress.stress > chatLimit) || !CanChat(chatA) || host.probeMode
                || host.probeCoreKey == null || ProbeAsleep)
            {
                abortChat = true;
                return StartResult.Skip;
            }
            VoiceChannel b = host.beep[1];
            if (b.Busy) return StartResult.Wait;
            string mood = e != null && !string.IsNullOrEmpty(e.Beep) ? e.Beep : "curious";
            string text = e != null ? PhraseClips.Subtitle(e) : ProbeRules.BeepSubtitle(mood);
            string who = ProbeDisplayNameSafe;
            AudioClip clip = cfg.beepsEnabled ? VoiceClip(host.probeVoice, mood) : null;
            if (clip == null)
            {
                // beeps off or no clip: the subtitle still tells what the probe said
                ShowProbeSubtitle(who, text, 2.5f);
                return StartResult.Instant;
            }
            if (cfg.intercomRadioFx)
            {
                RadioProfile p = Profile;
                b.SetBand(p.HighPassHz, p.LowPassHz);
                b.baseDistortion = Math.Max(0.08f, 0.5f * p.Distortion);
            }
            else
            {
                b.SetBand(CabinBeepHighPass, CabinBeepLowPass);
                b.baseDistortion = 0f;
            }
            b.Play(clip, CabinBeepVolume, ProbeDrowsy ? 0.9f : 1f, false, 0f);
            b.dropout = 0f;   // same cabin: no signal dropouts
            ShowProbeSubtitle(who, text, Math.Max(2.5f, b.duration + 1f));
            cabinBeepCh = b;
            lastLine = "cabin " + who + ": " + mood + " beeps (" + b.clipName + ")";
            Audit.Add(lastLine);
            Log.Debug("cabin beep " + who + " " + mood + " " + b.clipName);
            ch = b;
            return StartResult.Started;
        }
    }
}
