// One-word crew callouts (staging, parachutes): shared cooldown and parachute grouping. Pure C#, tested offline.
#if !DC_OFFLINE_TEST
using System;
using System.Collections.Generic;
#endif

namespace Lonapse
{
    public enum ChuteCallout { None, Semi, Full }

    public class CalloutGate
    {
        public float lastCallout = -999f;    // any one-word callout (stage or parachute)
        public float lastChuteSemi = -999f;  // start of the last semi-deploy group
        public float lastChuteFull = -999f;  // start of the last full-deploy group

        /// <summary>Shared cooldown: no two callouts within <paramref name="cooldown"/> seconds.</summary>
        public bool TryShared(float now, float cooldown)
        {
            if (now - lastCallout < cooldown) return false;
            lastCallout = now;
            return true;
        }

        public bool SharedReady(float now, float cooldown) { return now - lastCallout >= cooldown; }

        /// <summary>
        /// Parachute transitions seen in one poll. Several chutes within groupCooldown of a group start count as one
        /// group. A group is consumed even when the shared cooldown suppresses its callout (staging that deploys
        /// chutes gives a single callout). Full deployment speaks only when more than fullMinGap after the semi
        /// deployment of its chutes.
        /// </summary>
        public ChuteCallout OnChutes(float now, int semiTransitions, int fullTransitions,
                                     float groupCooldown, float fullMinGap, float sharedCooldown)
        {
            if (semiTransitions > 0 && now - lastChuteSemi >= groupCooldown)
            {
                lastChuteSemi = now;
                return TryShared(now, sharedCooldown) ? ChuteCallout.Semi : ChuteCallout.None;
            }
            if (fullTransitions > 0 && now - lastChuteFull >= groupCooldown)
            {
                lastChuteFull = now;
                if (now - lastChuteSemi <= fullMinGap) return ChuteCallout.None;
                return TryShared(now, sharedCooldown) ? ChuteCallout.Full : ChuteCallout.None;
            }
            return ChuteCallout.None;
        }
    }

#if !DC_OFFLINE_TEST
    /// <summary>
    /// Parachute state poll on the active vessel. The module list is cached and rebuilt only when marked dirty or on
    /// a vessel change. States are remembered so only transitions count (nothing fires for chutes already open at load).
    /// </summary>
    public class ChuteTracker
    {
        private readonly List<ModuleParachute> chutes = new List<ModuleParachute>();
        private readonly List<ModuleParachute.deploymentStates> states = new List<ModuleParachute.deploymentStates>();
        private Vessel vessel;
        public bool dirty = true;
        public int Count { get { return chutes.Count; } }

        private void Rebuild(Vessel v)
        {
            chutes.Clear();
            states.Clear();
            vessel = v;
            dirty = false;
            if (v == null || v.parts == null) return;
            for (int i = 0; i < v.parts.Count; i++)
            {
                Part p = v.parts[i];
                if (p == null || p.Modules == null) continue;
                for (int m = 0; m < p.Modules.Count; m++)
                {
                    ModuleParachute c = p.Modules[m] as ModuleParachute;
                    if (c == null) continue;
                    chutes.Add(c);
                    states.Add(c.deploymentState);
                }
            }
        }

        /// <summary>Counts transitions into SEMIDEPLOYED and into DEPLOYED since the last poll.</summary>
        public void Poll(Vessel v, out int semi, out int full)
        {
            semi = 0; full = 0;
            if (dirty || !ReferenceEquals(v, vessel)) { Rebuild(v); return; }
            for (int i = 0; i < chutes.Count; i++)
            {
                ModuleParachute c = chutes[i];
                if (c == null) continue; // destroyed part (Unity null)
                ModuleParachute.deploymentStates now = c.deploymentState, before = states[i];
                if (now == before) continue;
                states[i] = now;
                if (now == ModuleParachute.deploymentStates.SEMIDEPLOYED) semi++;
                else if (now == ModuleParachute.deploymentStates.DEPLOYED) full++;
            }
        }
    }
#endif
}
