// Docking approach detection (pure, tested offline) and docking-port geometry (KSP side).
using System;
#if !DC_OFFLINE_TEST
using System.Collections.Generic;
using UnityEngine;
#endif

namespace Lonapse
{
    public class DockingWatcher
    {
        public bool armed;
        public bool fired;          // approach call made, re-arm needs dockRearmDist
        public bool seenFar;        // target was seen beyond dockApproachDist since it became the target
        public bool hasTarget;
        public float distance = -1f;
        public float guardDistance = -1f;
        public float relSpeed;
        public float rangeRate;     // smoothed d(distance)/dt, negative = closing
        public float guardTime;     // seconds the guard has been active during this approach
        public bool guardBroken;    // parts broke during docking: no more suppression this approach
        private bool guardActive;
        private float holdTimer;
        private float prevDist = -1f;
        private object lastTarget;

        // close-approach phase (within dockCloseDist and closing)
        public bool closePhase;
        public bool closePhaseJustStarted;   // consumer clears
        public bool closeExchangeDue;        // consumer clears
        public float calloutDue = -1f;       // distance threshold crossed (30/15/5 m), consumer sets back to -1
        public float closeTimer;
        public static readonly float[] CalloutDistances = { 30f, 15f, 5f };
        private readonly bool[] calloutDone = new bool[3];
        public System.Random rng = new System.Random(7);

        public const float RangeRateTau = 0.5f;

        public float ClosingSpeed { get { return -rangeRate; } }

        private void ResetClose()
        {
            closePhase = false;
            closePhaseJustStarted = false;
            closeExchangeDue = false;
            calloutDue = -1f;
            closeTimer = 0f;
            for (int i = 0; i < calloutDone.Length; i++) calloutDone[i] = false;
        }

        /// <summary>
        /// True while docking contact is imminent: spin and impulse are then ignored. Capped at dockGuardMax seconds
        /// per approach, and off once parts broke.
        /// </summary>
        public bool InGuard(Tuning tu) { return guardActive; }

        /// <summary>Vessel change or undock: forget everything, a new approach must start from far away.</summary>
        public void Reset()
        {
            armed = false;
            fired = false;
            seenFar = false;
            hasTarget = false;
            distance = -1f;
            guardDistance = -1f;
            relSpeed = 0f;
            rangeRate = 0f;
            guardTime = 0f;
            guardBroken = false;
            guardActive = false;
            holdTimer = 0f;
            prevDist = -1f;
            lastTarget = null;
            ResetClose();
        }

        /// <summary>
        /// Feed the target state every frame (dt in real seconds). Returns true once per approach when:
        /// the target was first seen beyond dockApproachDist, is now within it, relative speed is below
        /// dockApproachSpeed and the range is closing faster than dockClosingRate, all held for dockApproachHold.
        /// After the call it re-arms only beyond dockRearmDist, on a new target, or after docking.
        /// </summary>
        public bool Update(bool targetValid, object target, float dist, float portDist, float speed, float dt, Tuning tu)
        {
            hasTarget = targetValid && target != null;
            if (!hasTarget)
            {
                holdTimer = 0f;
                distance = -1f;
                guardDistance = -1f;
                relSpeed = 0f;
                rangeRate = 0f;
                prevDist = -1f;
                guardActive = false;
                ResetClose();
                return false;
            }
            if (!ReferenceEquals(target, lastTarget))
            {
                lastTarget = target;
                armed = false;
                fired = false;
                seenFar = false;
                holdTimer = 0f;
                prevDist = -1f;
                rangeRate = 0f;
                guardTime = 0f;
                guardBroken = false;
                ResetClose();
            }

            float distBefore = prevDist;
            if (prevDist >= 0f && dt > 1e-4f)
            {
                if (Math.Abs(dist - prevDist) > 10f)
                {
                    // discontinuity (reference frame shift, target switch to another port): start over
                    rangeRate = 0f;
                    holdTimer = 0f;
                }
                else
                {
                    float r = (dist - prevDist) / dt;
                    // |d(dist)/dt| can never exceed the relative speed; clamping removes jitter spikes
                    float lim = Math.Abs(speed) + 0.05f;
                    r = DcMath.Clamp(DcMath.Finite(r), -lim, lim);
                    rangeRate += (r - rangeRate) * DcMath.ExpAlpha(dt, RangeRateTau);
                }
            }
            prevDist = dist;
            distance = dist;
            guardDistance = portDist >= 0f ? portDist : dist;
            relSpeed = speed;

            // guard: close to the port at low speed, at most dockGuardMax s per approach
            bool geometric = guardDistance < tu.dockGuardDist && speed < tu.dockApproachSpeed;
            if (geometric && !guardBroken && guardTime < tu.dockGuardMax)
            {
                guardTime += dt;
                guardActive = true;
            }
            else guardActive = false;

            if (dist > tu.dockRearmDist)
            {
                seenFar = true;
                armed = true;
                fired = false;
                guardTime = 0f;
                guardBroken = false;
            }
            else if (dist > tu.dockApproachDist)
            {
                seenFar = true;
                if (!fired) armed = true;
                guardTime = 0f;
                guardBroken = false;
            }
            if (dist > tu.dockApproachDist) ResetClose();

            UpdateClose(distBefore, dist, dt, tu);

            if (!armed || !seenFar) { holdTimer = 0f; return false; }
            bool closing = rangeRate < -tu.dockClosingRate;
            if (dist < tu.dockApproachDist && speed < tu.dockApproachSpeed && closing)
            {
                holdTimer += dt;
                if (holdTimer >= tu.dockApproachHold)
                {
                    armed = false;
                    fired = true;
                    holdTimer = 0f;
                    return true;
                }
            }
            else holdTimer = 0f;
            return false;
        }

        /// <summary>
        /// Close approach: starts within dockCloseDist while closing (after the target was seen far away), ends beyond
        /// dockCloseExit or on docking. Exchanges every dockCloseMin..dockCloseMax s while closing, callouts when the
        /// range first drops through 30, 15 and 5 m.
        /// </summary>
        private void UpdateClose(float distBefore, float dist, float dt, Tuning tu)
        {
            bool closing = rangeRate < -tu.dockClosingRate;
            if (!closePhase)
            {
                if (seenFar && dist < tu.dockCloseDist && closing)
                {
                    closePhase = true;
                    closePhaseJustStarted = true;
                    closeTimer = DcMath.Uniform(rng, tu.dockCloseMin, tu.dockCloseMax) * 0.6f;
                }
                else return;
            }
            else if (dist > tu.dockCloseExit)
            {
                ResetClose();
                return;
            }

            for (int i = 0; i < CalloutDistances.Length; i++)
            {
                float th = CalloutDistances[i];
                if (calloutDone[i] || th > tu.dockCloseDist + 0.01f) continue;
                if (dist <= th && (distBefore > th || distBefore < 0f || closePhaseJustStarted) && closing)
                {
                    // only one callout per frame; mark every threshold already passed as done
                    for (int j = 0; j <= i; j++) calloutDone[j] = true;
                    calloutDue = th;
                }
            }

            closeTimer -= dt;
            if (calloutDue > 0f && closeTimer < 4f) closeTimer = 4f; // keep exchanges clear of a callout
            if (closeTimer <= 0f && closing)
            {
                closeExchangeDue = true;
                closeTimer = DcMath.Uniform(rng, tu.dockCloseMin, tu.dockCloseMax);
            }
        }

        /// <summary>Parts broke during a couple: stop hiding motion for the rest of this approach.</summary>
        public void BreakGuard()
        {
            guardBroken = true;
            guardActive = false;
        }

        public void OnDocked()
        {
            // after a dock the next approach (to whatever target) has to start from far away again
            armed = false;
            fired = false;
            seenFar = false;
            holdTimer = 0f;
            lastTarget = null;
            prevDist = -1f;
            rangeRate = 0f;
            ResetClose();
        }
    }

#if !DC_OFFLINE_TEST
    /// <summary>
    /// Distance between docking ports: nearest own ModuleDockingNode.nodeTransform to the targeted port, or to the
    /// nearest docking node on the target vessel. Node lists are cached and rebuilt only when marked dirty or when
    /// either vessel changes, so there is no per-frame allocation.
    /// </summary>
    public class DockingGeometry
    {
        private readonly List<ModuleDockingNode> own = new List<ModuleDockingNode>();
        private readonly List<ModuleDockingNode> other = new List<ModuleDockingNode>();
        private Vessel ownVessel, otherVessel;
        public bool dirty = true;
        public int ownCount { get { return own.Count; } }
        public int otherCount { get { return other.Count; } }

        private static void Collect(Vessel v, List<ModuleDockingNode> list)
        {
            list.Clear();
            if (v == null || v.parts == null) return;
            for (int i = 0; i < v.parts.Count; i++)
            {
                Part p = v.parts[i];
                if (p == null || p.Modules == null) continue;
                for (int m = 0; m < p.Modules.Count; m++)
                {
                    ModuleDockingNode n = p.Modules[m] as ModuleDockingNode;
                    if (n != null) list.Add(n);
                }
            }
        }

        /// <summary>Port-to-port distance, or -1 when either side has no usable docking node.</summary>
        public float PortDistance(Vessel v, Vessel tv, ITargetable t)
        {
            if (dirty || !ReferenceEquals(v, ownVessel) || !ReferenceEquals(tv, otherVessel))
            {
                Collect(v, own);
                Collect(tv, other);
                ownVessel = v;
                otherVessel = tv;
                dirty = false;
            }
            ModuleDockingNode tn = t as ModuleDockingNode;
            if (tn != null && (tn.nodeTransform == null)) tn = null; // Unity null check covers destroyed nodes
            float best = float.MaxValue;
            for (int i = 0; i < own.Count; i++)
            {
                ModuleDockingNode a = own[i];
                if (a == null || a.nodeTransform == null) continue;
                Vector3 pa = a.nodeTransform.position;
                if (tn != null)
                {
                    float d = (pa - tn.nodeTransform.position).magnitude;
                    if (d < best) best = d;
                    continue;
                }
                for (int j = 0; j < other.Count; j++)
                {
                    ModuleDockingNode b = other[j];
                    if (b == null || b.nodeTransform == null) continue;
                    float d = (pa - b.nodeTransform.position).magnitude;
                    if (d < best) best = d;
                }
            }
            return best < float.MaxValue ? best : -1f;
        }
    }
#endif
}
