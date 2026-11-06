// Crew portraits after the pose. Kerbal.kerbalAvatarUpdateCycle renders the portrait from a
// coroutine early in the frame, before the Animator and before LateUpdate. MouthDriver and FaceDriver also write the
// jaw and the head right before that render (Camera.onPreCull), and the overlay counted those writes, yet the portrait
// showed no talking and the head snapped back. In the IVA view, rendered after LateUpdate, everything worked.
// A likely cause is that Unity skins the mesh once per frame after LateUpdate, so a render early in the next frame
// shows an older skin and a bone write in onPreCull comes too late. This is not verified.
//
// PortraitRefresh renders the portrait again at the end of the frame (WaitForEndOfFrame), after LateUpdate and the
// skinning pass, for every kerbal whose jaw or head was driven this frame. It calls the stock update by reflection
// (kerbalSeatCamUpdate with the seat camera, else kerbalAvatarUpdate), so the camera setup, the IVA head renderers
// and the canvas guard stay stock. The stock coroutine keeps running. Our render overwrites its texture in the same
// frame. TUNING portraitRefresh = 0 switches this off (Debug tab).
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Lonapse
{
    public class PortraitRefresh : MonoBehaviour
    {
        /// <summary>True during our own render. The pre-cull hooks then leave the bones alone (LateUpdate set them).</summary>
        public static bool Rendering;

        private readonly HashSet<Kerbal> want = new HashSet<Kerbal>();
        private readonly List<Kerbal> work = new List<Kerbal>();
        private static MethodInfo seatUpdate, avatarUpdate;
        private static FieldInfo runningField, visibleField;
        private static bool reflected, reflectOk;
        private bool failed;
        private int renders, rendersRate;
        private float rateAt;

        /// <summary>Portrait renders of the last full second, or why there are none.</summary>
        public string Status
        {
            get
            {
                if (failed) return "portrait refresh: off after an error (see KSP.log)";
                if (reflected && !reflectOk) return "portrait refresh: stock methods not found";
                return "portrait refresh: " + rendersRate + " renders/s";
            }
        }

        /// <summary>Render this kerbal's portrait again at the end of this frame.</summary>
        public void Request(Kerbal k)
        {
            if (!failed && !ReferenceEquals(k, null)) want.Add(k);
        }

        private void Start()
        {
            Reflect();
            StartCoroutine(Loop());
        }

        private static void Reflect()
        {
            if (reflected) return;
            reflected = true;
            const BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            Type t = typeof(Kerbal);
            seatUpdate = t.GetMethod("kerbalSeatCamUpdate", F, null, new Type[] { typeof(Camera) }, null);
            avatarUpdate = t.GetMethod("kerbalAvatarUpdate", F, null, Type.EmptyTypes, null);
            runningField = t.GetField("running", F);
            visibleField = t.GetField("visibleInPortrait", F);
            reflectOk = seatUpdate != null && avatarUpdate != null;
            Log.Info("Portrait refresh: " + (reflectOk ? "stock portrait update found" : "stock portrait update NOT found, refresh off")
                     + (runningField == null ? ", no running field" : "") + (visibleField == null ? ", no visibleInPortrait field" : ""));
        }

        private IEnumerator Loop()
        {
            WaitForEndOfFrame eof = new WaitForEndOfFrame();
            while (true)
            {
                yield return eof;
                try { RenderAll(); }
                catch (Exception e)
                {
                    failed = true;
                    Rendering = false;
                    Log.Exception("PortraitRefresh", e);
                }
            }
        }

        private void RenderAll()
        {
            float now = Time.unscaledTime;
            if (now >= rateAt) { rateAt = now + 1f; rendersRate = renders; renders = 0; }
            if (want.Count == 0) return;
            work.Clear();
            work.AddRange(want);
            want.Clear();
            if (failed || !reflectOk) return;
            for (int i = 0; i < work.Count; i++)
            {
                Kerbal k = work[i];
                if (k == null || !k.isActiveAndEnabled) continue;   // Unity null: destroyed
                if (runningField != null && !(bool)runningField.GetValue(k)) continue;   // no portrait coroutine
                if (visibleField != null && !(bool)visibleField.GetValue(k)) continue;   // portrait hidden
                ProtoCrewMember pcm = k.protoCrewMember;
                Camera seatCam = pcm != null && pcm.seat != null ? pcm.seat.portraitCamera : null;
                Rendering = true;
                try
                {
                    if (seatCam != null) seatUpdate.Invoke(k, new object[] { seatCam });
                    else if (k.kerbalCam != null) avatarUpdate.Invoke(k, null);
                    else continue;
                    renders++;
                }
                catch (TargetInvocationException e)
                {
                    failed = true;
                    Log.Exception("PortraitRefresh (" + k.crewMemberName + ")", e.InnerException ?? e);
                    return;
                }
                finally { Rendering = false; }
            }
            work.Clear();
        }
    }
}
