// Kerbal faces: the face of a speaking kerbal matches the line, and kerbals in the same part turn their
// head toward the speaker. MouthDriver (the jaw) is separate and untouched.
//
// Expression. The stock kerbalExpressionSystem.Update computes `expression` (whee - panic, positive is a grin,
// negative is fear) and calls animator.SetFloat("Expression", expression). Update here only works out the moods. The
// SetFloat pass runs from two PlayerLoop hooks (LoopHook): right before PreLateUpdate.DirectorUpdateAnimationBegin,
// where Animators in Normal mode evaluate, and right before FixedUpdate.DirectorFixedUpdate, where Animators in Fixed
// mode (AnimatePhysics before Unity 6) evaluate inside the physics step. Both points come after the stock Update in
// every engine version, so ours is the last SetFloat before the Animator reads it. The older way, a coroutine on
// `yield return null`, relied on coroutines resuming between Update and the Animator. That order is not guaranteed
// in newer Unity versions and it never covered Fixed-mode Animators, so it is kept only as a fallback for when a
// hook cannot be installed. The pass is idempotent, so running it several times a frame changes nothing. It sets
// the same parameter again: the
// stock value moved toward the mood of the line by a weight (faceWeight). The stock field itself is never written, so
// the stock lerp goes on undisturbed and the face returns to it when the weight eases back to 0 after the line.
// Between lines the stress still caps the face (FaceMath.IdleMood): from the kerbal's own tenseAt it looks more and
// more scared, clearly scared at its panicAt and full fear at 1, even while stock KSP would grin at speed and G.
// Listeners in the same vessel get half the mood. LateUpdate reads the parameter back (after the Animator) and the
// status shows "override ok" or "override lost" when the value is not ours.
//
// Head turn. While a kerbal speaks, the other kerbals in the same part turn bn_headPivot_b01 toward the speaker's
// headTransform, at most headTurnMaxDeg, smoothed with headTurnSpeed. The speaker glances at one listener at half
// the angle. The turn is a rotation around an axis of the kerbal root frame (headTurnAxis, 1 = Y = the body's up axis),
// measured from the body forward (+Z of the root, or -Z with headTurnInvert). It is written on top of the Animator pose
// of the head bone, in LateUpdate and right before a portrait render (Camera.onPreCull for kerbalCam), with the same
// observe-and-restore logic as MouthDriver: a bone value different from our last write is the new Animator pose, an
// unchanged value is still our own write. Once the turn is back at 0 the Animator pose is written once more and the
// bone is left alone.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Lonapse
{
    public class FaceDriver : MonoBehaviour
    {
        public LonapseAddon host;

        private const float RetryResolve = 2f;     // seconds before a kerbal without a face is searched again
        private const float PrefetchEvery = 2f;    // seconds between resolves of the active crew
        private const float MoodRise = 0.25f;      // seconds for the mood weight 0 -> 1 when a line starts
        private const float MoodFall = 0.5f;       // seconds for the mood weight 1 -> 0 after the line
        private const float YawEps = 0.05f;        // degrees: below this a returning head counts as back at 0
        private const string HeadBone = "bn_headPivot_b01";
        private const float ListenNodTime = 0.5f;  // seconds of a listener's nod

        private sealed class Face
        {
            public string name;
            public Transform root;
            public Kerbal kerbal;              // IVA only
            public bool eva;
            public Vessel vessel;
            public Part part;                  // Kerbal.InPart, IVA only
            public kerbalExpressionSystem kes;
            public Animator anim;
            public int exprHash;
            public bool hasExpr;               // the Animator has the float parameter
            public Transform head;             // bn_headPivot_b01, may be null
            public Transform lookSource;       // Kerbal.headTransform or the head bone: where others look at
            public Camera portraitCam;
            public int portraitCamId;
            public RenderTexture avatarTex; // Kerbal.avatarTexture, the portrait target
            // expression
            public bool speaking;
            public bool listening;
            public float moodTarget;           // kept while the weight eases out
            public float stress;               // the kerbal's own stress (KerbalVoice.sk), caps the happy side
            public float faceStress;           // stress smoothed for the face (FaceMath.StressCap)
            public float calmStress;           // low vessel or personal stress also rules out stock fear
            public float tenseAt, panicAt;     // the kerbal's own thresholds, so the face turns scared with the voice
            public bool badass;
            public float moodW;
            public float lastExpr;
            public bool exprWritten;           // SetFloat ran this frame
            public bool exprLost;              // LateUpdate read back a different value: the override did not hold
            // head
            public Face lookAt;                // face to turn toward this frame, null = straight
            public float lookScale;            // 1 listener, GlanceScale speaker
            public float yaw;                  // current turn in degrees
            public float yawTarget;
            public float nod;                  // current nod in degrees (speaker accents, listener nod)
            public float nodFast, nodSlow;     // FaceMath.Accent followers
            public float listenNodT = -1f;     // seconds into a listener nod, -1: none
            public bool wasLooking;            // looked at a speaker last frame
            public Quaternion animPose;
            public Quaternion lastWritten;
            public bool hasWritten;
            public bool needFinal;
            public bool animatorWrites;
            public int portraitSeen, portraitApplied;
        }

        private readonly Dictionary<string, Face> faces = new Dictionary<string, Face>();
        private readonly Dictionary<string, float> missingUntil = new Dictionary<string, float>();
        private readonly Dictionary<int, Face> portraitCams = new Dictionary<int, Face>();
        private readonly Dictionary<int, Face> portraitTextures = new Dictionary<int, Face>(); // avatarTexture id -> face
        private readonly List<string> dropKeys = new List<string>();
        private readonly List<Face> speakers = new List<Face>();
        private readonly List<float> speakerMoods = new List<float>();
        private VoiceChannel[] speakingCh = new VoiceChannel[8];
        private int speakingCount;
        private float testUntil = -1f, testStart;
        private bool wasEnabled;
        private bool exprOn, headOn;
        private bool active;                    // LateUpdate ran with the head turn on: the pre-cull hook may write
        private bool crewDirty = true;
        private bool portraitMapDirty;
        private float prefetchAt, statusAt, portraitCheckAt;
        private bool preCullLogged;
        private Tuning activeTuning;
        private Camera.CameraCallback preCullCb;
        // expression pass (coroutine), values from this frame's Update
        private bool exprPass, exprTest;
        private float exprSign = 1f, exprTestMood, exprWeight;
        private Coroutine exprLoop;
        private bool hookAnim, hookFixed;       // PlayerLoop hooks installed (LoopHook)
        private bool lostLogged;
        private static FaceDriver current;      // the instance the PlayerLoop hooks call
        private sealed class ExprBeforeAnimation { }       // PlayerLoop marker types
        private sealed class ExprBeforeFixedAnimation { }
        private int overrideOk, overrideLost;   // LateUpdate read-back of the written frames since the last status
        private string status = "face: idle";

        /// <summary>One line for the overlay and the Debug tab.</summary>
        public string Status { get { return status; } }

        /// <summary>
        /// Test for the Debug tab: every resolvable kerbal of the active vessel sweeps its expression between grin and
        /// fear (4 s period) and turns its head between -headTurnMaxDeg and +headTurnMaxDeg (3 s period). The feature
        /// switches still apply: with faceExpressions off only the head moves, with headTurn off only the face.
        /// </summary>
        public void TestFace(float seconds)
        {
            try
            {
                float now = Time.unscaledTime;
                testStart = now;
                testUntil = now + Math.Max(0.1f, seconds);
                missingUntil.Clear();
                ResolveActiveCrew(now);
                int withExpr = 0, withHead = 0;
                foreach (Face f in faces.Values)
                {
                    if (f.hasExpr) withExpr++;
                    if (f.head != null) withHead++;
                }
                status = "face test: " + faces.Count + " kerbal(s), " + withExpr + " with expression, " + withHead +
                         " with head bone, " + seconds.ToString("0.0") + " s";
                Log.Info(status);
            }
            catch (Exception e) { Log.Exception("FaceDriver.TestFace", e); }
        }

        /// <summary>Vessel or crew membership changed: faces are resolved again, heads that were turned go back.</summary>
        public void CrewChanged()
        {
            crewDirty = true;
            prefetchAt = 0f;
            missingUntil.Clear();
        }

        /// <summary>Drop every cached face. Heads that were turned go back to their Animator pose.</summary>
        public void Invalidate()
        {
            RestoreAll();
            faces.Clear();
            portraitCams.Clear();
            portraitTextures.Clear();
            missingUntil.Clear();
            speakers.Clear();
            speakerMoods.Clear();
            active = false;
        }

        private void OnEnable()
        {
            if (preCullCb == null) preCullCb = OnCameraPreCull;
            Camera.onPreCull -= preCullCb;
            Camera.onPreCull += preCullCb;
            InstallHooks();
            exprLoop = StartCoroutine(ExpressionLoop());
        }

        private void OnDisable()
        {
            if (preCullCb != null) Camera.onPreCull -= preCullCb;
            active = false;
            exprPass = false;
            RemoveHooks();
            if (exprLoop != null) { StopCoroutine(exprLoop); exprLoop = null; }
        }

        /// <summary>The SetFloat pass right before the Animator evaluates, in both update modes (see the header).</summary>
        private void InstallHooks()
        {
            current = this;
            try
            {
                hookAnim = LoopHook.Insert(typeof(ExprBeforeAnimation),
                    typeof(UnityEngine.PlayerLoop.PreLateUpdate.DirectorUpdateAnimationBegin), HookApply);
                hookFixed = LoopHook.Insert(typeof(ExprBeforeFixedAnimation),
                    typeof(UnityEngine.PlayerLoop.FixedUpdate.DirectorFixedUpdate), HookApply);
            }
            catch (Exception e) { Log.Exception("FaceDriver.InstallHooks", e); }
            Log.Info("Face: expression pass " + (hookAnim ? "before the Animator" : "in a coroutine (no animation hook)") +
                     (hookFixed ? ", also before Fixed-mode Animators" : ", no hook for Fixed-mode Animators"));
        }

        private void RemoveHooks()
        {
            if (ReferenceEquals(current, this)) current = null;
            try
            {
                if (hookAnim) LoopHook.Remove(typeof(ExprBeforeAnimation));
                if (hookFixed) LoopHook.Remove(typeof(ExprBeforeFixedAnimation));
            }
            catch (Exception e) { Log.Exception("FaceDriver.RemoveHooks", e); }
            hookAnim = hookFixed = false;
        }

        private static void HookApply()
        {
            FaceDriver d = current;
            if (d == null) return;   // Unity null: destroyed
            try { d.ApplyExpression(); }
            catch (Exception e) { d.exprPass = false; Log.Exception("FaceDriver.HookApply", e); }
        }

        /// <summary>
        /// Fallback: resumes once per frame after Update. Before Unity 6 this was also before the Animator; with the
        /// hooks installed it only repeats the same write.
        /// </summary>
        private IEnumerator ExpressionLoop()
        {
            while (true)
            {
                yield return null;
                if (hookAnim) continue;
                try { ApplyExpression(); }
                catch (Exception e) { exprPass = false; Log.Exception("FaceDriver.ExpressionLoop", e); }
            }
        }

        private void OnDestroy()
        {
            if (preCullCb != null) Camera.onPreCull -= preCullCb;
            RemoveHooks();
            active = false;
            try { RestoreAll(); } catch (Exception) { }
            faces.Clear();
            portraitCams.Clear();
            portraitTextures.Clear();
        }

        // ---- Update: speakers, moods, expression ----

        private void Update()
        {
            try { TickExpression(); }
            catch (Exception e) { Log.Exception("FaceDriver.Update", e); }
        }

        private void TickExpression()
        {
            exprPass = false;   // set again at the end when this frame's weights are ready
            Settings s = host != null ? host.settings : null;
            bool on = s != null && host.Started && HighLogic.LoadedSceneIsFlight && (s.faceExpressions || s.headTurn);
            if (!on)
            {
                exprOn = headOn = false;
                if (wasEnabled) { Invalidate(); status = "face: off"; }
                wasEnabled = false;
                return;
            }
            wasEnabled = true;
            exprOn = s.faceExpressions;
            headOn = s.headTurn;
            Tuning tu = s.tuning;
            activeTuning = tu;
            float now = Time.unscaledTime;
            float dt = Math.Min(Math.Max(0f, Time.unscaledDeltaTime), 0.1f);
            bool test = now < testUntil;

            if (crewDirty) Rebuild(now);
            else if (now >= prefetchAt) { prefetchAt = now + PrefetchEvery; ResolveActiveCrew(now); RecheckExpressions(); }
            DropDead();

            CollectSpeakers(now);

            foreach (Face f in faces.Values)
            {
                f.speaking = false;
                f.listening = false;
                f.lookAt = null;
                f.lookScale = 0f;
                f.exprWritten = false;
            }

            // speakers: their own mood, and the strongest mood they hear for the listeners
            for (int i = 0; i < speakers.Count; i++)
            {
                Face sp = speakers[i];
                sp.speaking = true;
                sp.moodTarget = speakerMoods[i];
            }
            foreach (Face f in faces.Values)
            {
                if (f.speaking) continue;
                bool heard = false;
                float mood = 0f;
                for (int i = 0; i < speakers.Count; i++)
                {
                    Face sp = speakers[i];
                    if (sp.vessel == null || !ReferenceEquals(sp.vessel, f.vessel)) continue;
                    mood = heard ? FaceMath.Stronger(mood, speakerMoods[i]) : speakerMoods[i];
                    heard = true;
                    // head turn: the first speaker in the same part
                    if (f.lookAt == null && SamePart(f, sp)) { f.lookAt = sp; f.lookScale = 1f; }
                }
                // a listener reacts to the line, but not with more fear than its own stress gives
                if (heard) { f.listening = true; f.moodTarget = FaceMath.ListenerMood(mood, f.faceStress, f.tenseAt, f.panicAt); }
            }
            // the speaker glances at the nearest listener in the same part
            for (int i = 0; i < speakers.Count; i++)
            {
                Face sp = speakers[i];
                Face best = null;
                float bestD = float.MaxValue;
                foreach (Face f in faces.Values)
                {
                    if (ReferenceEquals(f, sp) || !SamePart(f, sp) || f.lookSource == null || sp.lookSource == null) continue;
                    float d = (f.lookSource.position - sp.lookSource.position).sqrMagnitude;
                    if (d < bestD) { bestD = d; best = f; }
                }
                if (best != null) { sp.lookAt = best; sp.lookScale = FaceMath.GlanceScale; }
            }

            // expression weights. The SetFloat itself runs in ExpressionLoop, after the stock Update.
            float testMood = FaceMath.TestMood(now - testStart);
            foreach (Face f in faces.Values)
            {
                bool engaged = f.speaking || f.listening;
                KerbalVoice kv = host.roster != null ? host.roster.Find(f.name) : null;
                f.stress = kv != null ? kv.sk : 0f;
                f.calmStress = Math.Min(f.stress, host.stress != null ? host.stress.stress : 0f);
                // smoothed for the face, so a short stress jump does not make it twitch (about 0.7 s to follow)
                f.faceStress = FaceMath.Follow(f.faceStress, f.stress, dt, 1.5f);
                f.badass = kv != null && kv.BadassEffective;
                f.tenseAt = kv != null ? kv.tenseAt : 0.3f;
                f.panicAt = kv != null ? kv.PanicNow : 0.7f;
                f.moodW = FaceMath.Ramp(f.moodW, engaged || test, dt, MoodRise, MoodFall);
                if (test) f.moodTarget = testMood; // the fade after the test starts from the last test value
            }
            exprSign = tu.faceExpressionSign < 0f ? -1f : 1f;
            exprTest = test;
            exprTestMood = testMood;
            exprWeight = FaceMath.Clamp01(tu.faceWeight);
            exprPass = exprOn;
        }

        /// <summary>
        /// The SetFloat pass: the stock value moved toward the mood, only while a weight is up. Idempotent (it only
        /// reads this frame's weights and the stock value), so the hooks and the fallback may each run it.
        /// </summary>
        private void ApplyExpression()
        {
            if (!exprPass) return;
            foreach (Face f in faces.Values)
            {
                if (!f.hasExpr || f.anim == null || f.kes == null) continue;
                // the face keeps its fear and follows the stress: a calm line never turns a scared face into a grin
                float stockMood = exprSign < 0f ? -f.kes.expression : f.kes.expression;
                float mood, w;
                if (exprTest) { mood = exprTestMood; w = 1f; }
                else
                {
                    // between lines the stress still caps the face, so it does not grin between two scared lines
                    float lw = FaceMath.Smooth01(f.moodW);
                    mood = FaceMath.FaceMood(f.moodTarget, lw, stockMood, f.faceStress, f.tenseAt, f.panicAt);
                    if (lw <= 0f && Math.Abs(mood - stockMood) < 1e-4f
                        && stockMood >= FaceMath.LimitFear(-1f, f.calmStress)) continue;
                    w = exprWeight;
                }
                if (w <= 0f) continue;
                f.lastExpr = FaceMath.Blend(f.kes.expression, mood, w, exprSign);
                if (!exprTest) f.lastExpr = exprSign * FaceMath.LimitFear(exprSign * f.lastExpr, f.calmStress);
                f.anim.SetFloat(f.exprHash, f.lastExpr);
                f.exprWritten = true;
            }
        }

        /// <summary>LateUpdate (after the Animator): the parameter still holds our value, or something wrote over it.</summary>
        private void CheckExpression()
        {
            foreach (Face f in faces.Values)
            {
                if (!f.exprWritten || f.anim == null) continue;
                bool ok = FaceMath.OverrideHeld(f.anim.GetFloat(f.exprHash), f.lastExpr);
                f.exprLost = !ok;
                if (ok) overrideOk++; else overrideLost++;
                if (!ok && !lostLogged)
                {
                    lostLogged = true;
                    Log.Info("Face: expression override lost on " + f.name + " (wrote " + f.lastExpr.ToString("0.00") + ", read " +
                             f.anim.GetFloat(f.exprHash).ToString("0.00") + ", Animator " + f.anim.updateMode + "/" + f.anim.cullingMode +
                             ", hooks " + (hookAnim ? "anim" : "-") + (hookFixed ? "+fixed" : "") + ")");
                }
            }
        }

        /// <summary>Busy crew and callout channels with a kerbal speaker (same rule as MouthDriver), with their mood.</summary>
        private void CollectSpeakers(float now)
        {
            speakers.Clear();
            speakerMoods.Clear();
            speakingCount = 0;
            VoiceChannel[] crew = host.crew;
            int need = (crew != null ? crew.Length : 0) + 1;
            if (speakingCh.Length < need) speakingCh = new VoiceChannel[need];
            if (crew != null)
                for (int i = 0; i < crew.Length; i++) AddSpeaker(crew[i]);
            AddSpeaker(host.calloutCh);
            for (int i = 0; i < speakingCount; i++)
            {
                VoiceChannel c = speakingCh[i];
                Face f = GetFace(c.speaker.name, now);
                if (f == null || speakers.Contains(f)) continue;
                speakers.Add(f);
                speakerMoods.Add(MoodOf(c));
            }
        }

        private void AddSpeaker(VoiceChannel c)
        {
            if (c == null || c.src == null || !c.Busy || c.paused || c.speaker == null) return;
            speakingCh[speakingCount++] = c;
        }

        /// <summary>
        /// Mood of the playing line: band, event and phase from the Lonapse table row of c.phraseKey. Lines without a
        /// table row (or band "none") use the speaker's current stress band.
        /// </summary>
        private static float MoodOf(VoiceChannel c)
        {
            KerbalVoice k = c.speaker;
            string band = null, ev = null, phase = null;
            PhraseEntry e = null;
            if (!string.IsNullOrEmpty(c.phraseKey) && PhraseClips.table != null) e = PhraseClips.table.Get(c.phraseKey);
            if (e != null)
            {
                band = e.Band;
                ev = e.Event;
                phase = e.Phase;
            }
            if (string.IsNullOrEmpty(band) || band == "none") band = FaceMath.BandName((int)k.band);
            return FaceMath.Mood(band, ev, phase, c.intercom, k.BadassEffective); // persona flag, only with personalityEffects
        }

        private static bool SamePart(Face a, Face b)
        {
            return !a.eva && !b.eva && a.part != null && ReferenceEquals(a.part, b.part);
        }

        // ---- LateUpdate and portraits: head turn ----

        private void LateUpdate()
        {
            try { TickHead(); }
            catch (Exception e) { active = false; Log.Exception("FaceDriver.LateUpdate", e); }
        }

        private void TickHead()
        {
            if (!wasEnabled) { active = false; return; }
            CheckExpression();
            Tuning tu = activeTuning;
            if (tu == null) { active = false; return; }
            float now = Time.unscaledTime;
            float dt = Math.Min(Math.Max(0f, Time.unscaledDeltaTime), 0.1f);
            bool test = now < testUntil;
            float testYaw = FaceMath.TestYaw(now - testStart, tu.headTurnMaxDeg);

            foreach (Face f in faces.Values)
            {
                if (f.head == null || f.root == null) continue;
                Observe(f, true);
                if (!headOn) f.yawTarget = 0f;
                else if (test) f.yawTarget = testYaw;
                else if (f.lookAt != null && f.lookAt.lookSource != null) f.yawTarget = AngleTo(f, f.lookAt.lookSource.position, tu);
                else f.yawTarget = 0f;
                f.yaw = FaceMath.Follow(f.yaw, f.yawTarget, dt, tu.headTurnSpeed);
                if (f.yawTarget == 0f && Math.Abs(f.yaw) < YawEps) f.yaw = 0f;
                float nodTarget = headOn && !test ? NodTarget(f, dt, tu) : 0f;
                f.nod = FaceMath.Follow(f.nod, nodTarget, dt, 14f);
                if (nodTarget == 0f && Math.Abs(f.nod) < YawEps) f.nod = 0f;
                if (f.yaw != 0f || f.nod != 0f || f.needFinal)
                {
                    Write(f, Compose(f, f.yaw, f.nod, tu));
                    if (f.kerbal != null && tu.portraitRefresh > 0.5f && host.portraits != null) host.portraits.Request(f.kerbal);
                }
            }
            // the portrait camera and avatarTexture may appear after the face was resolved (same check as MouthDriver)
            if (now >= portraitCheckAt)
            {
                portraitCheckAt = now + 1f;
                foreach (Face f in faces.Values)
                    if (f.kerbal != null && (f.portraitCam != f.kerbal.kerbalCam || f.avatarTex != f.kerbal.avatarTexture)) portraitMapDirty = true;
            }
            if (portraitMapDirty) RebuildPortraitMap();
            active = true;

            if (now >= statusAt) { statusAt = now + 0.5f; status = BuildStatus(tu, test); }
        }

        /// <summary>Head nod in degrees: the speaker dips the head on stressed syllables (the mouth gets wider than
        /// its average), a listener in the same part nods once when a line that is not a fearful one ends.</summary>
        private float NodTarget(Face f, float dt, Tuning tu)
        {
            bool looking = f.lookAt != null && f.lookScale >= 1f;
            if (f.wasLooking && !looking && !f.speaking && f.moodTarget > -0.3f) f.listenNodT = 0f;
            f.wasLooking = looking;
            float accent = 0f;
            float open = f.speaking && host.mouth != null ? host.mouth.Speech(f.name) : 0f;
            accent = FaceMath.Accent(ref f.nodFast, ref f.nodSlow, open, dt);
            float listen = 0f;
            if (f.listenNodT >= 0f)
            {
                f.listenNodT += dt;
                listen = FaceMath.ListenerNod(f.listenNodT, ListenNodTime);
                if (f.listenNodT > ListenNodTime) f.listenNodT = -1f;
            }
            float deg = tu.headNodDeg * Math.Max(accent, 0.8f * listen);
            return tu.headNodInvert > 0.5f ? -deg : deg;
        }

        /// <summary>Turn target in degrees for f toward a world position, limited and scaled.</summary>
        private static float AngleTo(Face f, Vector3 target, Tuning tu)
        {
            Vector3 axis = AxisWorld(f, tu);
            Vector3 fwd = ForwardWorld(f, tu);
            Vector3 to = target - f.head.position;
            float a = FaceMath.SignedAngleAround(axis.x, axis.y, axis.z, fwd.x, fwd.y, fwd.z, to.x, to.y, to.z);
            return FaceMath.TurnTarget(a, tu.headTurnMaxDeg, f.lookScale);
        }

        private static Vector3 AxisWorld(Face f, Tuning tu)
        {
            switch (FaceMath.AxisIndex(tu.headTurnAxis))
            {
                case 0: return f.root.right;
                case 1: return f.root.up;
                default: return f.root.forward;
            }
        }

        /// <summary>Body forward (+Z of the root, -Z with headTurnInvert). With the turn axis on Z the root up is used.</summary>
        private static Vector3 ForwardWorld(Face f, Tuning tu)
        {
            Vector3 v = FaceMath.AxisIndex(tu.headTurnAxis) == 2 ? f.root.up : f.root.forward;
            return tu.headTurnInvert > 0.5f ? -v : v;
        }

        /// <summary>Animator pose of the head with a rotation of yaw degrees around the world turn axis and a nod of
        /// nod degrees around the side axis (turn axis cross forward) applied on top.</summary>
        private static Quaternion Compose(Face f, float yaw, float nod, Tuning tu)
        {
            if (yaw == 0f && nod == 0f) return f.animPose;
            Transform p = f.head.parent;
            Vector3 axisW = AxisWorld(f, tu);
            Vector3 axisP = p != null ? p.InverseTransformDirection(axisW) : axisW;
            Quaternion q = Quaternion.AngleAxis(yaw, axisP);
            if (nod != 0f)
            {
                Vector3 sideW = Vector3.Cross(axisW, ForwardWorld(f, tu));
                if (sideW.sqrMagnitude > 1e-6f)
                {
                    Vector3 sideP = p != null ? p.InverseTransformDirection(sideW.normalized) : sideW.normalized;
                    q = Quaternion.AngleAxis(nod, sideP) * q;
                }
            }
            return q * f.animPose;
        }

        /// <summary>A bone value different from our last write came from the Animator: it is the new Animator pose.</summary>
        private static void Observe(Face f, bool frame)
        {
            Quaternion cur = f.head.localRotation;
            if (!f.hasWritten || !Same(cur, f.lastWritten))
            {
                f.animPose = cur;
                if (frame) f.animatorWrites = f.hasWritten;
            }
            else if (frame) f.animatorWrites = false;
        }

        private static void Write(Face f, Quaternion q)
        {
            Transform h = f.head;
            if (h == null) return;
            h.localRotation = q;
            f.lastWritten = h.localRotation;
            f.hasWritten = true;
            f.needFinal = f.yaw != 0f || f.nod != 0f;
        }

        /// <summary>Camera.onPreCull: a kerbalCam portrait render (avatar coroutine, before the Animator and LateUpdate).</summary>
        private void OnCameraPreCull(Camera cam)
        {
            if (!active || (portraitCams.Count == 0 && portraitTextures.Count == 0) || ReferenceEquals(cam, null)) return;
            Face f;
            try
            {
                // Most pods render portraits with InternalSeat.portraitCamera into Kerbal.avatarTexture, not kerbalCam.
                if (!portraitCams.TryGetValue(cam.GetInstanceID(), out f))
                {
                    RenderTexture rt = cam.targetTexture;
                    if (ReferenceEquals(rt, null) || !portraitTextures.TryGetValue(rt.GetInstanceID(), out f)) return;
                }
                if (f.head == null || f.root == null) return;
                if (PortraitRefresh.Rendering) return; // our end-of-frame render: LateUpdate already set the head
                f.portraitSeen++;
                if (f.yaw == 0f && f.nod == 0f) return; // not turned: the Animator pose stays untouched
                Tuning tu = activeTuning;
                if (tu == null) return;
                Observe(f, false);
                Write(f, Compose(f, f.yaw, f.nod, tu));
                f.portraitApplied++;
            }
            catch (Exception e)
            {
                if (!preCullLogged) { preCullLogged = true; Log.Exception("FaceDriver.OnCameraPreCull", e); }
            }
        }

        private void RebuildPortraitMap()
        {
            portraitMapDirty = false;
            portraitCams.Clear();
            portraitTextures.Clear();
            foreach (Face f in faces.Values)
            {
                if (f.kerbal != null) { f.portraitCam = f.kerbal.kerbalCam; f.avatarTex = f.kerbal.avatarTexture; }
                if (f.head == null) continue;
                if (f.avatarTex != null) portraitTextures[f.avatarTex.GetInstanceID()] = f;
                if (f.portraitCam == null) continue;
                f.portraitCamId = f.portraitCam.GetInstanceID();
                portraitCams[f.portraitCamId] = f;
            }
        }

        private static bool Same(Quaternion a, Quaternion b)
        {
            return Math.Abs(a.x - b.x) < 1e-5f && Math.Abs(a.y - b.y) < 1e-5f &&
                   Math.Abs(a.z - b.z) < 1e-5f && Math.Abs(a.w - b.w) < 1e-5f;
        }

        private void RestoreAll()
        {
            foreach (Face f in faces.Values) Restore(f);
        }

        private static void Restore(Face f)
        {
            try
            {
                if (f.head != null && f.hasWritten && Same(f.head.localRotation, f.lastWritten))
                    f.head.localRotation = f.animPose;
                f.yaw = 0f;
                f.nod = 0f;
                f.listenNodT = -1f;
                f.needFinal = false;
            }
            catch (Exception) { }
        }

        // ---- resolution ----

        /// <summary>After CrewChanged: resolve the active crew again, keep faces whose rig is unchanged, restore the rest.</summary>
        private void Rebuild(float now)
        {
            crewDirty = false;
            prefetchAt = now + PrefetchEvery;
            Dictionary<string, Face> old = new Dictionary<string, Face>(faces);
            faces.Clear();
            portraitCams.Clear();
            portraitTextures.Clear();
            portraitMapDirty = true;
            ResolveActiveCrew(now);
            foreach (KeyValuePair<string, Face> kv in old)
            {
                Face nf;
                if (faces.TryGetValue(kv.Key, out nf) && Reusable(kv.Value, nf))
                {
                    Face o = kv.Value;
                    o.vessel = nf.vessel;
                    o.part = nf.part;
                    o.kerbal = nf.kerbal;
                    o.portraitCam = nf.portraitCam;
                    o.lookSource = nf.lookSource;
                    o.root = nf.root;
                    o.eva = nf.eva;
                    faces[kv.Key] = o;
                }
                else Restore(kv.Value);
            }
        }

        private static bool Reusable(Face o, Face n)
        {
            return o.head != null && ReferenceEquals(o.head, n.head) && ReferenceEquals(o.kes, n.kes);
        }

        private void DropDead()
        {
            dropKeys.Clear();
            foreach (KeyValuePair<string, Face> kv in faces)
                if (kv.Value.root == null) dropKeys.Add(kv.Key);
            for (int i = 0; i < dropKeys.Count; i++)
            {
                faces.Remove(dropKeys[i]);
                portraitMapDirty = true;
            }
        }

        private void ResolveActiveCrew(float now)
        {
            Vessel av = FlightGlobals.ActiveVessel;
            if (av == null) return;
            List<ProtoCrewMember> crew = av.GetVesselCrew();
            for (int i = 0; i < crew.Count; i++)
                if (crew[i] != null) GetFace(crew[i].name, now);
        }

        private Face GetFace(string name, float now)
        {
            if (string.IsNullOrEmpty(name)) return null;
            Face f;
            if (faces.TryGetValue(name, out f))
            {
                if (f.root != null) return f;
                faces.Remove(name);
                portraitMapDirty = true;
            }
            float until;
            if (missingUntil.TryGetValue(name, out until) && now < until) return null;
            f = Resolve(name);
            if (f == null) { missingUntil[name] = now + RetryResolve; return null; }
            missingUntil.Remove(name);
            faces[name] = f;
            portraitMapDirty = true;
            return f;
        }

        private Face Resolve(string name)
        {
            bool dbg = host != null && host.settings != null && host.settings.mouthDebug;
            try
            {
                Vessel av = FlightGlobals.ActiveVessel;
                if (av != null && !av.isEVA)
                {
                    List<ProtoCrewMember> crew = av.GetVesselCrew();
                    for (int i = 0; i < crew.Count; i++)
                    {
                        ProtoCrewMember pcm = crew[i];
                        if (pcm == null || pcm.name != name) continue;
                        Kerbal k = pcm.KerbalRef;
                        if (k == null) break;
                        Face f = MakeFace(name, k.transform, false, dbg);
                        if (f == null) break;
                        f.kerbal = k;
                        f.vessel = av;
                        f.part = k.InPart;
                        f.portraitCam = k.kerbalCam;
                        if (k.headTransform != null) f.lookSource = k.headTransform;
                        return f;
                    }
                }
                List<Vessel> loaded = FlightGlobals.VesselsLoaded;
                for (int i = 0; i < loaded.Count; i++)
                {
                    Vessel v = loaded[i];
                    if (v == null || !v.isEVA || v.rootPart == null) continue;
                    List<ProtoCrewMember> crew = v.GetVesselCrew();
                    if (crew.Count == 0 || crew[0] == null || crew[0].name != name) continue;
                    KerbalEVA eva = v.rootPart.GetComponent<KerbalEVA>();
                    Transform root = eva != null ? eva.transform : v.rootPart.transform;
                    Face f = MakeFace(name, root, true, dbg);
                    if (f == null) continue;
                    f.vessel = v;
                    return f;
                }
                if (dbg) Log.Info("Face: no face found for " + name);
            }
            catch (Exception e) { Log.Exception("FaceDriver.Resolve", e); }
            return null;
        }

        private static Face MakeFace(string name, Transform root, bool eva, bool dbg)
        {
            kerbalExpressionSystem kes = root.GetComponent<kerbalExpressionSystem>();
            if (kes == null) kes = root.GetComponentInChildren<kerbalExpressionSystem>(true);
            Transform head = FindHead(root);
            if (kes == null && head == null)
            {
                if (dbg) Log.Info("Face: " + (eva ? "EVA " : "IVA ") + name + ": no kerbalExpressionSystem and no " + HeadBone);
                return null;
            }
            Face f = new Face();
            f.name = name;
            f.root = root;
            f.eva = eva;
            f.kes = kes;
            f.head = head;
            f.lookSource = head;
            if (head != null)
            {
                f.animPose = head.localRotation;
                f.lastWritten = f.animPose;
            }
            if (kes != null)
            {
                f.exprHash = Animator.StringToHash(ExprParam(kes));
                CheckExpr(f);
            }
            if (dbg)
            {
                Log.Info("Face: " + (eva ? "EVA " : "IVA ") + name + ": expression " + (f.hasExpr ? "yes" : "no") +
                         (f.anim != null ? " (Animator " + f.anim.updateMode + "/" + f.anim.cullingMode + ")" : "") +
                         ", head " + (head != null ? head.name : "not found"));
            }
            return f;
        }

        private static string ExprParam(kerbalExpressionSystem kes)
        {
            return string.IsNullOrEmpty(kes.expressionParameterName) ? "Expression" : kes.expressionParameterName;
        }

        /// <summary>Animator and expression parameter of a face. Before the Animator is initialised the answer is no.</summary>
        private static void CheckExpr(Face f)
        {
            if (f.kes == null) return;
            if (f.anim == null) f.anim = f.kes.animator != null ? f.kes.animator : f.root.GetComponentInChildren<Animator>(true);
            f.hasExpr = HasFloat(f.anim, ExprParam(f.kes));
        }

        /// <summary>
        /// Prefetch tick: a face whose Animator was not initialised yet when it was resolved (hasExpr false) is checked
        /// again, so the expression is not off for the rest of the flight.
        /// </summary>
        private void RecheckExpressions()
        {
            bool dbg = host != null && host.settings != null && host.settings.mouthDebug;
            foreach (Face f in faces.Values)
            {
                if (f.hasExpr || f.kes == null || f.root == null) continue;
                CheckExpr(f);
                if (f.hasExpr && dbg) Log.Info("Face: " + f.name + ": expression found on a later check");
            }
        }

        /// <summary>True when the Animator has a float parameter of this name (SetFloat on a missing one logs a warning).</summary>
        private static bool HasFloat(Animator an, string param)
        {
            // an Animator that is not initialised yet has no parameters: no cached "no" from that (rechecked later)
            if (an == null || an.runtimeAnimatorController == null || !an.isInitialized) return false;
            AnimatorControllerParameter[] ps = an.parameters;
            for (int i = 0; i < ps.Length; i++)
                if (ps[i].name == param && ps[i].type == AnimatorControllerParameterType.Float) return true;
            return false;
        }

        /// <summary>bn_headPivot_b01, or the shallowest bone with "headpivot" in its name that is not an end bone.</summary>
        private static Transform FindHead(Transform root)
        {
            Transform best = null;
            int bestDepth = int.MaxValue;
            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                Transform t = all[i];
                if (t.name == HeadBone) return t;
                string low = t.name.ToLowerInvariant();
                if (!low.Contains("headpivot_b") || low.StartsWith("be_")) continue;
                int d = 0;
                for (Transform p = t; p != null && p != root; p = p.parent) d++;
                if (d < bestDepth) { best = t; bestDepth = d; }
            }
            return best;
        }

        private string BuildStatus(Tuning tu, bool test)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("face: ").Append(faces.Count).Append(" kerbal(s), expr ").Append(exprOn ? "on" : "off")
              .Append(", head ").Append(headOn ? "on" : "off").Append(" axis ").Append("XYZ"[FaceMath.AxisIndex(tu.headTurnAxis)])
              .Append(tu.headTurnInvert > 0.5f ? " inverted" : "").Append(" max ").Append(tu.headTurnMaxDeg.ToString("0")).Append(" deg");
            if (test) sb.Append(", TEST");
            if (overrideOk + overrideLost > 0)
                sb.Append(overrideLost == 0 ? ", override ok" : ", override lost " + overrideLost + "/" + (overrideOk + overrideLost));
            overrideOk = overrideLost = 0;
            foreach (Face f in faces.Values)
            {
                if (!f.speaking && !f.listening && f.moodW <= 0f && f.yaw == 0f && !test) continue;
                sb.Append(" | ").Append(f.name).Append(f.eva ? " EVA" : " IVA")
                  .Append(f.speaking ? " speaks" : (f.listening ? " listens" : ""))
                  .Append(" mood ").Append(f.moodTarget.ToString("0.00"))
                  .Append(" w ").Append(f.moodW.ToString("0.00"));
                if (f.exprWritten && f.kes != null)
                {
                    sb.Append(" expr ").Append(f.kes.expression.ToString("0.00")).Append("->").Append(f.lastExpr.ToString("0.00"));
                    if (f.exprLost) sb.Append(" LOST");
                }
                else if (!f.hasExpr) sb.Append(" no expr");
                if (f.head != null)
                {
                    sb.Append(" yaw ").Append(f.yaw.ToString("0")).Append("/").Append(f.yawTarget.ToString("0"));
                    if (f.lookAt != null) sb.Append(" at ").Append(f.lookAt.name);
                    if (f.animatorWrites) sb.Append(" anim");
                    if (f.portraitCam != null) sb.Append(" portrait ").Append(f.portraitApplied).Append('/').Append(f.portraitSeen);
                }
                else sb.Append(" no head");
            }
            return sb.ToString();
        }
    }
}
