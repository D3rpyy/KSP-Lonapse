// Telemetry pings, descent blips, stage and landing tones. All obey the crew gate and radio silence.
using System;
using UnityEngine;

namespace Lonapse
{
    public class BeepDirector
    {
        private readonly LonapseAddon host;
        private readonly System.Random rng;

        private float pingLeft = 20f;
        private float descentTimer;
        public bool descentActive;
        public float descentD;
        private float lastLandingTime = -999f;
        private float warpPingLeft = -1f;   // > 0: single "radio is back" ping pending after rails warp
        private float lastWarpPingTime = -999f;

        // req 5: descent blips, touchdown tone and touchdown exchange are one sequence
        public readonly DescentSequence descent = new DescentSequence(6f);
        // req 6: a ping is keyed like a transmission: key up, ping, key down
        private float pingSeqPing = -1f, pingSeqClose = -1f;

        public BeepDirector(LonapseAddon host, System.Random rng)
        {
            this.host = host;
            this.rng = rng;
        }

        private void TickPingSequence(float dt)
        {
            if (pingSeqPing > 0f)
            {
                pingSeqPing -= dt;
                if (pingSeqPing <= 0f)
                {
                    pingSeqPing = -1f;
                    AudioClip clip = ClipLibrary.Next("beeps/ping", rng);
                    VoiceChannel b = host.beep[0];
                    if (clip != null) { b.Play(clip, Tu.pingVolume, 1f, false, 0f); pingSeqClose = b.duration + 0.03f; }
                }
            }
            else if (pingSeqClose > 0f)
            {
                pingSeqClose -= dt;
                if (pingSeqClose <= 0f)
                {
                    pingSeqClose = -1f;
                    KeyClip(false);
                }
            }
        }

        /// <summary>Key-up or key-down sound of the current radio profile on the key channel. Returns its length.</summary>
        private float KeyClip(bool open)
        {
            RadioProfile p = host.profile ?? RadioProfiles.Get(RadioContext.Ship);
            string f = open ? p.OpenClip : p.CloseClip;
            if (ClipLibrary.CountOf(f) == 0) return 0f;
            AudioClip c = ClipLibrary.Next(f, rng);
            if (c == null) return 0f;
            host.keyCh.Play(c, Tu.radioKeyVolume * 0.8f, 1f, false, 0f);
            return host.keyCh.duration;
        }

        private Tuning Tu { get { return host.settings.tuning; } }
        private float U(float lo, float hi) { return DcMath.Uniform(rng, lo, hi); }

        private float PingInterval(int situation)
        {
            Tuning tu = Tu;
            float f = host.stationMode ? tu.stationPingFactor : 1f; // stations and bases ping less often
            switch ((Vessel.Situations)situation)
            {
                case Vessel.Situations.ORBITING:
                case Vessel.Situations.ESCAPING:
                    return tu.pingOrbit * f;
                case Vessel.Situations.FLYING:
                case Vessel.Situations.SUB_ORBITAL:
                    return tu.pingFlying * f;
                default:
                    return tu.pingLanded * f;
            }
        }

        public void OnGateOpened(int situation)
        {
            pingLeft = PingInterval(situation) * U(0.5f, 1.0f);
            descentTimer = 0f;
            warpPingLeft = -1f;
        }

        /// <summary>Rails warp ended: exactly one ping after the delay, regular pings restart a full interval later.</summary>
        public void OnGateOpenedAfterWarp(int situation)
        {
            descentTimer = 0f;
            float now = Time.unscaledTime;
            if (!DcMath.WarpPingAllowed(now, lastWarpPingTime, Tu.warpPingCooldown))
            {
                // short warp taps: no ping spam, just resume the regular ping timer
                warpPingLeft = -1f;
                pingLeft = PingInterval(situation) * U(0.5f, 1.0f);
                return;
            }
            lastWarpPingTime = now;
            warpPingLeft = Math.Max(0.05f, Tu.warpEndPingDelay);
            pingLeft = warpPingLeft + PingInterval(situation) * U(0.8f, 1.2f);
        }

        public void Update(float dt, ref FlightSample s)
        {
            if (!host.CanStartAudio || host.director.radioSilent) { pingSeqPing = pingSeqClose = -1f; return; }
            if (host.director.mcBlackout) pingLeft = Math.Max(pingLeft, 1f); // no telemetry pings in a blackout, the rest goes on
            if (!host.settings.beepsEnabled) { descentActive = false; warpPingLeft = -1f; pingSeqPing = pingSeqClose = -1f; return; }
            Tuning tu = Tu;
            TickPingSequence(dt);

            if (warpPingLeft > 0f)
            {
                warpPingLeft -= dt;
                if (warpPingLeft <= 0f)
                {
                    warpPingLeft = -1f;
                    PlayPing();
                }
            }

            // descent blips: only below descentMaxH (radar altitude, default 500 m) while descending,
            // soft, and never faster than descentFast (default 1.5 s)
            // not on EVA: a jumping or falling kerbal is no vessel descent (user test)
            bool descending = s.valid && !host.evaMode && !s.landed && s.vs < -tu.descentVsMin && s.h < tu.descentMaxH;
            descent.Update(dt, descending, (float)s.h, (float)s.vs, s.valid && s.landed);
            // the sequence ends the blips at touchdown, even before the situation event arrives
            if (descent.JustAccepted && !host.evaMode)
                TouchdownSequence(s.situation == (int)Vessel.Situations.SPLASHED);
            descentActive = descending && descent.BlipsActive;
            if (descentActive)
            {
                float d = Math.Max(DcMath.Ramp(tu.descentMaxH - s.h, 0, tu.descentMaxH) * DcMath.Ramp(-s.vs, 0, tu.descentVsSpan),
                                   host.stress.comp[StressModel.Ground]);
                descentD = d;
                float interval = DescentInterval(d, tu);
                descentTimer += dt;
                if (descentTimer >= interval)
                {
                    descentTimer = 0f;
                    AudioClip clip = ClipLibrary.Next("beeps/descent", rng);
                    if (clip != null)
                        host.beep[0].Play(clip, DcMath.Lerp(tu.descentVolLo, tu.descentVolHi, d), DcMath.Lerp(1.0f, tu.descentPitchHi, d), false, 0f);
                }
                return;
            }
            descentD = 0f;
            descentTimer = 0f;

            pingLeft -= dt;
            if (pingLeft <= 0f)
            {
                if (host.control.Busy || (host.director != null && host.director.IntercomTurnOpen))
                {
                    pingLeft = 2f;
                    return;
                }
                PlayPing();
                pingLeft = PingInterval(s.situation) * U(0.8f, 1.2f);
            }
        }

        /// <summary>Descent blip interval: descentSlow at d = 0 down to descentFast (never faster) at d = 1.</summary>
        public static float DescentInterval(float d, Tuning tu)
        {
            return DcMath.DescentInterval(d, tu.descentSlow, tu.descentFast);
        }

        /// <summary>One soft telemetry ping. Returns the channel or null.</summary>
        public VoiceChannel PlayPing()
        {
            if (!host.CanStartAudio || host.director.radioSilent || host.director.mcBlackout || !host.settings.beepsEnabled) return null;
            if (ClipLibrary.CountOf("beeps/ping") == 0) return null;
            if (pingSeqPing > 0f || pingSeqClose > 0f) return null; // one keyed ping at a time
            float open = KeyClip(true);
            if (open <= 0f)
            {
                AudioClip clip = ClipLibrary.Next("beeps/ping", rng);
                if (clip == null) return null;
                host.beep[0].Play(clip, Tu.pingVolume, 1f, false, 0f);
                return host.beep[0];
            }
            pingSeqPing = Math.Max(0.02f, open - 0.04f); // ping starts on the tail of the key-up noise
            return host.keyCh;
        }

        /// <summary>
        /// Staging: one word from the pilot (crew/callout). The beeps/stage tone plays as well when stageTone is on,
        /// and as the fallback when no callout could be spoken. Allowed as soon as the gate is open (the chatter
        /// holdoff after scene load does not apply). Every outcome is logged.
        /// </summary>
        public void OnStage(int stage, string source)
        {
            string tag = "stage " + stage + (source != null ? " (" + source + ")" : "");
            if (!host.CanStartAudio) { Log.Info("Stage callout skipped, " + tag + ": gate " + (host.paused ? "paused" : host.gateReason)); return; }
            if (host.director.radioSilent) { Log.Info("Stage callout skipped, " + tag + ": radio blackout"); return; }
            float now = Time.unscaledTime;
            if (!host.callouts.TryShared(now, Tu.calloutCooldown)) { Log.Info("Stage callout skipped, " + tag + ": cooldown"); return; }

            // user request: separation with a decoupler (also with an engine), ignition for an engine alone
            StageKind kind = host.StageContents(stage);
            tag += ", " + kind;
            // a callout that waits for an intercom line returns null; if it is dropped later the stage tone
            // still plays as the fallback (unless stageTone already played it now)
            Action<string> onDrop = host.settings.stageTone ? (Action<string>)null : dropWhy =>
            {
                if (!host.CanStartAudio) { Log.Info("Stage tone skipped, " + tag + ": gate " + (host.paused ? "paused" : host.gateReason)); return; }
                if (host.director.radioSilent) { Log.Info("Stage tone skipped, " + tag + ": radio blackout"); return; }
                Log.Info("Deferred stage callout dropped, " + tag + ": " + dropWhy + ", using the stage tone");
                PlayStageTone(tag);
            };
            // plan v6: in probe mode the probe beeps the stage, with the stage kind as the scene topic
            string why = host.probeMode
                ? host.director.PlayProbeCallout("staging", kind == StageKind.Ignition ? "ignition" : kind == StageKind.Separation ? "separation" : null, tag)
                : host.director.PlayCallout("staging", null, 0f, tag, onDrop,
                    x => PacingRules.StagingSceneKind(x.Id) == kind,
                    x => PacingRules.StagingSceneKind(x.Id) == StageKind.Any);
            bool spoke = why == null;
            if (!spoke) Log.Info("Stage callout not spoken, " + tag + ": " + why + (host.settings.stageTone ? "" : ", using the stage tone"));
            if (!spoke || host.settings.stageTone) PlayStageTone(tag);
        }

        /// <summary>Plan v5: probe beeps put beep[1] on the radio band. Tones get the channel's own band back.</summary>
        private void ResetBeep1()
        {
            host.beep[1].SetBand(250f, 6000f);
            host.beep[1].baseDistortion = 0.08f;
            host.beep[1].dropout = 0f;
        }

        private void PlayStageTone(string tag)
        {
            if (!host.settings.beepsEnabled) { Log.Info("Stage tone skipped, " + tag + ": beeps disabled"); return; }
            AudioClip clip = ClipLibrary.Next("beeps/stage", rng);
            if (clip == null) { Log.Info("Stage tone skipped, " + tag + ": no beeps/stage clip"); return; }
            ResetBeep1();
            host.beep[1].Play(clip, Tu.stageVolume, 1f, false, 0f);
            Log.Info("Stage tone, " + tag + ": " + host.beep[1].clipName);
        }

        /// <summary>Situation event LANDED or SPLASHED from flight. Same sequence as the descent detection, debounced.</summary>
        public void OnTouchdown(bool splash)
        {
            if (!host.CanStartAudio || host.director.radioSilent) return;
            TouchdownSequence(splash);
        }

        private void TouchdownSequence(bool splash)
        {
            if (!host.CanStartAudio || host.director.radioSilent) return;
            descentActive = false;
            descentTimer = 0f;
            host.beep[0].FadeOutOnce(0.08f); // no blip after the wheels touched
            float now = Time.unscaledTime;
            if (now - lastLandingTime < Tu.landingDebounce) return;
            lastLandingTime = now;
            // landing/splashdown tones are off by default: touchdown is marked by the crew/MC exchange
            if (host.settings.landingTones && host.settings.beepsEnabled)
            {
                AudioClip clip = ClipLibrary.Next(splash ? "beeps/splashdown" : "beeps/landing", rng);
                if (clip != null) { ResetBeep1(); host.beep[1].Play(clip, 0.8f, 1f, false, 0f); }
            }
            host.director.EnqueueTouchdown(Tu.touchdownDelay, splash);
            Log.Info(splash ? "Splashdown" : "Touchdown");
        }
    }
}
