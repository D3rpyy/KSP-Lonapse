// User settings and tuning constants. Persisted in GameData/Lonapse/PluginData/settings.cfg.
using System;
using System.Globalization;
using System.IO;
using System.Reflection;
#if !DC_OFFLINE_TEST
using UnityEngine;
#endif

namespace Lonapse
{
    /// <summary>
    /// Every threshold, weight and time constant. All public float fields are saved/loaded by reflection
    /// under the TUNING node with the same names.
    /// </summary>
    public class Tuning
    {
        // --- Stress components ---
        // spin: 1 rad/s to 5 rad/s. Lower made a small ship rolling on reaction wheels panic.
        public float spinLo = 1.0f, spinHi = 5.0f, wSpin = 1.00f;
        public float gLo = 2.5f, gHi = 8.0f, gTau = 0.15f, gClamp = 20f, wG = 0.85f;
        public float qLo = 15f, qHi = 45f, wQ = 0.60f;
        public float groundMaxH = 5000f, groundVsLo = 6f, groundVsHi = 40f, groundTti = 25f, groundTtiSpan = 22f, wGround = 0.90f;
        public float heatLo = 0.55f, heatHi = 0.92f, wHeat = 0.80f;
        public float wPlasma = 0.35f;
        public float fuelThrottle = 0.05f, fuelLow = 0.10f, wFuel = 0.40f;
        public float impulseTau = 4f, impulseStage = 0.20f, impulseLiftoff = 0.25f, impulsePartDie = 0.40f, wImpulse = 1.00f;
        public float warmup = 3f;
        // attackTau: slow build-up for lasting stress (spin, G, q, heat, fuel). fastAttackTau: sudden events
        // (impulses from staging, hits, part deaths, and an imminent ground impact) still count at once.
        public float attackTau = 3.0f, fastAttackTau = 0.35f, decayTau = 9f, holdTime = 1.5f;
        public float gStrainLo = 3f, gStrainHi = 9f, gStrainAttack = 0.2f, gStrainDecay = 1.0f;

        // --- Gate. Any on-rails warp (TimeWarp.Modes.HIGH, rate > 1) closes the gate. ---
        public float gateFadeTime = 0.30f, reopenDelayMin = 3f, reopenDelayMax = 8f;
        public float warpEndPingDelay = 1.0f, warpEndHoldoff = 3.0f, warpPingCooldown = 15f;

        // --- Phase 2: docking ---
        public float dockApproachDist = 50f, dockApproachSpeed = 2f, dockApproachHold = 1.0f, dockRearmDist = 100f;
        public float dockGuardDist = 3f, dockQuietTime = 2.0f, dockClosingRate = 0.05f, dockGuardMax = 5f;
        // Chance of a docking call when none of the three docking signs hold (DockIntent), 1 when all hold. The ports must face within dockIntentAngle degrees.
        public float dockIntentBase = 0.3f, dockIntentAngle = 45f;
        // Seconds of one approach during which other chatter waits (from the approach call, while the crew is calm).
        public float dockChatHold = 240f;

        // --- Phase 2: station and base mode ---
        // EVA: more radio, like a real EVA with constant comms (rate factor on the talk interval)
        public float evaRateFactor = 1.6f;
        // time warp: more talk and faster voices with the warp rate (per factor 10 of the rate, capped)
        public float warpRateGain = 0.4f, warpRateMax = 2.5f, warpPitchGain = 0.04f, warpPitchMax = 1.18f;
        public float stationRateFactor = 0.5f, stationMcFactor = 1.0f, stationCalmBias = 0.6f, stationPingFactor = 2.0f;
        // periodic MC-initiated "station, Houston" exchange on stations and bases
        public float stationMcMin = 150f, stationMcMax = 300f;

        // --- Phase 2: crew intercom chats ---
        // intercom at stress 0: 60 s everywhere. The station and quiet coast boosts are 1 now, they were for the
        // old 300 s gap and multiply (a station is nearly always a quiet coast). Independent of the radio rate slider.
        // The interval shrinks log-linearly with stress down to intercomStressMin at intercomStressPeak.
        public float intercomInterval = 60f, intercomStationBoost = 1f, intercomQuietBoost = 1f;
        public float intercomStressMin = 25f, intercomStressPeak = 0.6f;
        public float intercomMaxStress = 0.7f, intercomAbortStress = 0.78f, quietCoastStress = 0.1f, quietCoastTime = 60f;
        public float intercomHp = 100f, intercomLp = 8000f, intercomClickVolume = 0.9f, intercomGapMin = 0.25f, intercomGapMax = 0.9f;
        // small-room reverb, attenuations in millibels (applied as negative values)
        public float intercomRoomAtten = 600f, intercomReverbAtten = 200f, intercomReflAtten = 400f, intercomDecay = 0.4f;
        public float intercomMinPitchGap = 0.05f;

        // --- Blackout ---
        public float plasmaTau = 0.5f, blackoutEnter = 0.35f, blackoutEnterTime = 1.0f, blackoutDeep = 0.65f;
        public float blackoutExit = 0.20f, staticExitTime = 1.5f, blackoutExitTime = 2.0f, blackoutMinDuration = 3f;
        public float staticMin = 2.5f, staticMax = 4f, staticRampTime = 1.5f, staticFadeOut = 0.6f;
        public float crackleMin = 0.2f, crackleMax = 0.7f, chopMin = 0.3f, chopMax = 0.8f, staticExtraDistortion = 0.3f;
        public float reliefHoldoff = 8f;

        // --- Scheduler ---
        public float tQuiet = 110f, tManic = 1.0f, quietBand = 0.15f, burstBand = 0.6f;
        // crew delivery under stress: extra pitch at stress 1 in semitones, total pitch cap, clip truncation, overlap
        public float crewStressSemitones = 0f, crewPitchMax = 1.12f, truncStart = 0.85f, truncMinFrac = 0.8f, overlapMax = 0.35f;
        public float mcTenseLo = 0.20f, mcTenseHi = 0.45f;

        // --- Voice channels ---
        // crew RADIO lines get a mild transmit band-pass (crewHp .. crewLpCalm). The intercom is wider and has no such band.
        public float crewLpCalm = 4000f, crewLpStress = 3200f, crewLpMin = 1400f, crewHp = 300f;
        public float controlHp = 350f, controlLp = 2800f, controlDistortion = 0.30f, controlBedVolume = 0.12f;

        // --- Beeps ---
        public float pingOrbit = 40f, pingFlying = 25f, pingLanded = 90f, pingVolume = 0.5f;
        // descent blips: only below descentMaxH (radar altitude) while descending, never faster than descentFast s
        public float descentMaxH = 500f, descentVsMin = 2f, descentVsSpan = 40f, descentSlow = 5.0f, descentFast = 1.5f;
        public float descentVolLo = 0.25f, descentVolHi = 0.5f, descentPitchHi = 1.15f;
        public float stageCooldown = 0.4f, stageVolume = 0.8f, landingDebounce = 6f, touchdownDelay = 1.5f;

        // --- docking close-approach phase ---
        public float dockCloseDist = 30f, dockCloseExit = 40f, dockCloseMin = 8f, dockCloseMax = 15f, dockTenseClosing = 0.4f;
        public float dockCalloutLen = 1.2f;

        // --- one-word callouts (crew/callout): staging, parachutes, docking distances ---
        public float calloutCooldown = 1.5f, chuteGroupCooldown = 5f, chuteFullMinGap = 3f, chutePollInterval = 0.25f;

        // --- Radio, callouts, stress causes and pacing. ---
        // Chatter hold after a callout preempts it
        public float calloutHoldPad = 0.3f;
        // Ducking of game audio
        public float duckAttack = 0.08f, duckRelease = 0.4f;
        // Throttle callouts
        public float throttleFullAt = 0.98f, throttleCutAt = 0.05f, throttleMinGap = 5f;
        // Pitch factor per gender
        public float genderPitchFemale = 1.06f, genderPitchMale = 0.96f;
        // Launch cap on the panic band (0 or 1) and altitude where it stops
        public float launchCap = 1f, launchCapAlt = 10000f;
        // Panic pacing and escalation
        public float panicMinGap = 8f, panicCooldown = 45f, screamCooldown = 60f, speakerCooldown = 8f, talkBurst = 4f, talkPerMinute = 8f;
        public float panicEscTau = 40f, panicEscStep = 0.04f, panicEscCap = 0.16f;
        // EVA stress. evaSpinWeight is a factor on wSpin and wG while on EVA (0 while ragdolled).
        // evaSettleTime: seconds after a ragdoll in which EVA spin and G still count as nothing.
        public float evaSettleTime = 3f;
        public float evaSpinWeight = 0.3f, wEva = 0.4f, evaLowProp = 0.15f, evaLowEC = 0.1f, evaFarDist = 200f;
        // Rover stress component
        public float wRover = 0.7f, roverSpeedLo = 5f, roverSpeedHi = 25f, roverRoughLo = 0.5f, roverRoughHi = 3f;
        public float roverAirTimeLo = 0.3f, roverAirTimeHi = 1.5f, roverTiltLo = 25f, roverTiltHi = 45f;
        // Docking impulse
        public float dockSoftSpeed = 0.2f, dockHardSpeed = 0.5f, impulseDockHard = 0.35f;
        // CommNet signal to static level
        public float signalStaticMax = 0.6f, signalRangeFullKm = 500f, signalRangeZeroKm = 2500f;
        public float signalPollSec = 1.0f, signalHoldTime = 5f;
        // Maneuver node chatter
        public float planRateSec = 90f, burnWarnSec = 60f;
        // volume of the radio key-up and key-down sound (desk click or headset squelch)
        public float radioKeyVolume = 0.7f;
        // light delay (MC answers later far from Kerbin), touchdown ambience level
        // light delay (at most 10 s): about 0.9 s at the Mun, 2 s at Minmus, 8 to 10 s at Duna and beyond
        public float lightDelayMax = 10.0f, lightDelayScale = 2.6f;
        // master alarm: very quiet, slow in and out, so it stays in the background. Only at very high vessel stress.
        public float alarmVolume = 0.12f, alarmFadeIn = 2.5f, alarmFadeOut = 3f, alarmHold = 4f, alarmStress = 0.9f;
        // a stress cause (q, spin, heat, ...) gets its own lines at most once per this many seconds, at any radio amount
        public float causeRepeatGap = 45f;
        // A calm chatter exchange or chat (2+ lines) does not come back within this many seconds. New key only.
        public float calmRepeatGap = 600f;

        // --- Mouths and the intercom in blackout. mouthOpenDeg went from 12 to 18 in tuning version 7. ---
        // Talking mouths. mouthAxis is the local hinge axis (0 = X, 1 = Y, 2 = Z), mouthInvert 0 or 1 flips the sign.
        public float mouthOpenDeg = 18f, mouthAxis = 2f, mouthInvert = 0f;
        // crew portraits (7 to 10 fps) alternate wide and nearly closed per portrait frame while the voice is audible
        public float mouthPortraitAlternate = 1f;
        // envelope follower on the voice: gain on RMS, noise gate, attack and release in seconds
        public float mouthGain = 6f, mouthGate = 0.02f, mouthAttack = 0.03f, mouthRelease = 0.10f;
        // Intercom during blackout. Stress cap, first chat after entry (s), gap between blackout chats (s).
        public float blackoutIntercomMaxStress = 0.9f, blackoutIntercomFirst = 4f, blackoutIntercomGap = 30f;

        // --- Personality, game events, faces and head turns. ---
        // personality: how strongly the director prefers scenes that match the speaker's trait or persona (0..1)
        public float traitPreference = 0.35f, personaPreference = 0.4f;
        // power events: power_low below powerLowFrac, power_ok above powerOkFrac, only with at least powerMinMax EC capacity
        public float powerLowFrac = 0.10f, powerOkFrac = 0.25f, powerMinMax = 5f;
        // science, SOI, part loss, signal and crew loss events (seconds)
        public float scienceCooldown = 30f, scienceTopicCooldown = 60f, soiCooldown = 60f;
        public float partLostWindow = 1.5f, partLostCooldown = 20f;
        public float signalHold = 3f, signalCooldown = 45f;
        public float crewLostDelay = 3f, somberTime = 180f, eventMaxAge = 600f;
        // faces and head turn. headTurnAxis 0 = X, 1 = Y, 2 = Z, headTurnInvert 0 or 1.
        // faceExpressionSign is +1 or -1 (the only TUNING float allowed to be negative, see Sanitize).
        public float headTurnMaxDeg = 25f, headTurnAxis = 1f, headTurnInvert = 0f, headTurnSpeed = 4f;
        // head nods: the speaker on stressed syllables, a listener once at the end of a line. headNodInvert 0 or 1.
        public float headNodDeg = 4f, headNodInvert = 0f;
        // the mouth opens this many seconds before the sound (the loudness track of the clip)
        public float mouthLead = 0.06f;
        public float faceWeight = 0.8f, faceExpressionSign = 1f;

        // --- Probe mode. ---
        // pet talk: factor on the radio chatter gap in probe mode (> 1 is quieter)
        public float probeRateFactor = 1.0f;   // was 1.5, probes were too quiet
        // Probe and crew on one vessel. With 2 or more crew, this share of the intercom chats is a probe and
        // crew scene instead. With 1 crew member every intercom chat is one (there is nobody else to talk to).
        public float probeCrewShare = 0.35f;
        // electric charge fractions: drowsy below probeDrowsyFrac, asleep below probeSleepFrac
        public float probeDrowsyFrac = 0.15f, probeSleepFrac = 0.03f;
        // cap on the beep pitch (1 + 0.25 x stress)
        public float probeBeepPitchMax = 1.35f;

        // --- Probe hails, EVA relay and breathing, probe callouts and moments. ---
        // probe hails (the probe beeps first, Mission Control answers): mean gap in s. Replaces roomInterval.
        public float hailInterval = 360f;
        // EVA relay: an EVA kerbal reaches Mission Control through a nearby ship. The suit link to the ship is full up
        // to evaRelayFullKm and gone at evaRelayZeroKm (KSP unloads vessels at about 2.5 km).
        public float evaRelayFullKm = 1.0f, evaRelayZeroKm = 2.3f;
        // EVA fall: least gap in s between two eva_fall exchanges
        public float evaFallCooldown = 45f;
        // EVA breathing: running builds exertion over evaExertRise s and it fades over evaExertFall s.
        // At full exertion the breath loop plays evaBreathRate times as fast and evaBreathGain times as loud.
        public float evaExertRise = 20f, evaExertFall = 30f, evaBreathRate = 1.4f, evaBreathGain = 1.5f;
        // probe callouts (staging, chutes, throttle): chance that Mission Control answers the beep, and the least gap
        // in s between two MC answers (staging comes in bursts)
        public float probeCalloutMcChance = 0.35f, probeCalloutMcGap = 20f;
        // "say again?": only below this signal, with this chance per scene, at most once per sayAgainCooldown s
        public float sayAgainSignal = 0.35f, sayAgainChance = 0.35f, sayAgainCooldown = 120f;
        // rover moments: fast above roverFastSpeed m/s for roverFastSec s, air time above roverAirSec s, tilt above
        // roverTiltDeg, idle after roverIdleSec s standing still. At most one per roverMomentCooldown s.
        public float roverFastSpeed = 8f, roverFastSec = 5f, roverAirSec = 0.7f, roverTiltDeg = 30f, roverIdleSec = 600f;
        public float roverMomentCooldown = 180f;
        // data transmission: at most one data beep per dataBeepGap s while an antenna sends, dataBeepMax per send
        public float dataBeepGap = 12f, dataBeepMax = 4f;
        // crew portraits: 1 renders the portrait again at the end of the frame while the jaw or head is driven
        // (PortraitRefresh), 0 leaves only the stock portrait renders
        public float portraitRefresh = 1f;

        /// <summary>TUNING floats that may hold a negative value. The generic Sanitize check skips them.</summary>
        public static readonly string[] SignedFields = { "faceExpressionSign" };

        /// <summary>Schema version of the saved TUNING node. Bump when defaults of existing fields change.</summary>
        public const int CurrentVersion = 13;

        /// <summary>
        /// Fields whose defaults changed in version 3. A file written by an older version gets these reset to
        /// the new defaults, every other saved value is kept.
        /// </summary>
        public static readonly string[] ChangedInV3 =
        {
            "stationMcFactor", "intercomInterval", "intercomClickVolume",
            "descentMaxH", "descentSlow", "descentFast",
        };

        /// <summary>Replace NaN, Infinity and negative values with defaults, and keep lo below hi.</summary>
        public void Sanitize()
        {
            Tuning def = new Tuning();
            FieldInfo[] fields = typeof(Tuning).GetFields(BindingFlags.Public | BindingFlags.Instance);
            for (int i = 0; i < fields.Length; i++)
            {
                if (fields[i].FieldType != typeof(float)) continue;
                float v = (float)fields[i].GetValue(this);
                bool signed = Array.IndexOf(SignedFields, fields[i].Name) >= 0;
                if (float.IsNaN(v) || float.IsInfinity(v) || (!signed && v < 0f) || v > 1e6f || v < -1e6f)
                    fields[i].SetValue(this, fields[i].GetValue(def));
            }
            FixPair(ref spinLo, ref spinHi); FixPair(ref gLo, ref gHi); FixPair(ref qLo, ref qHi);
            FixPair(ref groundVsLo, ref groundVsHi); FixPair(ref heatLo, ref heatHi);
            FixPair(ref gStrainLo, ref gStrainHi); FixPair(ref staticMin, ref staticMax);
            FixPair(ref crackleMin, ref crackleMax); FixPair(ref chopMin, ref chopMax);
            FixPair(ref reopenDelayMin, ref reopenDelayMax); FixPair(ref mcTenseLo, ref mcTenseHi);
            FixPair(ref intercomGapMin, ref intercomGapMax); FixPair(ref intercomMaxStress, ref intercomAbortStress);
            FixPair(ref dockApproachDist, ref dockRearmDist);
            FixPair(ref stationMcMin, ref stationMcMax); FixPair(ref dockCloseMin, ref dockCloseMax);
            FixPair(ref dockCloseDist, ref dockCloseExit); FixPair(ref descentVolLo, ref descentVolHi);
            if (stationMcMin < 20f) stationMcMin = 20f;
            if (dockCloseMin < 3f) dockCloseMin = 3f;
            crewStressSemitones = DcMath.Clamp(crewStressSemitones, 0f, 12f);
            crewPitchMax = DcMath.Clamp(crewPitchMax, 0.8f, 2f);
            truncStart = DcMath.Clamp01(truncStart);
            truncMinFrac = DcMath.Clamp(truncMinFrac, 0.2f, 1f);
            overlapMax = DcMath.Clamp01(overlapMax);
            stationMcFactor = DcMath.Clamp01(stationMcFactor);
            intercomInterval = DcMath.Clamp(intercomInterval, 10f, 3600f);
            if (intercomRoomAtten > 10000f) intercomRoomAtten = 10000f;
            if (intercomReverbAtten > 10000f) intercomReverbAtten = 10000f;
            if (intercomReflAtten > 10000f) intercomReflAtten = 10000f;
            intercomDecay = DcMath.Clamp(intercomDecay, 0.1f, 20f);
            intercomMinPitchGap = DcMath.Clamp(intercomMinPitchGap, 0f, 0.3f);
            intercomStressMin = DcMath.Clamp(intercomStressMin, 5f, 3600f);
            intercomStressPeak = DcMath.Clamp(intercomStressPeak, 0.05f, 1f);
            if (stationRateFactor < 0.05f) stationRateFactor = 0.05f;
            evaRateFactor = DcMath.Clamp(evaRateFactor, 0.25f, 4f);
            warpRateGain = DcMath.Clamp(warpRateGain, 0f, 2f);
            warpRateMax = DcMath.Clamp(warpRateMax, 1f, 5f);
            warpPitchGain = DcMath.Clamp(warpPitchGain, 0f, 0.2f);
            warpPitchMax = DcMath.Clamp(warpPitchMax, 1f, 1.5f);
            if (stationPingFactor < 0.1f) stationPingFactor = 0.1f;
            if (intercomStationBoost < 0.1f) intercomStationBoost = 0.1f;
            if (intercomQuietBoost < 0.1f) intercomQuietBoost = 0.1f;
            stationCalmBias = DcMath.Clamp01(stationCalmBias);
            stationMcFactor = DcMath.Clamp01(stationMcFactor);
            tQuiet = DcMath.Clamp(tQuiet, 5f, 1800f);
            if (tManic < 0.2f) tManic = 0.2f;
            if (gateFadeTime > 0.45f) gateFadeTime = 0.45f; // crew-only rule: silence within 0.5 s
            if (crackleMin < 0.05f) crackleMin = 0.05f;
            if (descentFast < 0.1f) descentFast = 0.1f;

            // Ranges per field. Fields without a range here only get the generic checks above.
            calloutHoldPad = DcMath.Clamp(calloutHoldPad, 0f, 2f);
            genderPitchFemale = DcMath.Clamp(genderPitchFemale, 0.8f, 1.3f);
            genderPitchMale = DcMath.Clamp(genderPitchMale, 0.8f, 1.3f);
            launchCap = launchCap >= 0.5f ? 1f : 0f;
            panicMinGap = DcMath.Clamp(panicMinGap, 2f, 60f);
            panicCooldown = DcMath.Clamp(panicCooldown, 5f, 300f);
            screamCooldown = DcMath.Clamp(screamCooldown, 10f, 600f);
            speakerCooldown = DcMath.Clamp(speakerCooldown, 1f, 60f);
            talkBurst = DcMath.Clamp(talkBurst, 1f, 10f);
            talkPerMinute = DcMath.Clamp(talkPerMinute, 1f, 30f);
            panicEscTau = DcMath.Clamp(panicEscTau, 5f, 300f);
            planRateSec = DcMath.Clamp(planRateSec, 20f, 600f);
            burnWarnSec = DcMath.Clamp(burnWarnSec, 10f, 600f);
            alarmVolume = DcMath.Clamp(alarmVolume, 0f, 0.5f);
            lightDelayMax = DcMath.Clamp(lightDelayMax, 0f, 10f);
            lightDelayScale = DcMath.Clamp(lightDelayScale, 0f, 10f);
            alarmFadeIn = DcMath.Clamp(alarmFadeIn, 0.1f, 10f);
            alarmFadeOut = DcMath.Clamp(alarmFadeOut, 0.1f, 10f);
            alarmHold = DcMath.Clamp(alarmHold, 0f, 30f);
            alarmStress = DcMath.Clamp(alarmStress, 0f, 1f);
            causeRepeatGap = DcMath.Clamp(causeRepeatGap, 0f, 600f);
            calmRepeatGap = DcMath.Clamp(calmRepeatGap, 0f, 3600f);
            evaSettleTime = DcMath.Clamp(evaSettleTime, 0f, 30f);
            FixPair(ref dockSoftSpeed, ref dockHardSpeed);
            FixPair(ref roverSpeedLo, ref roverSpeedHi); FixPair(ref roverRoughLo, ref roverRoughHi);
            FixPair(ref roverAirTimeLo, ref roverAirTimeHi); FixPair(ref roverTiltLo, ref roverTiltHi);
            FixPair(ref signalRangeFullKm, ref signalRangeZeroKm);

            // mouths and blackout
            mouthOpenDeg = DcMath.Clamp(mouthOpenDeg, 0f, 45f);
            mouthAxis = DcMath.Clamp((float)Math.Round(mouthAxis), 0f, 2f);
            mouthInvert = mouthInvert >= 0.5f ? 1f : 0f;
            mouthPortraitAlternate = mouthPortraitAlternate >= 0.5f ? 1f : 0f;
            portraitRefresh = portraitRefresh >= 0.5f ? 1f : 0f;
            mouthGain = DcMath.Clamp(mouthGain, 0f, 50f);
            mouthGate = DcMath.Clamp(mouthGate, 0f, 0.5f);
            mouthAttack = DcMath.Clamp(mouthAttack, 0.005f, 1f);
            mouthRelease = DcMath.Clamp(mouthRelease, 0.005f, 2f);
            blackoutIntercomMaxStress = DcMath.Clamp01(blackoutIntercomMaxStress);
            blackoutIntercomFirst = DcMath.Clamp(blackoutIntercomFirst, 0f, 120f);
            blackoutIntercomGap = DcMath.Clamp(blackoutIntercomGap, 5f, 600f);

            // personality, events and faces
            traitPreference = DcMath.Clamp01(traitPreference);
            personaPreference = DcMath.Clamp01(personaPreference);
            powerLowFrac = DcMath.Clamp(powerLowFrac, 0f, 0.9f);
            powerOkFrac = DcMath.Clamp(powerOkFrac, 0f, 1f);
            FixPair(ref powerLowFrac, ref powerOkFrac);
            powerMinMax = DcMath.Clamp(powerMinMax, 0f, 100000f);
            scienceCooldown = DcMath.Clamp(scienceCooldown, 0f, 3600f);
            scienceTopicCooldown = DcMath.Clamp(scienceTopicCooldown, 0f, 36000f);
            soiCooldown = DcMath.Clamp(soiCooldown, 0f, 3600f);
            partLostWindow = DcMath.Clamp(partLostWindow, 0.1f, 10f);
            partLostCooldown = DcMath.Clamp(partLostCooldown, 0f, 600f);
            signalHold = DcMath.Clamp(signalHold, 0f, 60f);
            signalCooldown = DcMath.Clamp(signalCooldown, 0f, 3600f);
            crewLostDelay = DcMath.Clamp(crewLostDelay, 0f, 60f);
            somberTime = DcMath.Clamp(somberTime, 0f, 3600f);
            eventMaxAge = DcMath.Clamp(eventMaxAge, 1f, 3600f);
            headTurnMaxDeg = DcMath.Clamp(headTurnMaxDeg, 0f, 60f);
            headTurnAxis = DcMath.Clamp((float)Math.Round(headTurnAxis), 0f, 2f);
            headTurnInvert = headTurnInvert >= 0.5f ? 1f : 0f;
            headTurnSpeed = DcMath.Clamp(headTurnSpeed, 0.1f, 30f);
            headNodDeg = DcMath.Clamp(headNodDeg, 0f, 15f);
            headNodInvert = headNodInvert >= 0.5f ? 1f : 0f;
            mouthLead = DcMath.Clamp(mouthLead, 0f, 0.3f);
            faceWeight = DcMath.Clamp01(faceWeight);
            faceExpressionSign = faceExpressionSign < 0f ? -1f : 1f;

            // probe mode, EVA relay and moments
            hailInterval = DcMath.Clamp(hailInterval, 60f, 1800f);
            evaRelayFullKm = DcMath.Clamp(evaRelayFullKm, 0.01f, 100f);
            evaBreathRate = DcMath.Clamp(evaBreathRate, 1f, 2f);
            evaBreathGain = DcMath.Clamp(evaBreathGain, 1f, 3f);
            evaRelayZeroKm = DcMath.Clamp(evaRelayZeroKm, 0.02f, 100f);
            FixPair(ref evaRelayFullKm, ref evaRelayZeroKm);
            probeCalloutMcChance = DcMath.Clamp01(probeCalloutMcChance);
            probeCalloutMcGap = DcMath.Clamp(probeCalloutMcGap, 0f, 600f);
            sayAgainSignal = DcMath.Clamp01(sayAgainSignal);
            sayAgainChance = DcMath.Clamp01(sayAgainChance);
            sayAgainCooldown = DcMath.Clamp(sayAgainCooldown, 0f, 3600f);
            roverFastSpeed = DcMath.Clamp(roverFastSpeed, 1f, 100f);
            roverFastSec = DcMath.Clamp(roverFastSec, 0f, 60f);
            roverAirSec = DcMath.Clamp(roverAirSec, 0.1f, 10f);
            roverTiltDeg = DcMath.Clamp(roverTiltDeg, 5f, 90f);
            roverIdleSec = DcMath.Clamp(roverIdleSec, 30f, 7200f);
            roverMomentCooldown = DcMath.Clamp(roverMomentCooldown, 10f, 3600f);
            dataBeepGap = DcMath.Clamp(dataBeepGap, 2f, 120f);
            dataBeepMax = DcMath.Clamp((float)Math.Round(dataBeepMax), 1f, 20f);
            probeRateFactor = DcMath.Clamp(probeRateFactor, 0.5f, 4f);
            probeCrewShare = DcMath.Clamp01(probeCrewShare);
            probeDrowsyFrac = DcMath.Clamp(probeDrowsyFrac, 0f, 0.5f);
            probeSleepFrac = DcMath.Clamp(probeSleepFrac, 0f, 0.2f);
            if (probeSleepFrac > probeDrowsyFrac) probeSleepFrac = probeDrowsyFrac;
            probeBeepPitchMax = DcMath.Clamp(probeBeepPitchMax, 1f, 2f);
        }

        private static void FixPair(ref float lo, ref float hi)
        {
            if (hi <= lo) hi = lo + 0.001f;
        }
    }

    public class Settings
    {
        public bool enabled = true;
        public bool muted = false;
        public float masterVolume = 0.8f;
        public float crewVolume = 1.0f;
        public float controlVolume = 0.8f;
        public float beepVolume = 0.6f;
        public float sfxVolume = 1.0f;          // sound effects of the ship itself (docking), not a radio sound
        public float staticVolume = 0.5f;
        public float weakSignalVolume = 0.4f;   // extra static, bursts and grit on MC at a weak signal (1 = the old, harsher level)
        public float intercomVolume = 0.8f;
        public bool intercomEnabled = true;
        public bool radioEnabled = true;      // crew <-> Mission Control radio (lines, quindar, squelch, bed, blackout static)
        public bool beepsEnabled = true;      // pings, descent blips, stage tones
        public bool landingTones = false;     // landing/splashdown tones (off: touchdown is marked by the exchange only)
        public bool stageTone = false;        // also play the beeps/stage tone (it is always the fallback without callout clips)
        public int tuningVersion = Tuning.CurrentVersion;
        public float rateMultiplier = 1.0f;
        public bool showDebug = false;
        public string muteKey = "Y";
        public string muteModifier = "LeftAlt";
        public string countdownKey = "C";           // launch countdown hotkey (start, or stop with the yell), "None" = off
        public string countdownModifier = "LeftAlt";
        public bool useGameVolume = true;
        public bool useDsp = true;
        public int maxCrewVoices = 2;
        public bool verboseLog = false;
        public float windowX = 200f;
        public float windowY = 120f;

        // --- Player settings (the main window). ---
        public float calloutVolume = 1.0f;          // 0..2
        public float voiceBoostDb = 6f;             // 0..12
        public bool voiceCompressor = true;         // voice clarity: presence EQ and compressor
        public string duckMode = "listener";        // listener | off
        public float duckDepth = 0.35f;             // 0..0.8
        public bool throttleCallouts = true;        // callouts at full throttle and cut-off
        public bool roverStress = true;             // rough rover driving adds stress
        public string stationStyle = "auto";        // auto | check | exchange
        public string radioProfile = "auto";        // auto | station | ship | eva
        public bool evaRadio = true;                // EVA kerbals talk on the suit radio
        public bool warpChatter = true;             // calm chatter during rails warp, events wait for the end of it
        public bool evaGroup = true;                // kerbals nearby (EVA, other ships) join the radio
        public bool evaRelay = true;                // the EVA suit radio reaches MC through a nearby ship
        public float evaRadioVolume = 0.8f;         // 0..1
        public bool planChatter = true;             // chatter about maneuver nodes
        public string signalMode = "auto";          // auto | off | fixed
        public float signalFixed = 0.7f;            // 0..1
        public bool signalOcclusion = true;         // a body between vessel and Kerbin weakens the signal
        public bool subtitles = true;               // on by default
        public bool countdownAutoStage = false;     // launch countdown: activate the next stage on liftoff
        public bool countdownDown = false;          // count 10 .. 1 instead of 1 .. 10
        public bool rogerBeep = true;               // short beep when the crew lets go of the key
        public bool lightDelay = true;              // small extra answer delay far from Kerbin (max lightDelayMax s)
        public bool runtimeVoice = true;            // lines spoken at runtime by Piper (names, bodies, extra rows). On by default
        public bool runtimeReports = true;          // with runtimeVoice: Mission Control reads out real values now and then
        /// <summary>Mod folders (GameData/&lt;Mod&gt;/Lonapse) whose lines the player muted, split by commas.</summary>
        public string mutedMods = "";
        /// <summary>The Mods tab shows the tools for modders (reload, test, the last events).</summary>
        public bool modTools = false;
        public bool runtimeScience = true;          // with runtimeVoice: science points and contract funds
        public bool signalDropouts = true;          // weak signal: MC lines drop out and crunch
        public bool masterAlarm = true;             // soft caution tone in the background while a kerbal panics
        public bool dockingSfx = true;              // airlock sounds on dock and undock
        public bool evaBreathing = true;            // helmet breathing under the radio on EVA
        public bool flagSfx = true;                 // a thud and a flutter when a kerbal plants a flag
        public bool hatchSfx = true;                // hatch sounds when a kerbal goes out on EVA and comes back in
        public bool soiSfx = true;                  // a soft ping when the ship enters a new sphere of influence
        // --- Mouths ---
        public bool mouthAnimation = true;          // kerbals move their jaw while they talk
        public bool mouthDebug = false;             // log and show mouth driver details
        // --- Personality, faces, events and subtitles ---
        public bool personalityEffects = true;      // courage, stupidity, badass and veteran shape lines and panic
        public bool faceExpressions = true;         // faces match the line
        public bool headTurn = true;                // listeners turn their head to the speaker
        public bool subtitleSpeaker = true;         // speaker name above the subtitle
        public bool subtitleColors = true;          // colour per speaker, MC amber
        public bool eventSoi = true;                // entering a new sphere of influence
        public bool eventScience = true;            // science results
        public bool eventPartLoss = true;           // parts lost
        public bool eventPower = true;              // low and restored electric charge
        public bool eventSignal = true;             // signal lost and back
        public bool eventCrewLoss = true;           // a kerbal dies
        public string subtitlePos = "above_navball"; // above_navball (default, at X 0.5 / Y 0.78) | custom | top_left | bottom_left
        public float subtitleX = 0.5f;              // custom: centre of the subtitle column, fraction of the screen width
        public float subtitleY = 0.78f;             // custom: bottom edge of the newest subtitle, fraction of the screen height
        public float subtitleSize = 1.0f;           // 0.7 .. Text 15 px x size
        public float subtitleOpacity = 0.55f;       // panel opacity 0 .. 1
        public int subtitleMaxEntries = 2;          // 1 .. 4
        public float subtitleWidth = 1.0f;          // 0.5 .. Times the width of the position preset
        public float subtitleHold = SubtitleLayout.DefaultHold; // s a subtitle stays after its line ends, 0 .. 10
        public float radioStatic = 0.5f;            // 0..1, static under every radio line at any signal (DcMath.StaticCurve)
        public bool intercomRadioFx = false;        // crew-to-crew lines get the radio sound, not the cabin sound
        // --- Probe mode ---
        public bool probeMode = true;               // uncrewed vessels with a probe core get probe chatter
        public bool probePetTalk = true;            // MC talks to the probe, the probe answers in beeps
        public bool probeHail = true;               // the probe beeps first, Mission Control answers
        public bool probeCallouts = true;           // the probe beeps at staging, chutes and throttle
        public bool probePlanTalk = true;           // maneuver node and burn talk with the probe
        public bool probeStressTalk = true;         // mission Control calms the probe per stress cause
        public bool probeSayAgain = true;           // "say again?" when the signal is weak
        public bool probeCrewChats = true;          // a probe core on a crewed vessel beeps and the crew answers
        public bool probeMilestones = true;         // mission days, first orbit, first landing per body
        public bool probeRover = true;              // rover moments (fast, air time, tilt, idle)
        public bool probeTransmit = true;           // data beeps while an antenna sends science
        public bool probeSleep = true;              // drowsy and asleep probe on low electric charge
        public Tuning tuning = new Tuning();
        // --- Probe voices made or changed in the window (PROBE_VOICE nodes), and the voice per core part
        // (PROBE_PART nodes, key = ProbeCores.Normalise(part name)). Both win over GameData and stock.
        public System.Collections.Generic.List<ProbeVoiceSpec> probeVoices = new System.Collections.Generic.List<ProbeVoiceSpec>();
        public System.Collections.Generic.Dictionary<string, string> probeParts = new System.Collections.Generic.Dictionary<string, string>();
        public const string ProbeVoiceNodeName = "PROBE_VOICE", ProbePartNodeName = "PROBE_PART";

        /// <summary>The settings voice of that name, null when there is none.</summary>
        public ProbeVoiceSpec SettingsVoice(string name)
        {
            for (int i = 0; i < probeVoices.Count; i++) if (probeVoices[i].Name == name) return probeVoices[i];
            return null;
        }

        /// <summary>Allowed subtitlePos values, the first is the default. SubtitleLayout uses the same strings.</summary>
        public static readonly string[] SubtitlePresets = { "above_navball", "custom", "top_left", "bottom_left" };
        public const int SubtitleMaxEntriesCap = 4;

        // not persisted: set when the existing file could not be parsed, so the first save backs it up first
        [NonSerialized] public bool loadFailed;

        public const string NodeName = "LONAPSE";
        public const string LegacyNodeName = "QUINDAR";   // settings written before the rename to Lonapse
        public const string TuningNodeName = "TUNING";

        /// <summary>
        /// Pure part of the migration: resets fields whose defaults changed since <paramref name="fromVersion"/>.
        /// Returns the names that were reset (empty when nothing changed).
        /// </summary>
        public System.Collections.Generic.List<string> MigrateValues(int fromVersion)
        {
            System.Collections.Generic.List<string> reset = new System.Collections.Generic.List<string>();
            if (fromVersion >= Tuning.CurrentVersion) return reset;
            Tuning def = new Tuning();
            if (fromVersion < 3)
            {
                for (int i = 0; i < Tuning.ChangedInV3.Length; i++)
                {
                    FieldInfo f = typeof(Tuning).GetField(Tuning.ChangedInV3[i]);
                    if (f == null) continue;
                    float oldV = (float)f.GetValue(tuning), newV = (float)f.GetValue(def);
                    if (oldV != newV)
                    {
                        f.SetValue(tuning, newV);
                        reset.Add(f.Name + " " + oldV.ToString(CultureInfo.InvariantCulture) + " -> " + newV.ToString(CultureInfo.InvariantCulture));
                    }
                }
                // at most 2 overlapping crew voices now (the old default 3 was saved by every earlier version)
                if (maxCrewVoices == 3) { maxCrewVoices = 2; reset.Add("maxCrewVoices 3 -> 2"); }
            }
            if (fromVersion < 4)
            {
                // Intercom 300 s and stress-driven. Only replace values that still hold an old default.
                ResetIfOld(reset, "intercomInterval", 600f, 240f);
                ResetIfOld(reset, "intercomMaxStress", 0.35f, 0.35f);
                ResetIfOld(reset, "intercomAbortStress", 0.5f, 0.5f);
            }
            if (fromVersion < 5)
            {
                // Light delay up to 10 s (was 2 s). Only replace the old defaults.
                ResetIfOld(reset, "lightDelayMax", 2f, 2f);
                ResetIfOld(reset, "lightDelayScale", 0.45f, 0.45f);
            }
            if (fromVersion < 6)
            {
                // Stress builds slower, spin counts from 1 rad/s. Only replace old defaults.
                ResetIfOld(reset, "spinLo", 0.35f, 0.35f);
                ResetIfOld(reset, "spinHi", 2.5f, 2.5f);
                ResetIfOld(reset, "attackTau", 0.35f, 0.35f);
            }
            if (fromVersion < 7)
            {
                // Wider jaw so talking reads through expressions. Only replace the old default.
                ResetIfOld(reset, "mouthOpenDeg", 12f, 12f);
            }
            if (fromVersion < 8 && subtitlePos == "above_navball")
            {
                // Custom (X 0.5, Y 0.78) is the default subtitle position. Only the old default moves.
                subtitlePos = "custom";
                reset.Add("subtitlePos");
            }
            if (fromVersion < 10)
            {
                // Less pitch under stress, panic sounded like chipmunks. Only the old defaults move.
                ResetIfOld(reset, "crewStressSemitones", 2f, 2f);
                ResetIfOld(reset, "crewPitchMax", 1.2f, 1.2f);
            }
            if (fromVersion < 9 && subtitlePos == "custom" && subtitleX == 0.5f && subtitleY == 0.78f)
            {
                // above_navball sits at the old custom default and is the default again. Custom moved by the user stays.
                subtitlePos = "above_navball";
                reset.Add("subtitlePos");
            }
            if (fromVersion < 11)
            {
                // Crew chats every 60 s (was 300), probes talk as often as crews (was 1.5x the gap) and
                // subtitles are on by default. Only the old defaults move. Subtitles were off by
                // default before, so an old file with them off gets them on. Players can turn them off again.
                ResetIfOld(reset, "intercomInterval", 300f, 300f);
                ResetIfOld(reset, "probeRateFactor", 1.5f, 1.5f);
                ResetIfOld(reset, "scienceTopicCooldown", 300f, 300f);  // 60 s, a new biome gets its line
                ResetIfOld(reset, "throttleMinGap", 20f, 20f);          // 5 s, quick throttle changes get callouts
                ResetIfOld(reset, "intercomStationBoost", 2.5f, 2.5f);  // 1, the 60 s gap is lively enough
                ResetIfOld(reset, "intercomQuietBoost", 2f, 2f);        // 1, the same
                if (!subtitles) { subtitles = true; reset.Add("subtitles off -> on"); }
            }
            if (fromVersion < 12)
            {
                // The runtime voice is on by default. It was off in the development builds, so an old file
                // with it off gets it on. Players can turn it off again.
                if (!runtimeVoice) { runtimeVoice = true; reset.Add("runtimeVoice off -> on"); }
            }
            if (fromVersion < 13)
            {
                // No stress pitch on the crew any more, 0.5 semitones still sounded like chipmunks. Only the old
                // default moves.
                ResetIfOld(reset, "crewStressSemitones", 0.5f, 0.5f);
            }
            tuningVersion = Tuning.CurrentVersion;
            return reset;
        }

        private void ResetIfOld(System.Collections.Generic.List<string> reset, string field, float old1, float old2)
        {
            FieldInfo f = typeof(Tuning).GetField(field);
            if (f == null) return;
            float cur = (float)f.GetValue(tuning);
            if (cur != old1 && cur != old2) return; // the user changed it: keep
            float def = (float)f.GetValue(new Tuning());
            if (cur == def) return;
            f.SetValue(tuning, def);
            reset.Add(field + " " + cur.ToString(CultureInfo.InvariantCulture) + " -> " + def.ToString(CultureInfo.InvariantCulture));
        }

        public void Clamp()
        {
            masterVolume = DcMath.Clamp(DcMath.Finite(masterVolume), 0f, 1f);
            crewVolume = DcMath.Clamp(DcMath.Finite(crewVolume), 0f, 1f);
            controlVolume = DcMath.Clamp(DcMath.Finite(controlVolume), 0f, 1f);
            beepVolume = DcMath.Clamp(DcMath.Finite(beepVolume), 0f, 1f);
            sfxVolume = DcMath.Clamp(DcMath.Finite(sfxVolume), 0f, 1f);
            staticVolume = DcMath.Clamp(DcMath.Finite(staticVolume), 0f, 1f);
            weakSignalVolume = DcMath.Clamp(DcMath.Finite(weakSignalVolume), 0f, 1f);
            intercomVolume = DcMath.Clamp(DcMath.Finite(intercomVolume), 0f, 1f);
            if (float.IsNaN(rateMultiplier) || float.IsInfinity(rateMultiplier)) rateMultiplier = 1f;
            rateMultiplier = DcMath.Clamp(rateMultiplier, 0.25f, 4f);
            if (maxCrewVoices < 1) maxCrewVoices = 1;
            if (maxCrewVoices > 3) maxCrewVoices = 3;
            windowX = DcMath.Clamp(DcMath.Finite(windowX), 0f, 8000f);
            windowY = DcMath.Clamp(DcMath.Finite(windowY), 0f, 8000f);
            if (string.IsNullOrEmpty(muteKey)) muteKey = "Y";
            if (string.IsNullOrEmpty(muteModifier)) muteModifier = "None";
            if (string.IsNullOrEmpty(countdownKey)) countdownKey = "None";
            if (string.IsNullOrEmpty(countdownModifier)) countdownModifier = "None";
            calloutVolume = DcMath.Clamp(DcMath.Finite(calloutVolume), 0f, 2f);
            voiceBoostDb = DcMath.Clamp(DcMath.Finite(voiceBoostDb), 0f, 12f);
            duckDepth = DcMath.Clamp(DcMath.Finite(duckDepth), 0f, 0.8f);
            evaRadioVolume = DcMath.Clamp(DcMath.Finite(evaRadioVolume), 0f, 1f);
            signalFixed = DcMath.Clamp(DcMath.Finite(signalFixed), 0f, 1f);
            duckMode = OneOf(duckMode, "listener", "listener", "off");
            stationStyle = OneOf(stationStyle, "auto", "auto", "check", "exchange");
            radioProfile = OneOf(radioProfile, "auto", "auto", "station", "ship", "eva");
            signalMode = OneOf(signalMode, "auto", "auto", "off", "fixed");
            subtitlePos = OneOf(subtitlePos, SubtitlePresets[0], SubtitlePresets);
            if (float.IsNaN(subtitleX) || float.IsInfinity(subtitleX)) subtitleX = 0.5f;
            if (float.IsNaN(subtitleY) || float.IsInfinity(subtitleY)) subtitleY = 0.78f;
            subtitleX = DcMath.Clamp(subtitleX, 0f, 1f);
            subtitleY = DcMath.Clamp(subtitleY, 0f, 1f);
            if (float.IsNaN(subtitleSize) || float.IsInfinity(subtitleSize)) subtitleSize = 1f;
            subtitleSize = DcMath.Clamp(subtitleSize, 0.7f, 1.6f);
            if (float.IsNaN(subtitleOpacity) || float.IsInfinity(subtitleOpacity)) subtitleOpacity = 0.55f;
            subtitleOpacity = DcMath.Clamp(subtitleOpacity, 0f, 1f);
            if (float.IsNaN(subtitleWidth) || float.IsInfinity(subtitleWidth)) subtitleWidth = 1f;
            subtitleWidth = DcMath.Clamp(subtitleWidth, SubtitleLayout.MinWidthScale, SubtitleLayout.MaxWidthScale);
            if (float.IsNaN(subtitleHold) || float.IsInfinity(subtitleHold)) subtitleHold = SubtitleLayout.DefaultHold;
            subtitleHold = DcMath.Clamp(subtitleHold, 0f, SubtitleLayout.MaxHold);
            radioStatic = DcMath.Clamp(DcMath.Finite(radioStatic), 0f, 1f);
            if (subtitleMaxEntries < 1) subtitleMaxEntries = 1;
            if (subtitleMaxEntries > SubtitleMaxEntriesCap) subtitleMaxEntries = SubtitleMaxEntriesCap;
            if (tuning == null) tuning = new Tuning();
            tuning.Sanitize();
            if (probeVoices == null) probeVoices = new System.Collections.Generic.List<ProbeVoiceSpec>();
            if (probeParts == null) probeParts = new System.Collections.Generic.Dictionary<string, string>();
            // one entry per name, the last one wins (as when the file is read)
            var seenVoice = new System.Collections.Generic.HashSet<string>();
            for (int i = probeVoices.Count - 1; i >= 0; i--)
                if (probeVoices[i] == null || string.IsNullOrEmpty(probeVoices[i].Name) || !seenVoice.Add(probeVoices[i].Name)) probeVoices.RemoveAt(i);
        }

        /// <summary>Returns the trimmed, lower-case value when it is one of the allowed values, otherwise <paramref name="def"/>.</summary>
        private static string OneOf(string value, string def, params string[] allowed)
        {
            if (string.IsNullOrEmpty(value)) return def;
            string key = value.Trim().ToLowerInvariant();
            for (int i = 0; i < allowed.Length; i++)
                if (allowed[i] == key) return key;
            return def;
        }

#if !DC_OFFLINE_TEST
        public static string FilePath
        {
            get { return Path.Combine(Path.Combine(KSPUtil.ApplicationRootPath, "GameData/Lonapse/PluginData"), "settings.cfg"); }
        }

        public KeyCode MuteKeyCode { get { return ParseKey(muteKey, KeyCode.Y); } }
        public KeyCode MuteModifierCode { get { return ParseKey(muteModifier, KeyCode.None); } }
        public KeyCode CountdownKeyCode { get { return ParseKey(countdownKey, KeyCode.None); } }
        public KeyCode CountdownModifierCode { get { return ParseKey(countdownModifier, KeyCode.None); } }

        private static KeyCode ParseKey(string s, KeyCode fallback)
        {
            if (string.IsNullOrEmpty(s)) return fallback;
            try { return (KeyCode)Enum.Parse(typeof(KeyCode), s.Trim(), true); }
            catch (Exception) { Log.Warn("Unknown KeyCode '" + s + "', using " + fallback); return fallback; }
        }

        /// <summary>PluginData of the mod before it was renamed to Lonapse (GameData/Quindar).</summary>
        public static string LegacyDir
        {
            get { return Path.Combine(KSPUtil.ApplicationRootPath, "GameData/Quindar/PluginData"); }
        }

        /// <summary>Rename to Lonapse: on the first start the old PluginData files (settings, backups, dumps) are
        /// copied into the new folder. Nothing is overwritten and the old folder is left alone.</summary>
        private static void AdoptLegacyFiles(string path)
        {
            if (File.Exists(path)) return;
            string old = LegacyDir;
            if (!Directory.Exists(old) || !File.Exists(Path.Combine(old, "settings.cfg"))) return;
            try
            {
                string dir = Path.GetDirectoryName(path);
                Directory.CreateDirectory(dir);
                int n = 0;
                foreach (string f in Directory.GetFiles(old))
                {
                    string to = Path.Combine(dir, Path.GetFileName(f));
                    if (File.Exists(to)) continue;
                    File.Copy(f, to);
                    n++;
                }
                Log.Info("Rename to Lonapse: copied " + n + " file(s) from " + old + " to " + dir);
            }
            catch (Exception e) { Log.Warn("Rename to Lonapse: could not copy the old settings (" + e.Message + ")"); }
        }

        public static Settings Load()
        {
            Settings s = new Settings();
            string path = FilePath;
            try
            {
                AdoptLegacyFiles(path);
                if (!File.Exists(path))
                {
                    Log.Info("No settings file at " + path + ", writing defaults");
                    s.Clamp();
                    s.Save();
                    return s;
                }
                ConfigNode root = ConfigNode.Load(path);
                ConfigNode n = root != null ? (root.GetNode(NodeName) ?? root.GetNode(LegacyNodeName)) : null;
                if (n == null)
                {
                    Log.Warn("Settings file could not be parsed or has no " + NodeName + " node, using defaults: " + path);
                    s.Clamp();
                    s.loadFailed = true;
                    return s;
                }
                n.TryGetValue("enabled", ref s.enabled);
                n.TryGetValue("muted", ref s.muted);
                n.TryGetValue("masterVolume", ref s.masterVolume);
                n.TryGetValue("crewVolume", ref s.crewVolume);
                n.TryGetValue("controlVolume", ref s.controlVolume);
                n.TryGetValue("beepVolume", ref s.beepVolume);
                n.TryGetValue("sfxVolume", ref s.sfxVolume);
                n.TryGetValue("staticVolume", ref s.staticVolume);
                n.TryGetValue("weakSignalVolume", ref s.weakSignalVolume);
                n.TryGetValue("intercomVolume", ref s.intercomVolume);
                n.TryGetValue("intercomEnabled", ref s.intercomEnabled);
                n.TryGetValue("radioEnabled", ref s.radioEnabled);
                n.TryGetValue("beepsEnabled", ref s.beepsEnabled);
                n.TryGetValue("landingTones", ref s.landingTones);
                n.TryGetValue("stageTone", ref s.stageTone);
                int ver = 2; // files without the key were written by 0.1/0.2
                n.TryGetValue("tuningVersion", ref ver);
                s.tuningVersion = ver;
                n.TryGetValue("rateMultiplier", ref s.rateMultiplier);
                n.TryGetValue("showDebug", ref s.showDebug);
                n.TryGetValue("muteKey", ref s.muteKey);
                n.TryGetValue("countdownKey", ref s.countdownKey);
                n.TryGetValue("countdownModifier", ref s.countdownModifier);
                n.TryGetValue("muteModifier", ref s.muteModifier);
                n.TryGetValue("useGameVolume", ref s.useGameVolume);
                n.TryGetValue("useDsp", ref s.useDsp);
                n.TryGetValue("maxCrewVoices", ref s.maxCrewVoices);
                n.TryGetValue("verboseLog", ref s.verboseLog);
                n.TryGetValue("windowX", ref s.windowX);
                n.TryGetValue("windowY", ref s.windowY);
                n.TryGetValue("calloutVolume", ref s.calloutVolume);
                n.TryGetValue("voiceBoostDb", ref s.voiceBoostDb);
                n.TryGetValue("voiceCompressor", ref s.voiceCompressor);
                n.TryGetValue("duckMode", ref s.duckMode);
                n.TryGetValue("duckDepth", ref s.duckDepth);
                n.TryGetValue("throttleCallouts", ref s.throttleCallouts);
                n.TryGetValue("roverStress", ref s.roverStress);
                n.TryGetValue("stationStyle", ref s.stationStyle);
                n.TryGetValue("radioProfile", ref s.radioProfile);
                n.TryGetValue("evaRadio", ref s.evaRadio);
                n.TryGetValue("warpChatter", ref s.warpChatter);
                n.TryGetValue("evaGroup", ref s.evaGroup);
                n.TryGetValue("evaRelay", ref s.evaRelay);
                n.TryGetValue("evaRadioVolume", ref s.evaRadioVolume);
                n.TryGetValue("planChatter", ref s.planChatter);
                n.TryGetValue("signalMode", ref s.signalMode);
                n.TryGetValue("signalFixed", ref s.signalFixed);
                n.TryGetValue("signalOcclusion", ref s.signalOcclusion);
                n.TryGetValue("subtitles", ref s.subtitles);
                n.TryGetValue("countdownAutoStage", ref s.countdownAutoStage);
                n.TryGetValue("countdownDown", ref s.countdownDown);
                n.TryGetValue("rogerBeep", ref s.rogerBeep);
                n.TryGetValue("lightDelay", ref s.lightDelay);
                n.TryGetValue("runtimeVoice", ref s.runtimeVoice);
                n.TryGetValue("runtimeReports", ref s.runtimeReports);
                n.TryGetValue("runtimeScience", ref s.runtimeScience);
                n.TryGetValue("mutedMods", ref s.mutedMods);
                n.TryGetValue("modTools", ref s.modTools);
                n.TryGetValue("signalDropouts", ref s.signalDropouts);
                n.TryGetValue("masterAlarm", ref s.masterAlarm);
                n.TryGetValue("dockingSfx", ref s.dockingSfx);
                n.TryGetValue("soiSfx", ref s.soiSfx);
                n.TryGetValue("hatchSfx", ref s.hatchSfx);
                n.TryGetValue("flagSfx", ref s.flagSfx);
                n.TryGetValue("evaBreathing", ref s.evaBreathing);
                n.TryGetValue("mouthAnimation", ref s.mouthAnimation);
                n.TryGetValue("mouthDebug", ref s.mouthDebug);
                n.TryGetValue("personalityEffects", ref s.personalityEffects);
                n.TryGetValue("faceExpressions", ref s.faceExpressions);
                n.TryGetValue("headTurn", ref s.headTurn);
                n.TryGetValue("subtitleSpeaker", ref s.subtitleSpeaker);
                n.TryGetValue("subtitleColors", ref s.subtitleColors);
                n.TryGetValue("eventSoi", ref s.eventSoi);
                n.TryGetValue("eventScience", ref s.eventScience);
                n.TryGetValue("eventPartLoss", ref s.eventPartLoss);
                n.TryGetValue("eventPower", ref s.eventPower);
                n.TryGetValue("eventSignal", ref s.eventSignal);
                n.TryGetValue("eventCrewLoss", ref s.eventCrewLoss);
                n.TryGetValue("subtitlePos", ref s.subtitlePos);
                n.TryGetValue("subtitleX", ref s.subtitleX);
                n.TryGetValue("subtitleY", ref s.subtitleY);
                n.TryGetValue("subtitleSize", ref s.subtitleSize);
                n.TryGetValue("subtitleOpacity", ref s.subtitleOpacity);
                n.TryGetValue("subtitleMaxEntries", ref s.subtitleMaxEntries);
                n.TryGetValue("subtitleWidth", ref s.subtitleWidth);
                n.TryGetValue("subtitleHold", ref s.subtitleHold);
                n.TryGetValue("radioStatic", ref s.radioStatic);
                n.TryGetValue("intercomRadioFx", ref s.intercomRadioFx);
                n.TryGetValue("probeMode", ref s.probeMode);
                n.TryGetValue("probePetTalk", ref s.probePetTalk);
                n.TryGetValue("probeHail", ref s.probeHail);
                n.TryGetValue("probeCallouts", ref s.probeCallouts);
                n.TryGetValue("probePlanTalk", ref s.probePlanTalk);
                n.TryGetValue("probeStressTalk", ref s.probeStressTalk);
                n.TryGetValue("probeSayAgain", ref s.probeSayAgain);
                n.TryGetValue("probeCrewChats", ref s.probeCrewChats);
                n.TryGetValue("probeMilestones", ref s.probeMilestones);
                n.TryGetValue("probeRover", ref s.probeRover);
                n.TryGetValue("probeTransmit", ref s.probeTransmit);
                n.TryGetValue("probeSleep", ref s.probeSleep);

                foreach (ConfigNode pv in n.GetNodes(ProbeVoiceNodeName))
                {
                    var errs = new System.Collections.Generic.List<string>();
                    ProbeVoiceSpec spec = ProbeVoiceNodes.FromNode(pv, errs);
                    foreach (string er in errs) Log.Warn("settings.cfg " + ProbeVoiceNodeName + ": " + er);
                    if (spec != null) s.probeVoices.Add(spec);
                }
                foreach (ConfigNode pp in n.GetNodes(ProbePartNodeName))
                {
                    string part = pp.GetValue("part"), voice = pp.GetValue("voice");
                    if (!string.IsNullOrEmpty(part) && !string.IsNullOrEmpty(voice))
                        s.probeParts[ProbeCores.Normalise(part)] = voice.Trim().ToLowerInvariant();
                }

                ConfigNode t = n.GetNode(TuningNodeName);
                if (t != null)
                {
                    FieldInfo[] fields = typeof(Tuning).GetFields(BindingFlags.Public | BindingFlags.Instance);
                    for (int i = 0; i < fields.Length; i++)
                    {
                        if (fields[i].FieldType != typeof(float)) continue;
                        float v = (float)fields[i].GetValue(s.tuning);
                        if (t.TryGetValue(fields[i].Name, ref v)) fields[i].SetValue(s.tuning, v);
                    }
                }
                Log.Info("Settings loaded from " + path + " (tuning version " + s.tuningVersion + ")");
                if (s.tuningVersion < Tuning.CurrentVersion) s.Migrate(path);
            }
            catch (Exception e)
            {
                Log.Warn("Could not parse settings (" + e.Message + "), using defaults");
                s = new Settings();
                s.loadFailed = true;
            }
            s.Clamp();
            return s;
        }

        public bool Save()
        {
            string path = FilePath;
            try
            {
                string dir = Path.GetDirectoryName(path);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                if (loadFailed)
                {
                    // keep the broken file instead of silently overwriting the user's edits
                    if (File.Exists(path))
                    {
                        string bad = path + ".bad";
                        File.Copy(path, bad, true);
                        Log.Warn("Unreadable settings file backed up to " + bad + " before writing defaults");
                    }
                    loadFailed = false;
                }
                ConfigNode n = new ConfigNode(NodeName);
                n.AddValue("enabled", enabled);
                n.AddValue("muted", muted);
                n.AddValue("masterVolume", F(masterVolume));
                n.AddValue("crewVolume", F(crewVolume));
                n.AddValue("controlVolume", F(controlVolume));
                n.AddValue("beepVolume", F(beepVolume));
                n.AddValue("sfxVolume", F(sfxVolume));
                n.AddValue("staticVolume", F(staticVolume));
                n.AddValue("weakSignalVolume", F(weakSignalVolume));
                n.AddValue("intercomVolume", F(intercomVolume));
                n.AddValue("intercomEnabled", intercomEnabled);
                n.AddValue("radioEnabled", radioEnabled);
                n.AddValue("beepsEnabled", beepsEnabled);
                n.AddValue("landingTones", landingTones);
                n.AddValue("stageTone", stageTone);
                n.AddValue("tuningVersion", tuningVersion);
                n.AddValue("rateMultiplier", F(rateMultiplier));
                n.AddValue("showDebug", showDebug);
                n.AddValue("muteKey", muteKey);
                n.AddValue("countdownKey", countdownKey);
                n.AddValue("countdownModifier", countdownModifier);
                n.AddValue("muteModifier", muteModifier);
                n.AddValue("useGameVolume", useGameVolume);
                n.AddValue("useDsp", useDsp);
                n.AddValue("maxCrewVoices", maxCrewVoices);
                n.AddValue("verboseLog", verboseLog);
                n.AddValue("windowX", F(windowX));
                n.AddValue("windowY", F(windowY));
                n.AddValue("calloutVolume", F(calloutVolume));
                n.AddValue("voiceBoostDb", F(voiceBoostDb));
                n.AddValue("voiceCompressor", voiceCompressor);
                n.AddValue("duckMode", duckMode);
                n.AddValue("duckDepth", F(duckDepth));
                n.AddValue("throttleCallouts", throttleCallouts);
                n.AddValue("roverStress", roverStress);
                n.AddValue("stationStyle", stationStyle);
                n.AddValue("radioProfile", radioProfile);
                n.AddValue("evaRadio", evaRadio);
                n.AddValue("warpChatter", warpChatter);
                n.AddValue("evaGroup", evaGroup);
                n.AddValue("evaRelay", evaRelay);
                n.AddValue("evaRadioVolume", F(evaRadioVolume));
                n.AddValue("planChatter", planChatter);
                n.AddValue("signalMode", signalMode);
                n.AddValue("signalFixed", F(signalFixed));
                n.AddValue("signalOcclusion", signalOcclusion);
                n.AddValue("subtitles", subtitles);
                n.AddValue("countdownAutoStage", countdownAutoStage);
                n.AddValue("countdownDown", countdownDown);
                n.AddValue("rogerBeep", rogerBeep);
                n.AddValue("lightDelay", lightDelay);
                n.AddValue("runtimeVoice", runtimeVoice);
                n.AddValue("runtimeReports", runtimeReports);
                n.AddValue("runtimeScience", runtimeScience);
                n.AddValue("mutedMods", mutedMods ?? "");
                n.AddValue("modTools", modTools);
                n.AddValue("signalDropouts", signalDropouts);
                n.AddValue("masterAlarm", masterAlarm);
                n.AddValue("dockingSfx", dockingSfx);
                n.AddValue("soiSfx", soiSfx);
                n.AddValue("hatchSfx", hatchSfx);
                n.AddValue("flagSfx", flagSfx);
                n.AddValue("evaBreathing", evaBreathing);
                n.AddValue("mouthAnimation", mouthAnimation);
                n.AddValue("mouthDebug", mouthDebug);
                n.AddValue("personalityEffects", personalityEffects);
                n.AddValue("faceExpressions", faceExpressions);
                n.AddValue("headTurn", headTurn);
                n.AddValue("subtitleSpeaker", subtitleSpeaker);
                n.AddValue("subtitleColors", subtitleColors);
                n.AddValue("eventSoi", eventSoi);
                n.AddValue("eventScience", eventScience);
                n.AddValue("eventPartLoss", eventPartLoss);
                n.AddValue("eventPower", eventPower);
                n.AddValue("eventSignal", eventSignal);
                n.AddValue("eventCrewLoss", eventCrewLoss);
                n.AddValue("subtitlePos", subtitlePos);
                n.AddValue("subtitleX", F(subtitleX));
                n.AddValue("subtitleY", F(subtitleY));
                n.AddValue("subtitleSize", F(subtitleSize));
                n.AddValue("subtitleOpacity", F(subtitleOpacity));
                n.AddValue("subtitleMaxEntries", subtitleMaxEntries);
                n.AddValue("subtitleWidth", F(subtitleWidth));
                n.AddValue("subtitleHold", F(subtitleHold));
                n.AddValue("radioStatic", F(radioStatic));
                n.AddValue("intercomRadioFx", intercomRadioFx);
                n.AddValue("probeMode", probeMode);
                n.AddValue("probePetTalk", probePetTalk);
                n.AddValue("probeHail", probeHail);
                n.AddValue("probeCallouts", probeCallouts);
                n.AddValue("probePlanTalk", probePlanTalk);
                n.AddValue("probeStressTalk", probeStressTalk);
                n.AddValue("probeSayAgain", probeSayAgain);
                n.AddValue("probeCrewChats", probeCrewChats);
                n.AddValue("probeMilestones", probeMilestones);
                n.AddValue("probeRover", probeRover);
                n.AddValue("probeTransmit", probeTransmit);
                n.AddValue("probeSleep", probeSleep);
                foreach (ProbeVoiceSpec pv in probeVoices) n.AddNode(ProbeVoiceNodes.ToNode(pv, ProbeVoiceNodeName));
                foreach (var kv in probeParts)
                {
                    ConfigNode pn = n.AddNode(ProbePartNodeName);
                    pn.AddValue("part", kv.Key);
                    pn.AddValue("voice", kv.Value);
                }
                ConfigNode t = n.AddNode(TuningNodeName);
                FieldInfo[] fields = typeof(Tuning).GetFields(BindingFlags.Public | BindingFlags.Instance);
                for (int i = 0; i < fields.Length; i++)
                {
                    if (fields[i].FieldType != typeof(float)) continue;
                    t.AddValue(fields[i].Name, F((float)fields[i].GetValue(tuning)));
                }
                ConfigNode root = new ConfigNode();
                root.AddNode(n);
                bool ok = root.Save(path);
                if (!ok) Log.Warn("ConfigNode.Save returned false for " + path);
                else Log.Debug("Settings saved to " + path);
                return ok;
            }
            catch (Exception e)
            {
                Log.Warn("Could not save settings to " + path + ": " + e.Message);
                return false;
            }
        }

        private void Migrate(string path)
        {
            int from = tuningVersion;
            System.Collections.Generic.List<string> reset = MigrateValues(from);
            try
            {
                string bak = path + ".v" + from + ".bak";
                if (File.Exists(path) && !File.Exists(bak)) File.Copy(path, bak, false);
                Log.Info("Settings migrated from tuning version " + from + " to " + Tuning.CurrentVersion + ", backup: " + bak);
            }
            catch (Exception e) { Log.Warn("Could not back up settings before migration: " + e.Message); }
            for (int i = 0; i < reset.Count; i++) Log.Info("  reset to new default: " + reset[i]);
            migrated = true;
        }

        [NonSerialized] public bool migrated; // saved once after load so the file gets the new keys

        private static string F(float v)
        {
            return v.ToString("R", CultureInfo.InvariantCulture);
        }
#endif
    }
}
