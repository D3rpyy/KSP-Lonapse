// Debug dump for the talking-kerbal study: writes the transform tree, the components, the animator
// parameters and the expression system settings of every kerbal model the plugin can reach, so the mouth animation
// can be built on real bone names. Output: GameData/Lonapse/PluginData/kerbal_dump.txt, summary in KSP.log.
// The dump itself only reads. The jaw probe afterwards (JawProbe) rotates each lower jaw by a marker angle for one
// frame to prove whether the Animator writes the bone, then puts it back. Run it while nobody is talking.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace Lonapse
{
    public static class KerbalDump
    {
        private static readonly string[] FaceWords = { "jaw", "mouth", "lip", "teeth", "tooth", "tongue", "chin", "face", "head", "eye", "brow" };

        public static string FilePath
        {
            get { return Path.Combine(Path.Combine(KSPUtil.ApplicationRootPath, "GameData/Lonapse/PluginData"), "kerbal_dump.txt"); }
        }

        /// <summary>Dumps the IVA kerbals of the active vessel and every loaded EVA kerbal. Returns a short result line.</summary>
        public static string Run()
        {
            StringBuilder sb = new StringBuilder();
            int models = 0;
            List<string> faceHits = new List<string>();
            List<string> summary = new List<string>();
            List<JawProbe.Target> probes = new List<JawProbe.Target>();
            try
            {
                sb.AppendLine("Lonapse kerbal dump, " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                sb.AppendLine("Unity " + Application.unityVersion);
                Vessel av = FlightGlobals.ActiveVessel;
                sb.AppendLine("Active vessel: " + (av != null ? av.vesselName + " (" + av.vesselType + ", EVA " + av.isEVA + ")" : "none"));
                sb.AppendLine();

                HashSet<int> seen = new HashSet<int>();
                // IVA kerbals: ProtoCrewMember.KerbalRef is the model in the internal space (also used by the portraits).
                // Active vessel first, then every other loaded vessel (KerbalRef is usually null there).
                List<Vessel> vessels = new List<Vessel>();
                if (av != null && !av.isEVA) vessels.Add(av);
                foreach (Vessel v in FlightGlobals.VesselsLoaded)
                    if (v != null && !v.isEVA && !ReferenceEquals(v, av)) vessels.Add(v);
                foreach (Vessel v in vessels)
                {
                    foreach (ProtoCrewMember pcm in v.GetVesselCrew())
                    {
                        if (pcm == null) continue;
                        Kerbal k = pcm.KerbalRef;
                        string kind = "IVA " + pcm.gender.ToString().ToUpperInvariant();
                        sb.AppendLine("=== " + kind + " " + pcm.name + " (" + pcm.gender + ", " + pcm.trait + ", seat " + pcm.seatIdx +
                                      ", vessel " + v.vesselName + (ReferenceEquals(v, av) ? ", active" : "") + ")");
                        if (k == null)
                        {
                            sb.AppendLine("  KerbalRef is null (IVA not spawned)");
                            sb.AppendLine();
                            summary.Add(kind + " " + pcm.name + ": no KerbalRef");
                            continue;
                        }
                        if (!seen.Add(k.gameObject.GetInstanceID())) continue;
                        DumpModel(sb, k.gameObject, pcm.name, faceHits);
                        AddSummary(summary, probes, kind, pcm.name, k.transform);
                        models++;
                    }
                }
                // EVA kerbals: every loaded vessel with a KerbalEVA (stand an EVA kerbal of the other gender next to it)
                foreach (Vessel v in FlightGlobals.VesselsLoaded)
                {
                    if (v == null || !v.isEVA || v.rootPart == null) continue;
                    KerbalEVA eva = v.rootPart.GetComponent<KerbalEVA>();
                    GameObject go = eva != null ? eva.gameObject : v.rootPart.gameObject;
                    if (!seen.Add(go.GetInstanceID())) continue;
                    ProtoCrewMember pcm = v.GetVesselCrew().Count > 0 ? v.GetVesselCrew()[0] : null;
                    string who = pcm != null ? pcm.name : v.vesselName;
                    string kind = "EVA " + (pcm != null ? pcm.gender.ToString().ToUpperInvariant() : "UNKNOWN");
                    sb.AppendLine("=== " + kind + " " + who + (pcm != null ? " (" + pcm.gender + ", " + pcm.trait + ")" : "") +
                                  (ReferenceEquals(v, av) ? ", active" : "") + (eva == null ? ", no KerbalEVA on the root part" : ""));
                    DumpModel(sb, go, who, faceHits);
                    AddSummary(summary, probes, kind, who, go.transform);
                    models++;
                }
                if (models == 0) sb.AppendLine("No kerbal models found. Use it in flight with crew aboard, or on EVA.");

                sb.AppendLine("=== Summary (kind, name, jaw bone, animator)");
                foreach (string line in summary) sb.AppendLine("  " + line);
                sb.AppendLine();
                sb.AppendLine("=== Face related names (all models)");
                foreach (string h in faceHits) sb.AppendLine("  " + h);

                string path = FilePath;
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
                string msg = "Kerbal dump: " + models + " model(s), " + faceHits.Count + " face related names, written to " + path;
                Log.Info(msg);
                foreach (string h in faceHits) Log.Info("Kerbal dump face: " + h);
                foreach (string line in summary) Log.Info("Kerbal dump: " + line);
                if (probes.Count > 0)
                {
                    JawProbe.Launch(probes, path);
                    msg += ", jaw probe running (result appended in a moment)";
                }
                return msg;
            }
            catch (Exception e)
            {
                Log.Exception("KerbalDump", e);
                return "Kerbal dump failed: " + e.Message;
            }
        }

        private static void AddSummary(List<string> summary, List<JawProbe.Target> probes, string kind, string who, Transform root)
        {
            Transform jaw = FindLowerJaw(root);
            Animator an = root.GetComponentInChildren<Animator>(true);
            string anim = an == null ? "no Animator" : "Animator " + (an.runtimeAnimatorController != null ? an.runtimeAnimatorController.name : "no controller") +
                          " (" + an.cullingMode + ")";
            summary.Add(kind + " " + who + ": jaw " + (jaw != null ? Path_(jaw, root) : "NOT FOUND") + ", " + anim);
            if (jaw != null) probes.Add(new JawProbe.Target { label = kind + " " + who, jaw = jaw });
        }

        /// <summary>Same search as MouthDriver: bn_lowerJaw01, else the shallowest "lowerjaw" bone that is not an end bone.</summary>
        private static Transform FindLowerJaw(Transform root)
        {
            Transform best = null;
            int bestDepth = int.MaxValue;
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "bn_lowerJaw01") return t;
                string low = t.name.ToLowerInvariant();
                if (!low.Contains("lowerjaw") || low.Contains("end") || low.StartsWith("be_")) continue;
                int d = 0;
                for (Transform p = t; p != null && p != root; p = p.parent) d++;
                if (d < bestDepth) { best = t; bestDepth = d; }
            }
            return best;
        }

        private static void DumpModel(StringBuilder sb, GameObject root, string who, List<string> faceHits)
        {
            // expression system: the stock face driver (panic, whee, fear) and its animator parameter names
            foreach (kerbalExpressionSystem ex in root.GetComponentsInChildren<kerbalExpressionSystem>(true))
            {
                sb.AppendLine("  kerbalExpressionSystem on " + Path_(ex.transform, root.transform) + ": isIVA " + ex.isIVAController +
                              ", expression '" + ex.expressionParameterName + "', variance '" + ex.varianceParameterName +
                              "', secondary '" + ex.secondaryVarianceParameterName + "', helmet '" + ex.hasHelmetName +
                              "', idleBool '" + ex.idleBoolName + "', idleFloat '" + ex.idleFloatName +
                              "', panic " + ex.panicLevel.ToString("0.00") + ", whee " + ex.wheeLevel.ToString("0.00") +
                              ", fear " + ex.fearFactor.ToString("0.00"));
            }
            foreach (Animator an in root.GetComponentsInChildren<Animator>(true))
            {
                sb.AppendLine("  Animator on " + Path_(an.transform, root.transform) + ": controller " +
                              (an.runtimeAnimatorController != null ? an.runtimeAnimatorController.name : "none") +
                              ", layers " + an.layerCount + ", enabled " + an.enabled + ", cullingMode " + an.cullingMode);
                try
                {
                    foreach (AnimatorControllerParameter p in an.parameters)
                        sb.AppendLine("    param " + p.name + " (" + p.type + ")");
                    for (int l = 0; l < an.layerCount; l++)
                        sb.AppendLine("    layer " + l + " " + an.GetLayerName(l) + " weight " + an.GetLayerWeight(l).ToString("0.00"));
                }
                catch (Exception e) { sb.AppendLine("    parameters not readable: " + e.Message); }
            }
            foreach (Animation an in root.GetComponentsInChildren<Animation>(true))
            {
                sb.Append("  Animation on " + Path_(an.transform, root.transform) + ":");
                foreach (AnimationState st in an) sb.Append(" " + st.name);
                sb.AppendLine();
            }
            foreach (SkinnedMeshRenderer smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                Mesh m = smr.sharedMesh;
                sb.AppendLine("  SkinnedMeshRenderer " + Path_(smr.transform, root.transform) + ": bones " +
                              (smr.bones != null ? smr.bones.Length : 0) + ", root bone " + (smr.rootBone != null ? smr.rootBone.name : "none") +
                              ", blendshapes " + (m != null ? m.blendShapeCount : 0) + ", enabled " + smr.enabled);
                if (m != null)
                    for (int b = 0; b < m.blendShapeCount; b++) sb.AppendLine("    blendshape " + m.GetBlendShapeName(b));
            }
            sb.AppendLine("  Transform tree:");
            Tree(sb, root.transform, root.transform, 0, who, faceHits);
            sb.AppendLine();
        }

        private static void Tree(StringBuilder sb, Transform t, Transform root, int depth, string who, List<string> faceHits)
        {
            if (depth > 40) return;
            StringBuilder comps = new StringBuilder();
            foreach (Component c in t.GetComponents<Component>())
            {
                if (c == null || c is Transform) continue;
                comps.Append(comps.Length == 0 ? "  [" : ", ").Append(c.GetType().Name);
            }
            if (comps.Length > 0) comps.Append("]");
            Vector3 e = t.localEulerAngles;
            sb.Append(' ', 4 + depth * 2).Append(t.name).Append(t.gameObject.activeSelf ? "" : " (inactive)")
              .Append("  rot ").Append(e.x.ToString("0")).Append('/').Append(e.y.ToString("0")).Append('/').Append(e.z.ToString("0"))
              .Append(comps.ToString()).AppendLine();
            string low = t.name.ToLowerInvariant();
            for (int i = 0; i < FaceWords.Length; i++)
                if (low.Contains(FaceWords[i])) { faceHits.Add(who + ": " + Path_(t, root)); break; }
            for (int i = 0; i < t.childCount; i++) Tree(sb, t.GetChild(i), root, depth + 1, who, faceHits);
        }

        private static string Path_(Transform t, Transform root)
        {
            string p = t.name;
            while (t != root && t.parent != null) { t = t.parent; p = t.name + "/" + p; }
            return p;
        }
    }
    /// <summary>
    /// Proves whether the Animator writes the lower jaw. Runs after MouthDriver (execution order 10001):
    /// frame 1 reads the jaw and sets a marker rotation, frame 2 reads it again. If the marker is still there,
    /// nothing wrote the bone in between. Frame 2 and 3 values show whether the pose changes on its own.
    /// The original rotation is put back unless something else already overwrote the marker.
    /// </summary>
    [DefaultExecutionOrder(10001)]
    public class JawProbe : MonoBehaviour
    {
        public class Target
        {
            public string label;
            public Transform jaw;
            public Quaternion r0, marker, r1, r2;
        }

        private const float MarkerDeg = 25f;
        private List<Target> targets;
        private string path;
        private int frame;

        public static void Launch(List<Target> targets, string path)
        {
            GameObject go = new GameObject("Lonapse_JawProbe");
            JawProbe p = go.AddComponent<JawProbe>();
            p.targets = targets;
            p.path = path;
        }

        private void LateUpdate()
        {
            try
            {
                frame++;
                if (frame == 1)
                {
                    foreach (Target t in targets)
                    {
                        if (t.jaw == null) continue;
                        t.r0 = t.jaw.localRotation;
                        t.marker = t.r0 * Quaternion.AngleAxis(MarkerDeg, Vector3.forward);
                        t.jaw.localRotation = t.marker;
                        t.marker = t.jaw.localRotation;
                    }
                    return;
                }
                if (frame == 2)
                {
                    foreach (Target t in targets)
                    {
                        if (t.jaw == null) continue;
                        t.r1 = t.jaw.localRotation;
                        if (Quaternion.Angle(t.r1, t.marker) < 0.01f) t.jaw.localRotation = t.r0; // nobody wrote it: put it back
                    }
                    return;
                }
                foreach (Target t in targets) if (t.jaw != null) t.r2 = t.jaw.localRotation;
                Finish();
            }
            catch (Exception e)
            {
                Log.Exception("JawProbe", e);
                Destroy(gameObject);
            }
        }

        private void Finish()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine();
            sb.AppendLine("=== Jaw probe (marker " + MarkerDeg + " deg about local Z set in frame 1 after all scripts)");
            foreach (Target t in targets)
            {
                if (t.jaw == null) { sb.AppendLine("  " + t.label + ": jaw destroyed during the probe"); continue; }
                bool kept = Quaternion.Angle(t.r1, t.marker) < 0.01f;
                string verdict = kept
                    ? "marker survived: nothing writes the jaw each frame (Animator culled or no jaw curve)"
                    : "marker overwritten: the Animator (or another script) writes the jaw each frame";
                sb.AppendLine("  " + t.label + ": frame1 " + Eul(t.r0) + ", frame2 " + Eul(t.r1) + ", frame3 " + Eul(t.r2) +
                              ", moved by itself frame2 to 3 " + Quaternion.Angle(t.r1, t.r2).ToString("0.000") + " deg. " + verdict);
                Log.Info("Jaw probe: " + t.label + ": " + verdict + " (frame1 " + Eul(t.r0) + ", frame2 " + Eul(t.r1) + ", frame3 " + Eul(t.r2) + ")");
            }
            try { File.AppendAllText(path, sb.ToString(), Encoding.UTF8); }
            catch (Exception e) { Log.Exception("JawProbe.write", e); }
            Destroy(gameObject);
        }

        private static string Eul(Quaternion q)
        {
            Vector3 e = q.eulerAngles;
            return e.x.ToString("0.00") + "/" + e.y.ToString("0.00") + "/" + e.z.ToString("0.00");
        }
    }
}
