// Plays the Lonapse launch countdown on the Mission Control channel: quindar in, onU .. zeiD one per second,
// "¡eugepseD!" (liftoff), quindar out. Chatter is held while it runs. Optional auto-staging on liftoff.
// Stops when the gate closes, the radio is turned off, or the vessel leaves the pad (the player staged early).
using System;
using UnityEngine;
using KSP.UI.Screens;

namespace Lonapse
{
    public class CountdownPlayer : IDisposable
    {
        private readonly LonapseAddon host;
        private readonly LaunchCountdown count = new LaunchCountdown(0.6f, 1f);
        private float quindarOutLeft = -1f;
        private readonly System.Collections.Generic.Dictionary<string, AudioClip> speech = new System.Collections.Generic.Dictionary<string, AudioClip>();
        private readonly System.Collections.Generic.Dictionary<string, PhraseEntry> preparedLines = new System.Collections.Generic.Dictionary<string, PhraseEntry>();
        private Guid preparedVessel;
        private int preparationTicket;
        private bool preparing;
        private bool disposed;
        private int preparedGeneration = -1;
        private string preparationError;

        void Prepare()
        {
            Vessel vessel = FlightGlobals.ActiveVessel;
            if (disposed || !PhraseClips.Loaded || vessel == null || host.director == null) return;
            if (preparedGeneration != PhraseClips.SceneGeneration || preparedVessel != vessel.id)
            {
                if (Running) return;
                foreach (AudioClip old in speech.Values) if (old != null) UnityEngine.Object.Destroy(old);
                speech.Clear(); preparedLines.Clear(); preparationError = null; preparing = false;
                preparedGeneration = PhraseClips.SceneGeneration; preparedVessel = vessel.id; preparationTicket++;
            }
            if (preparing || speech.Count > 0) return;
            preparing = true;
            int requestedGeneration = preparedGeneration, ticket = preparationTicket;
            Guid requestedVessel = vessel.id;
            int remaining = 0;
            foreach (PhraseEntry entry in PhraseClips.table.Entries) if (entry.Event == "countdown") remaining++;
            if (remaining == 0) { preparationError = "no countdown dialogue"; return; }
            foreach (PhraseEntry entry in PhraseClips.table.Entries)
            {
                if (entry.Event != "countdown") continue;
                PhraseEntry line = host.director.ResolveSpeechLine(entry);
                if (line == null) { preparationError = "no matching countdown speech"; if (--remaining == 0) preparing = false; continue; }
                preparedLines[entry.ClipKey] = line;
                PhraseClips.Render(line, false, clip => {
                    if (disposed || ticket != preparationTicket || requestedGeneration != PhraseClips.SceneGeneration
                        || FlightGlobals.ActiveVessel == null || FlightGlobals.ActiveVessel.id != requestedVessel) { if (clip != null) UnityEngine.Object.Destroy(clip); return; }
                    if (clip == null) preparationError = "countdown speech could not be prepared";
                    else speech[line.ClipKey] = clip;
                    if (--remaining == 0) preparing = false;
                });
            }
        }

        // Band and distortion of the count lines: the mild ship radio band, the same for every number.
        public const float CleanHighPassHz = 300f;
        public const float CleanLowPassHz = 3600f;
        public const float CleanDistortion = 0.05f;

        public void Dispose()
        {
            disposed = true;
            foreach (AudioClip clip in speech.Values) if (clip != null) UnityEngine.Object.Destroy(clip);
            speech.Clear(); preparedLines.Clear();
        }

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
            string missing = RuntimeVoice.Missing("mc");
            if (missing != null) return missing;
            Prepare();
            if (preparationError != null) return preparationError;
            if (preparing) return "preparing countdown speech";
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

        /// <summary>Stops the count. With scream, launch control calls "Launch cancelled.", when sound is allowed.</summary>
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
            PhraseEntry e;
            if (!preparedLines.TryGetValue(key, out e)) return false;
            AudioClip clip;
            if (!speech.TryGetValue(key, out clip)) return false;
            if (clip == null) return false;
            VoiceChannel c = host.control;
            // A fixed receiver sound for every number, without weak-link narrowing or dropouts.
            c.SetBand(CleanHighPassHz, CleanLowPassHz);
            c.Play(clip, vol, 1f, false, 0f);
            c.SetRadioVoice(RadioVoice.Control);
            c.boost = MixMath.DbToLin(host.settings.voiceBoostDb) * host.settings.calloutVolume;
            c.compressor = host.settings.voiceCompressor;
            c.phraseKey = e.ClipKey;
            c.baseDistortion = CleanDistortion;
            c.extraDistortion = 0f;
            c.dropout = 0f;
            if (host.settings.subtitles && host.subtitles != null)
                host.subtitles.Show("MC", PhraseClips.Subtitle(e), Math.Min(c.duration, 0.95f), SubtitleLayout.DefaultHold, true);
            return true;
        }

        private void Hold()
        {
            // chatter waits while launch control has the air
            host.director.holdUntil = Math.Max(host.director.holdUntil, host.director.clock + 1.5f);
        }

        public void Update(float dt, Vessel v)
        {
            if (Running && (v == null || v.id != preparedVessel)) Abort("vessel changed");
            if (v != null && v.situation == Vessel.Situations.PRELAUNCH && RuntimeVoice.Missing("mc") == null) Prepare();
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
