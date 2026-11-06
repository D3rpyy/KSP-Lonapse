// Lonapse phrase clips: loads GameData/Lonapse/Phrases/phrases.tsv and resolves each row to its AudioClips
// (plan v2 4.7, plan v3). Scenes without a playable clip are dropped, so the director only sees what can be heard.
// sceneCount counts a {body} scene once. clipCount counts every clip, body variants included.
// Plan v5: probe rows are subtitle only. They need no clip, and the director shows their text
// with Subtitle and plays the probe beep itself.
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Lonapse
{
    public static class PhraseClips
    {
        public const string TableRel = "GameData/Lonapse/Phrases/phrases.tsv";

        public static PhraseTable table;
        /// <summary>Experimental (procedural talk): subtitle slot values from pools.tsv next to phrases.tsv. Empty
        /// when the file is missing, then a slot shows "...".</summary>
        public static SlotPools pools = new SlotPools();
        public const string PoolsRel = "GameData/Lonapse/Phrases/pools.tsv";
        /// <summary>Experimental (runtime voice): extra rows, in the source format of audio/lonapse/phrases. They have
        /// no clips, so they only play when Piper can speak them (runtimeCanSpeak).</summary>
        public const string ExtraRel = "GameData/Lonapse/Phrases/Extra";
        public static int extraFiles;
        /// <summary>Runtime voice: the runtime-only templates from every Phrases/Runtime/*.tsv, empty without the folder.</summary>
        public const string RuntimeRel = "GameData/Lonapse/Phrases/Runtime";
        public static List<RuntimeTemplate> runtimeTemplates = new List<RuntimeTemplate>();
        /// <summary>Set by the director: true when a row without a clip can be spoken at runtime.</summary>
        public static Func<PhraseEntry, bool> runtimeCanSpeak;
        private static readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
        private static bool built;
        public static int clipCount, sceneCount;
        // Plan v5: rows without a clip (probe) and playable probe scenes (IsProbeScene)
        public static int subtitleOnlyCount, probeSceneCount;
        public static string status = "not loaded";

        public static bool Loaded { get { return table != null && clipCount > 0; } }

        public static void Build()
        {
            if (built) return;
            built = true;
            clips.Clear();
            clipCount = 0;
            sceneCount = 0;
            subtitleOnlyCount = 0;
            probeSceneCount = 0;
            table = null;
            string path;
            try { path = Path.Combine(KSPUtil.ApplicationRootPath, TableRel); }
            catch (Exception e) { status = "no root path: " + e.Message; Log.Warn("Lonapse: " + status); return; }
            if (!File.Exists(path))
            {
                status = "missing " + TableRel;
                Log.Warn("Lonapse: " + status + ". Chatter falls back to random clips per folder.");
                return;
            }
            List<string> errors = new List<string>();
            try
            {
                var rows = new List<string>(File.ReadAllLines(path, System.Text.Encoding.UTF8));
                extraFiles = 0;
                string extra = Path.Combine(KSPUtil.ApplicationRootPath, ExtraRel);
                if (Directory.Exists(extra))
                {
                    string[] files = Directory.GetFiles(extra, "*.tsv");
                    Array.Sort(files, StringComparer.Ordinal);
                    foreach (string f in files) { rows.AddRange(File.ReadAllLines(f, System.Text.Encoding.UTF8)); extraFiles++; }
                }
                table = PhraseTable.Parse(rows, errors);
            }
            catch (Exception e)
            {
                status = "read failed: " + e.Message;
                Log.Warn("Lonapse: " + status);
                table = null;
                return;
            }
            for (int i = 0; i < errors.Count && i < 10; i++) Log.Warn("Lonapse table: " + errors[i]);
            if (errors.Count > 10) Log.Warn("Lonapse table: " + (errors.Count - 10) + " more error(s)");
            LoadPools();
            LoadRuntimeTemplates();

            GameDatabase db = GameDatabase.Instance;
            int missing = 0;
            foreach (PhraseEntry e in table.Entries)
            {
                if (e.SubtitleOnly) { subtitleOnlyCount++; continue; }
                string fixedG = PhraseTable.FixedGender(e.Role);
                for (int g = 0; g < 2; g++)
                {
                    string gs = g == 0 ? "m" : "f";
                    if (!PhraseTable.HasGender(e, gs) || (fixedG != null && fixedG != gs)) continue;
                    string url = e.ClipUrl(gs);
                    AudioClip c = null;
                    try { if (db != null && db.ExistsAudioClip(url)) c = db.GetAudioClip(url); }
                    catch (Exception) { c = null; }
                    if (c == null) { missing++; if (missing <= 5) Log.Warn("Lonapse clip missing: " + url); continue; }
                    clips[url] = c;
                    clipCount++;
                }
            }
            foreach (PhraseScene sc in table.Scenes)
            {
                if (!Playable(sc)) continue;
                sceneCount++;
                if (sc.IsProbe) probeSceneCount++;
            }
            status = clipCount + " clips, " + sceneCount + " scenes (" + probeSceneCount + " probe), "
                     + subtitleOnlyCount + " subtitle-only rows" + (missing > 0 ? ", " + missing + " missing" : "")
                     + (extraFiles > 0 ? ", " + extraFiles + " extra table(s) for the runtime voice" : "");
            Log.Info("Lonapse: " + status);
        }

        static void LoadRuntimeTemplates()
        {
            runtimeTemplates = new List<RuntimeTemplate>();
            try
            {
                string dir = Path.Combine(KSPUtil.ApplicationRootPath, RuntimeRel);
                if (!Directory.Exists(dir)) return;
                string[] paths = Directory.GetFiles(dir, "*.tsv");
                Array.Sort(paths, StringComparer.Ordinal);
                var files = new List<KeyValuePair<string, IList<string>>>();
                foreach (string f in paths)
                    files.Add(new KeyValuePair<string, IList<string>>(Path.GetFileName(f), File.ReadAllLines(f, System.Text.Encoding.UTF8)));
                int own = files.Count;
                // other mods: GameData/<Mod>/Lonapse/*.tsv, after the Lonapse files (a duplicate id is the mod's row)
                var modFiles = ModLineFiles();
                files.AddRange(modFiles);
                var rerr = new List<string>();
                runtimeTemplates = RuntimeLines.ParseAll(files, rerr);
                for (int i = 0; i < rerr.Count && i < 10; i++) Log.Warn("Lonapse runtime: " + rerr[i]);
                if (rerr.Count > 10) Log.Warn("Lonapse runtime: " + (rerr.Count - 10) + " more error(s)");
                // per mod: the templates of its files
                modTemplateCounts.Clear();
                foreach (RuntimeTemplate t in runtimeTemplates)
                {
                    string mod = ModOf(t.Where);
                    if (mod == null) continue;
                    int n;
                    modTemplateCounts.TryGetValue(mod, out n);
                    modTemplateCounts[mod] = n + 1;
                }
                int modCount = 0;
                var per = new List<string>();
                foreach (var kv in modTemplateCounts) { modCount += kv.Value; per.Add(kv.Key + " (" + kv.Value + ")"); }
                Log.Info("Lonapse: " + (runtimeTemplates.Count - modCount) + " runtime voice templates from " + own + " file(s) in Phrases/Runtime");
                if (modFiles.Count > 0)
                    Log.Info("Lonapse: " + modCount + " mod templates from " + modFiles.Count + " file(s): " + string.Join(", ", per.ToArray()));
            }
            catch (Exception e) { Log.Warn("Lonapse Phrases/Runtime: read failed: " + e.Message); runtimeTemplates = new List<RuntimeTemplate>(); }
        }

        /// <summary>Templates per mod folder (other mods' Lonapse/*.tsv), for the log and the Debug tab.</summary>
        public static readonly SortedDictionary<string, int> modTemplateCounts = new SortedDictionary<string, int>(StringComparer.Ordinal);

        /// <summary>The mod folder of a template from another mod ("ExampleMod/Lonapse/lines.tsv:3" gives
        /// "ExampleMod"), null for a Lonapse file.</summary>
        public static string ModOf(string where)
        {
            if (string.IsNullOrEmpty(where)) return null;
            int i = where.IndexOf("/Lonapse/", StringComparison.Ordinal);
            return i > 0 ? where.Substring(0, i) : null;
        }

        /// <summary>The .tsv files in GameData/&lt;Mod&gt;/Lonapse/ of every other mod, sorted by mod and name. A mod
        /// folder that cannot be read is skipped.</summary>
        static List<KeyValuePair<string, IList<string>>> ModLineFiles()
        {
            var r = new List<KeyValuePair<string, IList<string>>>();
            string gd = Path.Combine(KSPUtil.ApplicationRootPath, "GameData");
            if (!Directory.Exists(gd)) return r;
            string[] mods = Directory.GetDirectories(gd);
            Array.Sort(mods, StringComparer.Ordinal);
            foreach (string m in mods)
            {
                string mod = Path.GetFileName(m);
                if (mod == "Lonapse") continue;
                try
                {
                    string dir = Path.Combine(m, "Lonapse");
                    if (!Directory.Exists(dir)) continue;
                    string[] paths = Directory.GetFiles(dir, "*.tsv");
                    Array.Sort(paths, StringComparer.Ordinal);
                    foreach (string f in paths)
                        r.Add(new KeyValuePair<string, IList<string>>(mod + "/Lonapse/" + Path.GetFileName(f), File.ReadAllLines(f, System.Text.Encoding.UTF8)));
                }
                catch (Exception e) { Log.Warn("Lonapse: lines of " + mod + " not read: " + e.Message); }
            }
            return r;
        }

        static void LoadPools()
        {
            pools = new SlotPools();
            try
            {
                string pp = Path.Combine(KSPUtil.ApplicationRootPath, PoolsRel);
                if (!File.Exists(pp)) return;
                var perr = new List<string>();
                pools = SlotPools.Parse(new List<string>(File.ReadAllLines(pp, System.Text.Encoding.UTF8)), perr);
                for (int i = 0; i < perr.Count && i < 10; i++) Log.Warn("Lonapse pools: " + perr[i]);
                Log.Info("Lonapse: " + pools.Count + " subtitle slot pools, " + pools.SpanishCount + " values with Spanish");
            }
            catch (Exception e) { Log.Warn("Lonapse pools: read failed: " + e.Message); pools = new SlotPools(); }
        }

        private static readonly Queue<string> runtimeUrls = new Queue<string>();
        private const int RuntimeKeep = 16;

        /// <summary>Experimental (runtime voice): a clip made at runtime for a row that has no file, so Clip(e) finds
        /// it like any other. Only the last RuntimeKeep stay, older ones are destroyed.</summary>
        public static void SetRuntimeClip(PhraseEntry e, AudioClip clip)
        {
            if (e == null || clip == null) return;
            string url = e.ClipUrl(PhraseTable.FixedGender(e.Role) ?? "m");
            clips[url] = clip;
            runtimeUrls.Enqueue(url);
            while (runtimeUrls.Count > RuntimeKeep)
            {
                string old = runtimeUrls.Dequeue();
                AudioClip c;
                if (clips.TryGetValue(old, out c)) { clips.Remove(old); if (c != null) UnityEngine.Object.Destroy(c); }
            }
        }

        /// <summary>Clip for a row. A crew row falls back to the other gender, so a line is never lost.
        /// mc uses the male clip. A subtitle-only row gives null.</summary>
        public static AudioClip Clip(PhraseEntry e, bool female)
        {
            if (e == null || e.SubtitleOnly) return null;
            AudioClip c;
            string first = PhraseTable.FixedGender(e.Role) ?? (female ? "f" : "m");
            if (clips.TryGetValue(e.ClipUrl(first), out c) && c != null) return c;
            string other = first == "m" ? "f" : "m";
            if (clips.TryGetValue(e.ClipUrl(other), out c) && c != null) return c;
            return null;
        }

        /// <summary>Every voiced line of the scene has at least one clip. Subtitle-only lines (probe)
        /// need none. For a {body} scene the generic lines count (LinesFor falls back to them per line when a body
        /// variant has no clip).</summary>
        public static bool Playable(PhraseScene sc)
        {
            if (sc == null || sc.Lines.Count == 0) return false;
            for (int i = 0; i < sc.Lines.Count; i++)
                if (!sc.Lines[i].SubtitleOnly && Clip(sc.Lines[i], false) == null
                    && (runtimeCanSpeak == null || !runtimeCanSpeak(sc.Lines[i]))) return false;
            return true;
        }

        /// <summary>Plan v5: true for a probe row. It has no clip, only its subtitle.</summary>
        public static bool SubtitleOnly(PhraseEntry e)
        {
            return e != null && e.SubtitleOnly;
        }

        /// <summary>Plan v5: the speaker role of a row (crew mc probe), null for null.</summary>
        public static string SpeakerRole(PhraseEntry e)
        {
            return e == null ? null : e.Role;
        }

        /// <summary>Plan v5: beep mood of a probe row (happy offended ...), empty for other rows.</summary>
        public static string BeepMood(PhraseEntry e)
        {
            return e == null ? "" : e.Beep;
        }

        /// <summary>The lines of a scene for one body (FlightSample.body: lowercase bodyName or "generic"),
        /// with a clip for every line: a body variant without a clip is replaced by the generic line of the same
        /// seq. Use this instead of sc.Lines when playing a scene. A new list, safe to keep.</summary>
        public static List<PhraseEntry> LinesFor(PhraseScene sc, string body)
        {
            List<PhraseEntry> r = new List<PhraseEntry>();
            if (sc == null) return r;
            List<PhraseEntry> want = sc.LinesFor(body);
            for (int i = 0; i < want.Count; i++)
            {
                PhraseEntry e = want[i];
                if (e.HasBody && !e.SubtitleOnly && Clip(e, false) == null)
                {
                    for (int k = 0; k < sc.Lines.Count; k++)
                        if (sc.Lines[k].Seq == e.Seq) { e = sc.Lines[k]; break; }
                }
                r.Add(e);
            }
            return r;
        }

        public static string Subtitle(PhraseEntry e)
        {
            return e == null ? null : e.SubtitleText;
        }
    }
}
