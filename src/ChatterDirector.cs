// Scheduler, exchanges (call and response), utterance queue and overlap.
// v2: Lonapse scenes, priority (callout > warning > chatter) with preempt and hold, one crew speaker per
// Mission Control exchange, pacing (token bucket, cooldowns, panic gap, escalation), the station rule,
// EVA exchanges, radio keying per profile and subtitles.
using System;
using UnityEngine;

namespace Lonapse
{
    public enum UttKind { CrewLine, ControlLine, ControlFx, BedStart, BedStop, StaticBurst, PingPattern, IntercomLine, IntercomClick, RadioOpen, RadioClose, RogerBeep,
        // plan v5 probe mode: probe beep on beep[1] (radio)
        ProbeBeep }

    public class Utterance
    {
        public UttKind who;
        public KerbalVoice speaker;   // null = pick at start time
        public string folder;         // null for crew = folder from the speaker's band at start time
        public float startDelay;      // seconds after the previous item ended
        public float overlapAt;       // > 0: may start at this fraction of the previous crew line
        public float pitchMul = 1f;   // intercom pair pitch nudge
        public PhraseEntry entry;     // Lonapse row, null for folder clips

        public void Set(UttKind k, string f, float delay, float overlap, KerbalVoice sp)
        {
            who = k; folder = f; startDelay = delay; overlapAt = overlap; speaker = sp; pitchMul = 1f; entry = null;
        }
    }

    public partial class ChatterDirector
    {
        public const int Capacity = 32;

        private readonly Utterance[] queue = new Utterance[Capacity];
        private int head, count;

        private readonly LonapseAddon host;
        private readonly System.Random rng;

        public float clock;
        public float progress;
        public float target = 1f;
        public float holdoff;
        public bool radioSilent;   // blackout: nothing starts
        public bool staticChop;    // blackout Static state: chop voice lines, extra distortion

        private VoiceChannel lastChannel;
        private UttKind lastKind;
        private float lastStartClock;
        private float lastDuration;
        private float lastEndClock = -1f;

        public float currentInterval = 110f;

        // intercom (crew-to-crew) scheduler, separate from the radio scheduler
        public float intercomProgress;
        public float intercomTarget = 1f;
        public float intercomInterval = 240f;
        public bool intercomEligible;
        public float quietTimer;   // how long stress has stayed below quietCoastStress
        public bool QuietCoast { get { return quietTimer > Tu.quietCoastTime; } }
        public float stationMcLeft = 100f;
        private bool wasStation;
        private KerbalVoice chatA, chatB;  // current intercom pair
        private bool abortChat;

        // v2 pacing (req 2, 11)
        public float holdUntil = -1f;      // chatter queue waits until this clock (callout hold)
        private TokenBucket bucket;
        private float bucketBurst = -1f, bucketRate = -1f;
        private readonly Cooldowns cool = new Cooldowns();
        public readonly Escalation esc = new Escalation();
        private float lastPanic = -1e9f;
        private string lastSceneId;
        public string lastLine = "";       // audit text for the overlay
        public int panicDowngrades, bucketDenied;

        // v3 context (plan v3, req 3, 5, 6)
        private readonly FlightPhaseClassifier classifier = new FlightPhaseClassifier();
        /// <summary>Debug override of the flight phase. Null = automatic.</summary>
        public string forcePhase;
        /// <summary>Stress cause with the largest weighted component, or NoCause. Updated every frame.</summary>
        public string DominantCause { get; private set; }
        public const string NoCause = "none"; // never a row cause: CauseMatches lets only "any" rows through
        public const float CauseMin = 0.05f;   // weighted component below this is no cause
        /// <summary>Seconds to the next blackout intercom chat, -1 when not in blackout.</summary>
        public float blackoutIntercomLeft = -1f;
        private bool wasBlackoutPhase;
        private bool chatForced;               // debug chat: ignores the stress gates
        private float chatLimit = 1f;          // abort stress for the running chat, fixed when it is queued

        // air arbiter (user report: the intercom played over the radio), rules and gaps in AirRules
        private float lastRadioClock = -1e9f;  // last frame the radio had the air (audible, holding)
        private float lastChatClock = -1e9f;   // last frame the queued chat was audible or started an item
        private bool chatStarted;              // the queued chat had its first turn: resume gap instead of start gap
        private readonly Utterance[] scratch = new Utterance[Capacity];
        private static readonly Predicate<Utterance> RadioFirst = x => !IsChatItem(x.who);
        private static readonly Predicate<Utterance> ChatFirst = x => IsChatItem(x.who);
        // a callout that came during an intercom turn waits until the line is over (never cut a line mid-word)
        private bool calloutPending;
        private string pcEv, pcFallback, pcWhat;
        private float pcMaxLen, pcSince;
        private Predicate<PhraseScene>[] pcPrefer;
        private Action<string> pcOnDrop;       // told why when the waiting callout is dropped (stage tone fallback)

        /// <summary>
        /// Effective flight phase: forcePhase, else "blackout" while the BlackoutMachine is in Blackout,
        /// else the classifier result (null before the first valid sample).
        /// </summary>
        public string CurrentPhase
        {
            get
            {
                if (!string.IsNullOrEmpty(forcePhase)) return forcePhase;
                if (host.blackout != null && host.blackout.state == BlackoutState.Blackout) return FlightPhase.Blackout;
                return classifier.Current;
            }
        }

        /// <summary>The raw classifier result (no force, no blackout), for the overlay.</summary>
        public string ClassifiedPhase { get { return classifier.Current; } }

        public bool InBlackoutPhase { get { return CurrentPhase == FlightPhase.Blackout; } }

        /// <summary>Stress above this ends an intercom chat. In blackout the crew keeps talking up to blackoutIntercomMaxStress.</summary>
        private float IntercomAbortLimit { get { return InBlackoutPhase ? Tu.blackoutIntercomMaxStress : Tu.intercomAbortStress; } }

        /// <summary>Body key for {body} lines: bodyOverride when set (an event about another body, the debug tab),
        /// else the sample body, else generic. PhraseClips.LinesFor gets this.</summary>
        private string Body
        {
            get
            {
                if (!string.IsNullOrEmpty(bodyOverride)) return bodyOverride;
                return string.IsNullOrEmpty(host.sample.body) ? FlightPhase.GenericBody : host.sample.body;
            }
        }

        public ChatterDirector(LonapseAddon host, System.Random rng)
        {
            this.host = host;
            this.rng = rng;
            DominantCause = NoCause;
            for (int i = 0; i < Capacity; i++) queue[i] = new Utterance();
            target = U(0.6f, 1.4f);
            intercomTarget = U(0.6f, 1.4f);
        }

        private float U(float lo, float hi) { return DcMath.Uniform(rng, lo, hi); }
        private Tuning Tu { get { return host.settings.tuning; } }
        private RadioProfile Profile { get { return host.profile ?? RadioProfiles.Get(RadioContext.Ship); } }

        public int QueueCount { get { return count; } }
        public bool Holding { get { return clock < holdUntil; } }

        /// <summary>True when nothing is queued and no voice/control channel is busy.</summary>
        public bool Idle
        {
            get
            {
                if (count > 0) return false;
                for (int i = 0; i < host.crew.Length; i++) if (host.crew[i].Busy) return false;
                if (host.calloutCh != null && host.calloutCh.Busy) return false;
                return !host.control.Busy;
            }
        }

        /// <summary>Any voice is audible (crew, callout, control): the game audio ducks under it.</summary>
        public bool VoiceActive
        {
            get
            {
                for (int i = 0; i < host.crew.Length; i++) if (host.crew[i].Busy) return true;
                if (host.calloutCh != null && host.calloutCh.Busy) return true;
                return host.control.Busy;
            }
        }

        /// <summary>
        /// The radio is audible: a crew line on the radio preset, a callout, Mission Control (also the countdown),
        /// radio keying (also a keyed ping) or a static burst. The blackout static loop and its
        /// crackle are not counted (Static and Blackout states), neither are descent blips, tones or the alarm.
        /// </summary>
        public bool RadioAudible
        {
            get
            {
                for (int i = 0; i < host.crew.Length; i++) if (host.crew[i].Busy && !host.crew[i].intercom) return true;
                if (host.calloutCh != null && host.calloutCh.Busy) return true;
                if (host.control.Busy) return true; // the static bed is left out: it only runs under MC, and an orphan loop would block the intercom
                if (host.keyCh != null && host.keyCh.Busy) return true;
                if (host.probeMode && host.beep.Length > 1 && host.beep[1].Busy) return true; // the probe answers on the radio
                BlackoutState bs = host.blackout != null ? host.blackout.state : BlackoutState.Clear;
                return host.staticCh != null && host.staticCh.Busy && bs != BlackoutState.Static && bs != BlackoutState.Blackout;
            }
        }

        /// <summary>An intercom line or click is audible.</summary>
        public bool IntercomAudible
        {
            get
            {
                if (host.clickCh != null && host.clickCh.Busy) return true;
                for (int i = 0; i < host.crew.Length; i++) if (host.crew[i].Busy && host.crew[i].intercom) return true;
                return false;
            }
        }

        /// <summary>
        /// An intercom turn is open: a line or click is audible, or the queued chat is between a turn's click and its
        /// line (or before its release click). The radio waits for it (AirRules.RadioMayStart).
        /// </summary>
        public bool IntercomTurnOpen { get { return IntercomAudible || (chatStarted && HeadInOpenTurn()); } }

        private bool LineFollowsHead()
        {
            for (int i = 1; i < count; i++) if (queue[(head + i) % Capacity].who == UttKind.IntercomLine) return true;
            return false;
        }

        /// <summary>The head item is a click that opens a new intercom turn (a line follows).</summary>
        private bool HeadOpensTurn()
        {
            return count > 0 && queue[head].who == UttKind.IntercomClick && LineFollowsHead();
        }

        /// <summary>The head item belongs to the current turn: its line, or the release click after the last line.</summary>
        private bool HeadInOpenTurn()
        {
            if (count == 0) return false;
            UttKind k = queue[head].who;
            return k == UttKind.IntercomLine || (k == UttKind.IntercomClick && !LineFollowsHead());
        }

        /// <summary>A new intercom chat may be queued now (AirRules.IntercomMayStart).</summary>
        private bool IntercomMayStartNow
        {
            get { return AirRules.IntercomMayStart(RadioAudible, HasRadioItems(), Holding || calloutPending, clock - lastRadioClock); }
        }

        public float TimeToNext
        {
            get
            {
                if (radioSilent) return -1f;
                float t = Math.Max(0f, target - progress) * currentInterval;
                return t + Math.Max(0f, holdoff);
            }
        }

        public float IntercomTimeToNext
        {
            get { return intercomEligible ? Math.Max(0f, intercomTarget - intercomProgress) * intercomInterval + Math.Max(0f, holdoff) : -1f; }
        }

        // ---------------- lifecycle ----------------

        public void HardReset()
        {
            head = 0; count = 0;
            chatA = chatB = null;
            abortChat = false;
            progress = 0f;
            target = U(0.6f, 1.4f);
            lastChannel = null;
            lastEndClock = -1f;
            radioSilent = false;
            staticChop = false;
            intercomProgress = 0f;
            intercomTarget = U(0.6f, 1.4f);
            holdUntil = -1f;
            chatStarted = false;
            DropPendingCallout("reset");
            lastRadioClock = lastChatClock = -1e9f;
            ResetFinalCall();
        }

        /// <summary>Vessel switch: cooldowns, the token bucket and the escalation start fresh (plan 4.3).</summary>
        public void ResetPacing()
        {
            cool.Clear();
            bucket = null;
            lastPanic = -1e9f;
            esc.Decay(1f, 0f);
            lastSceneId = null;
            classifier.Reset(); // new vessel: its own phase, no hold from the old one
            ResetEventsForVessel();
            wasProbeMode = false; // plan v5: the probe timers start fresh on the next probe-mode frame
        }

        public void OnGateOpened()
        {
            HardReset();
            wasStation = false; // station check-in timer restarts after the gate was closed
            holdoff = U(Tu.reopenDelayMin, Tu.reopenDelayMax);
        }

        /// <summary>After on-rails warp: a single ping is scheduled by the BeepDirector, voices wait a short moment.</summary>
        public void OnGateOpenedAfterWarp()
        {
            HardReset();
            holdoff = Tu.warpEndHoldoff;
        }

        public void ClearQueue()
        {
            head = 0; count = 0;
            chatA = chatB = null;
            abortChat = false;
            chatForced = false;
            chatStarted = false;
            if (host.controlBed != null) host.controlBed.FadeOutOnce(0.25f); // its BedStop may have been dropped
            DropPendingCallout("queue cleared"); // the countdown (or a debug play) takes the air
        }

        /// <summary>Keeps the queued items that match, in order, and drops the rest. Returns the number kept.</summary>
        private int KeepOnly(Predicate<Utterance> keep)
        {
            int kept = AirRules.StablePartitionRing(queue, head, count, keep, scratch);
            for (int i = kept; i < count; i++)
            {
                Utterance u = queue[(head + i) % Capacity];
                u.speaker = null;
                u.entry = null;
            }
            count = kept;
            return kept;
        }

        private void ResetChat()
        {
            chatA = chatB = null;
            abortChat = false;
            chatForced = false;
            chatStarted = false;
        }

        /// <summary>
        /// Drop every queued item except intercom chat items (lines and clicks), in order. Blackout: the intercom does
        /// not use the radio, so a chat keeps going (req 6). Warnings: the chat pauses, the radio chatter goes.
        /// </summary>
        public void ClearRadioQueue()
        {
            if (KeepOnly(ChatFirst) == 0) ResetChat();
        }

        /// <summary>Drop the rest of the queued intercom chat, radio items stay in order.</summary>
        private void DropChat(string why)
        {
            int before = count;
            KeepOnly(RadioFirst);
            ResetChat();
            Log.Debug("Intercom chat dropped (" + why + "), " + (before - count) + " item(s)");
        }

        private bool HasRadioItems()
        {
            for (int i = 0; i < count; i++) if (!IsChatItem(queue[(head + i) % Capacity].who)) return true;
            return false;
        }

        private bool HasChatItems()
        {
            for (int i = 0; i < count; i++) if (IsChatItem(queue[(head + i) % Capacity].who)) return true;
            return false;
        }

        // ---------------- queue ----------------

        private bool PrevDone()
        {
            if (lastChannel == null) return true;
            if (!lastChannel.Busy) return true;
            return clock - lastStartClock > lastDuration + 0.5f;
        }

        private Utterance Enqueue(UttKind k, string folder, float delay, float overlap, KerbalVoice sp)
        {
            if (count >= Capacity)
            {
                Log.Debug("Utterance queue full, dropping " + k + " " + folder);
                return null;
            }
            if (count == 0 && PrevDone()) lastEndClock = clock; // delay counts from now
            Utterance u = queue[(head + count) % Capacity];
            u.Set(k, folder, delay, overlap, sp);
            count++;
            return u;
        }

        // chat items: the intercom. They stay in a blackout and the radio waits for them.
        private static bool IsChatItem(UttKind k) { return IsIntercomItem(k); }

        /// <summary>Radio items: crew/MC lines, quindar, squelch, keying, the MC static bed and static bursts.</summary>
        private static bool IsRadioItem(UttKind k)
        {
            return k == UttKind.CrewLine || k == UttKind.ControlLine || k == UttKind.ControlFx
                   || k == UttKind.BedStart || k == UttKind.StaticBurst || k == UttKind.RadioOpen || k == UttKind.RadioClose
                   || k == UttKind.RogerBeep || k == UttKind.ProbeBeep;
        }

        private bool CanChat(KerbalVoice k)
        {
            return k != null && !k.outDueToG && host.roster.voices.Contains(k);
        }

        private void Dequeue()
        {
            if (count == 0) return;
            queue[head].speaker = null;
            queue[head].entry = null;
            head = (head + 1) % Capacity;
            count--;
        }

        private enum StartResult { Started, Instant, Skip, Wait }

        private void ProcessQueue()
        {
            // a chat that waited too long for the radio is dropped (AirRules.ChatMaxPause)
            if (count > 0 && !IntercomAudible && HasChatItems() && AirRules.ChatPausedTooLong(clock - lastChatClock))
                DropChat("paused too long for the radio");
            if (Holding) return; // a callout has the air (req 2)
            int guard = 0;
            while (count > 0 && guard++ < 8)
            {
                // blackout: radio items go, intercom chat items stay (req 6)
                if (radioSilent && HasRadioItems()) { ClearRadioQueue(); if (count == 0) return; }
                // radio queued behind the chat goes first; the chat pauses between two turns, never inside one
                if (AirRules.ChatShouldYield(HeadOpensTurn(), HasRadioItems()))
                {
                    AirRules.StablePartitionRing(queue, head, count, RadioFirst, scratch);
                    Log.Debug("Intercom chat pauses for the radio");
                }
                Utterance u = queue[head];
                if (IsChatItem(u.who))
                {
                    // a new turn needs a quiet radio (start or resume gap); the line and release click of an open turn go on
                    if (HeadOpensTurn() && !AirRules.ChatTurnMayStart(chatStarted, RadioAudible, HasRadioItems(),
                                                                      calloutPending, clock - lastRadioClock)) return;
                }
                else if (!AirRules.RadioMayStart(IntercomTurnOpen)) return; // radio never starts inside an intercom turn
                bool prevDone = PrevDone();
                if (prevDone && lastEndClock < 0f) lastEndClock = clock;

                bool ready;
                if (u.overlapAt > 0f && !prevDone && lastKind == UttKind.CrewLine)
                    ready = clock - lastStartClock >= u.overlapAt * lastDuration;
                else
                    ready = prevDone && clock - lastEndClock >= u.startDelay;
                if (!ready) return;

                VoiceChannel ch;
                StartResult r = Start(u, !prevDone, out ch);
                if (r == StartResult.Wait) return;
                UttKind k = u.who;
                Dequeue();
                if (abortChat)
                {
                    // drop the rest of this intercom chat (partner out, left the vessel, or stress too high)
                    abortChat = false;
                    int dropped = 0;
                    while (count > 0 && IsChatItem(queue[head].who)) { Dequeue(); dropped++; }
                    ResetChat();
                    Log.Debug("Intercom chat aborted, " + dropped + " item(s) dropped");
                    continue;
                }
                if (r == StartResult.Started)
                {
                    if (IsChatItem(k)) { if (IsIntercomItem(k)) chatStarted = true; lastChatClock = clock; }
                    else lastRadioClock = clock;
                }
                if (r == StartResult.Started && ch != null)
                {
                    lastChannel = ch;
                    lastKind = k;
                    lastStartClock = clock;
                    lastDuration = ch.duration;
                    lastEndClock = -1f;
                    return; // at most one audible start per frame
                }
            }
        }

        private int CrewPlaying()
        {
            int n = 0;
            for (int i = 0; i < host.crew.Length; i++) if (host.crew[i].Busy) n++;
            return n;
        }

        public int CrewCap()
        {
            int byStress = 1 + (int)Math.Floor(2f * DcMath.Smooth(host.stress.stress, 0.6, 1.0));
            return Math.Max(1, Math.Min(host.settings.maxCrewVoices, byStress));
        }

        private VoiceChannel FreeCrewChannel()
        {
            for (int i = 0; i < host.crew.Length; i++) if (!host.crew[i].Busy) return host.crew[i];
            return null;
        }

        private float VoiceBoost { get { return MixMath.DbToLin(host.settings.voiceBoostDb); } }

        /// <summary>Radio profile on a crew radio line: higher high-pass and a low-pass ceiling (req 16).</summary>
        private void ApplyProfile(VoiceChannel c)
        {
            RadioProfile p = Profile;
            c.CapLowPass(p.LowPassHz);
        }

        /// <summary>Panic lines obey the global gap and the per-speaker cooldown, otherwise they drop to tense (req 11).</summary>
        private bool PanicOk(KerbalVoice sp)
        {
            Tuning tu = Tu;
            bool ready = sp == null || cool.Ready("panic:" + sp.CooldownKey, clock, tu.panicCooldown);
            return PacingRules.PanicAllowed(clock, lastPanic, tu.panicMinGap, ready);
        }

        private void StampLine(KerbalVoice sp, bool panic)
        {
            if (sp != null) cool.Stamp(sp.CooldownKey, clock);
            if (!panic) return;
            lastPanic = clock;
            if (sp != null) cool.Stamp("panic:" + sp.CooldownKey, clock);
            Tuning tu = Tu;
            esc.Bump(tu.panicEscStep > 0f ? tu.panicEscCap / tu.panicEscStep : 0f);
        }

        private void ShowSubtitle(PhraseEntry e, string who, float seconds)
        {
            if (e == null || !host.settings.subtitles || host.subtitles == null) return;
            host.subtitles.Show(who, PhraseClips.Subtitle(e), seconds);
        }

        private StartResult Start(Utterance u, bool overlapping, out VoiceChannel ch)
        {
            ch = null;
            // Absolute rule: the director never starts audio while the gate is closed.
            if (!host.CanStartAudio) return StartResult.Wait;
            float s = host.stress.stress;
            Tuning tu = Tu;
            Settings cfg = host.settings;

            // category toggles: radio (crew <-> MC), intercom (crew <-> crew), beeps are independent
            if (IsRadioItem(u.who) && !cfg.radioEnabled) return StartResult.Skip;
            if (u.who == UttKind.PingPattern && !cfg.beepsEnabled) return StartResult.Skip;
            if (IsIntercomItem(u.who) && !cfg.intercomEnabled) { abortChat = true; return StartResult.Skip; }

            switch (u.who)
            {
                case UttKind.CrewLine:
                {
                    if (!host.roster.AnyoneCanSpeak()) return StartResult.Skip;
                    if (CrewPlaying() >= CrewCap()) return StartResult.Wait;
                    VoiceChannel c = FreeCrewChannel();
                    if (c == null) return StartResult.Wait;
                    KerbalVoice sp = u.speaker;
                    if (sp == null || sp.outDueToG || !host.roster.voices.Contains(sp)) sp = host.roster.PickSpeaker(rng);
                    if (sp == null) return StartResult.Skip;
                    AudioClip clip;
                    string what;
                    bool panic;
                    if (u.entry != null)
                    {
                        clip = PhraseClips.Clip(u.entry, sp.isFemale);
                        what = u.entry.ClipKey;
                        panic = u.entry.Band == "panic";
                    }
                    else
                    {
                        string folder = u.folder ?? CrewFolder(sp, s);
                        if (folder == "crew/panic" && !PanicOk(sp)) { folder = "crew/tense"; panicDowngrades++; }
                        clip = ClipLibrary.NextFor(folder, sp.isFemale, sp.rng);
                        what = folder;
                        panic = folder == "crew/panic";
                    }
                    if (clip == null) return StartResult.Skip;
                    float sk = sp.sk, gs = host.stress.gStrain;
                    float pitch = DcMath.CrewPitch(sp.basePitch, sk, gs, tu.crewStressSemitones, tu.crewPitchMax);
                    float vol = DcMath.Lerp(0.55f, 1.0f, sk);
                    c.SetPreset(false, tu);
                    c.Play(clip, vol, pitch, false, 0f);
                    c.speaker = sp;
                    c.boost = VoiceBoost;
                    c.compressor = cfg.voiceCompressor;
                    c.phraseKey = u.entry != null ? u.entry.ClipKey : null;
                    c.tremRate = sp.tremRate;
                    c.SetCrewFilters(sk, sp.timbre, gs, tu);
                    ApplyProfile(c);
                    float keep = DcMath.TruncFraction(sk, tu.truncStart, tu.truncMinFrac);
                    if (keep < 1f) c.TruncateAt(clip.length * keep);
                    if (staticChop) c.TruncateAt(U(tu.chopMin, tu.chopMax) * pitch);
                    StampLine(sp, panic);
                    ShowSubtitle(u.entry, sp.displayName, c.duration);
                    lastLine = sp.displayName + ": " + what + " (" + sp.BandName + ")";
                    Audit.Add("crew " + lastLine + " s " + s.ToString("0.00"));
                    Log.Debug("crew " + sp.displayName + " " + what + " " + c.clipName + " pitch " + pitch.ToString("0.00") + " vol " + vol.ToString("0.00") + (overlapping ? " overlap" : ""));
                    ch = c;
                    return StartResult.Started;
                }
                case UttKind.IntercomLine:
                {
                    // never MC, no quindar, no static: a close cabin voice on the intercom preset
                    KerbalVoice sp = u.speaker;
                    // radio behaviour takes over at high stress; a chat needs both partners
                    // in blackout the limit is blackoutIntercomMaxStress (req 6), a debug chat ignores it
                    if ((!chatForced && s > chatLimit) || !CanChat(sp) || !CanChat(chatA) || !CanChat(chatB))
                    {
                        abortChat = true;
                        return StartResult.Skip;
                    }
                    if (CrewPlaying() >= CrewCap()) return StartResult.Wait;
                    VoiceChannel c = FreeCrewChannel();
                    if (c == null) return StartResult.Wait;
                    AudioClip clip = u.entry != null ? PhraseClips.Clip(u.entry, sp.isFemale)
                                                     : ClipLibrary.NextFor(u.folder ?? CrewFolder(sp, s), sp.isFemale, sp.rng);
                    if (clip == null) return StartResult.Skip;
                    float sk = sp.sk, gs = host.stress.gStrain;
                    float pitch = DcMath.CrewPitch(sp.basePitch * u.pitchMul, sk, gs, tu.crewStressSemitones, tu.crewPitchMax);
                    float vol = DcMath.Lerp(0.55f, 1.0f, sk);
                    c.SetPreset(true, tu);
                    c.Play(clip, vol, pitch, false, 0f);
                    c.speaker = sp;
                    c.boost = VoiceBoost;
                    c.compressor = cfg.voiceCompressor;
                    c.phraseKey = u.entry != null ? u.entry.ClipKey : null;
                    c.tremRate = sp.tremRate;
                    c.SetIntercomFilters(sp.timbre, gs, tu);
                    StampLine(sp, false);
                    ShowSubtitle(u.entry, sp.displayName, c.duration);
                    lastLine = "intercom " + sp.displayName + ": " + (u.entry != null ? u.entry.ClipKey : c.clipName);
                    Audit.Add(lastLine);
                    Log.Debug("intercom " + sp.displayName + " " + c.clipName + " pitch " + pitch.ToString("0.00"));
                    ch = c;
                    return StartResult.Started;
                }
                case UttKind.IntercomClick:
                {
                    if ((!chatForced && s > chatLimit) || !CanChat(chatA) || !CanChat(chatB))
                    {
                        abortChat = true;
                        return StartResult.Skip;
                    }
                    AudioClip clip = ClipLibrary.Click;
                    if (clip == null) return StartResult.Skip;
                    // dedicated source: a click is never skipped because crew channels are busy
                    VoiceChannel c = host.clickCh;
                    if (c.Busy) c.Stop();
                    c.Play(clip, tu.intercomClickVolume, 1f, false, 0f);
                    ch = c;
                    return StartResult.Started;
                }
                case UttKind.RadioOpen:
                case UttKind.RadioClose:
                {
                    // push-to-talk keying of the vessel radio: desk click on a station, headset squelch in a ship or suit
                    RadioProfile p = Profile;
                    string f = u.who == UttKind.RadioOpen ? p.OpenClip : p.CloseClip;
                    AudioClip clip = ClipLibrary.CountOf(f) > 0 ? ClipLibrary.Next(f, rng) : null;
                    if (clip == null) return StartResult.Instant;
                    VoiceChannel k = host.keyCh;
                    if (k.Busy) k.Stop();
                    k.Play(clip, tu.radioKeyVolume, U(0.97f, 1.03f), false, 0f);
                    ch = k;
                    return StartResult.Started;
                }
                case UttKind.RogerBeep:
                {
                    // the crew lets go of the key: a short roger beep before the squelch tail
                    if (!cfg.rogerBeep || ClipLibrary.CountOf("beeps/roger") == 0) return StartResult.Instant;
                    AudioClip clip = ClipLibrary.Next("beeps/roger", rng);
                    if (clip == null) return StartResult.Instant;
                    VoiceChannel k = host.keyCh;
                    if (k.Busy) k.Stop();
                    k.Play(clip, tu.radioKeyVolume * 0.75f, 1f, false, 0f);
                    ch = k;
                    return StartResult.Started;
                }
                case UttKind.ControlLine:
                {
                    VoiceChannel c = host.control;
                    if (c.Busy) return StartResult.Wait;
                    if (!host.signal.connected) { Log.Debug("control line skipped: no signal"); return StartResult.Skip; }
                    AudioClip clip = u.entry != null ? PhraseClips.Clip(u.entry, false) : ClipLibrary.Next(u.folder ?? "control/calm", rng);
                    if (clip == null) return StartResult.Skip;
                    // v3 req 7: no runtime pitch drop under stress, the panic delivery sounds like the calm one
                    float pitch = 1f;
                    float vol = DcMath.Lerp(0.80f, 0.60f, s);
                    RadioProfile p = Profile;
                    c.SetBand(p.HighPassHz, p.LowPassHz);
                    c.Play(clip, vol, pitch, false, 0f);
                    c.boost = VoiceBoost;
                    c.compressor = cfg.voiceCompressor;
                    c.phraseKey = u.entry != null ? u.entry.ClipKey : null;
                    c.baseDistortion = Math.Max(tu.controlDistortion, p.Distortion) + 0.3f * host.StaticLevel * host.settings.weakSignalVolume;
                    c.dropout = host.Dropout;
                    if (staticChop) c.TruncateAt(U(tu.chopMin, tu.chopMax) * pitch);
                    ShowSubtitle(u.entry, "MC", c.duration);
                    lastLine = "MC: " + (u.entry != null ? u.entry.ClipKey : u.folder);
                    Audit.Add(lastLine + " s " + s.ToString("0.00"));
                    Log.Debug("control " + (u.entry != null ? u.entry.ClipKey : u.folder) + " " + c.clipName + " pitch " + pitch.ToString("0.00") + " vol " + vol.ToString("0.00"));
                    ch = c;
                    return StartResult.Started;
                }
                case UttKind.ControlFx:
                {
                    VoiceChannel c = host.control;
                    if (c.Busy) return StartResult.Wait;
                    if (!host.signal.connected) return StartResult.Skip;
                    AudioClip clip = ClipLibrary.Next(u.folder, rng);
                    if (clip == null) return StartResult.Skip;
                    bool isQuindar = u.folder != null && u.folder.StartsWith("beeps/", StringComparison.Ordinal);
                    // quindar tones sit with the beep level, squelch with the control level
                    c.Play(clip, isQuindar ? 0.45f : 0.7f, 1f, false, 0f);
                    c.baseDistortion = isQuindar ? 0.05f : tu.controlDistortion;
                    ch = c;
                    return StartResult.Started;
                }
                case UttKind.BedStart:
                {
                    VoiceChannel b = host.controlBed;
                    if (!host.signal.connected) return StartResult.Instant;
                    if (!b.Busy && ClipLibrary.CountOf("static/loop") > 0)
                    {
                        AudioClip clip = ClipLibrary.Next("static/loop", rng);
                        // weaker signal, louder bed (req 17)
                        if (clip != null) b.Play(clip, DcMath.Clamp01(tu.controlBedVolume + host.StaticLevel * host.settings.weakSignalVolume), 1f, true, 0.12f);
                    }
                    return StartResult.Instant;
                }
                case UttKind.BedStop:
                    host.controlBed.FadeOut(0.25f);
                    return StartResult.Instant;
                case UttKind.StaticBurst:
                {
                    AudioClip clip = ClipLibrary.Next("static/burst", rng);
                    if (clip == null) return StartResult.Skip;
                    VoiceChannel st = host.staticCh;
                    if (st.Busy) st.Stop();
                    // weakSignalVolume (user request): the bursts were too loud next to the voices
                    st.Play(clip, 0.8f * host.settings.weakSignalVolume, 1f, false, 0f);
                    ch = st;
                    return StartResult.Started;
                }
                case UttKind.ProbeBeep:
                    return StartProbe(u, out ch);
                case UttKind.PingPattern:
                {
                    VoiceChannel b = host.beeps.PlayPing();
                    if (b == null) return StartResult.Skip;
                    ch = b;
                    return StartResult.Started;
                }
            }
            return StartResult.Skip;
        }

        // ---------------- exchange builders ----------------

        /// <summary>Crew folder from the speaker's band. Station/base mode prefers one band calmer below burst stress.</summary>
        private string CrewFolder(KerbalVoice sp, float s)
        {
            Band b = sp.band;
            if (host.stationMode && s < Tu.burstBand && rng.NextDouble() < Tu.stationCalmBias) b = KerbalVoice.Calmer(b);
            return KerbalVoice.FolderFor(b); // intercom and radio lines both follow the speaker's personal band
        }

        private string BandOf(KerbalVoice sp, float s)
        {
            Band b = sp != null ? sp.band : Band.Calm;
            if (host.stationMode && s < Tu.burstBand && rng.NextDouble() < Tu.stationCalmBias) b = KerbalVoice.Calmer(b);
            if (b == Band.Panic && !PanicOk(sp)) { b = Band.Tense; panicDowngrades++; }
            return KerbalVoice.BandNames[(int)b];
        }

        /// <summary>MC probability scale: Mission Control talks less to a station or base.</summary>
        private double McP(double p)
        {
            return host.stationMode ? p * Tu.stationMcFactor : p;
        }

        private string McFolder(float s)
        {
            return (s >= Tu.mcTenseLo && s < Tu.mcTenseHi) ? "control/tense" : "control/calm";
        }

        private float ResponseDelay(float s)
        {
            // far from Kerbin the answer takes a little longer (light delay, at most lightDelayMax s)
            return DcMath.Lerp(1.2f, 0.35f, s) * U(0.8f, 1.2f) + host.LightDelay;
        }

        /// <summary>MC transmission: quindar in, line, quindar out, static bed under it, squelch tail.</summary>
        private void EnqueueMc(string folder, float delay, PhraseEntry e = null)
        {
            if (count + 7 > Capacity) return;
            // a weak signal sometimes cracks before the transmission (req 17)
            // far away the delayed answer arrives with a short crackle first
            if (rng.NextDouble() < host.StaticLevel || host.LightDelay > 0.5f) Enqueue(UttKind.StaticBurst, null, delay, 0f, null);
            Enqueue(UttKind.BedStart, null, delay, 0f, null);
            Enqueue(UttKind.ControlFx, "beeps/quindar_in", 0f, 0f, null);
            Utterance u = Enqueue(UttKind.ControlLine, folder, 0.05f, 0f, null);
            if (u != null) u.entry = e;
            Enqueue(UttKind.ControlFx, "beeps/quindar_out", 0.03f, 0f, null);
            Enqueue(UttKind.BedStop, null, 0f, 0f, null);
            Enqueue(UttKind.ControlFx, "static/squelch", 0.02f, 0f, null);
        }

        /// <summary>
        /// A single MC line that fits the event: a one-line MC scene of that event, else a one-line calm MC chatter
        /// scene. Without the Lonapse table, a random control/calm clip (review F: no unrelated lines).
        /// </summary>
        private void EnqueueMcFor(string ev, float delay)
        {
            if (PhraseClips.Loaded)
            {
                Predicate<PhraseScene> single = x => x.Lines.Count == 1 && x.McLines == 1;
                PhraseScene sc = PickScene(ev, null, single) ?? PickScene("chatter", "calm", single);
                PhraseEntry e = FirstLine(sc);
                if (e != null) { EnqueueMc(e.Folder, delay, e); return; }
            }
            EnqueueMc("control/calm", delay);
        }

        /// <summary>Crew radio transmission: key up, line, key down. One speaker (req 7), keyed per profile (req 16).</summary>
        private void EnqueueCrew(string folder, float delay, float overlap, KerbalVoice sp = null, PhraseEntry e = null)
        {
            if (count + 4 > Capacity) return;
            bool keyed = overlap <= 0f;
            if (keyed) Enqueue(UttKind.RadioOpen, null, delay, 0f, null);
            Utterance u = Enqueue(UttKind.CrewLine, folder, keyed ? 0.02f : delay, overlap, sp);
            if (u != null) u.entry = e;
            if (keyed && host.settings.rogerBeep) Enqueue(UttKind.RogerBeep, null, 0.04f, 0f, null);
            if (keyed) Enqueue(UttKind.RadioClose, null, 0.02f, 0f, null);
        }

        // ---------------- scenes (req 20) ----------------

        /// <summary>Screams (scenes scream_*) are rare: at most one per kerbal per screamCooldown seconds, never without a speaker.</summary>
        private bool ScreamOk(PhraseScene x, KerbalVoice k)
        {
            if (!PacingRules.IsScream(x.Id)) return true;
            if (BadassNoScream(x, k)) return false; // plan v4 design 1: a badass never screams
            return k != null && cool.Ready("scream:" + k.CooldownKey, clock, Tu.screamCooldown);
        }

        private void StampScream(PhraseScene x, KerbalVoice k)
        {
            if (x != null && k != null && PacingRules.IsScream(x.Id))
            {
                cool.Stamp("scream:" + k.CooldownKey, clock);
                Log.Info("Scream from " + k.displayName);
            }
        }

        // ---- context (plan v3, req 3 and 5) ----

        /// <summary>Radio rule: FlightPhase.Matches. Serious phases get serious, that-phase and any rows, goofy phases
        /// goofy, that-phase and any rows, blackout only blackout and any rows. No phase yet: only any rows.</summary>
        private static bool RadioPhaseOk(PhraseScene sc, string phase)
        {
            return FlightPhase.Matches(sc.Phase, phase);
        }

        /// <summary>Intercom rule: always funny. Any and goofy rows, a goofy concrete phase row only in that phase,
        /// blackout rows only in blackout, never a serious row.</summary>
        private static bool IntercomPhaseOk(PhraseScene sc, string phase)
        {
            string row = sc.Phase;
            if (string.IsNullOrEmpty(row) || row == FlightPhase.Any || row == FlightPhase.Goofy) return true;
            if (row == FlightPhase.Blackout) return phase == FlightPhase.Blackout;
            return FlightPhase.IsGoofy(row) && row == phase;
        }

        /// <summary>Stress cause key for the phrase table: the largest comp*weight, "engine_out" while an engine is out.</summary>
        private string ComputeCause()
        {
            if (host.sample.engineOut) return "engine_out";
            StressModel st = host.stress;
            int best = -1;
            float bestV = CauseMin;
            for (int i = 0; i < StressModel.Count; i++)
            {
                float v = st.comp[i] * st.weight[i];
                if (v > bestV) { bestV = v; best = i; }
            }
            // rover and eva have no cause rows: they count as no cause (only cause-any rows)
            if (best < 0 || best == StressModel.Rover || best == StressModel.Eva) return NoCause;
            return StressModel.Names[best];
        }

        /// <summary>User test: a solar panel scene only on a ship with a solar panel.</summary>
        private bool SolarOk(PhraseScene sc) { return sc.Topic != PhraseTable.SolarTopic || host.sample.hasSolar; }

        private PhraseScene PickCore(string ev, string band, string cause, bool intercom, Predicate<PhraseScene> filter)
        {
            if (!PhraseClips.Loaded) return null;
            string phase = CurrentPhase;
            // phase is checked here (radio and intercom rules differ), cause by the table
            var list = PhraseClips.table.ScenesFor(ev, band, null, cause, sc => PhraseClips.Playable(sc)
                && (intercom ? IntercomPhaseOk(sc, phase) : RadioPhaseOk(sc, phase))
                && !sc.IsProbe && CrewCanVoice(sc) && !SomberBlocks(sc) && SolarOk(sc)
                && (filter == null || filter(sc)));
            PhraseScene p = PhraseTable.Pick(list, rng, lastSceneId);
            if (p != null) lastSceneId = p.Id;
            return p;
        }

        /// <summary>
        /// Radio scene of the event in the current phase. Cause-specific rows only for the current cause (no cause:
        /// only cause-any rows). In the tense and panic bands a scene of the current cause is preferred.
        /// </summary>
        private PhraseScene PickScene(string ev, string band, Predicate<PhraseScene> filter)
        {
            string cause = DominantCause ?? NoCause;
            if ((band == "tense" || band == "panic") && cause != NoCause)
            {
                PhraseScene sc = PickCore(ev, band, cause, false, x => x.Cause == cause && (filter == null || filter(x)));
                if (sc != null) return sc;
            }
            return PickCore(ev, band, cause, false, filter);
        }

        /// <summary>Intercom scene (crew-only, 2+ lines): funny rows only, see IntercomPhaseOk.</summary>
        private PhraseScene PickIntercomScene(string band, Predicate<PhraseScene> filter)
        {
            return PickCore("chatter", band, DominantCause ?? NoCause, true, filter);
        }

        private PhraseEntry FirstLine(PhraseScene sc)
        {
            if (sc == null) return null;
            var lines = PhraseClips.LinesFor(sc, Body);
            return lines.Count > 0 ? lines[0] : null;
        }

        /// <summary>Radio scene: every crew line from one speaker, MC lines with quindar. Returns false when nothing was queued.</summary>
        private bool EnqueueRadioScene(PhraseScene sc, KerbalVoice sp, float delay)
        {
            if (sc == null) return false;
            var lines = PhraseClips.LinesFor(sc, Body); // {body} variants (req 5)
            if (lines.Count == 0) return false;
            int need = 0;
            for (int i = 0; i < lines.Count; i++) need += lines[i].Role == "mc" ? 7 : 4;
            if (count + need > Capacity) return false;
            if (sc.CrewLines > 0 && (sp == null || !FitsSpeaker(sc, sp)))
            {
                // the crew speaker must have the scene's trait and persona (plan v4 design 3): nobody fits, no scene
                sp = PickFitting(sc);
                if (sp == null) { Log.Debug("Scene " + sc.Id + " skipped: nobody aboard fits trait " + sc.Trait + " / persona " + sc.Persona); return false; }
            }
            else if (sp == null) sp = host.roster.PickSpeaker(rng);
            if (PacingRules.IsScream(sc.Id) && !ScreamOk(sc, sp)) return false;
            StampScream(sc, sp);
            float s = host.stress.stress;
            for (int i = 0; i < lines.Count; i++)
            {
                PhraseEntry e = lines[i];
                float d = i == 0 ? delay : ResponseDelay(s);
                if (e.Role == "mc") EnqueueMc(e.Folder, d, e);
                else EnqueueCrew(e.Folder, d, 0f, sp, e);
            }
            Log.Debug("Scene " + sc.Id + " (" + sc.Event + ", " + sc.Band + ") queued" + (sp != null ? " for " + sp.displayName : ""));
            return true;
        }

        /// <summary>Crew speaker for a new exchange: prefers one whose speaker cooldown is over (req 11).</summary>
        private KerbalVoice PickRested()
        {
            float cd = Tu.speakerCooldown;
            KerbalVoice first = null;
            for (int i = 0; i < 4; i++)
            {
                KerbalVoice k = host.roster.PickSpeaker(rng);
                if (k == null) return null;
                if (first == null) first = k;
                if (cool.Ready(k.CooldownKey, clock, cd)) return k;
            }
            return first;
        }

        private bool TakeToken()
        {
            Tuning tu = Tu;
            if (bucket == null || bucketBurst != tu.talkBurst || bucketRate != tu.talkPerMinute)
            {
                bucket = new TokenBucket(tu.talkBurst, tu.talkPerMinute);
                bucketBurst = tu.talkBurst;
                bucketRate = tu.talkPerMinute;
            }
            if (bucket.TryTake(clock)) return true;
            bucketDenied++;
            return false;
        }

        /// <summary>Scheduled radio activity. Returns false when the rate limiter said no (try again later).</summary>
        private bool Fire(bool appendOverlap)
        {
            if (!TakeToken()) return false;
            if (host.probeMode) return FireProbe(appendOverlap); // plan v5: pet talk instead of crew exchanges
            float s = host.stress.stress;
            Tuning tu = Tu;

            if (host.evaMode)
            {
                // EVA (req 15): the kerbal talks with Mission Control only, quiet unless something is wrong
                if (s < tu.quietBand)
                {
                    if (rng.NextDouble() < 0.4) { if (host.settings.beepsEnabled) Enqueue(UttKind.PingPattern, null, 0f, 0f, null); }
                    else if (!EnqueueRadioScene(PickScene("eva_check", null, null), null, U(0f, 0.3f)))
                        EnqueueMcFor("eva_check", U(0f, 0.3f));
                    return true;
                }
            }
            else if (host.stationMode && PacingRules.StationChoice(host.settings.stationStyle, s, tu.burstBand) == StationAction.Check)
            {
                // stations and bases at low stress: only the MC check-in timer talks, here just a ping (req 9)
                if (host.settings.beepsEnabled && rng.NextDouble() < 0.5) Enqueue(UttKind.PingPattern, null, 0f, 0f, null);
                return true;
            }

            if (appendOverlap)
            {
                // burst at high stress: one extra crew line over the running exchange, no MC
                KerbalVoice extra = PickRested();
                Utterance u = Enqueue(UttKind.CrewLine, null, DcMath.Lerp(0.6f, 0.05f, s),
                                      rng.NextDouble() < tu.overlapMax * DcMath.Smooth(s, 0.6, 1.0) ? U(0.5f, 0.8f) : 0f, extra);
                if (u != null && PhraseClips.Loaded && extra != null)
                {
                    PhraseScene one = PickSceneFor("chatter", BandOf(extra, s), extra, sc => sc.Lines.Count == 1 && sc.CrewLines == 1
                                                                                   && sc.GenderOk(extra.isFemale) && ScreamOk(sc, extra));
                    PhraseEntry e1 = FirstLine(one);
                    if (e1 != null) { u.entry = e1; StampScream(one, extra); }
                }
                return true;
            }

            if (s < tu.quietBand && rng.NextDouble() < 0.3)
            {
                if (host.settings.beepsEnabled) Enqueue(UttKind.PingPattern, null, 0f, 0f, null);
                return true;
            }

            KerbalVoice sp = PickRested();
            if (sp == null)
            {
                EnqueueMcFor("chatter", U(0f, 0.3f));
                return true;
            }
            if (PhraseClips.Loaded)
            {
                string band = BandOf(sp, s);
                double mcP = McP(1.0);
                // plan v4: persona and trait scenes for this speaker are preferred (PickSceneFor), never a misfit
                PhraseScene sc = PickSceneFor("chatter", band, sp, x => !x.IsIntercom && x.GenderOk(sp.isFemale) && ScreamOk(x, sp)
                                                                 && (x.McLines == 0 || rng.NextDouble() < mcP));
                if (sc == null) sc = PickSceneFor("chatter", band, sp, x => !x.IsIntercom && ScreamOk(x, sp));
                if (EnqueueRadioScene(sc, sp, U(0f, 0.3f)))
                {
                    // a single crew line sometimes gets a short MC acknowledgement
                    if (sc.McLines == 0 && rng.NextDouble() < McP(0.5))
                    {
                        // in a panic MC answers with the slow, calm panic lines (the contrast the user asked for)
                        PhraseScene ack = PickScene("chatter", band, x => x.Lines.Count == 1 && x.McLines == 1)
                                          ?? PickScene("chatter", band == "panic" ? "tense" : band, x => x.Lines.Count == 1 && x.McLines == 1);
                        PhraseEntry ae = FirstLine(ack);
                        if (ae != null) EnqueueMc(ae.Folder, ResponseDelay(s), ae);
                    }
                    return true;
                }
            }
            FireFolders(s, sp);
            return true;
        }

        /// <summary>v1 exchange shapes on folder clips, used when the Lonapse table is missing. One crew speaker.</summary>
        private void FireFolders(float s, KerbalVoice sp)
        {
            Tuning tu = Tu;
            if (s < tu.quietBand)
            {
                if (rng.NextDouble() >= McP(0.5))
                {
                    EnqueueCrew("crew/calm", U(0f, 0.3f), 0f, sp);
                    if (rng.NextDouble() < McP(0.85)) EnqueueMc("control/calm", ResponseDelay(s));
                }
                else
                {
                    EnqueueMc("control/calm", U(0f, 0.3f));
                    if (rng.NextDouble() < 0.85) EnqueueCrew("crew/calm", ResponseDelay(s), 0f, sp);
                }
            }
            else if (s < tu.burstBand)
            {
                if (rng.NextDouble() >= McP(0.25))
                {
                    EnqueueCrew(null, U(0f, 0.3f), 0f, sp);
                    if (rng.NextDouble() < McP(0.8)) EnqueueMc(McFolder(s), ResponseDelay(s));
                }
                else
                {
                    EnqueueMc(McFolder(s), U(0f, 0.3f));
                    EnqueueCrew(null, ResponseDelay(s), 0f, sp);
                }
            }
            else
            {
                int n = 1 + (rng.NextDouble() < 0.5 ? 1 : 0);
                for (int i = 0; i < n; i++) EnqueueCrew(null, i == 0 ? U(0f, 0.2f) : DcMath.Lerp(0.6f, 0.05f, s), 0f, sp);
                if (rng.NextDouble() < McP(DcMath.Lerp(0.75f, 0.45f, s)))
                    EnqueueMc("control/calm", ResponseDelay(s) + U(0.4f, 1.2f));
            }
        }

        /// <summary>Settings window "Test exchange". Caller checks the gate as well.</summary>
        public void TriggerTestExchange()
        {
            if (!host.CanStartAudio || radioSilent) return;
            if (count + 10 > Capacity) return;
            holdoff = 0f;
            bucket = null; // a test always plays
            Fire(false);
            progress = 0f;
            target = U(0.6f, 1.4f);
            Log.Info("Test exchange queued at stress " + host.stress.stress.ToString("0.00"));
        }

        /// <summary>Scripted relief exchange after a reentry blackout.</summary>
        public void EnqueueRelief()
        {
            if (host.probeMode) return; // plan v5: no crew in probe mode
            // a blackout chat is not cut: the relief waits for its current line, the chat pauses (AirRules)
            ClearRadioQueue();
            Enqueue(UttKind.StaticBurst, null, 0f, 0f, null);
            PhraseScene sc = PickScene("relief", null, null);
            if (sc != null && sc.McLines == 0)
            {
                // relief rows are mostly crew-only: MC calls first, the crew answers relieved
                EnqueueMcFor("relief", 0.4f);
                EnqueueRadioScene(sc, null, U(0.3f, 0.6f));
                return;
            }
            if (EnqueueRadioScene(sc, null, 0.4f)) return;
            EnqueueMcFor("relief", 0.4f);
            int n = 2 + (rng.NextDouble() < 0.5 ? 1 : 0);
            for (int i = 0; i < n; i++)
                EnqueueCrew("crew/relief", U(0.1f, 0.4f), (i > 0 && rng.NextDouble() < 0.5) ? U(0.5f, 0.8f) : 0f);
            EnqueueMcFor("relief", ResponseDelay(0.2f));
        }

        /// <summary>After landing or splashdown: one crew/MC exchange (the end of the descent sequence, req 5).</summary>
        public void EnqueueTouchdown(float delay, bool splash)
        {
            // plan v6: the probe beeps about its landing, Mission Control answers
            if (host.probeMode)
            {
                // plan v6: the first landing on a body this session is a milestone, otherwise the normal touchdown beeps
                if (host.ProbeFirstLanding() && EnqueueProbeMilestone(ProbeMilestones.Landing, delay)) return;
                EnqueueProbeReaction(splash ? ProbeRules.Splashdown : ProbeRules.Touchdown, delay, false);
                return;
            }
            if (count + 7 > Capacity) return;
            if (EnqueueRadioScene(PickScene(splash ? "splashdown" : "touchdown", null, null), null, delay)) return;
            string f = (rng.NextDouble() < 0.6 && ClipLibrary.CountOf("crew/relief") > 0) ? "crew/relief" : "crew/calm";
            EnqueueCrew(f, delay, 0f);
            EnqueueMcFor("touchdown", ResponseDelay(0.2f));
        }

        /// <summary>EVA fall (user test): the EVA kerbal and Mission Control about a trip or a fall. False when nothing was queued.</summary>
        public bool EnqueueEvaFall()
        {
            if (!host.evaMode || !host.CanStartAudio || radioSilent || !host.settings.radioEnabled) return false;
            if (HasRadioItems() || Holding || airEventPrio > 0 || count + 10 > Capacity) return false;
            PhraseScene sc = PickScene("eva_fall", null, null);
            if (!EnqueueRadioScene(sc, null, U(0.4f, 0.9f))) return false;
            Log.Info("EVA fall: " + sc.Id);
            return true;
        }

        /// <summary>Docking approach: short crew call, Mission Control answers.</summary>
        public void EnqueueDockingApproach()
        {
            if (host.probeMode) return;
            if (!host.CanStartAudio || radioSilent || count + 10 > Capacity) return;
            if (!EnqueueRadioScene(PickScene("dock_approach", null, null), null, U(0f, 0.4f)))
            {
                EnqueueCrew("crew/tense", U(0f, 0.4f), 0f);
                EnqueueMcFor("dock_approach", ResponseDelay(0.3f));
            }
            Log.Debug("Docking approach exchange queued");
        }

        /// <summary>Close approach (within ~30 m and closing): crew calm or tense by closing speed, MC answers.</summary>
        public void EnqueueDockingClose(bool tense)
        {
            if (host.probeMode) return;
            if (!host.CanStartAudio || radioSilent || !host.settings.radioEnabled || HasRadioItems()) return;
            if (tense && EnqueueRadioScene(PickScene("dock_close", null, null), null, U(0f, 0.3f))) return;
            EnqueueCrew(tense ? "crew/tense" : "crew/calm", U(0f, 0.3f), 0f);
            EnqueueMcFor("dock_approach", ResponseDelay(0.2f));
        }

        /// <summary>Docked. A hard dock (req 14) is a warning: it cuts chatter and plays the dock_hard scene.</summary>
        public void EnqueueDocked(bool hard)
        {
            if (host.probeMode) { EnqueueProbeReaction(ProbeRules.Docked, hard ? 0.4f : 0.8f, hard); return; }
            if (!host.CanStartAudio || radioSilent) return;
            if (hard)
            {
                PreemptChatter();
                if (EnqueueRadioScene(PickScene("dock_hard", null, null), null, 0.4f)) { Log.Info("Hard dock exchange queued"); return; }
            }
            if (count + 10 > Capacity) return;
            if (!EnqueueRadioScene(PickScene("docked", null, null), null, 0.8f))
            {
                EnqueueCrew(ClipLibrary.CountOf("crew/relief") > 0 ? "crew/relief" : "crew/calm", 0.8f, 0f);
                EnqueueMcFor("docked", ResponseDelay(0.2f));
            }
            Log.Debug("Docked exchange queued");
        }

        /// <summary>Maneuver plan changed (req 18): a short pre-planning exchange, chatter priority.</summary>
        public bool EnqueuePlan()
        {
            if (host.probeMode)
            {
                // plan v6: MC proposes the burn, the probe judges it. Consumed when it cannot play (asleep, no link).
                if (!host.settings.probePlanTalk || !ProbeLink || ProbeAsleep) return true;
                if (!host.CanStartAudio || radioSilent || !host.settings.radioEnabled || HasRadioItems() || Holding) return false;
                EnqueueProbeReaction("plan_node", U(0.3f, 1.0f), false);
                return true;
            }
            if (!host.CanStartAudio || radioSilent || !host.settings.radioEnabled || HasRadioItems() || Holding) return false;
            PhraseScene sc = PickScene("plan_node", null, null);
            if (sc == null) { EnqueueMcFor("plan_node", U(0.3f, 1.0f)); EnqueueCrew("crew/calm", ResponseDelay(0.2f), 0f); return true; }
            if (!EnqueueRadioScene(sc, null, U(0.3f, 1.0f))) return false;
            Log.Info("Pre-planning exchange queued");
            return true;
        }

        /// <summary>A burn is near (req 18): warning priority, cuts chatter.</summary>
        public bool EnqueueBurnSoon()
        {
            // plan v6: Mission Control gets the probe ready for the burn (consumed either way)
            if (host.probeMode) { if (host.settings.probePlanTalk) EnqueueProbeReaction(ProbeRules.BurnSoon, 0.3f, true); return true; }
            if (!host.CanStartAudio || radioSilent || !host.settings.radioEnabled) return false;
            PhraseScene sc = PickScene("burn_soon", null, null);
            if (sc == null)
            {
                // no Lonapse table: a tense crew line and an MC answer
                if (count + 10 > Capacity) return false;
                PreemptChatter();
                EnqueueCrew("crew/tense", 0.3f, 0f);
                EnqueueMc("control/tense", ResponseDelay(0.4f));
                return true;
            }
            PreemptChatter();
            if (!EnqueueRadioScene(sc, null, 0.3f)) return false;
            Log.Info("Burn warning queued");
            return true;
        }

        // ---------------- priority (req 2) ----------------

        /// <summary>
        /// Warnings: fade the radio chatter that is playing and drop the queued radio items. Callouts are left alone.
        /// An intercom line is never cut: it plays to its end, the warning waits for it (AirRules.RadioMayStart) and
        /// the chat pauses (keepChat) or is dropped.
        /// </summary>
        private void PreemptChatter(bool keepChat = true)
        {
            for (int i = 0; i < host.crew.Length; i++) if (!host.crew[i].intercom) host.crew[i].FadeOutOnce(0.15f);
            // launch control's countdown keeps the air (liftoff call under the staging callout)
            string pk = host.control.phraseKey;
            if (pk == null || !pk.StartsWith("countdown", StringComparison.Ordinal)) host.control.FadeOutOnce(0.15f);
            host.controlBed.FadeOutOnce(0.2f);
            host.keyCh.FadeOutOnce(0.1f);
            if (keepChat) ClearRadioQueue();
            else ClearQueue();
            // an intercom line that is still playing stays the item the next one waits for
            bool chatPlaying = lastChannel != null && lastChannel.Busy && (lastChannel.intercom || ReferenceEquals(lastChannel, host.clickCh));
            if (!chatPlaying) lastChannel = null;
        }

        /// <summary>
        /// One callout from one kerbal (pilot preferred) right now, outside the queue: staging, parachutes, throttle,
        /// docking distances. Uses a Lonapse callout scene for <paramref name="ev"/> when there is one, else crew/callout,
        /// else <paramref name="fallbackFolder"/> cut to <paramref name="maxLen"/> s. Chatter that is playing fades out
        /// and the queue holds until the callout ended. Returns a reason string on failure, null on success.
        /// During an open intercom turn the callout waits in a single slot (AirRules.CalloutRank decides who keeps
        /// it) and null is returned as well. If it is dropped later, <paramref name="onDrop"/> gets the reason.
        /// </summary>
        public string PlayCallout(string ev, string fallbackFolder, float maxLen, string what, params Predicate<PhraseScene>[] prefer)
        {
            return PlayCallout(ev, fallbackFolder, maxLen, what, null, prefer);
        }

        public string PlayCallout(string ev, string fallbackFolder, float maxLen, string what, Action<string> onDrop,
                                  params Predicate<PhraseScene>[] prefer)
        {
            // plan v6: no crew in probe mode, the probe beeps its own callout (staging calls PlayProbeCallout itself)
            if (host.probeMode) return PlayProbeCallout(ev, null, what);
            if (!host.CanStartAudio) return "gate " + host.gateReason;
            if (radioSilent) return "radio blackout";
            if (!host.settings.radioEnabled) return "radio disabled";
            if (IntercomTurnOpen)
            {
                // never over or through an intercom line: the callout follows right after it, the chat pauses
                if (calloutPending)
                {
                    if (!AirRules.CalloutReplacesPending(pcEv, ev))
                        return "waiting slot taken by (" + pcWhat + ")";
                    DropPendingCallout("replaced by (" + what + ")");
                }
                calloutPending = true;
                pcEv = ev; pcFallback = fallbackFolder; pcMaxLen = maxLen; pcWhat = what; pcPrefer = prefer; pcSince = clock;
                pcOnDrop = onDrop;
                Log.Info("Callout (" + what + ") waits for the intercom line to end");
                return null;
            }
            return PlayCalloutNow(ev, fallbackFolder, maxLen, what, prefer);
        }

        /// <summary>A callout that waited for an intercom turn: plays when the turn is over, dropped after AirRules.CalloutMaxDeferFor s.</summary>
        private void UpdatePendingCallout()
        {
            if (!calloutPending) return;
            string why = null;
            if (!host.CanStartAudio) why = "gate " + host.gateReason;
            else if (radioSilent) why = "radio blackout";
            else if (!host.settings.radioEnabled) why = "radio disabled";
            else if (clock - pcSince > AirRules.CalloutMaxDeferFor(pcEv)) why = "waited too long for the intercom";
            else if (IntercomTurnOpen) return; // the line is still going
            else
            {
                why = PlayCalloutNow(pcEv, pcFallback, pcMaxLen, pcWhat, pcPrefer);
                if (why == null) { calloutPending = false; pcPrefer = null; pcOnDrop = null; return; }
            }
            DropPendingCallout(why);
        }

        /// <summary>Forget the waiting callout (if any), log it and tell its onDrop callback why.</summary>
        private void DropPendingCallout(string why)
        {
            if (!calloutPending) return;
            Action<string> cb = pcOnDrop;
            string what = pcWhat;
            calloutPending = false;
            pcPrefer = null;
            pcOnDrop = null;
            Log.Info("Deferred callout (" + what + ") not spoken: " + why);
            if (cb != null)
            {
                try { cb(why); }
                catch (Exception e) { Log.Exception("ChatterDirector.DropPendingCallout", e); }
            }
        }

        private string PlayCalloutNow(string ev, string fallbackFolder, float maxLen, string what, Predicate<PhraseScene>[] prefer)
        {
            KerbalVoice sp = KerbalVoice.PickCallout(host.roster.voices, rng);
            if (sp == null) return "no conscious crew";

            // prefer: filters tried in order (staging by stage contents), then any scene of the event
            PhraseScene sc = null;
            if (ev != null && prefer != null)
                for (int i = 0; i < prefer.Length && sc == null; i++)
                {
                    Predicate<PhraseScene> f = prefer[i];
                    sc = PickScene(ev, null, x => x.Lines.Count > 0 && x.Lines[0].Role == "crew" && FitsSpeaker(x, sp) && f(x));
                }
            if (sc == null && ev != null) sc = PickScene(ev, null, x => x.Lines.Count > 0 && x.Lines[0].Role == "crew" && FitsSpeaker(x, sp));
            AudioClip clip = null;
            string folder = null;
            var scLines = sc != null ? PhraseClips.LinesFor(sc, Body) : null; // {body} variants (req 5)
            if (scLines != null && scLines.Count > 0) clip = PhraseClips.Clip(scLines[0], sp.isFemale);
            if (clip == null)
            {
                sc = null;
                // without an event, crew/callout would give a staging or chute word: use the given folder
                folder = (ev == null && fallbackFolder != null) ? fallbackFolder : "crew/callout";
                if (ClipLibrary.CountOf(folder) == 0)
                {
                    if (fallbackFolder == null) return "no crew/callout clips";
                    folder = fallbackFolder;
                }
                clip = ClipLibrary.NextFor(folder, sp.isFemale, sp.rng);
            }
            if (clip == null) return "no clip in " + folder;

            VoiceChannel c = host.calloutCh;
            Tuning tu = Tu;
            float sk = sp.sk, gs = host.stress.gStrain;
            float pitch = DcMath.CrewPitch(sp.basePitch, sk, gs, tu.crewStressSemitones, tu.crewPitchMax);
            float vol = DcMath.Lerp(0.65f, 1.0f, sk);
            PreemptChatter();
            c.SetPreset(false, tu);
            c.Play(clip, vol, pitch, false, 0f);
            c.speaker = sp;
            c.boost = VoiceBoost * host.settings.calloutVolume; // req 1: own gain on top of the voice boost
            c.compressor = host.settings.voiceCompressor;
            c.phraseKey = sc != null ? scLines[0].ClipKey : null;
            c.tremRate = sp.tremRate;
            c.SetCrewFilters(sk, sp.timbre, gs, tu);
            ApplyProfile(c);
            if (maxLen > 0f && clip.length / pitch > maxLen) c.TruncateAt(maxLen * pitch);
            if (staticChop) c.TruncateAt(U(tu.chopMin, tu.chopMax) * pitch);
            // short key-up noise under the first syllable
            AudioClip key = ClipLibrary.CountOf(Profile.OpenClip) > 0 ? ClipLibrary.Next(Profile.OpenClip, rng) : null;
            if (key != null) host.keyCh.Play(key, tu.radioKeyVolume, 1f, false, 0f);
            holdUntil = clock + c.duration + tu.calloutHoldPad;
            StampLine(sp, false);
            if (sc != null)
            {
                ShowSubtitle(scLines[0], sp.displayName, c.duration);
                // the rest of the scene (usually the MC acknowledgement) follows after the hold
                for (int i = 1; i < scLines.Count; i++)
                {
                    PhraseEntry e = scLines[i];
                    if (e.Role == "mc") EnqueueMc(e.Folder, ResponseDelay(0.3f), e);
                    else EnqueueCrew(e.Folder, ResponseDelay(0.3f), 0f, sp, e);
                }
            }
            lastLine = "callout " + sp.displayName + ": " + (sc != null ? scLines[0].ClipKey : folder);
            Audit.Add(lastLine + " (" + what + ")");
            Log.Info("Callout (" + what + "): " + sp.displayName + (sp.isPilot ? " [pilot]" : "") + " " + (sc != null ? sc.Id : folder) + " " + c.clipName);
            return null;
        }

        /// <summary>Crew-to-crew intercom chat: a crew-only Lonapse scene (or 2 to 4 folder turns), soft clicks around it.</summary>
        /// <param name="blackoutFirst">true for the blackout timer: a blackout-phase scene first (req 6)</param>
        /// <param name="forced">a scene chosen by the caller (debug), played whatever the stress</param>
        private bool FireIntercom(bool blackoutFirst = false, PhraseScene forced = null)
        {
            KerbalVoice a, b;
            if (!PickChatPair(forced, out a, out b)) return false;
            float s = host.stress.stress;
            PhraseScene sc = forced;
            if (sc == null && PhraseClips.Loaded)
            {
                // speaker A starts the chat: persona and trait scenes for A are preferred, never a misfit (plan v4)
                if (blackoutFirst) sc = PickIntercomSceneFor(null, a, x => x.IsIntercom && x.Phase == FlightPhase.Blackout);
                if (sc == null) sc = PickIntercomSceneFor(KerbalVoice.BandNames[(int)(a.band == Band.Panic ? Band.Tense : a.band)], a, x => x.IsIntercom);
                if (sc == null) sc = PickIntercomSceneFor("calm", a, x => x.IsIntercom);
            }
            var lines = sc != null ? PhraseClips.LinesFor(sc, Body) : null; // {body} variants (req 5)
            if (lines != null && lines.Count == 0) { sc = null; lines = null; }
            int turns = lines != null ? lines.Count : 2 + rng.Next(3);
            // req 9: on stations and bases the intercom gets one extra turn
            PhraseEntry extraLine = null;
            if (host.stationMode && forced == null)
            {
                if (sc != null)
                {
                    KerbalVoice extraSp = (lines.Count % 2) == 1 ? b : a; // who speaks the extra turn
                    PhraseScene one = PickIntercomScene("calm", x => x.Lines.Count == 1 && x.CrewLines == 1 && FitsSpeaker(x, extraSp));
                    extraLine = FirstLine(one);
                    if (extraLine != null) turns++;
                }
                else turns++;
            }
            if (count + 2 * turns + 1 > Capacity) return false;
            chatA = a;
            chatB = b;
            chatForced = forced != null;
            chatStarted = false;
            lastChatClock = clock;
            chatLimit = IntercomAbortLimit;   // a blackout chat keeps the blackout limit after the blackout ends
            // keep the two voices audibly apart even when their base pitches are close
            float nudge = DcMath.PairPitchNudge(a.basePitch, b.basePitch, Tu.intercomMinPitchGap);
            for (int i = 0; i < turns; i++)
            {
                bool second = (i % 2) == 1;
                // push-to-talk click at the start of every line
                Enqueue(UttKind.IntercomClick, null, i == 0 ? U(0f, 0.3f) : U(Tu.intercomGapMin, Tu.intercomGapMax), 0f, null);
                Utterance u = Enqueue(UttKind.IntercomLine, null, 0.04f, 0f, second ? b : a);
                if (u == null) continue;
                if (second) u.pitchMul = nudge;
                if (lines != null) u.entry = i < lines.Count ? lines[i] : extraLine;
            }
            Enqueue(UttKind.IntercomClick, null, 0.1f, 0f, null); // release click after the last line
            Log.Debug("Intercom chat queued: " + a.displayName + " and " + b.displayName + ", " + turns + " turns" + (sc != null ? ", scene " + sc.Id : "") + ", s " + s.ToString("0.00"));
            return true;
        }

        // ---------------- per frame ----------------

        public void Update(float dt)
        {
            clock += dt;
            Tuning tu = Tu;
            float s = host.stress.stress;
            float rate = host.settings.rateMultiplier * (host.stationMode ? tu.stationRateFactor : 1f);
            currentInterval = DcMath.MeanInterval(s, tu.tQuiet, tu.tManic, rate);
            if (host.probeMode) currentInterval *= tu.probeRateFactor; // plan v5: fewer pet talk lines

            esc.Decay(dt, tu.panicEscTau);
            host.roster.panicOffset = esc.Offset(tu.panicEscStep, tu.panicEscCap);

            if (s < tu.quietCoastStress) quietTimer += dt; else quietTimer = 0f;

            // flight phase and stress cause (plan v3, req 3)
            if (host.sample.valid)
            {
                try { classifier.Update(host.sample.ToPhaseInput(), PhaseNow()); }
                catch (Exception e) { Log.Exception("FlightPhaseClassifier", e); }
            }
            host.sample.phase = CurrentPhase;
            DominantCause = ComputeCause();

            // who has the air (AirRules): the radio, or an intercom chat
            if (RadioAudible || Holding) lastRadioClock = clock;
            if (IntercomAudible) lastChatClock = clock;
            UpdatePendingCallout();
            UpdateEvents();
            ProcessQueue();

            bool blackoutPhase = InBlackoutPhase;
            UpdateBlackoutIntercom(dt, blackoutPhase, s);

            // intercom scheduler: only with >= 2 conscious crew and low stress. The intercom is not radio, so
            // static and blackout do not stop it (req 6). In blackout the blackout timer below takes over.
            // plan v4 design 10: no intercom banter while the crew mourns (Somber)
            intercomEligible = host.settings.intercomEnabled && !blackoutPhase && !host.evaMode && !Somber
                               && s < tu.intercomMaxStress && host.roster.SpeakableCount() >= 2;
            float lowStressInterval = DcMath.IntercomInterval(tu.intercomInterval, host.stationMode, QuietCoast,
                                                              tu.intercomStationBoost, tu.intercomQuietBoost);
            // more crew stress -> more intercom talk, until radio takes over above intercomMaxStress
            intercomInterval = DcMath.IntercomIntervalStress(lowStressInterval, s, tu.intercomStressMin, tu.intercomStressPeak);
            if (intercomEligible && holdoff <= 0f && !Holding)
            {
                intercomProgress += dt / Math.Max(1f, intercomInterval);
                if (intercomProgress >= intercomTarget)
                {
                    // never while the radio plays, is queued or pending, nor within IntercomStartGap s after it
                    if (count == 0 && Idle && IntercomMayStartNow)
                    {
                        FireIntercom();
                        intercomProgress = 0f;
                        intercomTarget = U(0.6f, 1.4f);
                        // give the chat room before the radio scheduler fires
                        if (progress > target * 0.8f) progress = target * 0.8f;
                    }
                    else intercomProgress = intercomTarget;
                }
            }

            bool radioOn = host.settings.radioEnabled;
            bool checkStyle = !host.probeMode && host.stationMode && PacingRules.StationChoice(host.settings.stationStyle, s, tu.burstBand) == StationAction.Check;
            if (radioOn && checkStyle && !radioSilent && holdoff <= 0f)
            {
                // stations and bases: Mission Control checks in now and then. Only this mechanism at low stress (req 9).
                stationMcLeft -= dt;
                if (stationMcLeft <= 0f && !HasRadioItems() && !Holding)
                {
                    if (!EnqueueRadioScene(PickScene("station_check", null, null), PickRested(), U(0f, 0.3f)))
                    {
                        EnqueueMcFor("station_check", U(0f, 0.3f));
                        EnqueueCrew("crew/calm", ResponseDelay(s), 0f);
                    }
                    stationMcLeft = U(tu.stationMcMin, tu.stationMcMax);
                    Log.Info("Station call from Mission Control queued, next in about " + stationMcLeft.ToString("0") + " s");
                }
            }
            if (host.stationMode && !wasStation) stationMcLeft = U(tu.stationMcMin, tu.stationMcMax) * 0.5f; // first check-in sooner
            wasStation = host.stationMode;

            if (!radioSilent)
            {
                if (holdoff > 0f) holdoff -= dt;
                else if (!radioOn) progress = 0f; // radio disabled: no crew <-> MC exchanges
                else if (!Holding)
                {
                    progress += dt / Math.Max(0.05f, currentInterval);
                    if (progress >= target)
                    {
                        // only a chat in the queue: the radio goes first, the chat pauses between turns
                        if (!HasRadioItems())
                        {
                            if (Fire(false))
                            {
                                progress = 0f;
                                target = U(0.6f, 1.4f);
                            }
                            else progress = target * 0.85f; // rate limited: try again a little later
                        }
                        else if (!host.probeMode && s >= tu.burstBand && count + 10 <= Capacity && rng.NextDouble() < tu.overlapMax * DcMath.Smooth(s, 0.6, 1.0))
                        {
                            if (Fire(true))
                            {
                                progress = 0f;
                                target = U(0.6f, 1.4f);
                            }
                            else progress = target * 0.85f;
                        }
                        else progress = target; // wait for the queue to drain
                    }
                }
            }

            UpdateProbe(dt);
            UpdateLiveFilters();
        }

        /// <summary>Game time for the phase hysteresis (the director clock when there is no Planetarium).</summary>
        private double PhaseNow()
        {
            try { return Planetarium.GetUniversalTime(); }
            catch (Exception) { return clock; }
        }

        /// <summary>
        /// Blackout intercom (req 6): blackoutIntercomFirst s after the blackout starts, then about every
        /// blackoutIntercomGap s (+-25 %), a crew-only chat that prefers blackout-phase scenes. Needs 2 speakers.
        /// </summary>
        private void UpdateBlackoutIntercom(float dt, bool blackoutPhase, float s)
        {
            Tuning tu = Tu;
            if (!blackoutPhase)
            {
                blackoutIntercomLeft = -1f;
                wasBlackoutPhase = false;
                return;
            }
            if (!wasBlackoutPhase)
            {
                wasBlackoutPhase = true;
                blackoutIntercomLeft = tu.blackoutIntercomFirst;
                Log.Info("Blackout intercom: first chat in " + blackoutIntercomLeft.ToString("0.0") + " s");
            }
            blackoutIntercomLeft -= dt;
            if (blackoutIntercomLeft > 0f) return;

            if (!host.settings.intercomEnabled || host.evaMode || Somber || host.roster.SpeakableCount() < 2)
            {
                // one kerbal (or intercom off): nobody to talk to, check again after a gap
                blackoutIntercomLeft = NextBlackoutGap();
                return;
            }
            if (s > tu.blackoutIntercomMaxStress) { blackoutIntercomLeft = 2f; return; } // too busy, try again soon
            if (count > 0 || !Idle || !IntercomMayStartNow) { blackoutIntercomLeft = 0f; return; } // wait for the air to clear

            if (FireIntercom(true))
            {
                blackoutIntercomLeft = NextBlackoutGap();
                intercomProgress = 0f;
                Log.Info("Blackout intercom chat queued, next in about " + blackoutIntercomLeft.ToString("0") + " s");
            }
            else blackoutIntercomLeft = 2f;
        }

        private float NextBlackoutGap()
        {
            return Math.Max(1f, Tu.blackoutIntercomGap * U(0.75f, 1.25f));
        }

        /// <summary>
        /// Debug tab: play a random playable scene that matches the filters now. Empty or null = any.
        /// Ignores the schedulers, rate limits and stress gates, but never plays with the gate closed
        /// (disabled, muted, no crew, warp). Returns a status line.
        /// </summary>
        public string DebugPlayRandom(string evt, string phase, string cause, string body, string topic = null)
        {
            string want = (string.IsNullOrEmpty(evt) ? "any" : evt) + "/" + (string.IsNullOrEmpty(phase) ? "any" : phase) + "/"
                          + (string.IsNullOrEmpty(cause) ? "any" : cause) + "/" + (string.IsNullOrEmpty(body) ? "current" : body)
                          + (string.IsNullOrEmpty(topic) ? "" : "/topic " + topic);
            if (!host.CanStartAudio) return "not played: gate " + host.gateReason;
            if (!PhraseClips.Loaded) return "not played: Lonapse table not loaded (" + PhraseClips.status + ")";
            if (host.probeMode) return DebugPlayProbe(evt, phase, topic); // plan v5: probe scenes only

            var all = new System.Collections.Generic.List<PhraseScene>();
            var exact = new System.Collections.Generic.List<PhraseScene>();
            foreach (PhraseScene sc in PhraseClips.table.Scenes)
            {
                if (!string.IsNullOrEmpty(evt) && sc.Event != evt) continue;
                if (sc.IsProbe) continue; // crew mode never plays a probe scene
                if (!string.IsNullOrEmpty(phase) && !PhraseTable.PhaseMatches(sc.Phase, phase)) continue;
                // a concrete test phase must not pull in blackout rows (PhaseMatches already handles that)
                if (!string.IsNullOrEmpty(cause) && !PhraseTable.CauseMatches(sc.Cause, cause)) continue;
                // v4 topic (science experiment, home for soi_enter): topic-any rows fit too, like the events do
                if (!string.IsNullOrEmpty(topic) && !PhraseTable.TopicMatches(sc.Topic, topic)) continue;
                if (!PhraseClips.Playable(sc)) continue;
                all.Add(sc);
                // preferred: the exact cause, the exact topic, and a {body} scene when a body was chosen
                bool causeExact = string.IsNullOrEmpty(cause) || sc.Cause == cause;
                bool topicExact = string.IsNullOrEmpty(topic) || sc.Topic == topic;
                bool bodyExact = string.IsNullOrEmpty(body) || sc.HasBody;
                if (causeExact && topicExact && bodyExact) exact.Add(sc);
            }
            PhraseScene pick = PhraseTable.Pick(exact.Count > 0 ? exact : all, rng, lastSceneId);
            if (pick == null) return "no scene matches " + want;
            lastSceneId = pick.Id;

            string bodyKey = string.IsNullOrEmpty(body) ? Body : FlightPhase.BodyKey(body);
            string tag = pick.Id + " (" + pick.Event + "/" + pick.Phase + "/" + pick.Cause + "/" + bodyKey
                         + (string.IsNullOrEmpty(pick.Topic) || pick.Topic == PhraseTable.Any ? "" : "/" + pick.Topic) + ")";

            if (pick.IsIntercom)
            {
                if (!host.settings.intercomEnabled) return "not played: intercom disabled, " + tag;
                if (host.roster.SpeakableCount() < 2) return "not played: intercom needs 2 conscious crew, " + tag;
                // radio chatter fades, the chat starts IntercomStartGap s after the radio is quiet
                PreemptChatter(false);
                holdUntil = -1f;
                string saved = bodyOverride;
                bodyOverride = bodyKey;
                bool ok;
                try { ok = FireIntercom(false, pick); }
                finally { bodyOverride = saved; }
                if (!ok) return "not played: queue full, " + tag;
            }
            else
            {
                if (!host.settings.radioEnabled) return "not played: radio disabled, " + tag;
                if (radioSilent) return "not played: radio blackout, " + tag;
                // a playing intercom line ends first, the scene follows (AirRules.RadioMayStart)
                PreemptChatter(false);
                holdUntil = -1f;
                string saved = bodyOverride;
                bodyOverride = bodyKey;
                bool ok;
                // EnqueueRadioScene re-picks the speaker when the rested one lacks the scene's trait or persona
                try { ok = EnqueueRadioScene(pick, PickRested(), 0.2f); }
                finally { bodyOverride = saved; }
                if (!ok) return "not played: scream cooldown, queue full or nobody with trait " + pick.Trait + " / persona " + pick.Persona + ", " + tag;
                if (pick.Event == EventWatchers.CrewLost) StartSomber("debug " + pick.Id);
            }
            Log.Info("Debug play " + tag + " for " + want);
            return "played " + tag;
        }

        /// <summary>Continuous response of filters on lines that are already playing.</summary>
        public void UpdateLiveFilters()
        {
            Tuning tu = Tu;
            float gs = host.stress.gStrain;
            float extra = staticChop ? tu.staticExtraDistortion : 0f;
            for (int i = 0; i < host.crew.Length; i++)
            {
                VoiceChannel c = host.crew[i];
                if (!c.Busy || c.speaker == null) continue;
                KerbalVoice sp = c.speaker;
                if (c.intercom)
                {
                    c.SetIntercomFilters(sp.timbre, gs, tu);
                    c.tremRate = sp.tremRate;
                    continue;
                }
                c.SetCrewFilters(sp.sk, sp.timbre, gs, tu);
                ApplyProfile(c);
                c.extraDistortion = extra;
                bool gDominant = 0.40f * gs >= 0.10f * DcMath.Smooth(sp.sk, 0.8, 1.0);
                c.tremRate = gDominant ? sp.tremRate : 9f;
            }
            if (host.control.Busy) host.control.extraDistortion = extra;
        }
    }
}
