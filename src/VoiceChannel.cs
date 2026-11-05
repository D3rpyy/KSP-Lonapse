// One AudioSource with its own filter chain, gain ramp, truncation and pause handling.
using System;
using UnityEngine;

namespace Lonapse
{
    public enum ChannelKind { Crew, Control, ControlBed, Beep, Static, IntercomClick, Breath }

    public class VoiceChannel
    {
        public readonly string label;
        public readonly ChannelKind kind;

        private GameObject go;
        public AudioSource src;
        private AudioHighPassFilter hp;
        private AudioLowPassFilter lp;
        private AudioDistortionFilter dist;
        private AudioEchoFilter echo;
        private AudioReverbFilter reverb;  // crew channels only, used by the intercom preset
        private TremoloDsp trem;
        private VoiceDsp dsp;             // crew and control: presence EQ, compressor, voice boost
        public float boost = 1f;          // linear voice gain (voiceBoostDb, calloutVolume), applied in VoiceDsp
        public bool compressor = true;
        public string phraseKey;          // Lonapse clip key of the playing line, null for other clips
        public float dropout;             // weak-signal dropouts in VoiceDsp, 0..1
        private bool hasEcho;
        private float radioHp;
        public bool intercom;              // current preset: intercom (true) or radio (false)
        public bool radioFx;               // an intercom line that uses the radio sound (intercomRadioFx)

        public float gain;
        private float gainTarget;
        private float gainRate;      // units per second
        private bool stopWhenSilent;
        private bool hasClip;
        public bool paused;

        public float volumeScale = 1f;
        public float stopAt = -1f;   // clip time in seconds, -1 = no truncation
        private bool truncating;

        public KerbalVoice speaker;
        public string clipName = "";
        public float duration;       // real seconds the item is expected to take (length / pitch, or truncated)

        public float baseDistortion;
        public float extraDistortion;
        public float tremDepth;
        public float tremRate = 5.5f;
        private float tremPhase;     // fallback tremolo on volume when DSP is off
        private bool dspWanted = true;

        public VoiceChannel(Transform parent, string label, ChannelKind kind, int priority,
                            float hpHz, float lpHz, float distortion, bool cabinEcho)
        {
            this.label = label;
            this.kind = kind;
            go = new GameObject("Lonapse_" + label);
            go.transform.parent = parent;
            go.transform.localPosition = Vector3.zero;

            // component add order = filter order
            src = go.AddComponent<AudioSource>();
            src.spatialBlend = 0f;
            src.dopplerLevel = 0f;
            src.spread = 0f;
            src.playOnAwake = false;
            src.loop = false;
            src.bypassEffects = false;
            src.bypassListenerEffects = true;
            src.bypassReverbZones = true;
            // the game listener is ducked under voice (req 3): our sources apply the listener base volume themselves
            src.ignoreListenerVolume = true;
            src.ignoreListenerPause = false;
            src.priority = priority;
            src.volume = 0f;
            src.mute = false;

            hp = go.AddComponent<AudioHighPassFilter>();
            hp.cutoffFrequency = hpHz;
            radioHp = hpHz;
            hp.highpassResonanceQ = 1f;

            lp = go.AddComponent<AudioLowPassFilter>();
            lp.cutoffFrequency = lpHz;
            lp.lowpassResonanceQ = 1f;

            dist = go.AddComponent<AudioDistortionFilter>();
            baseDistortion = distortion;
            dist.distortionLevel = distortion;
            dist.enabled = distortion > 0.001f;

            echo = go.AddComponent<AudioEchoFilter>();
            echo.delay = 18f;
            echo.decayRatio = 0.3f;
            echo.wetMix = 0.15f;
            echo.dryMix = 1f;
            echo.enabled = cabinEcho;
            hasEcho = cabinEcho;

            if (kind == ChannelKind.Crew)
            {
                // small, dry-ish room for the intercom: mostly direct sound with short early reflections
                reverb = go.AddComponent<AudioReverbFilter>();
                reverb.reverbPreset = AudioReverbPreset.User;
                reverb.dryLevel = 0f;
                reverb.roomHF = -700f;
                reverb.enabled = false;
            }

            trem = go.AddComponent<TremoloDsp>();
            try { trem.sampleRate = AudioSettings.outputSampleRate; } catch (Exception) { trem.sampleRate = 48000f; }
            trem.depth = 0f;

            if (kind == ChannelKind.Crew || kind == ChannelKind.Control)
            {
                dsp = go.AddComponent<VoiceDsp>();
                try { dsp.sampleRate = AudioSettings.outputSampleRate; } catch (Exception) { dsp.sampleRate = 48000f; }
            }
        }

        /// <summary>Radio profile band-pass (req 16) for the control channel.</summary>
        public void SetBand(float hpHz, float lpHz)
        {
            if (hp != null) hp.cutoffFrequency = hpHz;
            if (lp != null) lp.cutoffFrequency = lpHz;
            radioHp = hpHz;
        }

        /// <summary>Lower the current low-pass to at most this cutoff (radio profile ceiling on crew lines).</summary>
        public void CapLowPass(float lpHz)
        {
            if (lp != null && lp.cutoffFrequency > lpHz) lp.cutoffFrequency = lpHz;
        }

        public float ReductionDb { get { return dsp != null ? dsp.lastReductionDb : 0f; } }

        /// <summary>True while a fade-out to stop is running.</summary>
        public bool FadingOut { get { return hasClip && stopWhenSilent; } }

        /// <summary>Fade out unless already fading (calling FadeOut every frame would never reach zero).</summary>
        public void FadeOutOnce(float seconds)
        {
            if (Busy && !stopWhenSilent) FadeOut(seconds);
        }

        /// <summary>True while a clip is playing or paused mid-play.</summary>
        public bool Busy { get { return hasClip && src != null && (src.isPlaying || paused); } }

        public void Play(AudioClip clip, float volScale, float pitch, bool loop, float fadeIn)
        {
            if (src == null || clip == null) return;
            try
            {
                if (clip.loadState == AudioDataLoadState.Unloaded) clip.LoadAudioData();
            }
            catch (Exception) { }
            src.Stop();
            src.clip = clip;
            src.loop = loop;
            src.pitch = DcMath.Clamp(pitch, 0.25f, 3f);
            volumeScale = volScale;
            stopAt = -1f;
            truncating = false;
            stopWhenSilent = false;
            extraDistortion = 0f;
            if (fadeIn > 0.001f)
            {
                gain = 0f;
                gainTarget = 1f;
                gainRate = 1f / fadeIn;
            }
            else
            {
                gain = 1f;
                gainTarget = 1f;
                gainRate = 1f;
            }
            clipName = clip.name ?? "";
            boost = 1f;
            phraseKey = null;
            dropout = 0f;
            duration = clip.length / Math.Max(0.05f, Math.Abs(src.pitch));
            paused = false;
            hasClip = true;
            // a reused channel must not inherit the previous speaker's live filters or tremolo
            speaker = null;
            tremDepth = 0f;
            if (trem != null) trem.depth = 0f;
            src.volume = 0f; // real volume is applied in Update before the next audio frame
            src.Play();
        }

        /// <summary>
        /// Extra clip mixed through this source while it plays (crackle on the static loop). It is scaled by the
        /// source volume, so the gate gain silences it too, and Stop()/Pause() of the source cover it.
        /// </summary>
        public void OneShot(AudioClip clip, float scale)
        {
            if (src == null || clip == null || !hasClip || paused) return;
            src.PlayOneShot(clip, DcMath.Clamp01(scale));
        }

        /// <summary>Stop the clip at the given clip time with a 30 ms fade.</summary>
        public void TruncateAt(float clipTime)
        {
            if (!hasClip || src == null || src.clip == null) return;
            if (clipTime <= 0f) clipTime = 0.05f;
            if (stopAt < 0f || clipTime < stopAt) stopAt = clipTime;
            float p = Math.Max(0.05f, Math.Abs(src.pitch));
            duration = Math.Min(duration, stopAt / p + 0.03f);
        }

        public void FadeOut(float seconds)
        {
            if (!hasClip) return;
            gainTarget = 0f;
            gainRate = seconds > 0.001f ? Math.Max(gain, 0.001f) / seconds : 1000f;
            stopWhenSilent = true;
        }

        public void FadeTo(float target, float seconds)
        {
            gainTarget = DcMath.Clamp01(target);
            gainRate = seconds > 0.001f ? Math.Max(Math.Abs(gainTarget - gain), 0.001f) / seconds : 1000f;
            stopWhenSilent = false;
        }

        public void Stop()
        {
            if (src != null)
            {
                src.volume = 0f;
                src.Stop();
                src.clip = null;
                src.loop = false;
            }
            hasClip = false;
            paused = false;
            gain = 0f;
            gainTarget = 0f;
            stopWhenSilent = false;
            truncating = false;
            stopAt = -1f;
            speaker = null;
            clipName = "";
            phraseKey = null;
            tremDepth = 0f;
            extraDistortion = 0f;
            if (trem != null) trem.depth = 0f;
        }

        public void Pause()
        {
            if (src == null || !hasClip || paused) return;
            if (src.isPlaying)
            {
                src.Pause();
                paused = true;
            }
        }

        public void UnPause()
        {
            if (src == null || !paused) return;
            paused = false;
            src.UnPause();
        }

        public void SetDsp(bool useDsp)
        {
            dspWanted = useDsp;
            if (trem != null && trem.enabled != useDsp) trem.enabled = useDsp;
        }

        /// <summary>
        /// Crew channels: switch between the radio preset (cabin echo, stress-driven low-pass, distortion) and the
        /// intercom preset (wider band, no distortion, light room reverb). Call before Play.
        /// With <paramref name="radioSound"/> an intercom line still counts as intercom (volume, fades, chat logic),
        /// but it gets the radio filters.
        /// </summary>
        public void SetPreset(bool useIntercom, Tuning tu, bool radioSound = false)
        {
            intercom = useIntercom;
            radioFx = useIntercom && radioSound;
            if (useIntercom && !radioSound)
            {
                hp.cutoffFrequency = tu.intercomHp;
                lp.cutoffFrequency = tu.intercomLp;
                if (echo != null) echo.enabled = false;
                if (reverb != null)
                {
                    reverb.room = -tu.intercomRoomAtten;
                    reverb.reverbLevel = -tu.intercomReverbAtten;
                    reverb.reflectionsLevel = -tu.intercomReflAtten;
                    reverb.decayTime = tu.intercomDecay;
                    reverb.enabled = true;
                }
                baseDistortion = 0f;
            }
            else
            {
                // radio: transmit band-pass, crewHp .. crewLpCalm (the low-pass then follows stress in SetCrewFilters)
                hp.cutoffFrequency = kind == ChannelKind.Crew ? tu.crewHp : radioHp;
                if (echo != null) echo.enabled = hasEcho;
                if (reverb != null) reverb.enabled = false;
            }
        }

        /// <summary>Intercom response while playing: fixed band (times the kerbal's timbre), G wobble only.</summary>
        public void SetIntercomFilters(float timbre, float gStrain, Tuning tu)
        {
            float cutoff = tu.intercomLp * timbre * DcMath.Lerp(1f, 0.5f, gStrain);
            lp.cutoffFrequency = Math.Max(cutoff, tu.crewLpMin);
            baseDistortion = 0f;
            tremDepth = 0.40f * gStrain;
        }

        /// <summary>Crew filter response, updated every frame while playing.</summary>
        public void SetCrewFilters(float sk, float timbre, float gStrain, Tuning tu)
        {
            float cutoff = DcMath.Lerp(tu.crewLpCalm, tu.crewLpStress, sk) * timbre * DcMath.Lerp(1f, 0.25f, gStrain);
            if (cutoff < tu.crewLpMin) cutoff = tu.crewLpMin;
            lp.cutoffFrequency = cutoff;
            baseDistortion = 0.5f * DcMath.Smooth(sk, 0.5, 1.0) + 0.1f * gStrain;
            tremDepth = 0.40f * gStrain + 0.10f * DcMath.Smooth(sk, 0.8, 1.0);
        }

        /// <param name="categoryVolume">settings volume for this kind</param>
        /// <param name="globalMul">gateGain * masterVolume * gameGain</param>
        public void Update(float dt, float categoryVolume, float globalMul)
        {
            if (src == null) return;
            if (!hasClip)
            {
                if (src.volume != 0f) src.volume = 0f;
                return;
            }
            if (paused) return;

            if (!src.isPlaying)
            {
                // finished on its own (or never started)
                Stop();
                return;
            }

            if (stopAt > 0f && !truncating && src.time >= stopAt)
            {
                truncating = true;
                FadeOut(0.03f);
            }

            if (gain != gainTarget)
            {
                float step = gainRate * dt;
                if (gain < gainTarget) gain = Math.Min(gainTarget, gain + step);
                else gain = Math.Max(gainTarget, gain - step);
            }
            if (stopWhenSilent && gain <= 0f)
            {
                Stop();
                return;
            }

            float d = DcMath.Clamp01(baseDistortion + extraDistortion);
            if (dist != null)
            {
                bool on = d > 0.001f;
                if (dist.enabled != on) dist.enabled = on;
                if (on) dist.distortionLevel = d;
            }

            float tremMul = 1f;
            float depth = DcMath.Clamp01(tremDepth);
            if (dspWanted)
            {
                if (trem != null)
                {
                    trem.depth = depth;
                    trem.rate = tremRate;
                }
            }
            else if (depth > 0.0005f)
            {
                tremPhase += 2f * (float)Math.PI * tremRate * dt;
                if (tremPhase > 2f * (float)Math.PI) tremPhase -= 2f * (float)Math.PI;
                tremMul = 1f - depth * 0.5f * (1f + (float)Math.Sin(tremPhase));
            }

            float boostMul = 1f;
            if (dsp != null)
            {
                bool on = dspWanted;
                if (dsp.enabled != on) dsp.enabled = on;
                dsp.outGain = boost;
                dsp.dropout = dropout;
                dsp.compressorOn = compressor;
                if (!on) boostMul = boost; // no DSP: boost through the source volume (clamped at 1)
            }
            float vol = categoryVolume * volumeScale * gain * globalMul * tremMul * boostMul;
            src.volume = DcMath.Clamp01(DcMath.Finite(vol));
        }

        public void Destroy()
        {
            try { Stop(); } catch (Exception) { }
            if (go != null) UnityEngine.Object.Destroy(go);
            go = null;
            src = null;
        }
    }
}
