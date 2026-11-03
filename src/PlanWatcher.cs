// Maneuver-node chatter (plan v2, req 18). Polls the node list every 0.5 s. A changed plan gives a
// pre-planning exchange (rate limited by planRateSec). A node closer than burnWarnSec gives one burn warning.
using System;
using System.Collections.Generic;

namespace Lonapse
{
    public class PlanWatcher
    {
        private float pollLeft;
        private string lastSig;
        private float clock;
        private float lastPlanChatter = -1e9f;
        private bool pending;
        private float lastBurnWarn = -1e9f;
        private float stableSince;
        private readonly HashSet<long> warned = new HashSet<long>();
        public int nodeCount;
        public double nextBurnIn = -1;

        public bool planDue;    // a plan_node exchange should be played
        public bool burnDue;    // a burn_soon warning should be played

        public void Reset()
        {
            lastSig = null;
            pending = false;
            warned.Clear();
            planDue = burnDue = false;
            nodeCount = 0;
            nextBurnIn = -1;
        }

        public void Update(float dt, Vessel v, Tuning tu)
        {
            clock += dt;
            pollLeft -= dt;
            if (pollLeft > 0f) return;
            pollLeft = 0.5f;
            List<ManeuverNode> nodes = null;
            try { if (v != null && v.patchedConicSolver != null) nodes = v.patchedConicSolver.maneuverNodes; }
            catch (Exception) { nodes = null; }
            int n = nodes != null ? nodes.Count : 0;
            nodeCount = n;
            double[] uts = new double[n], dvs = new double[n];
            double now = 0;
            try { now = Planetarium.GetUniversalTime(); } catch (Exception) { }
            nextBurnIn = -1;
            for (int i = 0; i < n; i++)
            {
                ManeuverNode m = nodes[i];
                if (m == null) continue;
                uts[i] = m.UT;
                dvs[i] = m.DeltaV.magnitude;
                double dtBurn = m.UT - now;
                if (dtBurn > 0 && (nextBurnIn < 0 || dtBurn < nextBurnIn)) nextBurnIn = dtBurn;
                long key = (long)Math.Round(m.UT);
                // dragging a node inside the window changes its key: the minimum gap stops a repeat
                if (dtBurn > 0 && dtBurn < tu.burnWarnSec && dvs[i] > 0.5 && !warned.Contains(key)
                    && clock - lastBurnWarn >= tu.burnWarnSec)
                {
                    warned.Add(key);
                    lastBurnWarn = clock;
                    burnDue = true;
                    Log.Info("Maneuver burn in " + dtBurn.ToString("0") + " s, " + dvs[i].ToString("0.0") + " m/s");
                }
            }
            // a warning that could not be played yet goes stale once the burn is no longer close
            if (burnDue && (nextBurnIn < 0 || nextBurnIn > tu.burnWarnSec)) burnDue = false;
            string sig = PacingRules.PlanSignature(n, uts, dvs);
            if (lastSig == null) { lastSig = sig; return; } // first poll after a vessel switch: no chatter
            if (sig != lastSig)
            {
                // dragging a node changes the signature every poll: wait until it holds still for 2 s
                lastSig = sig;
                pending = n > 0; // deleting the plan is not worth a call
                stableSince = clock;
                return;
            }
            if (!pending || clock - stableSince < 2f) return;
            pending = false;
            if (clock - lastPlanChatter < tu.planRateSec) return;
            lastPlanChatter = clock;
            planDue = true;
            Log.Info("Maneuver plan changed (" + n + " node(s)), pre-planning exchange due");
        }
    }
}
