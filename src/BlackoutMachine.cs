// Reentry radio blackout: Clear -> Static -> Blackout -> Recovering -> Clear.
using System;
using UnityEngine;

namespace Lonapse
{
    public enum BlackoutState { Clear, Static, Blackout, Recovering }

    public class BlackoutMachine
    {
        private readonly LonapseAddon host;
        private readonly System.Random rng;

        public BlackoutState state = BlackoutState.Clear;
        private float enterTimer, exitTimer, stateTimer, staticDuration, crackleLeft;
        public float blackoutDuration;

        public BlackoutMachine(LonapseAddon host, System.Random rng)
        {
            this.host = host;
            this.rng = rng;
        }

        private Tuning Tu { get { return host.settings.tuning; } }
        private float U(float lo, float hi) { return DcMath.Uniform(rng, lo, hi); }

        /// <summary>Back to Clear without any sound (gate closed, vessel change).</summary>
        public void Reset()
        {
            if (state != BlackoutState.Clear) Log.Info("Blackout state reset to Clear");
            state = BlackoutState.Clear;
            enterTimer = exitTimer = stateTimer = 0f;
            blackoutDuration = 0f;
            host.director.radioSilent = false;
            host.director.mcBlackout = false;
            host.director.staticChop = false;
        }

        /// <summary>Blackout with crew: Mission Control and the telemetry beeps fade, the crew keeps its radio.</summary>
        private void FadeOutMissionControl(float seconds)
        {
            host.control.FadeOut(seconds);
            host.controlBed.FadeOut(seconds);
            for (int i = 0; i < host.beep.Length; i++) host.beep[i].FadeOut(seconds);
        }

        /// <summary>The crew still talks in a blackout, only Mission Control cannot hear them. Without crew nothing talks.</summary>
        private bool CrewTalks { get { return !host.probeMode; } }

        /// <summary>Like LonapseAddon.FadeOutRadio, but a crew channel on the intercom preset keeps playing.</summary>
        private void FadeOutRadioKeepIntercom(float seconds)
        {
            for (int i = 0; i < host.crew.Length; i++)
                if (!host.crew[i].intercom) host.crew[i].FadeOut(seconds);
            host.control.FadeOut(seconds);
            host.controlBed.FadeOut(seconds);
            host.crewBed.FadeOut(seconds);
            host.calloutCh.FadeOut(seconds);
            host.keyCh.FadeOut(seconds);
            for (int i = 0; i < host.beep.Length; i++) host.beep[i].FadeOut(seconds);
        }

        private void Go(BlackoutState next)
        {
            Log.Info("Blackout: " + state + " -> " + next + " (p " + host.stress.plasma.ToString("0.00") + ", qv " + host.stress.qv.ToString("0") + ", shockTemp " + host.sample.shockTemp.ToString("0") + ")");
            state = next;
            stateTimer = 0f;
            enterTimer = 0f;
            exitTimer = 0f;
        }

        public void Update(float dt)
        {
            if (!host.CanStartAudio) return;
            if (!host.settings.radioEnabled)
            {
                // blackout static and the relief exchange are radio: nothing while the radio toggle is off
                if (state != BlackoutState.Clear)
                {
                    Reset();
                    host.staticCh.FadeOutOnce(0.3f);
                }
                return;
            }
            Tuning tu = Tu;
            float p = host.stress.plasma;
            stateTimer += dt;

            switch (state)
            {
                case BlackoutState.Clear:
                    host.director.radioSilent = false;
                    host.director.mcBlackout = false;
                    host.director.staticChop = false;
                    if (p >= tu.blackoutEnter) enterTimer += dt; else enterTimer = 0f;
                    if (enterTimer >= tu.blackoutEnterTime)
                    {
                        Go(BlackoutState.Static);
                        staticDuration = U(tu.staticMin, tu.staticMax);
                        crackleLeft = U(tu.crackleMin, tu.crackleMax);
                        host.director.staticChop = true;
                        AudioClip loop = ClipLibrary.Next("sfx/loop", rng);
                        if (loop != null) host.staticCh.Play(loop, 1f, 1f, true, tu.staticRampTime);
                    }
                    break;

                case BlackoutState.Static:
                    host.director.staticChop = true;
                    crackleLeft -= dt;
                    if (crackleLeft <= 0f)
                    {
                        crackleLeft = U(tu.crackleMin, tu.crackleMax);
                        AudioClip burst = ClipLibrary.Next("sfx/burst", rng);
                        if (burst != null)
                        {
                            // overlay crackle on the static loop (OneShot goes through the same source, so it
                            // follows the same volume and gate), or play it directly if there is no loop
                            if (host.staticCh.Busy) host.staticCh.OneShot(burst, U(0.5f, 1.0f));
                            else host.staticCh.Play(burst, U(0.5f, 1.0f), 1f, false, 0f);
                        }
                    }
                    if (p < tu.blackoutExit) exitTimer += dt; else exitTimer = 0f;
                    if (exitTimer >= tu.staticExitTime)
                    {
                        Go(BlackoutState.Clear);
                        host.director.staticChop = false;
                        host.staticCh.FadeOut(tu.staticFadeOut);
                    }
                    else if (stateTimer >= staticDuration || p >= tu.blackoutDeep)
                    {
                        Go(BlackoutState.Blackout);
                        blackoutDuration = 0f;
                        host.director.staticChop = false;
                        host.staticCh.FadeOut(tu.staticFadeOut);
                        if (CrewTalks)
                        {
                            // the crew keeps talking (staging callouts, radio lines), Mission Control cannot answer
                            host.director.mcBlackout = true;
                            host.director.DropMcItems();
                            FadeOutMissionControl(0.3f);
                        }
                        else
                        {
                            // probe: the radio goes quiet. The intercom keeps going: the director schedules
                            // blackout intercom chats while CurrentPhase is "blackout"
                            host.director.radioSilent = true;
                            host.director.ClearRadioQueue();
                            FadeOutRadioKeepIntercom(0.3f);
                        }
                    }
                    break;

                case BlackoutState.Blackout:
                    host.director.radioSilent = !CrewTalks;
                    host.director.mcBlackout = CrewTalks;
                    blackoutDuration += dt;
                    if (p < tu.blackoutExit) exitTimer += dt; else exitTimer = 0f;
                    if (exitTimer >= tu.blackoutExitTime)
                    {
                        host.director.radioSilent = false;
                        host.director.mcBlackout = false;
                        if (blackoutDuration >= tu.blackoutMinDuration)
                        {
                            Go(BlackoutState.Recovering);
                            host.director.EnqueueRelief();
                        }
                        else Go(BlackoutState.Clear);
                    }
                    break;

                case BlackoutState.Recovering:
                    host.director.radioSilent = false;
                    host.director.mcBlackout = false;
                    // safety timeout so a missing clip can never keep us here
                    if ((stateTimer > 0.5f && host.director.Idle) || stateTimer > 30f)
                    {
                        Go(BlackoutState.Clear);
                        host.director.holdoff = Math.Max(host.director.holdoff, tu.reliefHoldoff);
                    }
                    break;
            }
        }
    }
}
