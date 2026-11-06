// Talking kerbals (req 9): opens the lower jaw of the kerbal whose voice line is playing, on the IVA model that the
// portrait camera renders (ProtoCrewMember.KerbalRef) and on EVA kerbals. The open amount follows the RMS level of
// the playing channel. Without a usable level (muted, volume 0, no output data) a procedural flap is used instead.
//
// The stock Animator (and with it the kerbal expressions) may write the jaw every frame, so this runs in LateUpdate
// with a very late execution order. While a kerbal talks the jaw is not driven as a small offset on the expression
// pose, because a wide-open scream pose would hide it. It is driven from a "closed reference": the most closed
// Animator pose seen around the hinge axis, at most MaxRefDrift degrees from the reference pose. The first
// WarmFrames frames (and WarmTime s) after resolve are not tracked, because a freshly spawned IVA kerbal can show a
// bind or blend pose. After that the reference pose is re-taken and the closed reference also relaxes slowly
// (RefRelaxTau) toward the idle Animator pose while the kerbal is not talking, so one outlier frame cannot stick. A talk
// weight eases between the Animator pose and that speech pose at the start and the end of a line. Without speech
// the Animator pose is left alone (no writes).
//
// Crew portraits are rendered by Kerbal.kerbalAvatarUpdateCycle, a coroutine that calls kerbalCam.Render() every
// 0.10 to 0.15 s. Coroutines run after Update, before the Animator and before this LateUpdate. A Camera.onPreCull hook
// writes the jaw right before such a portrait render. Because portraits refresh at only 7 to 10 fps, a smooth 6 to
// 8 Hz flap aliases into nothing there, so portrait renders alternate wide and nearly closed per portrait frame.
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Lonapse
{
    [DefaultExecutionOrder(10000)]
    public class MouthDriver : MonoBehaviour
    {
        public LonapseAddon host;

        private const int BufLen = 256;
        private const float SilentFlapAfter = 0.2f;   // seconds of no signal at the start of a line before the flap takes over
        private const float RetryResolve = 2f;        // seconds before a kerbal without a rig is searched again
        private const float CloseEps = 0.002f;
        private const float TalkFade = 0.15f;         // seconds for the talk weight to go 0 -> 1 or 1 -> 0
        private const float MaxRefDrift = 25f;        // degrees the closed reference may differ from the resolve-time pose
        private const float PortraitOpenFloor = 0.75f;
        private const float PortraitClosed = 0.15f;
        private const float VoicedTarget = 0.02f;     // raw open target above this counts as an audible voice
        private const float VoicedOpen = 0.08f;       // or the smoothed open amount above this
        private const float PrefetchEvery = 5f;       // seconds between background resolves of the active crew
        private const int WarmFrames = 10;            // frames after resolve before the closed reference is tracked
        private const float WarmTime = 0.3f;          // and at least this many seconds
        private const float RefRelaxTau = 20f;        // seconds, closed reference relaxes toward the idle pose

        private sealed class Rig
        {
            public string name;
            public Transform root;
            public Transform jaw;
            public bool eva;
            public uint seed;
            public Kerbal kerbal;          // IVA only, for the portrait camera
            public Camera portraitCam;     // Kerbal.kerbalCam, null for EVA or when not created yet
            public int portraitCamId;
            public RenderTexture avatarTex; // Kerbal.avatarTexture, the portrait target
            public Quaternion refPose;     // jaw pose at resolve time, re-taken once the rig is warm
            public int warmFrames;         // frames observed since resolve
            public float warmAge;          // seconds observed since resolve
            public bool warm;              // past WarmFrames and WarmTime: the closed reference is tracked
            public Quaternion animPose;    // last pose the Animator wrote (or the resolve pose)
            public Quaternion closedRef;   // most closed Animator pose around the hinge, within MaxRefDrift of refPose
            public Quaternion lastWritten;
            public bool hasWritten;
            public bool needFinal;         // a write with talk weight > 0 happened, one write of animPose is still due
            public bool animatorWrites;    // last frame's detection, for the status and the debug log
            public bool speaking;          // speaking (or test flapping) this frame
            public float talkW;            // linear talk weight 0..1 (eased with Smooth01 when applied)
            public float env;
            public float rawTarget;        // open target before smoothing
            public float open;             // smoothed open amount 0..1
            public float silentFor;
            public bool signalSeen;
            public AudioClip lastClip;
            public float[] track;          // loudness track of lastClip, null: follow the channel output
            public bool flapping;
            public float flapT;
            public int portraitFlipFrame = -1;
            public bool portraitPhase;     // true: the next portrait frame shows the wide value
            public int portraitSeen, portraitApplied;         // counters in the current second
            public int portraitSeenRate, portraitAppliedRate; // counts of the last full second
        }

        private readonly Dictionary<string, Rig> rigs = new Dictionary<string, Rig>();
        // rigs from before CrewChanged: reused when the kerbal resolves to the same jaw bone, restored otherwise
        private readonly Dictionary<string, Rig> stale = new Dictionary<string, Rig>();
        private bool staleSweep;
        private readonly Dictionary<string, float> missingUntil = new Dictionary<string, float>();
        private readonly Dictionary<int, Rig> portraitCams = new Dictionary<int, Rig>();
        // Kerbal.avatarTexture instance id -> rig. Most pods render the portrait with InternalSeat.portraitCamera
        // (Kerbal.kerbalSeatCamUpdate), not with kerbalCam. Both render into avatarTexture, so the texture is the key.
        private readonly Dictionary<int, Rig> portraitTextures = new Dictionary<int, Rig>();
        private readonly List<Rig> work = new List<Rig>();
        private readonly List<string> dropKeys = new List<string>();
        private readonly float[] buf = new float[BufLen];
        private VoiceChannel[] speakingCh = new VoiceChannel[8];
        private int speakingCount;
        // loudness tracks of the clips by instance id (null: the clip has no readable samples), the oldest go first
        private readonly Dictionary<int, float[]> tracks = new Dictionary<int, float[]>();
        private readonly Queue<int> trackOrder = new Queue<int>();
        private const int TrackKeep = 64;
        private int tracksBuilt, tracksFailed;
        private static bool faceBonesLogged;
        private float testUntil = -1f;
        private bool wasEnabled;
        private bool active;               // Tick ran with the feature on this frame: the pre-cull hook may write
        private Tuning activeTuning;
        private float statusAt;
        private float debugAt;
        private float rateAt;
        private float prefetchAt;
        private int refAxis = -1;          // axis and sign the closed references were measured with
        private float refSign;
        private bool portraitMapDirty;
        private bool preCullLogged;
        private Camera.CameraCallback preCullCb;
        private string status = "mouth: idle";
        private string portraitStatus = "portraits: none";

        /// <summary>One line for the overlay and the Debug tab.</summary>
        public string Status { get { return status; } }

        /// <summary>Per rig: portrait renders seen and portrait jaw writes in the last second.</summary>
        public string PortraitStatus { get { return portraitStatus; } }

        /// <summary>Flap every resolvable crew rig of the active vessel for the given number of seconds.</summary>
        public void TestMouth(float seconds)
        {
            try
            {
                float now = Time.unscaledTime;
                testUntil = now + Math.Max(0.1f, seconds);
                missingUntil.Clear();
                int found = 0, total = 0;
                Vessel av = FlightGlobals.ActiveVessel;
                if (av != null)
                {
                    foreach (ProtoCrewMember pcm in av.GetVesselCrew())
                    {
                        if (pcm == null) continue;
                        total++;
                        if (GetRig(pcm.name, now) != null) found++;
                    }
                }
                status = "mouth test: " + found + " of " + total + " crew rig(s) found, " + seconds.ToString("0.0") + " s";
                Log.Info(status);
            }
            catch (Exception e) { Log.Exception("MouthDriver.TestMouth", e); }
        }

        /// <summary>Drop all cached rigs (vessel change, crew change). Jaws that were moved go back to their pose.</summary>
        public void Invalidate()
        {
            RestoreAll();
            rigs.Clear();
            portraitCams.Clear();
            portraitTextures.Clear();
            missingUntil.Clear();
            active = false;
        }

        /// <summary>
        /// Vessel or crew membership changed: every kerbal is resolved again next frame. A kerbal whose jaw is still
        /// the same bone keeps its rig (reference poses, talk weight, open amount), so a line goes on without a snap.
        /// The others go back to their Animator pose.
        /// </summary>
        public void CrewChanged()
        {
            foreach (KeyValuePair<string, Rig> kv in rigs) stale[kv.Key] = kv.Value;
            rigs.Clear();
            portraitCams.Clear();
            portraitTextures.Clear();
            missingUntil.Clear();
            portraitMapDirty = true;
            prefetchAt = 0f; // resolve the active crew on the next Tick, then sweep what was not reused
            staleSweep = true;
        }

        private void OnEnable()
        {
            if (preCullCb == null) preCullCb = OnCameraPreCull;
            Camera.onPreCull -= preCullCb;
            Camera.onPreCull += preCullCb;
        }

        private void OnDisable()
        {
            if (preCullCb != null) Camera.onPreCull -= preCullCb;
            active = false;
        }

        private void OnDestroy()
        {
            if (preCullCb != null) Camera.onPreCull -= preCullCb;
            active = false;
            try { RestoreAll(); } catch (Exception) { }
            rigs.Clear();
            stale.Clear();
            portraitCams.Clear();
            portraitTextures.Clear();
        }

        private void LateUpdate()
        {
            try { Tick(); }
            catch (Exception e) { active = false; Log.Exception("MouthDriver.LateUpdate", e); }
        }

        private void Tick()
        {
            Settings s = host != null ? host.settings : null;
            bool on = s != null && host.Started && s.mouthAnimation && HighLogic.LoadedSceneIsFlight;
            if (!on)
            {
                active = false;
                if (wasEnabled) { Invalidate(); status = "mouth: off"; portraitStatus = "portraits: off"; }
                wasEnabled = false;
                return;
            }
            wasEnabled = true;
            Tuning tu = s.tuning;
            float now = Time.unscaledTime;
            float dt = Math.Min(Math.Max(0f, Time.unscaledDeltaTime), 0.1f);
            bool test = now < testUntil;
            CheckReferenceAxis(tu);

            CollectSpeakers();

            // speaking rigs: resolve (cached) and drive from their channel
            work.Clear();
            for (int i = 0; i < speakingCount; i++)
            {
                VoiceChannel c = speakingCh[i];
                Rig r = GetRig(c.speaker.name, now);
                if (r == null || work.Contains(r)) continue;
                work.Add(r);
                Observe(r, tu, true);
                Warm(r, dt);
                r.rawTarget = Target(r, c, dt, tu);
                Drive(r, true, dt, tu);
            }

            // test mode: every crew rig of the active vessel flaps. Otherwise the crew is resolved now and then in the
            // background, so the closed reference learns the idle pose before the first line (not only a scream pose).
            if (test || now >= prefetchAt)
            {
                prefetchAt = now + PrefetchEvery;
                ResolveActiveCrew(now);
            }
            if (staleSweep)
            {
                // after CrewChanged: speakers and the active crew had their chance to reuse an old rig
                staleSweep = false;
                RestoreRigs(stale.Values);
                stale.Clear();
            }

            // the others: test flap, or ease the talk weight out and then leave the bone alone
            dropKeys.Clear();
            foreach (KeyValuePair<string, Rig> kv in rigs)
            {
                Rig r = kv.Value;
                if (r.jaw == null || r.root == null) { dropKeys.Add(kv.Key); continue; }
                if (work.Contains(r)) continue;
                r.env = 0f;
                r.silentFor = 0f;
                r.signalSeen = false;
                r.lastClip = null;
                Observe(r, tu, true);
                Warm(r, dt);
                if (test)
                {
                    r.flapT += dt;
                    r.flapping = true;
                    r.rawTarget = MouthMath.Flap(r.flapT, r.seed);
                    Drive(r, true, dt, tu);
                    continue;
                }
                r.flapping = false;
                r.rawTarget = 0f;
                if (r.talkW <= 0f) Relax(r, dt);
                Drive(r, false, dt, tu);
            }
            for (int i = 0; i < dropKeys.Count; i++)
            {
                if (s.mouthDebug) Log.Info("Mouth: rig of " + dropKeys[i] + " is gone, dropped from the cache");
                rigs.Remove(dropKeys[i]);
                portraitMapDirty = true;
            }

            if (now >= rateAt)
            {
                rateAt = now + 1f;
                foreach (Rig r in rigs.Values)
                {
                    r.portraitSeenRate = r.portraitSeen;
                    r.portraitAppliedRate = r.portraitApplied;
                    r.portraitSeen = 0;
                    r.portraitApplied = 0;
                    if (r.kerbal != null && (r.portraitCam != r.kerbal.kerbalCam || r.avatarTex != r.kerbal.avatarTexture)) portraitMapDirty = true;
                }
            }
            if (portraitMapDirty) RebuildPortraitMap();

            activeTuning = tu;
            active = true;

            if (now >= statusAt) { statusAt = now + 0.5f; status = BuildStatus(tu, test); portraitStatus = BuildPortraitStatus(); }
            if (s.mouthDebug && now >= debugAt && (speakingCount > 0 || test))
            {
                debugAt = now + 1f;
                Log.Info("Mouth debug: " + status + " / " + portraitStatus);
            }
        }

        /// <summary>
        /// The loudness track of a clip, built once from its samples. Null when Unity cannot give the samples (a
        /// compressed or streamed clip): the mouth follows the channel output then, as before.
        /// </summary>
        private float[] TrackOf(AudioClip clip)
        {
            if (clip == null) return null;
            int id = clip.GetInstanceID();
            float[] t;
            if (tracks.TryGetValue(id, out t)) return t;
            t = null;
            try
            {
                if (clip.loadType == AudioClipLoadType.DecompressOnLoad && clip.loadState == AudioDataLoadState.Loaded
                    && clip.samples > 0 && clip.channels > 0 && clip.samples < 48000 * 60)
                {
                    var data = new float[clip.samples * clip.channels];
                    if (clip.GetData(data, 0)) t = MouthMath.Envelope(data, clip.channels, clip.frequency, MouthMath.EnvelopeFrame);
                }
            }
            catch (Exception) { t = null; }
            if (t != null) tracksBuilt++; else tracksFailed++;
            if (tracksFailed == 1 && t == null)
                Log.Info("Mouth: no samples for clip " + clip.name + " (" + clip.loadType + "), the mouth follows the output for such clips");
            tracks[id] = t;
            trackOrder.Enqueue(id);
            while (trackOrder.Count > TrackKeep) tracks.Remove(trackOrder.Dequeue());
            return t;
        }

        /// <summary>How far the kerbal's mouth is open now, 0..1. 0 when the kerbal is not talking. For the head nod.</summary>
        public float Speech(string name)
        {
            Rig r;
            if (name == null || !rigs.TryGetValue(name, out r) || !r.speaking) return 0f;
            return r.open;
        }

        /// <summary>Busy crew and callout channels with a kerbal speaker. Mission Control has no speaker.</summary>
        private void CollectSpeakers()
        {
            speakingCount = 0;
            VoiceChannel[] crew = host.crew;
            int need = (crew != null ? crew.Length : 0) + 1;
            if (speakingCh.Length < need) speakingCh = new VoiceChannel[need];
            if (crew != null)
                for (int i = 0; i < crew.Length; i++) AddSpeaker(crew[i]);
            AddSpeaker(host.calloutCh);
        }

        private void AddSpeaker(VoiceChannel c)
        {
            if (c == null || c.src == null || !c.Busy || c.paused || c.speaker == null) return;
            speakingCh[speakingCount++] = c;
        }

        /// <summary>Open target for a speaking rig from the channel output. Falls back to Flap without a level.</summary>
        private float Target(Rig r, VoiceChannel c, float dt, Tuning tu)
        {
            AudioSource src = c.src;
            if (!ReferenceEquals(r.lastClip, src.clip))
            {
                // a new line: start the envelope and the silence check from zero
                r.lastClip = src.clip;
                r.track = TrackOf(src.clip);
                r.env = 0f;
                r.silentFor = 0f;
                r.signalSeen = false;
                r.flapT = 0f;
                r.portraitPhase = false;
            }
            if (r.track != null)
            {
                // the clean voice of the clip, a little ahead of the sound: no static, works when muted
                float t = src.time + tu.mouthLead * Math.Abs(src.pitch);
                r.signalSeen = true;
                r.silentFor = 0f;
                r.flapping = false;
                r.env = MouthMath.EnvelopeAt(r.track, MouthMath.EnvelopeFrame, t);
                return MouthMath.EnvelopeOpen(r.env);
            }
            float vol = src.volume;
            float level = 0f;
            bool silent;
            if (vol < 0.001f || src.mute) silent = true;
            else
            {
                src.GetOutputData(buf, 0);
                silent = MouthMath.Peak(buf, BufLen) < 1e-6f;
                level = MouthMath.Rms(buf, BufLen) / Math.Max(0.05f, vol);
            }
            if (silent) r.silentFor += dt;
            else { r.silentFor = 0f; r.signalSeen = true; }

            // no level at all since the line started (muted, gate closed, no output data): procedural talking.
            // Once real signal was seen, digital silence is a pause in the line and the mouth closes.
            if (!r.signalSeen && r.silentFor > SilentFlapAfter)
            {
                r.flapping = true;
                r.flapT += dt;
                return MouthMath.Flap(r.flapT, r.seed);
            }
            r.flapping = false;
            r.env = MouthMath.Follow(r.env, level, dt, tu.mouthAttack, tu.mouthRelease);
            return MouthMath.Open(r.env, tu.mouthGate, tu.mouthGain);
        }

        /// <summary>
        /// Smooth the open amount toward the raw target, ease the talk weight, and write the jaw while the weight is
        /// above 0. One more write of the Animator pose follows when the weight reaches 0, then the bone is left alone.
        /// </summary>
        private void Drive(Rig r, bool talking, float dt, Tuning tu)
        {
            r.speaking = talking;
            float target = talking ? r.rawTarget : 0f;
            r.open = MouthMath.Follow(r.open, target, dt, 0.025f, 0.06f);
            if (target <= 0f && r.open < CloseEps) r.open = 0f;
            r.talkW = MouthMath.RampWeight(r.talkW, talking, dt, TalkFade);
            if (r.talkW > 0f || r.needFinal)
            {
                Write(r, Compose(r, r.open, tu));
                if (r.kerbal != null && tu.portraitRefresh > 0.5f && host.portraits != null) host.portraits.Request(r.kerbal);
            }
        }

        /// <summary>
        /// Reads the bone. A value different from our last write came from the Animator: it is the new Animator pose
        /// and a candidate for the closed reference. An unchanged value is still our own write and is ignored, so
        /// offsets never add up and our writes never feed the closed reference.
        /// </summary>
        private void Observe(Rig r, Tuning tu, bool frame)
        {
            Quaternion cur = r.jaw.localRotation;
            if (!r.hasWritten || !Same(cur, r.lastWritten))
            {
                r.animPose = cur;
                if (frame) r.animatorWrites = r.hasWritten;
                TrackClosed(r, cur, tu);
            }
            else if (frame) r.animatorWrites = false;
        }

        /// <summary>
        /// Count the frames after resolve. Once warm, the reference pose is re-taken from the Animator pose (when not
        /// talking) and the closed reference starts from it.
        /// </summary>
        private static void Warm(Rig r, float dt)
        {
            if (r.warm) return;
            r.warmFrames++;
            r.warmAge += dt;
            if (!MouthMath.RefWarm(r.warmFrames, r.warmAge, WarmFrames, WarmTime)) return;
            r.warm = true;
            if (r.talkW <= 0f) r.refPose = r.animPose;
            r.closedRef = r.refPose;
        }

        /// <summary>Not talking: the closed reference eases toward the idle Animator pose (within MaxRefDrift).</summary>
        private static void Relax(Rig r, float dt)
        {
            if (!r.warm) return;
            Quaternion idle = Quaternion.RotateTowards(r.refPose, r.animPose, MaxRefDrift);
            r.closedRef = Quaternion.Slerp(r.closedRef, idle, MouthMath.DecayAlpha(dt, RefRelaxTau));
        }

        /// <summary>Keep the most closed pose around the hinge, limited to MaxRefDrift degrees from the reference pose.</summary>
        private void TrackClosed(Rig r, Quaternion pose, Tuning tu)
        {
            if (!r.warm) return;
            Quaternion cand = Quaternion.RotateTowards(r.refPose, pose, MaxRefDrift);
            if (OpenAngle(r, cand, tu) < OpenAngle(r, r.closedRef, tu)) r.closedRef = cand;
        }

        /// <summary>Signed hinge angle of q relative to the resolve pose, positive in the opening direction.</summary>
        private static float OpenAngle(Rig r, Quaternion q, Tuning tu)
        {
            Quaternion d = Quaternion.Inverse(r.refPose) * q;
            float sign = tu.mouthInvert > 0.5f ? -1f : 1f;
            return sign * MouthMath.HingeAngleDeg(d.x, d.y, d.z, d.w, MouthMath.AxisIndex(tu.mouthAxis));
        }

        /// <summary>Slerp(Animator pose, closedRef * hinge(open), eased talk weight).</summary>
        private static Quaternion Compose(Rig r, float open, Tuning tu)
        {
            float w = MouthMath.Smooth01(r.talkW);
            if (w <= 0f) return r.animPose;
            float sign = tu.mouthInvert > 0.5f ? -1f : 1f;
            Quaternion target = r.closedRef * Quaternion.AngleAxis(sign * open * tu.mouthOpenDeg, AxisVector(tu.mouthAxis));
            return w >= 1f ? target : Quaternion.Slerp(r.animPose, target, w);
        }

        private static void Write(Rig r, Quaternion q)
        {
            Transform j = r.jaw;
            if (j == null) return;
            j.localRotation = q;
            // read back: the stored value is what the next read compares against
            r.lastWritten = j.localRotation;
            r.hasWritten = true;
            r.needFinal = r.talkW > 0f;
        }

        /// <summary>
        /// Camera.onPreCull for every camera. Only the kerbalCam of a cached IVA rig does anything: it is a portrait
        /// render from the avatar coroutine, which runs before the Animator and before LateUpdate.
        /// </summary>
        private void OnCameraPreCull(Camera cam)
        {
            if (!active || (portraitCams.Count == 0 && portraitTextures.Count == 0) || ReferenceEquals(cam, null)) return;
            Rig r;
            try
            {
                if (!portraitCams.TryGetValue(cam.GetInstanceID(), out r))
                {
                    RenderTexture rt = cam.targetTexture;
                    if (ReferenceEquals(rt, null) || !portraitTextures.TryGetValue(rt.GetInstanceID(), out r)) return;
                }
                if (r.jaw == null) return;
                if (PortraitRefresh.Rendering) return; // our end-of-frame render: LateUpdate already set the jaw
                r.portraitSeen++;
                if (r.talkW <= 0f) return; // not talking: the Animator pose stays untouched
                Tuning tu = activeTuning;
                if (tu == null) return;
                Observe(r, tu, false);
                float open = r.open;
                if (r.speaking && tu.mouthPortraitAlternate > 0.5f)
                {
                    bool voiced = r.rawTarget > VoicedTarget || r.open > VoicedOpen;
                    // flip once per frame, even when kerbalCam and the seat camera both render this frame
                    if (voiced && r.portraitFlipFrame != Time.frameCount)
                    {
                        r.portraitPhase = !r.portraitPhase;
                        r.portraitFlipFrame = Time.frameCount;
                    }
                    open = MouthMath.PortraitOpen(r.portraitPhase, voiced, r.open, PortraitOpenFloor, PortraitClosed);
                }
                Write(r, Compose(r, open, tu));
                r.portraitApplied++;
            }
            catch (Exception e)
            {
                if (!preCullLogged) { preCullLogged = true; Log.Exception("MouthDriver.OnCameraPreCull", e); }
            }
        }

        /// <summary>Axis or invert changed (Debug tab): closed references measured with the old hinge are reset.</summary>
        private void CheckReferenceAxis(Tuning tu)
        {
            int axis = MouthMath.AxisIndex(tu.mouthAxis);
            float sign = tu.mouthInvert > 0.5f ? -1f : 1f;
            if (axis == refAxis && sign == refSign) return;
            refAxis = axis;
            refSign = sign;
            foreach (Rig r in rigs.Values)
            {
                r.closedRef = r.refPose;
                TrackClosed(r, r.animPose, tu);
            }
        }

        private void RebuildPortraitMap()
        {
            portraitMapDirty = false;
            portraitCams.Clear();
            portraitTextures.Clear();
            foreach (Rig r in rigs.Values)
            {
                if (r.kerbal != null)
                {
                    r.portraitCam = r.kerbal.kerbalCam;
                    r.avatarTex = r.kerbal.avatarTexture;
                }
                if (r.avatarTex != null) portraitTextures[r.avatarTex.GetInstanceID()] = r;
                if (r.portraitCam == null) continue;
                r.portraitCamId = r.portraitCam.GetInstanceID();
                portraitCams[r.portraitCamId] = r;
            }
        }

        private static bool Same(Quaternion a, Quaternion b)
        {
            return Math.Abs(a.x - b.x) < 1e-5f && Math.Abs(a.y - b.y) < 1e-5f &&
                   Math.Abs(a.z - b.z) < 1e-5f && Math.Abs(a.w - b.w) < 1e-5f;
        }

        private static Vector3 AxisVector(float axis)
        {
            switch (MouthMath.AxisIndex(axis))
            {
                case 0: return Vector3.right;
                case 1: return Vector3.up;
                default: return Vector3.forward;
            }
        }

        private void RestoreAll()
        {
            RestoreRigs(rigs.Values);
            RestoreRigs(stale.Values);
            stale.Clear();
            staleSweep = false;
        }

        private static void RestoreRigs(IEnumerable<Rig> list)
        {
            foreach (Rig r in list)
            {
                try
                {
                    if (r.jaw != null && r.hasWritten && Same(r.jaw.localRotation, r.lastWritten))
                        r.jaw.localRotation = r.animPose;
                }
                catch (Exception) { }
            }
        }

        private void ResolveActiveCrew(float now)
        {
            Vessel av = FlightGlobals.ActiveVessel;
            if (av == null) return;
            List<ProtoCrewMember> crew = av.GetVesselCrew();
            for (int i = 0; i < crew.Count; i++)
                if (crew[i] != null) GetRig(crew[i].name, now);
        }

        // ---- rig resolution ----

        /// <summary>Cached rig for a kerbal, or null. A kerbal without a rig is searched again after RetryResolve.</summary>
        private Rig GetRig(string name, float now)
        {
            if (string.IsNullOrEmpty(name)) return null;
            Rig r;
            if (rigs.TryGetValue(name, out r))
            {
                if (r.jaw != null && r.root != null) return r;
                rigs.Remove(name);
                portraitMapDirty = true;
            }
            float until;
            if (missingUntil.TryGetValue(name, out until) && now < until) return null;

            r = Resolve(name);
            if (r == null)
            {
                missingUntil[name] = now + RetryResolve;
                return null;
            }
            Rig old;
            if (stale.TryGetValue(name, out old))
            {
                stale.Remove(name);
                if (old.jaw != null && ReferenceEquals(old.jaw, r.jaw))
                {
                    // same bone after CrewChanged: keep refPose, closedRef, talkW and the rest of the state
                    old.root = r.root;
                    old.eva = r.eva;
                    old.kerbal = r.kerbal;
                    old.portraitCam = r.portraitCam;
                    r = old;
                }
                else RestoreRigs(new Rig[] { old });
            }
            missingUntil.Remove(name);
            rigs[name] = r;
            portraitMapDirty = true;
            return r;
        }

        private Rig Resolve(string name)
        {
            bool dbg = host != null && host.settings != null && host.settings.mouthDebug;
            try
            {
                Vessel av = FlightGlobals.ActiveVessel;
                // IVA: the model in the internal space, also rendered by the crew portraits
                if (av != null && !av.isEVA)
                {
                    List<ProtoCrewMember> crew = av.GetVesselCrew();
                    for (int i = 0; i < crew.Count; i++)
                    {
                        ProtoCrewMember pcm = crew[i];
                        if (pcm == null || pcm.name != name) continue;
                        Kerbal k = pcm.KerbalRef;
                        if (k == null)
                        {
                            if (dbg) Log.Info("Mouth: " + name + " has no KerbalRef (IVA not spawned)");
                            break;
                        }
                        Rig r = MakeRig(name, k.transform, false, pcm.gender.ToString(), dbg);
                        if (r != null)
                        {
                            r.kerbal = k;
                            r.portraitCam = k.kerbalCam;
                            if (dbg) Log.Info("Mouth: " + name + " portrait camera " + (r.portraitCam != null ? r.portraitCam.name : "not created yet"));
                            return r;
                        }
                        break;
                    }
                }
                // EVA: the active EVA kerbal or one loaded nearby
                List<Vessel> loaded = FlightGlobals.VesselsLoaded;
                for (int i = 0; i < loaded.Count; i++)
                {
                    Vessel v = loaded[i];
                    if (v == null || !v.isEVA || v.rootPart == null) continue;
                    List<ProtoCrewMember> crew = v.GetVesselCrew();
                    if (crew.Count == 0 || crew[0] == null || crew[0].name != name) continue;
                    KerbalEVA eva = v.rootPart.GetComponent<KerbalEVA>();
                    Transform root = eva != null ? eva.transform : v.rootPart.transform;
                    Rig r = MakeRig(name, root, true, crew[0].gender.ToString(), dbg);
                    if (r != null) return r;
                }
                if (dbg) Log.Info("Mouth: no rig found for " + name);
            }
            catch (Exception e) { Log.Exception("MouthDriver.Resolve", e); }
            return null;
        }

        private static Rig MakeRig(string name, Transform root, bool eva, string gender, bool dbg)
        {
            Transform jaw = FindJaw(root);
            if (jaw == null)
            {
                if (dbg)
                {
                    Log.Info("Mouth: " + (eva ? "EVA" : "IVA") + " " + name + " (" + gender + "): no lowerJaw bone under " + root.name +
                             ", jaw-like names: " + JawLikeNames(root));
                }
                return null;
            }
            if (!faceBonesLogged && jaw.parent != null)
            {
                // once per game: the bones of the head, to see what a mouth shape could move
                faceBonesLogged = true;
                var sb = new StringBuilder();
                foreach (Transform t in jaw.parent.GetComponentsInChildren<Transform>(true))
                    sb.Append(sb.Length == 0 ? "" : ", ").Append(t.name);
                Log.Info("Mouth: head bones of " + name + " under " + jaw.parent.name + ": " + sb);
            }
            Rig r = new Rig();
            r.name = name;
            r.root = root;
            r.jaw = jaw;
            r.eva = eva;
            r.seed = StableHash.Hash(name, 9);
            r.refPose = jaw.localRotation;
            r.animPose = r.refPose;
            r.closedRef = r.refPose;
            r.lastWritten = r.refPose;
            if (dbg)
            {
                Vector3 e = jaw.localEulerAngles;
                Animator an = root.GetComponentInChildren<Animator>(true);
                Log.Info("Mouth: " + (eva ? "EVA" : "IVA") + " " + name + " (" + gender + "): jaw " + PathOf(jaw, root) +
                         ", local rot " + e.x.ToString("0.0") + "/" + e.y.ToString("0.0") + "/" + e.z.ToString("0.0") +
                         ", animator " + (an != null ? (an.runtimeAnimatorController != null ? an.runtimeAnimatorController.name : "no controller") +
                         " culling " + an.cullingMode : "none"));
            }
            return r;
        }

        /// <summary>bn_lowerJaw01 if present, else the shallowest bone whose name contains "lowerjaw" and is not an end bone.</summary>
        private static Transform FindJaw(Transform root)
        {
            Transform best = null;
            int bestDepth = int.MaxValue;
            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                Transform t = all[i];
                string n = t.name;
                if (n == "bn_lowerJaw01") return t;
                string low = n.ToLowerInvariant();
                if (!low.Contains("lowerjaw") || low.Contains("end") || low.StartsWith("be_")) continue;
                int d = Depth(t, root);
                if (d < bestDepth) { best = t; bestDepth = d; }
            }
            return best;
        }

        private static string JawLikeNames(Transform root)
        {
            StringBuilder sb = new StringBuilder();
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                string low = t.name.ToLowerInvariant();
                if (low.Contains("jaw") || low.Contains("mouth") || low.Contains("chin"))
                    sb.Append(sb.Length == 0 ? "" : ", ").Append(t.name);
            }
            return sb.Length == 0 ? "none" : sb.ToString();
        }

        private static int Depth(Transform t, Transform root)
        {
            int d = 0;
            while (t != null && t != root) { t = t.parent; d++; }
            return d;
        }

        private static string PathOf(Transform t, Transform root)
        {
            string p = t.name;
            while (t != root && t.parent != null) { t = t.parent; p = t.name + "/" + p; }
            return p;
        }

        private string BuildStatus(Tuning tu, bool test)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("mouth: ").Append(rigs.Count).Append(" rig(s), axis ").Append("XYZ"[MouthMath.AxisIndex(tu.mouthAxis)])
              .Append(tu.mouthInvert > 0.5f ? " inverted" : "").Append(", ").Append(tu.mouthOpenDeg.ToString("0")).Append(" deg");
            sb.Append(", tracks ").Append(tracksBuilt).Append(" built ").Append(tracksFailed).Append(" without samples");
            if (test) sb.Append(", TEST");
            foreach (Rig r in rigs.Values)
            {
                if (r.talkW <= 0f && !work.Contains(r)) continue;
                sb.Append(" | ").Append(r.name).Append(r.eva ? " EVA" : " IVA")
                  .Append(" open ").Append(r.open.ToString("0.00"))
                  .Append(" w ").Append(r.talkW.ToString("0.00"))
                  .Append(r.flapping ? " flap" : (r.track != null ? " track " : " env ") + r.env.ToString("0.000"))
                  .Append(" ref ").Append(OpenAngle(r, r.closedRef, tu).ToString("0")).Append("/")
                  .Append(OpenAngle(r, r.animPose, tu).ToString("0")).Append(" deg")
                  .Append(r.animatorWrites ? " anim" : "");
            }
            if (speakingCount > work.Count) sb.Append(" | ").Append(speakingCount - work.Count).Append(" speaker(s) without rig");
            return sb.ToString();
        }

        /// <summary>"name applied/seen per second" for every IVA rig: seen = portrait renders, applied = jaw writes.</summary>
        private string BuildPortraitStatus()
        {
            if (rigs.Count == 0) return "portraits: no rigs";
            StringBuilder sb = new StringBuilder("portraits (jaw writes / renders per s):");
            int n = 0;
            foreach (Rig r in rigs.Values)
            {
                if (r.eva) continue;
                n++;
                sb.Append(' ').Append(r.name).Append(' ');
                if (r.portraitCam == null && r.avatarTex == null) sb.Append("no cam");
                else sb.Append(r.portraitAppliedRate).Append('/').Append(r.portraitSeenRate);
                sb.Append(',');
            }
            if (n == 0) return "portraits: only EVA rigs";
            sb.Length--;
            return sb.ToString();
        }
    }
}
