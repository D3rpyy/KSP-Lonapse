// Probe voices: AudioClips made in game with ProbeSynth, and the ModuleLonapse part module.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Lonapse
{
    /// <summary>
    /// Clips of probe voices that are not stock, made on first use and kept for the game session.
    /// The key is the sound signature plus the mood, so a voice changed in the window gets new clips and two voices
    /// with the same sound share them. At most MaxVoices sounds are kept, the oldest are destroyed.
    /// </summary>
    public static class ProbeVoiceBank
    {
        private const int MaxVoices = 8;
        private static readonly Dictionary<string, AudioClip[]> clips = new Dictionary<string, AudioClip[]>();
        private static readonly List<string> signatures = new List<string>();   // oldest first

        /// <summary>A clip of the voice in that mood (one of 3 variants), null when it cannot be made.</summary>
        public static AudioClip Next(ProbeVoiceSpec v, string mood, System.Random rng)
        {
            if (v == null || string.IsNullOrEmpty(mood)) return null;
            AudioClip[] set = Get(v, mood);
            if (set == null || set.Length == 0) return null;
            return set[rng != null ? rng.Next(set.Length) : 0];
        }

        private static AudioClip[] Get(ProbeVoiceSpec v, string mood)
        {
            string sig = v.Signature();
            string key = sig + "#" + mood;
            AudioClip[] set;
            if (clips.TryGetValue(key, out set) && Alive(set)) return set;
            try
            {
                float t0 = Time.realtimeSinceStartup;
                // the same voice always sounds the same: the timing jitter is seeded from its sound and mood
                System.Random jr = new System.Random((int)StableHash.Fnv1a(key));
                ProbeSynth.Uniform u = (a, b) => a + (b - a) * jr.NextDouble();
                set = new AudioClip[ProbeSynth.VariantsPerMood];
                for (int i = 0; i < set.Length; i++)
                {
                    float[] data = ProbeSynth.Render(v, mood, i, u);
                    AudioClip c = AudioClip.Create("probe_" + v.Name + "_" + mood + "_" + i, data.Length, 1, ProbeSynth.SampleRate, false);
                    c.SetData(data, 0);
                    set[i] = c;
                }
                clips[key] = set;
                Touch(sig);
                Log.Debug("Probe voice " + v.Name + " " + mood + ": " + set.Length + " clips made in "
                          + ((Time.realtimeSinceStartup - t0) * 1000f).ToString("0") + " ms");
                return set;
            }
            catch (Exception e)
            {
                Log.Exception("ProbeVoiceBank " + v.Name + " " + mood, e);
                return null;
            }
        }

        private static bool Alive(AudioClip[] set)
        {
            if (set == null) return false;
            for (int i = 0; i < set.Length; i++) if (set[i] == null) return false;   // Unity null: destroyed
            return true;
        }

        /// <summary>Marks a sound as used, and destroys the clips of the oldest sounds beyond MaxVoices.</summary>
        private static void Touch(string sig)
        {
            signatures.Remove(sig);
            signatures.Add(sig);
            while (signatures.Count > MaxVoices)
            {
                string old = signatures[0];
                signatures.RemoveAt(0);
                var drop = new List<string>();
                foreach (var kv in clips) if (kv.Key.StartsWith(old + "#", StringComparison.Ordinal)) drop.Add(kv.Key);
                foreach (string k in drop)
                {
                    foreach (AudioClip c in clips[k]) if (c != null) UnityEngine.Object.Destroy(c);
                    clips.Remove(k);
                }
            }
        }
    }

    /// <summary>
    /// Reads and writes a voice as a ConfigNode: LONAPSE_PROBE_VOICE in GameData, PROBE_VOICE in settings.cfg.
    /// Both have the same keys and MOOD child nodes, so a voice copied in the window keeps a mod's moods.
    /// </summary>
    public static class ProbeVoiceNodes
    {
        public const string MoodNodeName = "MOOD";

        public static ProbeVoiceSpec FromNode(ConfigNode n, List<string> errors)
        {
            if (n == null) return null;
            ProbeVoiceSpec v = ProbeVoiceSpec.FromValues(Values(n), errors);
            if (v == null) return null;
            foreach (ConfigNode mn in n.GetNodes(MoodNodeName))
                v.SetMood(ProbeMoodSpec.FromValues(Values(mn), mn.GetValues("notes"), v.Name, errors));
            return v;
        }

        public static ConfigNode ToNode(ProbeVoiceSpec v, string nodeName)
        {
            ConfigNode n = new ConfigNode(nodeName);
            foreach (var kv in v.ToValues()) n.AddValue(kv.Key, kv.Value);
            if (v.Moods != null)
                foreach (ProbeMoodSpec m in v.Moods.Values)
                {
                    ConfigNode mn = n.AddNode(MoodNodeName);
                    foreach (var kv in m.ToValues()) mn.AddValue(kv.Key, kv.Value);
                    foreach (string line in m.Notes) mn.AddValue("notes", line);
                }
            return n;
        }

        private static Dictionary<string, string> Values(ConfigNode n)
        {
            var d = new Dictionary<string, string>();
            for (int i = 0; i < n.values.Count; i++)
                if (n.values[i].name != "notes") d[n.values[i].name] = n.values[i].value;
            return d;
        }
    }

    /// <summary>
    /// Reads the LONAPSE_PROBE_LINES nodes from GameData (name, displayName) and registers them as linesets, so the
    /// core column of a table and the lines of a voice can name them. Works without Module Manager. Once per session.
    /// </summary>
    public static class ProbeLinesetNodes
    {
        public const string NodeName = "LONAPSE_PROBE_LINES";
        private static bool loaded;

        public static void Load()
        {
            if (loaded) return;
            try
            {
                if (GameDatabase.Instance == null) return;
                loaded = true;
                foreach (ConfigNode n in GameDatabase.Instance.GetConfigNodes(NodeName))
                {
                    var d = new Dictionary<string, string>();
                    for (int i = 0; i < n.values.Count; i++) d[n.values[i].name] = n.values[i].value;
                    var errs = new List<string>();
                    ProbeCores.RegisterLineset(d, errs);
                    foreach (string e in errs) Log.Warn(NodeName + ": " + e);
                }
                string[] ids = ProbeCores.LinesKeys();
                if (ids.Length > ProbeCores.Keys.Length)
                    Log.Info("Probe linesets from GameData: " + string.Join(", ", ids, ProbeCores.Keys.Length, ids.Length - ProbeCores.Keys.Length));
            }
            catch (Exception e) { Log.Exception(NodeName, e); }
        }
    }

    /// <summary>
    /// Gives a probe core part its Lonapse voice. Added with a Module Manager patch, for example:
    /// <code>@PART[myProbeCore]:NEEDS[Lonapse] { MODULE { name = ModuleLonapse  voice = okto  priority = 0 } }</code>
    /// voice is a stock voice or the name of a LONAPSE_PROBE_VOICE node. On a vessel with several cores the highest
    /// priority wins. A voice the player picks in the Lonapse window wins over both. lines (optional) is a lineset from a
    /// LONAPSE_PROBE_LINES node or a stock core, for the lines the probe and Mission Control say. displayName (optional, may be a
    /// #autoLOC tag) is the subtitle name of the part. It wins over the voice's own displayName, so several parts can
    /// share one voice.
    /// </summary>
    public class ModuleLonapse : PartModule
    {
        [KSPField] public string voice = "";
        [KSPField] public int priority = 0;
        [KSPField] public string displayName = "";
        [KSPField] public string lines = "";   // phrase table core: a stock core or a lineset, wins over the lines of the voice

        public override string GetInfo() { return string.Empty; }   // nothing in the editor part info
        public override string GetModuleDisplayName() { return "Lonapse probe voice"; }
    }
}
