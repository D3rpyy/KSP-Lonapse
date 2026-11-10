// PlayerLoop hooks: run a callback at a fixed point of Unity's frame, right before a named engine step, whatever the
// engine version does with coroutines and script order. FaceDriver uses it to write the expression parameter right
// before the Animator evaluates: PreLateUpdate.DirectorUpdateAnimationBegin for Animators in Normal or UnscaledTime
// mode, FixedUpdate.DirectorFixedUpdate for Animators in Fixed mode (AnimatePhysics before Unity 6), which are
// evaluated inside the physics step.
//
// The hooks change the live PlayerLoop, so every Insert has a Remove (OnDisable / OnDestroy). Insert reads the current
// loop first, so the systems other mods added stay in place.
using System;
using UnityEngine.LowLevel;

namespace Lonapse
{
    internal static class LoopHook
    {
        /// <summary>
        /// Insert a system of type marker (with fn) right before the subsystem of type before. False when that step is
        /// not in the loop (the caller keeps its fallback). A marker that is already in the loop is replaced.
        /// </summary>
        public static bool Insert(Type marker, Type before, PlayerLoopSystem.UpdateFunction fn)
        {
            PlayerLoopSystem root = PlayerLoop.GetCurrentPlayerLoop();
            Strip(ref root, marker);
            PlayerLoopSystem add = new PlayerLoopSystem { type = marker, updateDelegate = fn };
            if (!InsertBefore(ref root, before, add)) return false;
            PlayerLoop.SetPlayerLoop(root);
            return true;
        }

        /// <summary>Remove every system of type marker from the loop.</summary>
        public static void Remove(Type marker)
        {
            PlayerLoopSystem root = PlayerLoop.GetCurrentPlayerLoop();
            if (Strip(ref root, marker)) PlayerLoop.SetPlayerLoop(root);
        }

        private static bool InsertBefore(ref PlayerLoopSystem sys, Type before, PlayerLoopSystem add)
        {
            PlayerLoopSystem[] subs = sys.subSystemList;
            if (subs == null) return false;
            for (int i = 0; i < subs.Length; i++)
            {
                if (subs[i].type == before)
                {
                    PlayerLoopSystem[] n = new PlayerLoopSystem[subs.Length + 1];
                    Array.Copy(subs, 0, n, 0, i);
                    n[i] = add;
                    Array.Copy(subs, i, n, i + 1, subs.Length - i);
                    sys.subSystemList = n;
                    return true;
                }
                if (InsertBefore(ref subs[i], before, add)) return true;
            }
            return false;
        }

        private static bool Strip(ref PlayerLoopSystem sys, Type marker)
        {
            PlayerLoopSystem[] subs = sys.subSystemList;
            if (subs == null) return false;
            bool changed = false;
            int keep = 0;
            for (int i = 0; i < subs.Length; i++)
            {
                if (subs[i].type == marker) { changed = true; continue; }
                if (Strip(ref subs[i], marker)) changed = true;
                subs[keep++] = subs[i];
            }
            if (keep != subs.Length)
            {
                PlayerLoopSystem[] n = new PlayerLoopSystem[keep];
                Array.Copy(subs, n, keep);
                sys.subSystemList = n;
            }
            return changed;
        }
    }
}
