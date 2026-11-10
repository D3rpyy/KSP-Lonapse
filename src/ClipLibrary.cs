// Clip discovery and per-folder shuffle bags.
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Lonapse
{
    public class ClipBag
    {
        public readonly string folder;
        public readonly List<AudioClip> clips = new List<AudioClip>();
        private ShuffleBag bag;

        public ClipBag(string folder) { this.folder = folder; }

        public int Count { get { return clips.Count; } }

        public void Seal() { bag = new ShuffleBag(clips.Count); }

        public AudioClip Next(System.Random rng)
        {
            if (clips.Count == 0) return null;
            if (bag == null || bag.Count != clips.Count) Seal();
            int i = bag.Next(rng);
            return (i >= 0 && i < clips.Count) ? clips[i] : null;
        }
    }

    public static class ClipLibrary
    {
        public const string Prefix = "Lonapse/Sounds/";

        // The fixed folders. Folders (below) is this list plus the probe folders.
        private static readonly string[] BaseFolders =
        {
            "crew/calm", "crew/tense", "crew/panic", "crew/relief", "crew/callout",
            "control/calm", "control/tense",
            "beeps/ping", "beeps/descent", "beeps/stage", "beeps/landing", "beeps/splashdown",
            "beeps/quindar_in", "beeps/quindar_out",
            "sfx/burst", "sfx/loop", "sfx/squelch",
            "beeps/click_desk", "beeps/headset_open", "beeps/headset_close", "sfx/breath_loop", "beeps/master_alarm",
            "beeps/roger",
            "sfx/airlock_dock", "sfx/airlock_undock", "sfx/soi_change", "sfx/hatch_open", "sfx/hatch_close", "sfx/flag_plant", "sfx/jetpack_on", "sfx/jetpack_off", "sfx/jetpack_loop", "sfx/thud_heavy", "sfx/thud_light", "sfx/footstep", "sfx/cabin_loop", "sfx/cabin_power_down",
        };

        // one entry per folder in BaseFolders, in the same order
        private static readonly int[] BaseMinimums = { 24, 24, 24, 8, 8, 24, 12, 3, 2, 2, 1, 1, 1, 1, 6, 2, 2, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1 };

        // Built clips per probe beep folder (build_clips.py)
        public const int ProbeBeepMinimum = 3;

        /// <summary>Every folder the library loads: the fixed folders, then ProbeCores.Folders()
        /// (42 folders beeps/probe_&lt;core&gt;_&lt;mood&gt;) and beeps/probe_data.</summary>
        public static readonly string[] Folders = BuildFolders();

        // one entry per folder in Folders, in the same order
        public static readonly int[] Minimums = BuildMinimums();

        private static string[] BuildFolders()
        {
            List<string> r = new List<string>(BaseFolders);
            r.Add("sfx/control_room_loop");
            r.Add("sfx/cabin_rattle");
            r.Add("sfx/cabin_separation");
            foreach (string f in ProbeCores.Folders()) if (!r.Contains(f)) r.Add(f);
            r.Add(ProbeCores.DataFolder);
            return r.ToArray();
        }

        private static int[] BuildMinimums()
        {
            int[] m = new int[Folders.Length];
            for (int i = 0; i < Folders.Length; i++)
            {
                if (i < BaseMinimums.Length) m[i] = BaseMinimums[i];
                else if (Folders[i].StartsWith("beeps/probe_", StringComparison.Ordinal)) m[i] = ProbeBeepMinimum;
                else m[i] = 1;
            }
            return m;
        }

        /// <summary>Minimum clip count of folder i, 1 when the table is short (a new folder must never crash the start).</summary>
        public static int MinimumOf(int i) { return i >= 0 && i < Minimums.Length ? Minimums[i] : 1; }

        private static readonly Dictionary<string, ClipBag> bags = new Dictionary<string, ClipBag>();
        private static bool built;
        public static int totalClips;

        public static bool Built { get { return built; } }

        /// <summary>Synthesized soft intercom click (not a file in Sounds/). Null if creation failed.</summary>
        public static AudioClip Click;

        private static void BuildClick()
        {
            try
            {
                // 25 ms filtered-noise push-to-talk click, synthesized in code (no file in Sounds/)
                float[] data = ClickSynth.Generate(44100, 12345, 0.025f);
                Click = AudioClip.Create("Lonapse/intercom_click", data.Length, 1, 44100, false);
                Click.SetData(data, 0);
            }
            catch (Exception e)
            {
                Click = null;
                Log.Warn("Could not synthesize the intercom click (" + e.Message + "), chats play without clicks");
            }
        }

        public static void Build()
        {
            // the synthesized click is a runtime asset: recreate it if a scene change unloaded it
            if (Click == null) BuildClick();
            if (built) return;
            built = true;
            bags.Clear();
            totalClips = 0;
            for (int i = 0; i < Folders.Length; i++) bags[Folders[i]] = new ClipBag(Folders[i]);

            GameDatabase db = GameDatabase.Instance;
            if (db == null)
            {
                Log.Warn("GameDatabase.Instance is null, no clips loaded. Chatter stays silent.");
                SealAll();
                return;
            }

            // 1. Primary: scan loaded audio
            HashSet<string> unknown = new HashSet<string>();
            try
            {
                List<AudioClip> all = db.databaseAudio;
                if (all != null)
                {
                    for (int i = 0; i < all.Count; i++)
                    {
                        AudioClip c = all[i];
                        if (c == null) continue;
                        string n = c.name;
                        if (n == null || !n.StartsWith(Prefix, StringComparison.Ordinal)) continue;
                        string rest = n.Substring(Prefix.Length);
                        int slash = rest.LastIndexOf('/');
                        if (slash <= 0) { unknown.Add(rest); continue; }
                        string folder = rest.Substring(0, slash);
                        ClipBag bag;
                        if (!bags.TryGetValue(folder, out bag)) { unknown.Add(folder); continue; }
                        Add(bag, c, n);
                    }
                }
            }
            catch (Exception e) { Log.Warn("Clip scan failed: " + e.Message); }

            foreach (string u in unknown) Log.Info("Ignoring clips in unknown folder: " + u);

            // 2. Fallback: probe by contract file names for folders that ended up empty
            for (int i = 0; i < Folders.Length; i++)
            {
                ClipBag bag = bags[Folders[i]];
                if (bag.Count > 0) continue;
                string baseName = Folders[i].Replace('/', '_');
                int found = 0;
                try
                {
                    for (int k = 0; k < 500; k++)
                    {
                        string url = Prefix + Folders[i] + "/" + baseName + "_" + k.ToString("000");
                        if (!db.ExistsAudioClip(url)) break;
                        AudioClip c = db.GetAudioClip(url);
                        if (c == null) break;
                        if (Add(bag, c, url)) found++;
                    }
                }
                catch (Exception e) { Log.Warn("Clip probe failed for " + Folders[i] + ": " + e.Message); }
                if (found > 0) Log.Info("Found " + found + " clip(s) for " + Folders[i] + " by url probe (clip names differ from urls)");
            }

            SealAll();
            Log.Info("Clip library: " + totalClips + " clip(s) total");
            for (int i = 0; i < Folders.Length; i++)
            {
                int c = bags[Folders[i]].Count;
                if (c == 0) Log.Warn("  " + Folders[i] + ": 0 clips (min " + MinimumOf(i) + "), sounds of this kind are skipped");
                else if (c < MinimumOf(i)) Log.Warn("  " + Folders[i] + ": " + c + " clip(s), below target minimum " + MinimumOf(i));
                else Log.Info("  " + Folders[i] + ": " + c + " clip(s)");
            }
            if (totalClips == 0)
                Log.Warn("No clips found under GameData/" + Prefix + ". The plugin runs but stays silent.");
        }

        private static bool Add(ClipBag bag, AudioClip c, string url)
        {
            float len = 0f;
            try { len = c.length; } catch (Exception) { len = 0f; }
            if (len <= 0.01f)
            {
                Log.Warn("Skipping clip with length " + len + " s: " + url);
                return false;
            }
            if (bag.clips.Contains(c)) return false;
            bag.clips.Add(c);
            totalClips++;
            return true;
        }

        private static void SealAll()
        {
            foreach (KeyValuePair<string, ClipBag> kv in bags) kv.Value.Seal();
        }

        public static int CountOf(string folder)
        {
            ClipBag b;
            return bags.TryGetValue(folder, out b) ? b.Count : 0;
        }

        public static string FallbackOf(string folder)
        {
            switch (folder)
            {
                case "crew/panic": return "crew/tense";
                case "crew/tense": return "crew/calm";
                case "crew/relief": return "crew/calm";
                case "control/tense": return "control/calm";
            }
            // An empty core beep folder falls back to the generic core with the same mood
            const string probe = "beeps/probe_";
            if (folder != null && folder.StartsWith(probe, StringComparison.Ordinal))
            {
                int us = folder.LastIndexOf('_');
                string generic = probe + ProbeCores.Generic + folder.Substring(us);
                if (us > probe.Length && generic != folder) return generic;
            }
            return null;
        }

        /// <summary>Follows the fallback chain. Returns null (and logs once) when the final folder is empty.</summary>
        public static AudioClip Next(string folder, System.Random rng)
        {
            string f = folder;
            int guard = 0;
            while (f != null && guard++ < 5)
            {
                ClipBag b;
                if (bags.TryGetValue(f, out b) && b.Count > 0) return b.Next(rng);
                f = FallbackOf(f);
            }
            Log.WarnOnce("empty:" + folder, "No clip available for " + folder + " (also after fallbacks), item skipped");
            return null;
        }

        /// <summary>
        /// Like Next, but prefers a clip whose name ends in "_f" (female) or "_m" (male), as the Lonapse clips do.
        /// Clips without a gender suffix always match. Falls back to any clip after a few draws.
        /// </summary>
        public static AudioClip NextFor(string folder, bool female, System.Random rng)
        {
            AudioClip first = null;
            string want = female ? "_f" : "_m", other = female ? "_m" : "_f";
            for (int i = 0; i < 8; i++)
            {
                AudioClip c = Next(folder, rng);
                if (c == null) return null;
                if (first == null) first = c;
                string n = c.name ?? "";
                if (n.EndsWith(want, StringComparison.Ordinal) || !n.EndsWith(other, StringComparison.Ordinal)) return c;
            }
            return first;
        }

        public static bool Has(string folder)
        {
            string f = folder;
            int guard = 0;
            while (f != null && guard++ < 5)
            {
                if (CountOf(f) > 0) return true;
                f = FallbackOf(f);
            }
            return false;
        }

        public static string Summary()
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < Folders.Length; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(Folders[i]).Append('=').Append(CountOf(Folders[i]));
            }
            return sb.ToString();
        }
    }
}
