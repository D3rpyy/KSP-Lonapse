// Plays the Lonapse launch countdown on the Mission Control channel: quindar in, onU .. zeiD one per second,
// "¡eugepseD!" (liftoff), quindar out. Chatter is held while it runs. Optional auto-staging on liftoff.
// Stops when the gate closes, the radio is turned off, or the vessel leaves the pad (the player staged early).
using System;
using UnityEngine;
using KSP.UI.Screens;

namespace Lonapse
{
    public class CountdownPlayer
    {
        private readonly LonapseAddon host;
        private readonly LaunchCountdown count = new LaunchCountdown(0.6f, 1f);
        private float quindarOutLeft = -1f;

        // Band and distortion of the count lines: the mild ship radio band, the same for every number.
        public const float CleanHighPassHz = 300f;
        public const float CleanLowPassHz = 3600f;
        public const float CleanDistortion = 0.05f;

        public CountdownPlayer(LonapseAddon host) { this.host = host; }

        public bool Running { get { return count.Running; } }
        public int Said { get { return count.Said; } }

        /// <summary>The countdown can start: gate open, radio on, vessel on the pad, clips present.</summary>
        public string WhyNot(Vessel v)
        {
            if (!host.CanStartAudio) return "gate " + host.gateReason;
            if (!host.settings.radioEnabled) return "radio disabled";
            if (v == null || v.situation != Vessel.Situations.PRELAUNCH) return "not on the launch pad";
            if (!PhraseClips.Loaded || PhraseClips.table.Get(LaunchCountdown.ClipKey(1)) == null) return "no countdown clips";
            return null;
        }

        public void Start(Vessel v)
        {
            string why = WhyNot(v);
            if (why != null)
            {
                Log.Info("Countdown not started: " + why);
                try { ScreenMessages.PostScreenMessage("Lonapse: no countdown, " + why, 2f); } catch (Exception) { }
                return;
            }
            host.director.ClearQueue();
            host.control.Stop();
            // The player started the count, so the count gets a clean air. Rule: every busy crew line fades out,
            // whatever its preset. An intercom line (c.intercom) and the intercom click fade instead of talking over
            // "ten". A radio crew line fades too, with or without an open intercom turn: ClearQueue above already
            // dropped the queued radio, so the line playing now is the only one left of that exchange.
            // The callout channel is left alone (one short word, like the staging callout under the liftoff call).
            foreach (VoiceChannel c in host.crew) if (c != null && c.Busy) c.FadeOutOnce(0.25f);
            if (host.clickCh != null && host.clickCh.Busy) host.clickCh.FadeOutOnce(0.1f);
            count.Start();
            quindarOutLeft = -1f;
            Hold();
            AudioClip q = ClipLibrary.Next("beeps/quindar_in", new System.Random());
            if (q != null) host.control.Play(q, 0.45f, 1f, false, 0f);
            Log.Info("Countdown started" + (host.settings.countdownAutoStage ? " (auto stage on liftoff)" : ""));
        }

        /// <summary>Stops the count. With scream, launch control yells "¡haaaaaaA!" (user request), when sound is allowed.</summary>
        public void Abort(string why, bool scream = false)
        {
            if (!count.Running) return;
            count.Abort();
            quindarOutLeft = -1f;
            Log.Info("Countdown stopped at " + count.Said + ": " + why);
            if (scream && host.CanStartAudio && host.settings.radioEnabled && Play(LaunchCountdown.AbortKey, 1.0f))
            {
                host.director.holdUntil = host.director.clock + host.control.duration + 0.5f;
                quindarOutLeft = host.control.duration + 0.2f;
            }
            else host.control.FadeOutOnce(0.15f);
        }

        /// <summary>Settings window "Stop countdown".</summary>
        public void Stop() { Abort("stopped by the player", true); }

        private bool Play(string key, float vol)
        {
            PhraseEntry e = PhraseClips.table != null ? PhraseClips.table.Get(key) : null;
            AudioClip clip = PhraseClips.Clip(e, false);
            if (clip == null) return false;
            VoiceChannel c = host.control;
            // Clean, fixed band for every count line (user report: some numbers sounded muffled). Not the station or
            // EVA profile (low-pass 2800 or 2600 Hz), no weak-signal dropouts, only a trace of radio distortion.
            c.SetBand(CleanHighPassHz, CleanLowPassHz);
            c.Play(clip, vol, 1f, false, 0f);
            c.boost = MixMath.DbToLin(host.settings.voiceBoostDb) * host.settings.calloutVolume;
            c.compressor = host.settings.voiceCompressor;
            c.phraseKey = e.ClipKey;
            c.baseDistortion = CleanDistortion;
            c.extraDistortion = 0f;
            c.dropout = 0f;
            if (host.settings.subtitles && host.subtitles != null)
                host.subtitles.Show("MC", PhraseClips.Subtitle(e), Math.Min(c.duration, 0.95f), SubtitleLayout.DefaultHold);
            return true;
        }

        private void Hold()
        {
            // chatter waits while launch control has the air
            host.director.holdUntil = Math.Max(host.director.holdUntil, host.director.clock + 1.5f);
        }

        public void Update(float dt, Vessel v)
        {
            if (quindarOutLeft > 0f)
            {
                quindarOutLeft -= dt;
                if (quindarOutLeft <= 0f || !host.control.Busy)
                {
                    quindarOutLeft = -1f;
                    if (host.CanStartAudio)
                    {
                        AudioClip q = ClipLibrary.Next("beeps/quindar_out", new System.Random());
                        if (q != null && !host.control.Busy) host.control.Play(q, 0.45f, 1f, false, 0f);
                    }
                }
            }
            if (!count.Running) return;
            if (!host.CanStartAudio) { Abort("gate " + host.gateReason); return; }
            if (!host.settings.radioEnabled) { Abort("radio disabled"); return; }
            bool onPad;
            try { onPad = v != null && v.situation == Vessel.Situations.PRELAUNCH; } catch (Exception) { onPad = false; }
            if (!onPad)
            {
                if (count.Said >= LaunchCountdown.Numbers)
                {
                    // the player staged on "zeiD": a normal launch, finish with the liftoff call
                    count.Abort();
                    Play(LaunchCountdown.ClipKey(LaunchCountdown.Liftoff), 0.85f);
                    quindarOutLeft = 2.5f;
                    host.director.holdUntil = host.director.clock + 3f;
                    Log.Info("Countdown: staged on the last number, liftoff call");
                }
                else Abort("vessel left the pad before liftoff", true);
                return;
            }
            Hold();

            int step = count.Update(dt);
            if (step == 0) return;
            Play(LaunchCountdown.ClipKey(step, host.settings.countdownDown), 0.85f);
            if (step == LaunchCountdown.Liftoff)
            {
                quindarOutLeft = 2.5f;
                host.director.holdUntil = host.director.clock + 3f;
                if (host.settings.countdownAutoStage)
                {
                    try { StageManager.ActivateNextStage(); Log.Info("Countdown: liftoff, next stage activated"); }
                    catch (Exception ex) { Log.Warn("Countdown: auto stage failed: " + ex.Message); }
                }
                else Log.Info("Countdown: liftoff call");
            }
        }
    }
}
