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

        // far callouts: the range drops through 500, 250 and 100 m while closing, before the approach call. Each comes once
        // per approach and comes again after the target went further than 1.15 x its distance.
        public static readonly float[] FarCalloutDistances = { 500f, 250f, 100f };
        private readonly bool[] farDone = new bool[3];
        public float farCalloutDue = -1f;   // distance threshold crossed, consumer sets back to -1

        // The approach is on from the approach call until docking, undock, a lost target or the target further than dockRearmDist.
        // Other chatter waits for dockChatHold s of it.
        public bool approachActive;
        public float approachTime;
        public bool callSkipped;    // the consumer skipped the approach call (no intent to dock): no chatter hold
        /// <summary>The approach call that Update just returned is not played: other chatter does not wait for it.</summary>
        public void SkipApproach() { callSkipped = true; approachActive = false; approachTime = 0f; }
        public bool HoldsChat(Tuning tu) { return approachActive && approachTime < tu.dockChatHold; }

        private void ResetFar()
        {
            farCalloutDue = -1f;
            for (int i = 0; i < farDone.Length; i++) farDone[i] = false;
        }
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
            approachActive = false;
            approachTime = 0f;
            ResetFar();
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
                approachActive = false;
                approachTime = 0f;
                ResetFar();
                ResetClose();
                return false;
            }
            if (!ReferenceEquals(target, lastTarget))
            {
                lastTarget = target;
                ResetFar();
                approachActive = false;
                approachTime = 0f;
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
                    // |d(dist)/dt| can never exceed the relative speed, so clamping removes jitter spikes
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
            UpdateFar(distBefore, dist, tu);
            approachActive = fired && !callSkipped && dist < tu.dockRearmDist;
            if (approachActive) approachTime += dt; else approachTime = 0f;

            if (!armed || !seenFar) { holdTimer = 0f; return false; }
            bool closing = rangeRate < -tu.dockClosingRate;
            if (dist < tu.dockApproachDist && speed < tu.dockApproachSpeed && closing)
            {
                holdTimer += dt;
                if (holdTimer >= tu.dockApproachHold)
                {
                    armed = false;
                    fired = true;
                    callSkipped = false;
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
                    // only one callout per frame. Every threshold already passed is marked done.
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

        /// <summary>Far callouts: the range first drops through 500, 250 or 100 m while closing.</summary>
        private void UpdateFar(float distBefore, float dist, Tuning tu)
        {
            bool closing = rangeRate < -tu.dockClosingRate;
            for (int i = 0; i < FarCalloutDistances.Length; i++)
            {
                float th = FarCalloutDistances[i];
                if (dist > th * 1.15f) { farDone[i] = false; continue; }
                if (farDone[i]) continue;
                if (dist <= th && distBefore > th && closing)
                {
                    farDone[i] = true;
                    farCalloutDue = th;   // the nearest threshold crossed in this frame wins
                }
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
            approachActive = false;
            approachTime = 0f;
            ResetFar();
            ResetClose();
        }
    }

    /// <summary>
    /// Whether the player really means to dock, from the ports. Three signs (bit flags): a free port on the target
    /// of the same size as one of ours, one of our ports armed (free and chosen: the ship is controlled from it or the
    /// player targeted a port, not only the vessel), and the two ports facing each other. Two of the three make
    /// the docking calls and callouts sure, one makes them likelier.
    /// </summary>
    public static class DockIntent
    {
        public const int Compatible = 1, Armed = 2, Facing = 4;
        public const int All = Compatible | Armed | Facing;

        public static int Count(int flags)
        {
            int n = 0;
            if ((flags & Compatible) != 0) n++;
            if ((flags & Armed) != 0) n++;
            if ((flags & Facing) != 0) n++;
            return n;
        }

        /// <summary>Two port axes face each other when they are about opposite: their dot product is at most
        /// -cos(maxAngleDeg). The direction the axes point (out of the port or into it) does not matter.</summary>
        public static bool AxesFace(float dot, float maxAngleDeg)
        {
            return dot <= -(float)Math.Cos(maxAngleDeg * Math.PI / 180.0);
        }

        /// <summary>Chance that a docking call plays: baseChance with none of the signs, halfway with one, 1 with two
        /// or three. A normal docking (a free port of the same size, ports facing) is sure without "Control From Here".</summary>
        public static float Chance(int flags, float baseChance)
        {
            float b = DcMath.Clamp(DcMath.Finite(baseChance), 0f, 1f);
            return b + (1f - b) * Math.Min(Count(flags), 2) / 2f;
        }

        public static string Describe(int flags)
        {
            return ((flags & Compatible) != 0 ? "compatible port" : "no compatible port") + ", "
                 + ((flags & Armed) != 0 ? "own port armed (control or port target)" : "own port not armed") + ", "
                 + ((flags & Facing) != 0 ? "ports facing" : "ports not facing");
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

        static bool StateIs(ModuleDockingNode n, string prefix)
        {
            string s = null;
            try { s = n.state; } catch (Exception) { }
            return s != null && s.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// DockIntent flags of the best pair of ports (most signs, then the nearest): the target port, or any port of
        /// the target vessel, against each of our ports. Uses the node lists PortDistance filled, so call that first.
        /// A free target port is "Ready". An own port is armed when it is free ("Ready" or "Acquire", a closed shielded
        /// port is not) and chosen: the ship is controlled from it, or the player targeted a port. "Ready" alone is the
        /// idle state of every free port, so it shows no intent. 0 when a side has no port.
        /// </summary>
        public int Intent(ITargetable t, float maxAngleDeg)
        {
            ModuleDockingNode tn = t as ModuleDockingNode;
            if (tn != null && tn.nodeTransform == null) tn = null;
            Part control = null;
            try { if (ownVessel != null) control = ownVessel.GetReferenceTransformPart(); } catch (Exception) { }
            int bestFlags = 0;
            float bestDist = float.MaxValue;
            bool any = false;
            for (int i = 0; i < own.Count; i++)
            {
                ModuleDockingNode a = own[i];
                if (a == null || a.nodeTransform == null) continue;
                bool chosen = tn != null || (control != null && ReferenceEquals(a.part, control));
                bool armed = chosen && (StateIs(a, "Ready") || StateIs(a, "Acquire"));
                int count = tn != null ? 1 : other.Count;
                for (int j = 0; j < count; j++)
                {
                    ModuleDockingNode b = tn != null ? tn : other[j];
                    if (b == null || b.nodeTransform == null) continue;
                    int f = 0;
                    if (armed) f |= DockIntent.Armed;
                    if (string.Equals(a.nodeType, b.nodeType, StringComparison.OrdinalIgnoreCase) && StateIs(b, "Ready")) f |= DockIntent.Compatible;
                    if (DockIntent.AxesFace(Vector3.Dot(a.nodeTransform.forward, b.nodeTransform.forward), maxAngleDeg)) f |= DockIntent.Facing;
                    float d = (a.nodeTransform.position - b.nodeTransform.position).magnitude;
                    if (!any || DockIntent.Count(f) > DockIntent.Count(bestFlags) || (DockIntent.Count(f) == DockIntent.Count(bestFlags) && d < bestDist))
                    {
                        any = true;
                        bestFlags = f;
                        bestDist = d;
                    }
                }
            }
            return bestFlags;
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
