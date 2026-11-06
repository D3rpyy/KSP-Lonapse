// Scheduler, exchanges (call and response), utterance queue and overlap.
// Lonapse scenes, priority (callout > warning > chatter) with preempt and hold, one crew speaker per
// Mission Control exchange, pacing (token bucket, cooldowns, panic gap, escalation), the station rule,
// EVA exchanges, radio keying per profile and subtitles.
using System;
using UnityEngine;

namespace Lonapse
{
    public enum UttKind { CrewLine, ControlLine, ControlFx, BedStart, BedStop, StaticBurst, PingPattern, IntercomLine, IntercomClick, RadioOpen, RadioClose, RogerBeep,
        // probe mode: probe beep on beep[1] (radio)
        ProbeBeep,
        // A probe core on a crewed vessel beeps in the cabin (beep[1], intercom sound), part of a chat
        CabinBeep,
        // a line shown as a subtitle only, no voice (a mod event with the runtime voice off). speaker null: Mission Control
        SubtitleLine }

    public class Utterance
    {
        public UttKind who;
        public KerbalVoice speaker;   // null = pick at start time
        public string folder;         // null for crew = folder from the speaker's band at start time
        public float startDelay;      // seconds after the previous item ended
        public float overlapAt;       // > 0: may start at this fraction of the previous crew line
        public float pitchMul = 1f;   // intercom pair pitch nudge
        public PhraseEntry entry;     // Lonapse row, null for folder clips
        public ChatterDirector.RuntimeLine rt; // experimental: the same MC line with a crew name, rendered at runtime

        public void Set(UttKind k, string f, float delay, float overlap, KerbalVoice sp)
        {
            who = k; folder = f; startDelay = delay; overlapAt = overlap; speaker = sp; pitchMul = 1f; entry = null; rt = null;
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
        public bool radioSilent;   // blackout without crew (probe mode): nothing starts
        public bool mcBlackout;    // blackout with crew: the crew keeps talking, Mission Control cannot hear or answer
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

        // pacing
        public float holdUntil = -1f;      // chatter queue waits until this clock (callout hold)
        private TokenBucket bucket;
        private float bucketBurst = -1f, bucketRate = -1f;
        private readonly Cooldowns cool = new Cooldowns();
        public readonly Escalation esc = new Escalation();
        private float lastPanic = -1e9f;
        private string lastSceneId;
        /// <summary>Line memory (shuffle bag), shared by every flight scene and kept between sessions.</summary>
        private static SceneMemory Mem { get { return LonapseAddon.LineMemory; } }
        // director clock when a cause-specific scene of each cause last played (Tuning.causeRepeatGap)
        private readonly System.Collections.Generic.Dictionary<string, float> causeSaid = new System.Collections.Generic.Dictionary<string, float>();
        // director clock when each tense or panic radio scene was last picked (Tuning.causeRepeatGap, StressRepeatFilter)
        private readonly System.Collections.Generic.Dictionary<string, float> stressSaid = new System.Collections.Generic.Dictionary<string, float>();
        // director clock when each calm chatter scene with 2+ lines was last picked (Tuning.calmRepeatGap, CalmRepeatFilter)
        private readonly System.Collections.Generic.Dictionary<string, float> calmSaid = new System.Collections.Generic.Dictionary<string, float>();
        public string lastLine = "";       // audit text for the overlay
        public int panicDowngrades, bucketDenied;

        // flight phase and context
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

        // air arbiter (the intercom played over the radio), rules and gaps in AirRules
        private float lastRadioClock = -1e9f;  // last frame the radio had the air (audible, holding)
        private float lastChatClock = -1e9f;   // last frame the queued chat was audible or started an item
        private bool chatStarted;              // the queued chat had its first turn: resume gap instead of start gap
        private float intercomWaitStart = -1f; // clock when the intercom scheduler became due, -1 when it is not waiting
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

        /// <summary>
        /// The lines to play for a scene: the {body} variants for Body. Use this instead of
        /// PhraseClips.LinesFor(sc, Body) when the lines are really played.
        /// </summary>
        private System.Collections.Generic.List<PhraseEntry> PlayLines(PhraseScene sc)
        {
            return PlayLines(sc, null);
        }

        /// <summary>PlayLines, and the subtitle slots of the lines filled in (experimental, procedural talk): pool
        /// values from the line memory, flight values, and <paramref name="perLine"/> (may be null) for {other} and
        /// {self} per line index.</summary>
        private System.Collections.Generic.List<PhraseEntry> PlayLines(PhraseScene sc,
            Func<int, System.Collections.Generic.IDictionary<string, string>> perLine)
        {
            var lines = PlayLinesRaw(sc);
            bool any = false;
            for (int i = 0; i < lines.Count && !any; i++) any = lines[i] != null && SubtitleSlots.NeedsFill(lines[i].English);
            if (!any) return lines;
            var unresolved = new System.Collections.Generic.List<string>();
            bool named;
            var filled = SubtitleSlots.Fill(lines, PhraseClips.pools, SlotFlightValues(), perLine,
                                            (pool, vals) => Mem.TakeValue(pool, vals, rng), unresolved, SlotConditions(),
                                            RuntimeOn, NamesRecent, out named);
            if (named) NameSaid();
            foreach (string u in unresolved) Log.Warn("Lonapse slot not filled: " + u);
            for (int i = 0; i < filled.Count; i++)
                if (!ReferenceEquals(filled[i], lines[i])) Log.Info("Subtitle " + filled[i].ClipKey + ": " + filled[i].English);
            return filled;
        }

        /// <summary>The conditions for [ ] versions and pool values now: phase, kerbin or away, and the vessel parts.</summary>
        private System.Collections.Generic.List<string> SlotConditions()
        {
            string phase = !string.IsNullOrEmpty(debugSlotPhase) ? debugSlotPhase : CurrentPhase;
            var c = SubtitleSlots.ConditionsFor(phase, Body);
            c.AddRange(VesselConditions());
            return c;
        }

        private System.Collections.Generic.List<string> vesselConds;
        private float vesselCondsAt = -100f;
        private Guid vesselCondsId;

        /// <summary>Parts of the active vessel as slot conditions (solar, antenna, legs, ...), so a subtitle only
        /// names a part that is there. Worked out at most every 5 s per vessel.</summary>
        private System.Collections.Generic.List<string> VesselConditions()
        {
            Vessel v = null;
            try { v = FlightGlobals.ActiveVessel; } catch (Exception) { }
            if (vesselConds != null && v != null && v.id == vesselCondsId && clock - vesselCondsAt < 5f) return vesselConds;
            var r = new System.Collections.Generic.List<string>();
            try
            {
                if (v != null)
                    foreach (Part p in v.parts)
                    {
                        if (p == null) continue;
                        foreach (PartModule m in p.Modules)
                        {
                            string tag = null;
                            if (m is ModuleDeployableSolarPanel) tag = "solar";
                            else if (m is ModuleDataTransmitter) { if (((ModuleDataTransmitter)m).antennaType != AntennaType.INTERNAL) tag = "antenna"; }
                            else if (m is ModuleWheelBase) { if (((ModuleWheelBase)m).wheelType == WheelType.LEG) tag = "legs"; }
                            else if (m is RetractableLadder) tag = "ladder";
                            else if (m is ModuleDockingNode) tag = "docking";
                            else if (m is ModuleAblator) tag = "heatshield";
                            else if (m is ModuleParachute) tag = "chute";
                            else if (m is ModuleEngines) tag = "engine";
                            else if (m is CompoundParts.CModuleStrut) tag = "strut";
                            else if (m is ModuleCommand) { if (((ModuleCommand)m).minimumCrew == 0) tag = "probe"; }
                            else if (m is ModuleScienceExperiment) tag = SubtitleSlots.ExperimentCondition(((ModuleScienceExperiment)m).experimentID);
                            if (tag != null && !r.Contains(tag)) r.Add(tag);
                        }
                    }
            }
            catch (Exception e) { Log.Exception("Slot vessel conditions", e); }
            vesselConds = r;
            vesselCondsAt = clock;
            vesselCondsId = v != null ? v.id : Guid.Empty;
            return r;
        }

        /// <summary>A scene with slots or versions can be played only when every line can be filled now
        /// (SubtitleSlots.CanFill): no subtitle about a solar panel on a ship without one. Intercom scenes get
        /// names for {other} and {self}, other scenes none.</summary>
        private bool SlotsOk(PhraseScene sc)
        {
            if (sc == null || !sc.HasSlots) return true;
            System.Collections.Generic.IDictionary<string, string> names = new System.Collections.Generic.Dictionary<string, string> {
                { SubtitleSlots.Self, "a" }, { SubtitleSlots.Other, "b" } };
            Func<int, System.Collections.Generic.IDictionary<string, string>> per = null;
            if (sc.IsIntercom) per = i => names;
            // flight values and conditions once per frame: a pick checks hundreds of scenes
            if (slotCacheClock != clock || slotCacheValues == null || slotCacheDebugPhase != debugSlotPhase || slotCacheBody != Body)
            {
                slotCacheBody = Body;
                slotCacheValues = SlotFlightValues();
                slotCacheConds = SlotConditions();
                slotCacheClock = clock;
                slotCacheDebugPhase = debugSlotPhase;
            }
            return SubtitleSlots.CanFill(PhraseClips.LinesFor(sc, Body), PhraseClips.pools, slotCacheValues, per, slotCacheConds);
        }
        private System.Collections.Generic.Dictionary<string, string> slotCacheValues;
        private System.Collections.Generic.List<string> slotCacheConds;
        private float slotCacheClock = -1f;
        private string slotCacheDebugPhase, slotCacheBody;

        /// <summary>The flight values for the subtitle slots, and with the runtime voice on also every number of
        /// RuntimeNumbers ({fuel}, {ec} ...), so a recorded line can have a version that says them.</summary>
        private System.Collections.Generic.Dictionary<string, string> SlotFlightValues()
        {
            var d = BaseFlightValues();
            var nums = RuntimeNumbers();
            if (nums != null)
                foreach (var kv in nums)
                    if (!d.ContainsKey(kv.Key)) d[kv.Key] = kv.Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
            return d;
        }

        /// <summary>day, here, vessel and alt_km: there on every flight.</summary>
        private System.Collections.Generic.Dictionary<string, string> BaseFlightValues()
        {
            string here = null, vessel = null;
            double alt = double.NaN;
            bool kerbinDays = true;
            try
            {
                Vessel v = FlightGlobals.ActiveVessel;
                if (v != null)
                {
                    // on EVA {vessel} is the ship nearby, not the kerbal ("Stay close to" the own name)
                    double sd;
                    Vessel named = v.isEVA ? ShipNear(v, out sd) : v;
                    vessel = named != null ? Names.Of(named) : null;   // stock craft are named #autoLOC_...
                    alt = v.altitude;
                    if (v.mainBody != null) here = v.mainBody.bodyName;
                }
                kerbinDays = GameSettings.KERBIN_TIME;
            }
            catch (Exception) { }
            if (!string.IsNullOrEmpty(bodyOverride)) here = char.ToUpperInvariant(bodyOverride[0]) + bodyOverride.Substring(1);
            if (here == "Mun") here = "the Mun";   // as KSP writes it, a capital at a sentence start
            return SubtitleSlots.FlightValues(host.missionTime, kerbinDays, here, vessel, alt);
        }

        private System.Collections.Generic.List<PhraseEntry> PlayLinesRaw(PhraseScene sc)
        {
            return PhraseClips.LinesFor(sc, Body);
        }
        /// <summary>Debug tab (experimental, procedural talk): the phrase test only picks scenes with subtitle slots.</summary>
        public bool debugSlotsOnly;
        /// <summary>Debug tab: the test phase while a test scene is filled, so [landed] versions can be tried in orbit.</summary>
        private string debugSlotPhase;

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
                if (cabinBeepCh != null)
                {
                    if (cabinBeepCh.Busy) return true;
                    cabinBeepCh = null;   // done: a later stage tone on the same channel is not intercom
                }
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
            for (int i = 1; i < count; i++)
            {
                UttKind k = queue[(head + i) % Capacity].who;
                if (k == UttKind.IntercomLine || k == UttKind.CabinBeep) return true;
            }
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
            return k == UttKind.IntercomLine || k == UttKind.CabinBeep || (k == UttKind.IntercomClick && !LineFollowsHead());
        }

        /// <summary>
        /// A new intercom chat may be queued now (AirRules.IntercomMayStart): the radio is quiet, nothing radio is
        /// queued or pending, and the radio scheduler does not fire soon (a started chat would make it wait).
        /// </summary>
        private bool IntercomMayStartNow
        {
            get
            {
                float due = radioSilent || !host.settings.radioEnabled ? -1f : TimeToNext;
                float waited = intercomWaitStart < 0f ? 0f : clock - intercomWaitStart;
                return AirRules.IntercomMayStart(RadioAudible, HasRadioItems(), Holding || calloutPending, clock - lastRadioClock,
                                                 due, waited);
            }
        }

        /// <summary>A started intercom chat holds the air until its last item (AirRules.ChatHoldsAir): the radio queues behind it.</summary>
        public bool ChatHoldsAir { get { return AirRules.ChatHoldsAir(chatStarted, HasChatItems(), IntercomAudible); } }

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
            mcBlackout = false;
            staticChop = false;
            intercomProgress = 0f;
            intercomWaitStart = -1f;
            intercomTarget = U(0.6f, 1.4f);
            holdUntil = -1f;
            chatStarted = false;
            DropPendingCallout("reset");
            lastRadioClock = lastChatClock = -1e9f;
            ResetFinalCall();
        }

        /// <summary>Vessel switch: cooldowns, the token bucket and the escalation start fresh.
        /// <paramref name="evaHop"/>: a kerbal goes out on EVA or comes back, a waiting science line stays.</summary>
        public void ResetPacing(bool evaHop = false)
        {
            cool.Clear();
            bucket = null;
            lastPanic = -1e9f;
            esc.Decay(1f, 0f);
            lastSceneId = null;
            causeSaid.Clear();
            stressSaid.Clear();
            calmSaid.Clear();
            classifier.Reset(); // new vessel: its own phase, no hold from the old one
            ResetEventsForVessel(evaHop);
            wasProbeMode = false; // the probe timers start fresh on the next probe-mode frame
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
            if (host.crewBed != null) host.crewBed.FadeOutOnce(0.25f);
            DropPendingCallout("queue cleared"); // the countdown (or a debug play) takes the air
        }

        /// <summary>Keeps the queued items that match, in order, and drops the rest. Returns the number kept.</summary>
        /// <summary>A one-line MC scene that can acknowledge a crew line. Never a question: nobody would answer it.</summary>
        internal static bool IsMcAck(PhraseScene x)
        {
            if (x.Lines.Count != 1 || x.McLines != 1) return false;
            string en = x.Lines[0].English ?? "";
            return !en.TrimEnd().EndsWith("?");
        }

        private int KeepOnly(Predicate<Utterance> keep)
        {
            int kept = AirRules.StablePartitionRing(queue, head, count, keep, scratch);
            for (int i = kept; i < count; i++)
            {
                Utterance u = queue[(head + i) % Capacity];
                if (IsReplyLine(u.entry)) Log.Info("Runtime voice: " + u.entry.ClipKey + " dropped with the radio queue");
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
        /// not use the radio, so a chat keeps going. Warnings: the chat pauses, the radio chatter goes.
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

        /// <summary>Mission Control items: its lines, quindar and squelch, its static bed and static bursts.</summary>
        private static bool IsMcItem(UttKind k)
        {
            return k == UttKind.ControlLine || k == UttKind.ControlFx || k == UttKind.BedStart || k == UttKind.StaticBurst;
        }

        private bool HasMcItems()
        {
            for (int i = 0; i < count; i++) if (IsMcItem(queue[(head + i) % Capacity].who)) return true;
            return false;
        }

        /// <summary>A queued crew line calls Control.</summary>
        private bool HasControlCalls()
        {
            for (int i = 0; i < count; i++)
            {
                Utterance u = queue[(head + i) % Capacity];
                if (u.who == UttKind.CrewLine && AddressesControl(u.entry)) return true;
            }
            return false;
        }

        /// <summary>Blackout with crew: drop the Mission Control items, everything else stays in order.</summary>
        public void DropMcItems()
        {
            int before = count;
            // also the crew lines that call Control: nobody would answer them
            KeepOnly(x => !IsMcItem(x.who) && !(x.who == UttKind.CrewLine && AddressesControl(x.entry)));
            if (before != count) Log.Debug("Blackout: " + (before - count) + " Mission Control item(s) dropped");
        }

        /// <summary>Blackout with crew: only radio scenes without Mission Control, and nobody calls Control (user
        /// report: the crew kept trying to reach Control). The crew knows the radio is out.</summary>
        /// <summary>Mission Control cannot hear the crew: a blackout with crew, or no link (after
        /// the link was lost the crew kept calling Control, and its answers were skipped without a word).</summary>
        private bool McUnreachable { get { return mcBlackout || (!host.probeMode && !host.signal.connected); } }

        private bool BlackoutOk(PhraseScene sc)
        {
            if (!McUnreachable) return true;
            if (sc.Lines.Count == 0 || sc.McLines > 0) return false;
            for (int i = 0; i < sc.Lines.Count; i++) if (AddressesControl(sc.Lines[i])) return false;
            return true;
        }

        /// <summary>A line that talks to Mission Control: "Control" with a capital in any English version.</summary>
        internal static bool AddressesControl(PhraseEntry e)
        {
            return e != null && e.English != null && System.Text.RegularExpressions.Regex.IsMatch(e.English, @"\bControl\b");
        }

        private bool HasChatItems()
        {
            for (int i = 0; i < count; i++) if (IsChatItem(queue[(head + i) % Capacity].who)) return true;
            return false;
        }

        // ---------------- queue ----------------

        private bool PrevDone()
        {
            if (clock < subtitleHoldUntil) return false;   // a subtitle-only line holds the air for its reading time
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
                   || k == UttKind.RogerBeep || k == UttKind.ProbeBeep || k == UttKind.SubtitleLine;
        }

        /// <summary>A crew reply to a Mission Control runtime line (rt_re_...), logged when it plays or is skipped.</summary>
        private static bool IsReplyLine(PhraseEntry e)
        {
            return e != null && e.ClipKey != null && e.ClipKey.StartsWith("rt_re_", StringComparison.Ordinal);
        }

        /// <summary>Voice pitch in a time warp (the kerbals and Mission Control sound sped up), else 1.</summary>
        private float WarpPitch
        {
            get { return host.warpChatter ? DcMath.WarpFactor(host.warpRate, Tu.warpPitchGain, Tu.warpPitchMax) : 1f; }
        }

        /// <summary>Until this clock a subtitle-only line holds the air (PrevDone).</summary>
        private float subtitleHoldUntil = -1f;

        /// <summary>Reading time of a subtitle-only line: 0.065 s per character, at least 2 s.</summary>
        internal static float SubtitleReadSeconds(string text)
        {
            return Math.Max(2f, 0.065f * (text ?? "").Length);
        }

        private bool CanChat(KerbalVoice k)
        {
            return k != null && !k.outDueToG && host.roster.InGroup(k);
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
            if (Holding) return; // a callout has the air
            int guard = 0;
            while (count > 0 && guard++ < 8)
            {
                // blackout: radio items go, intercom chat items stay
                if (radioSilent && HasRadioItems()) { ClearRadioQueue(); if (count == 0) return; }
                // blackout with crew: only the Mission Control part goes, the crew keeps keying the radio
                if (McUnreachable && (HasMcItems() || HasControlCalls())) { DropMcItems(); if (count == 0) return; }
                // a started chat holds the air to its end (radio cut into the chat): radio queued
                // behind it waits. A chat that has not started yet still lets the radio go first.
                bool chatHolds = ChatHoldsAir;
                if (chatHolds)
                {
                    if (!IsChatItem(queue[head].who) && HasChatItems()) AirRules.StablePartitionRing(queue, head, count, ChatFirst, scratch);
                }
                else if (AirRules.ChatShouldYield(HeadOpensTurn(), HasRadioItems()))
                {
                    AirRules.StablePartitionRing(queue, head, count, RadioFirst, scratch);
                    Log.Debug("Intercom chat waits for the radio");
                }
                Utterance u = queue[head];
                if (IsChatItem(u.who))
                {
                    // a new turn needs a quiet radio (start or resume gap). The line and release click of an open turn go on.
                    // Radio queued behind a started chat does not stop the next turn: it waits for the chat.
                    if (HeadOpensTurn() && !AirRules.ChatTurnMayStart(chatStarted, RadioAudible, !chatHolds && HasRadioItems(),
                                                                      calloutPending, clock - lastRadioClock)) return;
                }
                else if (!AirRules.RadioMayStart(IntercomTurnOpen, chatHolds)) return; // radio waits for the whole chat
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

        /// <summary>Radio profile on a crew radio line: higher high-pass and a low-pass ceiling.</summary>
        private void ApplyProfile(VoiceChannel c)
        {
            RadioProfile p = Profile;
            c.CapLowPass(p.LowPassHz);
        }

        /// <summary>Panic lines obey the global gap and the per-speaker cooldown, otherwise they drop to tense.</summary>
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
            if (WarpOver(u.entry))
            {
                // a line about the warp, and the warp is over
                Log.Info("Runtime voice: " + u.entry.ClipKey + " skipped, the time warp ended");
                if (IsIntercomItem(u.who)) abortChat = true;
                return StartResult.Skip;
            }

            switch (u.who)
            {
                case UttKind.CrewLine:
                {
                    bool reply = IsReplyLine(u.entry);
                    if (!host.roster.AnyoneCanSpeak()) { if (reply) Log.Info("Runtime voice: " + u.entry.ClipKey + " skipped, nobody can speak"); return StartResult.Skip; }
                    if (CrewPlaying() >= CrewCap()) return StartResult.Wait;
                    VoiceChannel c = FreeCrewChannel();
                    if (c == null) return StartResult.Wait;
                    KerbalVoice sp = u.speaker;
                    // a line queued for one kerbal never moves to another: after a switch the subtitle, the name
                    // slots and the scene all belong to that kerbal, so skip it when they left the roster
                    if (sp != null && !host.roster.InGroup(sp))
                    {
                        Log.Info("Runtime voice: " + (u.entry != null ? u.entry.ClipKey : "crew line") + " skipped, " + sp.displayName + " is no longer nearby");
                        return StartResult.Skip;
                    }
                    // a kerbal of another vessel is never replaced by one of the cabin
                    if (sp != null && sp.outDueToG && host.roster.radio.Contains(sp)) return StartResult.Skip;
                    if (sp == null || sp.outDueToG) sp = host.roster.PickSpeaker(rng);
                    if (sp == null) return StartResult.Skip;
                    AudioClip rtClip;
                    PhraseEntry rtEntry;
                    int turn = RuntimeTurn(u, sp, out rtClip, out rtEntry);
                    if (turn == TurnWait) return StartResult.Wait;
                    if (turn == TurnSkip) { if (reply) Log.Info("Runtime voice: " + u.entry.ClipKey + " skipped at its turn"); return StartResult.Skip; }
                    NoteCrewSpeaker(sp);
                    PhraseEntry shown = rtEntry ?? u.entry;
                    AudioClip clip;
                    string what;
                    bool panic;
                    if (u.entry != null)
                    {
                        clip = rtClip ?? PhraseClips.Clip(u.entry, sp.isFemale);
                        what = shown.ClipKey;
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
                    if (clip == null) { if (reply) Log.Info("Runtime voice: " + u.entry.ClipKey + " skipped, its clip is gone"); return StartResult.Skip; }
                    if (reply) Log.Info("Runtime voice: " + u.entry.ClipKey + " plays (" + sp.displayName + ")");
                    float sk = sp.sk, gs = host.stress.gStrain;
                    // a recorded panic clip gets no stress pitch on top (a runtime voice render has no panic delivery)
                    bool recordedPanic = panic && rtClip == null;
                    float pitch = DcMath.CrewPitch(sp.Pitch, sk, gs, tu.crewStressSemitones, tu.crewPitchMax, recordedPanic) * WarpPitch;
                    float vol = DcMath.CrewVolume(sk) * (recordedPanic ? DcMath.PanicClipGain : 1f);
                    c.SetPreset(false, tu);
                    c.Play(clip, vol, pitch, false, 0f);
                    c.recordedPanic = recordedPanic;
                    c.speaker = sp;
                    c.boost = VoiceBoost;
                    c.compressor = cfg.voiceCompressor;
                    c.phraseKey = u.entry != null ? u.entry.ClipKey : null;
                    c.tremRate = sp.tremRate;
                    c.SetCrewFilters(sk, sp.timbre, gs, tu);
                    ApplyProfile(c);
                    StartCrewBed();
                    float keep = DcMath.TruncFraction(sk, tu.truncStart, tu.truncMinFrac, recordedPanic);
                    if (keep < 1f) c.TruncateAt(clip.length * keep);
                    if (staticChop) c.TruncateAt(U(tu.chopMin, tu.chopMax) * pitch);
                    StampLine(sp, panic);
                    ShowSubtitle(shown, sp.displayName, c.duration);
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
                    // radio behaviour takes over at high stress. A chat needs both partners.
                    // in blackout the limit is blackoutIntercomMaxStress, a debug chat ignores it
                    if ((!chatForced && s > chatLimit) || !CanChat(sp) || !CanChat(chatA) || !CanChat(chatB))
                    {
                        abortChat = true;
                        return StartResult.Skip;
                    }
                    if (CrewPlaying() >= CrewCap()) return StartResult.Wait;
                    VoiceChannel c = FreeCrewChannel();
                    if (c == null) return StartResult.Wait;
                    AudioClip rtClip;
                    PhraseEntry rtEntry;
                    int turn = RuntimeTurn(u, sp, out rtClip, out rtEntry);
                    if (turn == TurnWait) return StartResult.Wait;
                    if (turn == TurnSkip) return StartResult.Skip;
                    PhraseEntry shown = rtEntry ?? u.entry;
                    string icFolder = u.entry == null ? (u.folder ?? CrewFolder(sp, s)) : null;
                    AudioClip clip = rtClip ?? (u.entry != null ? PhraseClips.Clip(u.entry, sp.isFemale)
                                                     : ClipLibrary.NextFor(icFolder, sp.isFemale, sp.rng));
                    if (clip == null) return StartResult.Skip;
                    float sk = sp.sk, gs = host.stress.gStrain;
                    bool recordedPanic = rtClip == null && (u.entry != null ? u.entry.Band == "panic" : icFolder == "crew/panic");
                    float pitch = DcMath.CrewPitch(sp.Pitch * u.pitchMul, sk, gs, tu.crewStressSemitones, tu.crewPitchMax, recordedPanic) * WarpPitch;
                    float vol = DcMath.CrewVolume(sk) * (recordedPanic ? DcMath.PanicClipGain : 1f);
                    c.SetPreset(true, tu, cfg.intercomRadioFx);
                    c.Play(clip, vol, pitch, false, 0f);
                    c.recordedPanic = recordedPanic;
                    c.speaker = sp;
                    c.boost = VoiceBoost;
                    c.compressor = cfg.voiceCompressor;
                    c.phraseKey = u.entry != null ? u.entry.ClipKey : null;
                    c.tremRate = sp.tremRate;
                    // radio sound on the intercom: the crew radio band, the radio profile band and grit, and the static
                    // bed. No signal dropouts: the crew is in the same cabin, not on CommNet.
                    if (c.radioFx) { RadioFxFilters(c, sp, gs, tu); StartCrewBed(); }
                    else c.SetIntercomFilters(sp.timbre, gs, tu);
                    StampLine(sp, false);
                    ShowSubtitle(shown, sp.displayName, c.duration);
                    lastLine = "intercom " + sp.displayName + ": " + (shown != null ? shown.ClipKey : c.clipName);
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
                    float clickVol = tu.intercomClickVolume;
                    if (cfg.intercomRadioFx)
                    {
                        // radio keying instead of the cabin click: squelch when the first speaker keys up, a roger
                        // beep (or the squelch tail) when a speaker lets go. Always on the click channel, so the
                        // radio rules never see the chat as radio traffic.
                        RadioProfile rp = Profile;
                        string f = !chatStarted ? rp.OpenClip : (cfg.rogerBeep ? "beeps/roger" : rp.CloseClip);
                        AudioClip key = ClipLibrary.CountOf(f) > 0 ? ClipLibrary.Next(f, rng) : null;
                        if (key != null) { clip = key; clickVol = f == "beeps/roger" ? tu.radioKeyVolume * 0.75f : tu.radioKeyVolume; }
                    }
                    if (clip == null) return StartResult.Skip;
                    // dedicated source: a click is never skipped because crew channels are busy
                    VoiceChannel c = host.clickCh;
                    if (c.Busy) c.Stop();
                    c.Play(clip, clickVol, 1f, false, 0f);
                    ch = c;
                    return StartResult.Started;
                }
                case UttKind.SubtitleLine:
                {
                    // the line is only read: shown for its reading time, the air is held as long
                    if (u.entry == null) return StartResult.Skip;
                    if (u.speaker != null && (!host.roster.InGroup(u.speaker) || u.speaker.outDueToG)) return StartResult.Skip;
                    if (u.speaker == null && McUnreachable) return StartResult.Skip;
                    float secs = SubtitleReadSeconds(u.entry.English);
                    ShowSubtitle(u.entry, u.speaker != null ? u.speaker.displayName : "MC", secs);
                    subtitleHoldUntil = clock + secs;
                    lastEndClock = -1f;
                    return StartResult.Instant;
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
                    AudioClip rtClip;
                    PhraseEntry rtEntry;
                    int turn = RuntimeTurn(u, null, out rtClip, out rtEntry);
                    if (turn == TurnWait) return StartResult.Wait;
                    if (turn == TurnSkip) return StartResult.Skip;
                    PhraseEntry shown = rtEntry ?? u.entry;
                    AudioClip clip = rtClip ?? (u.entry != null ? PhraseClips.Clip(u.entry, false) : ClipLibrary.Next(u.folder ?? "control/calm", rng));
                    if (clip == null) return StartResult.Skip;
                    // No runtime pitch drop under stress, the panic delivery sounds like the calm one.
                    // One controller, like a real CAPCOM. The livelier sound is in the clips.
                    float pitch = WarpPitch;   // time warp: everybody sounds sped up
                    float vol = DcMath.McVolume;
                    RadioProfile p = Profile;
                    c.SetBand(p.HighPassHz, p.LowPassHz);
                    c.Play(clip, vol, pitch, false, 0f);
                    c.boost = VoiceBoost;
                    c.compressor = cfg.voiceCompressor;
                    c.phraseKey = u.entry != null ? u.entry.ClipKey : null; // the table row (FaceDriver), also for a named line
                    c.baseDistortion = Math.Max(tu.controlDistortion, p.Distortion) + 0.3f * host.StaticLevel * host.settings.weakSignalVolume;
                    c.dropout = host.Dropout;
                    if (staticChop) c.TruncateAt(U(tu.chopMin, tu.chopMax) * pitch);
                    ShowSubtitle(shown, "MC", c.duration);
                    lastLine = "MC: " + (shown != null ? shown.ClipKey : u.folder);
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
                        // weaker signal, louder bed
                        // radioStatic: extra static at any signal, on top of the weak-signal part
                        if (clip != null) b.Play(clip, DcMath.Clamp01(tu.controlBedVolume + DcMath.StaticCurve(cfg.radioStatic) * RadioStaticMcBed
                                                                     + host.StaticLevel * host.settings.weakSignalVolume), 1f, true, 0.12f);
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
                    // weakSignalVolume: the bursts were too loud next to the voices
                    st.Play(clip, 0.8f * host.settings.weakSignalVolume, 1f, false, 0f);
                    ch = st;
                    return StartResult.Started;
                }
                case UttKind.ProbeBeep:
                    return StartProbe(u, out ch);
                case UttKind.CabinBeep:
                    return StartCabinBeep(u, out ch);
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

        /// <summary>MC transmission: quindar in, line, quindar out, static bed under it, squelch tail. to: the kerbal
        /// Mission Control talks to in this scene (the one who answers or just spoke), for a runtime name.</summary>
        private void EnqueueMc(string folder, float delay, PhraseEntry e = null, KerbalVoice to = null)
        {
            if (count + 7 > Capacity) return;
            // a weak signal sometimes cracks before the transmission
            // far away the delayed answer arrives with a short crackle first
            // radioStatic also gives an occasional crackle at a full signal
            double crackle = Math.Max(host.StaticLevel, RadioStaticBurstChance * host.settings.radioStatic);
            if (rng.NextDouble() < crackle || host.LightDelay > 0.5f) Enqueue(UttKind.StaticBurst, null, delay, 0f, null);
            Enqueue(UttKind.BedStart, null, delay, 0f, null);
            Enqueue(UttKind.ControlFx, "beeps/quindar_in", 0f, 0f, null);
            Utterance u = Enqueue(UttKind.ControlLine, folder, 0.05f, 0f, null);
            if (u != null) { u.entry = e; u.rt = PlanMc(e, to); }
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
                return; // with the table, never a random clip: it would have no subtitle and could say anything
            }
            EnqueueMc("control/calm", delay);
        }

        /// <summary>Crew radio transmission: key up, line, key down. One speaker, keyed per profile.</summary>
        private void EnqueueCrew(string folder, float delay, float overlap, KerbalVoice sp = null, PhraseEntry e = null)
        {
            if (count + 4 > Capacity) return;
            bool keyed = overlap <= 0f;
            if (keyed) Enqueue(UttKind.RadioOpen, null, delay, 0f, null);
            Utterance u = Enqueue(UttKind.CrewLine, folder, keyed ? 0.02f : delay, overlap, sp);
            if (u != null) { u.entry = e; u.rt = PlanLine(e, sp, null); }
            if (keyed && host.settings.rogerBeep) Enqueue(UttKind.RogerBeep, null, 0.04f, 0f, null);
            if (keyed) Enqueue(UttKind.RadioClose, null, 0.02f, 0f, null);
        }

        // ---------------- scenes ----------------

        /// <summary>Screams (scenes scream_*) are rare: at most one per kerbal per screamCooldown seconds, never without a speaker.</summary>
        private bool ScreamOk(PhraseScene x, KerbalVoice k)
        {
            if (!PacingRules.IsScream(x.Id)) return true;
            if (BadassNoScream(x, k)) return false; // a badass never screams
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

        // ---- context ----

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

        /// <summary>A part scene only when the part is there: a solar panel scene on a ship with a panel, a chute
        /// scene before or after the chutes open, a heat shield scene on a ship with a shield.</summary>
        private bool PartsOk(PhraseScene sc)
        {
            var s = host.sample;
            return PhraseTable.PartTopicOk(sc.Topic, s.hasSolar, s.hasShield, s.chuteStowed, s.chuteOpen);
        }

        private PhraseScene PickCore(string ev, string band, string cause, bool intercom, Predicate<PhraseScene> filter)
        {
            var list = CoreList(ev, band, cause, intercom, filter);
            if (list == null) return null;
            bool stressBand = !intercom && (band == "tense" || band == "panic");
            PhraseScene p = Mem.Take(list, rng, lastSceneId);
            if (p != null)
            {
                lastSceneId = p.Id;
                if (stressBand) stressSaid[p.Id] = clock;
                else if (ev == "chatter" && p.Lines.Count >= 2) calmSaid[p.Id] = clock;
            }
            return p;
        }

        /// <summary>The scenes PickCore picks from (after the repeat filters), null when the table is not loaded.</summary>
        private System.Collections.Generic.List<PhraseScene> CoreList(string ev, string band, string cause, bool intercom, Predicate<PhraseScene> filter)
        {
            if (!PhraseClips.Loaded) return null;
            string phase = CurrentPhase;
            // phase is checked here (radio and intercom rules differ), cause by the table
            var list = PhraseClips.table.ScenesFor(ev, band, null, cause, sc => PhraseClips.Playable(sc)
                && (intercom ? IntercomPhaseOk(sc, phase) : RadioPhaseOk(sc, phase))
                && !sc.IsProbe && CrewCanVoice(sc) && !SomberBlocks(sc) && PartsOk(sc)
                && (intercom || BlackoutOk(sc)) && sc.PlaysAt(Body)
                && (filter == null || filter(sc)) && SlotsOk(sc));
            bool stressBand = !intercom && (band == "tense" || band == "panic");
            if (stressBand) list = StressRepeatFilter(list);
            else if (ev == "chatter") list = CalmRepeatFilter(list);
            return list;
        }

        /// <summary>
        /// Calm chatter ("Did you see that maneuver? Perfect." / "We saw it. A bit crooked." in a
        /// loop on the pad). A pilot on the pad had one trait scene left, and Pick returns a single candidate even when
        /// it was the last scene. Exchanges and chats (2+ lines) picked within calmRepeatGap s are left out, so the
        /// trait and persona tiers fall through to the next tier instead of repeating. Single lines (MC acks) stay.
        /// </summary>
        private System.Collections.Generic.List<PhraseScene> CalmRepeatFilter(System.Collections.Generic.List<PhraseScene> list)
        {
            if (list == null || list.Count == 0 || calmSaid.Count == 0) return list;
            float gap = Tu.calmRepeatGap;
            list.RemoveAll(x =>
            {
                float t;
                return x.Lines.Count >= 2 && calmSaid.TryGetValue(x.Id, out t) && clock - t < gap;
            });
            return list;
        }

        /// <summary>
        /// Tense and panic radio picks (MC said "Keep an eye on the data" over and over while the
        /// craft was spinning). Scenes picked within causeRepeatGap s are left out. When every candidate was picked
        /// that recently, a cause scene may repeat, a generic cause-any scene never: then nothing is picked.
        /// A cause-any scene is left out for the longer
        /// calmRepeatGap. Those scenes play whenever the cause was just talked about, so they came back every minute.
        /// </summary>
        private System.Collections.Generic.List<PhraseScene> StressRepeatFilter(System.Collections.Generic.List<PhraseScene> list)
        {
            if (list == null || list.Count == 0) return list;
            float gap = Tu.causeRepeatGap;
            float anyGap = Math.Max(gap, Tu.calmRepeatGap);
            var fresh = new System.Collections.Generic.List<PhraseScene>(list.Count);
            for (int i = 0; i < list.Count; i++)
            {
                float t;
                float g = list[i].Cause == "any" ? anyGap : gap;
                if (!stressSaid.TryGetValue(list[i].Id, out t) || clock - t >= g) fresh.Add(list[i]);
            }
            if (fresh.Count > 0) return fresh;
            list.RemoveAll(x => x.Cause == "any");
            return list;
        }

        /// <summary>
        /// Short MC acknowledgement of a single crew line. It answers the cause of that line first (a spin line gets
        /// a spin answer), in the panic band also from the calmer tense rows. Else a cause-any ack via PickScene.
        /// </summary>
        private PhraseScene PickMcAck(string band, string cause)
        {
            if (!string.IsNullOrEmpty(cause) && cause != "any")
            {
                Predicate<PhraseScene> same = x => x.Cause == cause && IsMcAck(x);
                PhraseScene sc = PickCore("chatter", band, cause, false, same);
                if (sc == null && band == "panic") sc = PickCore("chatter", "tense", cause, false, same);
                if (sc != null) return sc;
            }
            return PickScene("chatter", band, IsMcAck) ?? PickScene("chatter", band == "panic" ? "tense" : band, IsMcAck);
        }

        /// <summary>
        /// Radio scene of the event in the current phase. Cause-specific rows only for the current cause (no cause:
        /// only cause-any rows). In the tense and panic bands a scene of the current cause is preferred.
        /// </summary>
        private PhraseScene PickScene(string ev, string band, Predicate<PhraseScene> filter)
        {
            string cause = SceneCause();
            if ((band == "tense" || band == "panic") && cause != NoCause)
            {
                PhraseScene sc = PickCore(ev, band, cause, false, x => x.Cause == cause && (filter == null || filter(x)));
                if (sc != null) { NoteCause(sc); return sc; }
            }
            PhraseScene p = PickCore(ev, band, cause, false, filter);
            NoteCause(p);
            return p;
        }

        /// <summary>The cause PickScene uses: the dominant cause, or none when it was talked about within causeRepeatGap.</summary>
        private string SceneCause()
        {
            string cause = DominantCause ?? NoCause;
            // the same cause is not talked about again within causeRepeatGap: then only cause-any scenes
            if (cause != NoCause && !CauseFresh(cause)) cause = NoCause;
            return cause;
        }

        private bool CauseFresh(string cause)
        {
            float t;
            return !causeSaid.TryGetValue(cause, out t) || clock - t >= Tu.causeRepeatGap;
        }

        private void NoteCause(PhraseScene sc)
        {
            if (sc != null && !string.IsNullOrEmpty(sc.Cause) && sc.Cause != "any") causeSaid[sc.Cause] = clock;
        }

        /// <summary>Intercom scene (crew-only, 2+ lines): funny rows only, see IntercomPhaseOk.</summary>
        private PhraseScene PickIntercomScene(string band, Predicate<PhraseScene> filter)
        {
            return PickCore("chatter", band, DominantCause ?? NoCause, true, filter);
        }

        private PhraseEntry FirstLine(PhraseScene sc)
        {
            if (sc == null) return null;
            var lines = PlayLines(sc);
            return lines.Count > 0 ? lines[0] : null;
        }

        /// <summary>Radio scene: every crew line from one speaker, MC lines with quindar. Returns false when nothing was queued.</summary>
        private bool EnqueueRadioScene(PhraseScene sc, KerbalVoice sp, float delay)
        {
            if (sc == null) return false;
            var lines = PlayLines(sc); // {body} variants
            if (lines.Count == 0) return false;
            int need = 0;
            for (int i = 0; i < lines.Count; i++) need += lines[i].Role == "mc" ? 7 : 4;
            if (count + need > Capacity) return false;
            if (sc.CrewLines > 0 && (sp == null || !FitsSpeaker(sc, sp)))
            {
                // the crew speaker must have the scene's trait and persona : nobody fits, no scene
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
                if (e.Role == "mc") EnqueueMc(e.Folder, d, e, sc.CrewLines > 0 ? sp : null);
                else EnqueueCrew(e.Folder, d, 0f, sp, e);
            }
            RecordedReply(sc, lines[lines.Count - 1], sc.CrewLines > 0 ? sp : null);
            Log.Debug("Scene " + sc.Id + " (" + sc.Event + ", " + sc.Band + ") queued" + (sp != null ? " for " + sp.displayName : ""));
            return true;
        }

        /// <summary>Crew speaker for a new exchange: prefers one whose speaker cooldown is over.</summary>
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
            // time warp: often a runtime line about the warp instead of the usual exchange
            if (!appendOverlap && host.warpChatter && rng.NextDouble() < WarpLineChance && QueueWarpLine()) return true;
            if (host.probeMode) return FireProbe(appendOverlap); // pet talk instead of crew exchanges
            float s = host.stress.stress;
            Tuning tu = Tu;

            if (host.evaMode)
            {
                // EVA: the kerbal talks with Mission Control only, quiet unless something is wrong
                if (s < tu.quietBand)
                {
                    // a real EVA has constant comms: mostly talk, a runtime EVA line now and then for variety
                    double r = rng.NextDouble();
                    if (McUnreachable)
                    {
                        // no link to Mission Control: the suit radio still works between the kerbals nearby
                        if (r < 0.6) TryEvaChat();
                        return true;
                    }
                    if (r < 0.15) { if (host.settings.beepsEnabled) Enqueue(UttKind.PingPattern, null, 0f, 0f, null); }
                    else if (r < 0.45 && TryEvaChat()) { }   // two kerbals of an EVA group on the suit radio
                    else if (r < 0.7 && QueueEvaLine()) { }
                    else if (!EnqueueRadioScene(PickScene("eva_check", null, EvaSceneOk), null, U(0f, 0.3f)) && !PhraseClips.Loaded)
                        EnqueueMcFor("eva_check", U(0f, 0.3f));
                    return true;
                }
            }
            else if (host.stationMode && PacingRules.StationChoice(host.settings.stationStyle, s, tu.burstBand) == StationAction.Check)
            {
                // stations and bases at low stress: only the MC check-in timer talks, here just a ping
                if (host.settings.beepsEnabled && rng.NextDouble() < 0.5) Enqueue(UttKind.PingPattern, null, 0f, 0f, null);
                return true;
            }

            if (appendOverlap)
            {
                // burst at high stress: one extra crew line over the running exchange, no MC
                KerbalVoice extra = PickRested();
                PhraseScene one = null;
                PhraseEntry e1 = null;
                if (PhraseClips.Loaded)
                {
                    // with the table only a real line: a folder clip would have no subtitle
                    if (extra == null) return true;
                    one = PickSceneFor("chatter", BandOf(extra, s), extra, sc => sc.Lines.Count == 1 && sc.CrewLines == 1
                                                                     && sc.GenderOk(extra.isFemale) && ScreamOk(sc, extra));
                    e1 = FirstLine(one);
                    if (e1 == null) return true;
                }
                Utterance u = Enqueue(UttKind.CrewLine, null, DcMath.Lerp(0.6f, 0.05f, s),
                                      rng.NextDouble() < tu.overlapMax * DcMath.Smooth(s, 0.6, 1.0) ? U(0.5f, 0.8f) : 0f, extra);
                if (u != null && e1 != null) { u.entry = e1; StampScream(one, extra); }
                return true;
            }

            // kerbals of other vessels nearby (a kerbal outside, a lander): now and then a radio chat with them.
            // It needs no link to Mission Control.
            if (s < tu.quietBand && HasRadioPartners && rng.NextDouble() < (McUnreachable ? 0.5 : 0.3) && TryEvaChat()) return true;

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
                // Persona and trait scenes for this speaker are preferred (PickSceneFor), never a misfit
                PhraseScene sc = PickSceneFor("chatter", band, sp, x => !x.IsIntercom && x.GenderOk(sp.isFemale) && ScreamOk(x, sp)
                                                                 && (x.McLines == 0 || rng.NextDouble() < mcP));
                if (sc == null) sc = PickSceneFor("chatter", band, sp, x => !x.IsIntercom && ScreamOk(x, sp));
                if (EnqueueRadioScene(sc, sp, U(0f, 0.3f)))
                {
                    // a single crew line sometimes gets a short MC acknowledgement
                    if (sc.McLines == 0 && rng.NextDouble() < McP(0.5))
                    {
                        // in a panic MC answers with the slow, calm panic lines (the contrast the user asked for)
                        // it answers the crew line's own cause (a generic line after every spin line)
                        PhraseScene ack = PickMcAck(band, sc.Cause);
                        PhraseEntry ae = FirstLine(ack);
                        if (ae != null) EnqueueMc(ae.Folder, ResponseDelay(s), ae, sp);
                    }
                    return true;
                }
                return false; // no fitting scene: stay quiet rather than play a random clip without a subtitle
            }
            FireFolders(s, sp);
            return true;
        }

        /// <summary>Simple exchange shapes on folder clips, used when the Lonapse table is missing. One crew speaker.</summary>
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
            if (host.probeMode) return; // no crew in probe mode
            // a blackout chat is not cut: a started chat holds the air and the relief follows when it ends (AirRules)
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
            if (PhraseClips.Loaded) return; // folder clips only without the table (no subtitle, any content)
            EnqueueMcFor("relief", 0.4f);
            int n = 2 + (rng.NextDouble() < 0.5 ? 1 : 0);
            for (int i = 0; i < n; i++)
                EnqueueCrew("crew/relief", U(0.1f, 0.4f), (i > 0 && rng.NextDouble() < 0.5) ? U(0.5f, 0.8f) : 0f);
            EnqueueMcFor("relief", ResponseDelay(0.2f));
        }

        /// <summary>After landing or splashdown: one crew/MC exchange (the end of the descent sequence).</summary>
        public void EnqueueTouchdown(float delay, bool splash)
        {
            // The probe beeps about its landing, Mission Control answers
            if (host.probeMode)
            {
                // The first landing on a body this session is a milestone, otherwise the normal touchdown beeps
                if (host.ProbeFirstLanding() && EnqueueProbeMilestone(ProbeMilestones.Landing, delay)) return;
                EnqueueProbeReaction(splash ? ProbeRules.Splashdown : ProbeRules.Touchdown, delay, false);
                return;
            }
            if (count + 7 > Capacity) return;
            if (EnqueueRadioScene(PickScene(splash ? "splashdown" : "touchdown", null, null), null, delay)) return;
            if (PhraseClips.Loaded) return;
            string f = (rng.NextDouble() < 0.6 && ClipLibrary.CountOf("crew/relief") > 0) ? "crew/relief" : "crew/calm";
            EnqueueCrew(f, delay, 0f);
            EnqueueMcFor("touchdown", ResponseDelay(0.2f));
        }

        /// <summary>EVA fall: the EVA kerbal and Mission Control about a trip or a fall. False when nothing was queued.</summary>
        public bool EnqueueEvaFall()
        {
            if (!host.evaMode || !host.CanStartAudio || radioSilent || !host.settings.radioEnabled) return false;
            if (HasRadioItems() || Holding || airEventPrio > 0 || count + 10 > Capacity) return false;
            PhraseScene sc = PickScene("eva_fall", null, null);
            // the kerbal who fell talks, not one of the others nearby on EVA
            if (!EnqueueRadioScene(sc, host.roster.Own, U(0.4f, 0.9f))) return false;
            Log.Info("EVA fall: " + sc.Id);
            return true;
        }

        /// <summary>Docking approach: short crew call, Mission Control answers.</summary>
        public void EnqueueDockingApproach()
        {
            if (host.probeMode) return;
            if (!host.CanStartAudio || radioSilent || count + 10 > Capacity) return;
            if (!EnqueueRadioScene(PickScene("dock_approach", null, null), null, U(0f, 0.4f)) && !PhraseClips.Loaded)
            {
                EnqueueCrew("crew/tense", U(0f, 0.4f), 0f);
                EnqueueMcFor("dock_approach", ResponseDelay(0.3f));
            }
            Log.Debug("Docking approach exchange queued");
            RuntimeDock();
        }

        /// <summary>Close approach (within ~30 m and closing): crew calm or tense by closing speed, MC answers.</summary>
        public void EnqueueDockingClose(bool tense)
        {
            if (host.probeMode) return;
            if (!host.CanStartAudio || radioSilent || !host.settings.radioEnabled || HasRadioItems()) return;
            if (tense && EnqueueRadioScene(PickScene("dock_close", null, null), null, U(0f, 0.3f))) return;
            if (PhraseClips.Loaded) return;
            EnqueueCrew(tense ? "crew/tense" : "crew/calm", U(0f, 0.3f), 0f);
            EnqueueMcFor("dock_approach", ResponseDelay(0.2f));
        }

        /// <summary>Docked. A hard dock is a warning: it cuts chatter and plays the dock_hard scene.</summary>
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
            if (!EnqueueRadioScene(PickScene("docked", null, null), null, 0.8f) && !PhraseClips.Loaded)
            {
                EnqueueCrew(ClipLibrary.CountOf("crew/relief") > 0 ? "crew/relief" : "crew/calm", 0.8f, 0f);
                EnqueueMcFor("docked", ResponseDelay(0.2f));
            }
            Log.Debug("Docked exchange queued");
        }

        /// <summary>Maneuver plan changed: a short pre-planning exchange, chatter priority.</summary>
        public bool EnqueuePlan()
        {
            if (host.probeMode)
            {
                // MC proposes the burn, the probe judges it. Consumed when it cannot play (asleep, no link).
                if (!host.settings.probePlanTalk || !ProbeLink || ProbeAsleep) return true;
                if (!host.CanStartAudio || radioSilent || !host.settings.radioEnabled || HasRadioItems() || Holding) return false;
                EnqueueProbeReaction("plan_node", U(0.3f, 1.0f), false);
                return true;
            }
            if (!host.CanStartAudio || radioSilent || !host.settings.radioEnabled || HasRadioItems() || Holding) return false;
            PhraseScene sc = PickScene("plan_node", null, null);
            if (sc == null)
            {
                if (PhraseClips.Loaded) return true; // nothing fits now: consumed, no random clips
                EnqueueMcFor("plan_node", U(0.3f, 1.0f)); EnqueueCrew("crew/calm", ResponseDelay(0.2f), 0f); return true;
            }
            if (!EnqueueRadioScene(sc, null, U(0.3f, 1.0f))) return false;
            Log.Info("Pre-planning exchange queued");
            return true;
        }

        /// <summary>A burn is near: warning priority, cuts chatter.</summary>
        public bool EnqueueBurnSoon()
        {
            // Mission Control gets the probe ready for the burn (consumed either way)
            if (host.probeMode) { if (host.settings.probePlanTalk) EnqueueProbeReaction(ProbeRules.BurnSoon, 0.3f, true); return true; }
            if (!host.CanStartAudio || radioSilent || !host.settings.radioEnabled) return false;
            PhraseScene sc = PickScene("burn_soon", null, null);
            if (sc == null && PhraseClips.Loaded) return true; // nothing fits now: consumed, no random clips
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

        // ---------------- priority ----------------

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
            // No crew in probe mode, the probe beeps its own callout (staging calls PlayProbeCallout itself)
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
            var scLines = sc != null ? PlayLines(sc) : null; // {body} variants
            if (scLines != null && scLines.Count > 0) clip = PhraseClips.Clip(scLines[0], sp.isFemale);
            // with the table only a real line: a folder clip has no subtitle and could say anything
            if (clip == null && PhraseClips.Loaded) return "no fitting line in the phrase table";
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
            float pitch = DcMath.CrewPitch(sp.Pitch, sk, gs, tu.crewStressSemitones, tu.crewPitchMax);
            float vol = DcMath.CalloutVolume(sk);
            PreemptChatter();
            c.SetPreset(false, tu);
            c.Play(clip, vol, pitch, false, 0f);
            c.speaker = sp;
            c.boost = VoiceBoost * host.settings.calloutVolume; // own gain on top of the voice boost
            c.compressor = host.settings.voiceCompressor;
            c.phraseKey = sc != null ? scLines[0].ClipKey : null;
            c.tremRate = sp.tremRate;
            c.SetCrewFilters(sk, sp.timbre, gs, tu);
            ApplyProfile(c);
            StartCrewBed();
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
                    if (e.Role == "mc") EnqueueMc(e.Folder, ResponseDelay(0.3f), e, sp);
                    else EnqueueCrew(e.Folder, ResponseDelay(0.3f), 0f, sp, e);
                }
            }
            lastLine = "callout " + sp.displayName + ": " + (sc != null ? scLines[0].ClipKey : folder);
            Audit.Add(lastLine + " (" + what + ")");
            Log.Info("Callout (" + what + "): " + sp.displayName + (sp.isPilot ? " [pilot]" : "") + " " + (sc != null ? sc.Id : folder) + " " + c.clipName);
            return null;
        }

        /// <summary>Crew-to-crew intercom chat: a crew-only Lonapse scene (or 2 to 4 folder turns), soft clicks around it.</summary>
        /// <param name="blackoutFirst">true for the blackout timer: a blackout-phase scene first</param>
        /// <param name="forced">a scene chosen by the caller (debug), played whatever the stress</param>
        private bool FireIntercom(bool blackoutFirst = false, PhraseScene forced = null)
        {
            KerbalVoice a, b;
            if (!PickChatPair(forced, out a, out b)) return false;
            float s = host.stress.stress;
            // experimental runtime voice: sometimes a chat with real values, spoken in game (it queues itself when ready)
            if (forced == null && !blackoutFirst && TryRuntimeChat(a, b)) return true;
            PhraseScene sc = forced;
            if (sc == null && PhraseClips.Loaded)
            {
                // speaker A starts the chat: persona and trait scenes for A are preferred, never a misfit
                if (blackoutFirst) sc = PickIntercomSceneFor(null, a, x => x.IsIntercom && x.Phase == FlightPhase.Blackout);
                if (sc == null) sc = PickIntercomSceneFor(KerbalVoice.BandNames[(int)(a.band == Band.Panic ? Band.Tense : a.band)], a, x => x.IsIntercom);
                if (sc == null) sc = PickIntercomSceneFor("calm", a, x => x.IsIntercom);
            }
            // {other} and {self}: line i is spoken by a when i is even (see the turn loop below)
            string nameA = SubtitleSlots.FirstName(a.displayName), nameB = SubtitleSlots.FirstName(b.displayName);
            var lines = sc != null ? PlayLines(sc, i => new System.Collections.Generic.Dictionary<string, string> {
                { SubtitleSlots.Self, i % 2 == 0 ? nameA : nameB }, { SubtitleSlots.Other, i % 2 == 0 ? nameB : nameA } }) : null;
            if (lines != null && lines.Count == 0) { sc = null; lines = null; }
            int turns = lines != null ? lines.Count : 2 + rng.Next(3);
            // On stations and bases the intercom gets one extra turn
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
            if (!QueueChatTurns(a, b, lines, turns, extraLine, forced != null)) return false;
            Log.Debug("Intercom chat queued: " + a.displayName + " and " + b.displayName + ", " + turns + " turns" + (sc != null ? ", scene " + sc.Id : "") + ", s " + s.ToString("0.00"));
            return true;
        }

        /// <summary>Queues the turns of an intercom chat between a and b: a click and a line per turn, a release click
        /// at the end. lines null: folder clips. False when the queue has no room.</summary>
        private bool QueueChatTurns(KerbalVoice a, KerbalVoice b, System.Collections.Generic.List<PhraseEntry> lines, int turns,
                                    PhraseEntry extraLine, bool forced)
        {
            if (count + 2 * turns + 1 > Capacity) return false;
            chatA = a;
            chatB = b;
            chatForced = forced;
            chatStarted = false;
            lastChatClock = clock;
            chatLimit = IntercomAbortLimit;   // a blackout chat keeps the blackout limit after the blackout ends
            // keep the two voices audibly apart even when their base pitches are close
            float nudge = DcMath.PairPitchNudge(a.Pitch, b.Pitch, Tu.intercomMinPitchGap);
            for (int i = 0; i < turns; i++)
            {
                bool second = (i % 2) == 1;
                // push-to-talk click at the start of every line
                Enqueue(UttKind.IntercomClick, null, i == 0 ? U(0f, 0.3f) : U(Tu.intercomGapMin, Tu.intercomGapMax), 0f, null);
                Utterance u = Enqueue(UttKind.IntercomLine, null, 0.04f, 0f, second ? b : a);
                if (u == null) continue;
                if (second) u.pitchMul = nudge;
                if (lines != null) u.entry = i < lines.Count ? lines[i] : extraLine;
                if (u.entry != null) u.rt = PlanIntercom(u.entry, second ? b : a, second ? a : b);
            }
            Enqueue(UttKind.IntercomClick, null, 0.1f, 0f, null); // release click after the last line
            return true;
        }

        // ---------------- per frame ----------------

        public void Update(float dt)
        {
            clock += dt;
            Tuning tu = Tu;
            float s = host.stress.stress;
            float rate = host.settings.rateMultiplier * (host.stationMode ? tu.stationRateFactor : 1f)
                         * (host.evaMode ? tu.evaRateFactor : 1f)
                         * (host.warpChatter ? DcMath.WarpFactor(host.warpRate, tu.warpRateGain, tu.warpRateMax) : 1f);
            currentInterval = DcMath.MeanInterval(s, tu.tQuiet, tu.tManic, rate);
            if (host.probeMode) currentInterval *= tu.probeRateFactor; // fewer pet talk lines

            esc.Decay(dt, tu.panicEscTau);
            host.roster.panicOffset = esc.Offset(tu.panicEscStep, tu.panicEscCap);

            if (s < tu.quietCoastStress) quietTimer += dt; else quietTimer = 0f;

            // flight phase and stress cause
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
            RuntimeTick();
            ModTick();
            ProcessQueue();

            bool blackoutPhase = InBlackoutPhase;
            UpdateBlackoutIntercom(dt, blackoutPhase, s);

            // intercom scheduler: only with >= 2 conscious crew and low stress. The intercom is not radio, so
            // static and blackout do not stop it. In blackout the blackout timer below takes over.
            // no intercom banter while the crew mourns (Somber)
            // With a probe core aboard one kerbal is enough, the probe is the other partner
            bool probeCrew = ProbeCrewPossible;
            int speakable = host.roster.SpeakableCount();
            intercomEligible = host.settings.intercomEnabled && !blackoutPhase && !host.evaMode && !Somber
                               && s < tu.intercomMaxStress && (speakable >= 2 || (probeCrew && speakable >= 1));
            float lowStressInterval = DcMath.IntercomInterval(tu.intercomInterval, host.stationMode, QuietCoast,
                                                              tu.intercomStationBoost, tu.intercomQuietBoost);
            // more crew stress -> more intercom talk, until radio takes over above intercomMaxStress
            intercomInterval = DcMath.IntercomIntervalStress(lowStressInterval, s, tu.intercomStressMin, tu.intercomStressPeak);
            if (intercomEligible && holdoff <= 0f && !Holding)
            {
                intercomProgress += dt / Math.Max(1f, intercomInterval);
                if (intercomProgress >= intercomTarget)
                {
                    if (intercomWaitStart < 0f) intercomWaitStart = clock;
                    // never while the radio plays, is queued or pending, within IntercomStartGap s after it, nor
                    // when the radio scheduler is about to fire (AirRules.IntercomRadioLead)
                    if (count == 0 && Idle && IntercomMayStartNow)
                    {
                        FireChat(probeCrew, speakable);
                        intercomWaitStart = -1f;
                        intercomProgress = 0f;
                        intercomTarget = U(0.6f, 1.4f);
                        // give the chat room before the radio scheduler fires
                        if (progress > target * 0.8f) progress = target * 0.8f;
                    }
                    else intercomProgress = intercomTarget;
                }
            }
            else if (!intercomEligible) intercomWaitStart = -1f;

            bool radioOn = host.settings.radioEnabled;
            bool checkStyle = !host.probeMode && host.stationMode && PacingRules.StationChoice(host.settings.stationStyle, s, tu.burstBand) == StationAction.Check;
            if (radioOn && checkStyle && !radioSilent && !McUnreachable && holdoff <= 0f)
            {
                // stations and bases: Mission Control checks in now and then. Only this mechanism at low stress.
                stationMcLeft -= dt;
                if (stationMcLeft <= 0f && !HasRadioItems() && !Holding)
                {
                    if (!EnqueueRadioScene(PickScene("station_check", null, null), PickRested(), U(0f, 0.3f)) && !PhraseClips.Loaded)
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
                        // a started chat holds the air: the radio fires right after it (fresh pick, nothing stale queued)
                        if (ChatHoldsAir) progress = target;
                        // only a chat that has not started in the queue: the radio goes first
                        else if (!HasRadioItems())
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
        /// Blackout intercom: blackoutIntercomFirst s after the blackout starts, then about every
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
            // a concrete test phase also picks the [phase] versions of the subtitles
            debugSlotPhase = Array.IndexOf(SubtitleSlots.Conditions, phase ?? "") >= 0 ? phase : null;
            try { return DebugPlayRandomCore(evt, phase, cause, body, topic); }
            finally { debugSlotPhase = null; }
        }

        private string DebugPlayRandomCore(string evt, string phase, string cause, string body, string topic)
        {
            string want = (string.IsNullOrEmpty(evt) ? "any" : evt) + "/" + (string.IsNullOrEmpty(phase) ? "any" : phase) + "/"
                          + (string.IsNullOrEmpty(cause) ? "any" : cause) + "/" + (string.IsNullOrEmpty(body) ? "current" : body)
                          + (string.IsNullOrEmpty(topic) ? "" : "/topic " + topic);
            if (!host.CanStartAudio) return "not played: gate " + host.gateReason;
            if (!PhraseClips.Loaded) return "not played: Lonapse table not loaded (" + PhraseClips.status + ")";
            if (host.probeMode) return DebugPlayProbe(evt, phase, topic); // probe scenes only
            if (PhraseTable.IsProbeCrewEvent(evt)) return DebugPlayProbeCrew();

            var all = new System.Collections.Generic.List<PhraseScene>();
            var exact = new System.Collections.Generic.List<PhraseScene>();
            foreach (PhraseScene sc in PhraseClips.table.Scenes)
            {
                if (!string.IsNullOrEmpty(evt) && sc.Event != evt) continue;
                if (sc.IsProbe) continue; // crew mode never plays a probe scene
                if (debugSlotsOnly && !sc.HasSlots) continue;
                if (!string.IsNullOrEmpty(phase) && !PhraseTable.PhaseMatches(sc.Phase, phase)) continue;
                // a concrete test phase must not pull in blackout rows (PhaseMatches already handles that)
                if (!string.IsNullOrEmpty(cause) && !PhraseTable.CauseMatches(sc.Cause, cause)) continue;
                // topic (science experiment, home for soi_enter): topic-any rows fit too, like the events do
                if (!string.IsNullOrEmpty(topic) && !PhraseTable.TopicMatches(sc.Topic, topic)) continue;
                if (!PhraseClips.Playable(sc)) continue;
                if (!SlotsOk(sc)) continue;
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
                    if (c.radioFx) RadioFxFilters(c, sp, gs, tu);
                    else c.SetIntercomFilters(sp.timbre, gs, tu);
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
            UpdateCrewBed();
        }

        // radioStatic: static bed levels. At 100 % the crew bed sits a bit above the MC bed at a full signal.
        private const float RadioStaticMcBed = 0.30f, RadioStaticCrewBed = 0.6f, RadioStaticBurstChance = 0.35f;

        /// <summary>
        /// Intercom line with radio sound effects: the crew radio filters, the radio profile band (300 to 3600 Hz on a
        /// ship) and at least half the profile grit, so it sounds like a radio even at zero stress.
        /// </summary>
        private void RadioFxFilters(VoiceChannel c, KerbalVoice sp, float gs, Tuning tu)
        {
            c.SetCrewFilters(sp.sk, sp.timbre, gs, tu);
            ApplyProfile(c);
            c.baseDistortion = Math.Max(c.baseDistortion, 0.5f * Profile.Distortion);
        }

        /// <summary>Static loop under a crew radio line, only with radioStatic above 0. UpdateCrewBed ends it.</summary>
        private void StartCrewBed()
        {
            VoiceChannel b = host.crewBed;
            float amount = host.settings.radioStatic;
            if (b == null || amount <= 0f || b.Busy || ClipLibrary.CountOf("static/loop") == 0) return;
            AudioClip clip = ClipLibrary.Next("static/loop", rng);
            if (clip != null) b.Play(clip, DcMath.Clamp01(RadioStaticCrewBed * DcMath.StaticCurve(amount)), 1f, true, 0.08f);
        }

        /// <summary>The crew bed only runs while a crew radio line plays. Following the lines (no queue item) means
        /// a cleared queue or a skipped line can never leave the loop running.</summary>
        private void UpdateCrewBed()
        {
            VoiceChannel b = host.crewBed;
            if (b == null || !b.Busy) return;
            bool line = host.calloutCh != null && host.calloutCh.Busy;
            for (int i = 0; i < host.crew.Length && !line; i++) line = host.crew[i].Busy && (!host.crew[i].intercom || host.crew[i].radioFx);
            if (!line || host.settings.radioStatic <= 0f) b.FadeOutOnce(0.2f);
        }
    }
}
