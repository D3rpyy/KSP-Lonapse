// Runtime category dialogue and prepared speech clips. Scene metadata is independent of audio files.
// sceneCount counts a {body} scene once. clipCount counts every clip, body variants included.
// Probe rows are subtitle only. They need no clip, and the director shows their text
// with Subtitle and plays the probe beep itself.
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Lonapse
{
    public static class PhraseClips
    {


        public static PhraseTable table;
        /// <summary>Empty vocabulary for legacy mod subtitle adapters. Shipped dialogue has paired alternatives.</summary>
        public static SlotPools pools = new SlotPools();

        /// <summary>Experimental (runtime voice): extra rows, in the source format of audio/lonapse/phrases. They have
        /// no clips, so they only play when Piper can speak them (runtimeCanSpeak).</summary>
        public const string ProbeRuntimeRel = "GameData/Lonapse/Phrases/Probe";
        public const string ExtraRel = "GameData/Lonapse/Phrases/Extra";
        public static int extraFiles;
        /// <summary>General runtime templates; probe scene tables live in Probe.</summary>
        public const string RuntimeRel = "GameData/Lonapse/Phrases/Crew";
        /// <summary>Lines Lonapse ships for other mods: Compat/&lt;Assembly&gt;/*.tsv, read only when that assembly is
        /// loaded. They count as lines of that mod (the Mods tab, its switch).</summary>
        public const string CompatRel = "GameData/Lonapse/Compat";
        public static List<RuntimeTemplate> runtimeTemplates = new List<RuntimeTemplate>();
        /// <summary>Every parsed runtime template, the muted mods included. runtimeTemplates is this without the
        /// templates of the muted mods (ApplyModMutes).</summary>
        public static List<RuntimeTemplate> allRuntimeTemplates = new List<RuntimeTemplate>();
        /// <summary>The mod folders whose lines the player muted (Settings.mutedMods).</summary>
        public static readonly HashSet<string> mutedMods = new HashSet<string>(StringComparer.Ordinal);
        /// <summary>Set by the director: true when a row without a clip can be spoken at runtime.</summary>
        public static Func<PhraseEntry, bool> runtimeCanSpeak;
        private static readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
        private static bool built;
        public static int clipCount, sceneCount;
        public static int SceneGeneration { get; private set; }
        // Rows without a clip (probe) and playable probe scenes (IsProbeScene)
        public static int subtitleOnlyCount, probeSceneCount;
        public static string status = "not loaded";

        public static bool Loaded { get { return table != null && table.Count > 0; } }

        public static void Build()
        {
            if (built) return;
            built = true;
            LoadSceneTables();
        }

        static void LoadSceneTables()
        {
            DialogueTable.Clear();
            clipCount = 0; sceneCount = 0; subtitleOnlyCount = 0; probeSceneCount = 0;
            List<string> errors = new List<string>();
            try
            {
                var rows = new List<string>();
                string crewDir = Path.Combine(KSPUtil.ApplicationRootPath, RuntimeRel);
                string[] scenePaths = Directory.Exists(crewDir) ? Directory.GetFiles(crewDir, "*.tsv") : new string[0];
                Array.Sort(scenePaths, StringComparer.Ordinal);
                foreach (string scenePath in scenePaths)
                {
                    var sceneRows = File.ReadAllLines(scenePath, System.Text.Encoding.UTF8);
                    if (RuntimeSceneTable.IsSceneFile(sceneRows)) rows.AddRange(RuntimeSceneTable.Compile(sceneRows, scenePath, errors));
                }
                extraFiles = 0;
                string extra = Path.Combine(KSPUtil.ApplicationRootPath, ExtraRel);
                if (Directory.Exists(extra))
                {
                    string[] files = Directory.GetFiles(extra, "*.tsv");
                    Array.Sort(files, StringComparer.Ordinal);
                    foreach (string f in files)
                    {
                        if (Path.GetFileName(f) == "probe_runtime.tsv" && File.Exists(Path.Combine(KSPUtil.ApplicationRootPath, ProbeRuntimeRel + "/personalities.tsv"))) continue;
                        rows.AddRange(File.ReadAllLines(f, System.Text.Encoding.UTF8)); extraFiles++;
                    }
                }
                foreach (var file in ProbeRuntimeFiles())
                {
                    if (RuntimeSceneTable.IsSceneFile(file.Value))
                    { rows.AddRange(RuntimeSceneTable.Compile(file.Value, file.Key, errors)); }
                    ProbeRuntimeTable probe = ProbeRuntimeTable.Parse(file.Value, file.Key, errors);
                    rows.AddRange(probe.SceneRows); extraFiles++;
                }
                table = PhraseTable.Parse(rows, errors);
                SceneGeneration++;
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
            LoadRuntimeTemplates();

            foreach (PhraseEntry e in table.Entries) if (e.SubtitleOnly) subtitleOnlyCount++;
            foreach (PhraseScene sc in table.Scenes)
            {
                sceneCount++;
                if (sc.IsProbe) probeSceneCount++;
            }
            status = "runtime speech, " + sceneCount + " scenes (" + probeSceneCount + " probe), "
                     + subtitleOnlyCount + " subtitle-only rows"
                     + (extraFiles > 0 ? ", " + extraFiles + " extra table(s) for the runtime voice" : "");
            Log.Info("Lonapse: " + status);
        }

        /// <summary>Reads Phrases/Crew, contextual Probe scenes and mod line files again (the Mods tab, after editing a file). A
        /// short result for the overlay, the bad rows go to KSP.log as at the start.</summary>
        public static string ReloadRuntimeTemplates()
        {
            LoadSceneTables();
            int mods = 0;
            foreach (var kv in modTemplateCounts) mods += kv.Value;
            return "Reloaded: " + (allRuntimeTemplates.Count - mods) + " Lonapse and " + mods + " mod templates, "
                   + lastRuntimeErrors + " bad row(s) (see KSP.log)";
        }

        static int lastRuntimeErrors;

        /// <summary>runtimeTemplates from allRuntimeTemplates without the templates of the muted mods.</summary>
        public static void ApplyModMutes()
        {
            var r = new List<RuntimeTemplate>(allRuntimeTemplates.Count);
            foreach (RuntimeTemplate t in allRuntimeTemplates)
            {
                string mod = ModOf(t.Where);
                if (mod == null || !mutedMods.Contains(mod)) r.Add(t);
            }
            runtimeTemplates = r;
        }

        /// <summary>The muted mods from the setting ("A,B"), applied to runtimeTemplates.</summary>
        public static void SetMutedMods(string csv)
        {
            mutedMods.Clear();
            if (!string.IsNullOrEmpty(csv))
                foreach (string m in csv.Split(','))
                    if (m.Trim().Length > 0) mutedMods.Add(m.Trim());
            ApplyModMutes();
        }

        /// <summary>The setting value of the muted mods, sorted.</summary>
        public static string MutedModsValue()
        {
            var l = new List<string>(mutedMods);
            l.Sort(StringComparer.Ordinal);
            return string.Join(",", l.ToArray());
        }

        /// <summary>A mod event that only has lines in muted mods (Trigger then says why it is dropped).</summary>
        public static bool MutedEvent(string id)
        {
            if (mutedMods.Count == 0 || id == null) return false;
            bool muted = false;
            foreach (RuntimeTemplate t in allRuntimeTemplates)
            {
                if (!t.On.Contains(id)) continue;
                string mod = ModOf(t.Where);
                if (mod == null || !mutedMods.Contains(mod)) return false;
                muted = true;
            }
            return muted;
        }

        static void LoadRuntimeTemplates()
        {
            runtimeTemplates = new List<RuntimeTemplate>();
            allRuntimeTemplates = runtimeTemplates;
            lastRuntimeErrors = 0;
            try
            {
                string dir = Path.Combine(KSPUtil.ApplicationRootPath, RuntimeRel);
                string[] paths = Directory.Exists(dir) ? Directory.GetFiles(dir, "*.tsv") : new string[0];
                Array.Sort(paths, StringComparer.Ordinal);
                var files = new List<KeyValuePair<string, IList<string>>>();
                foreach (string f in paths)
                {
                    string name = Path.GetFileName(f);
                    if (name == "probe_context.tsv" && File.Exists(Path.Combine(KSPUtil.ApplicationRootPath, ProbeRuntimeRel + "/reports.tsv"))) continue;
                    if (name == "probe_stress.tsv" && File.Exists(Path.Combine(KSPUtil.ApplicationRootPath, ProbeRuntimeRel + "/stress.tsv"))) continue;
                    var fileRows = File.ReadAllLines(f, System.Text.Encoding.UTF8);
                    files.Add(new KeyValuePair<string, IList<string>>(name, RuntimeSceneTable.TemplateRows(fileRows)));
                }
                int own = files.Count;
                // other mods: GameData/<Mod>/Lonapse/*.tsv, after the Lonapse files (a duplicate id is the mod's row)
                var modFiles = CompatLineFiles();
                modFiles.AddRange(ModLineFiles());   // after the compat lines, so a mod's own row wins a duplicate id
                files.AddRange(modFiles);
                var rerr = new List<string>();
                allRuntimeTemplates = RuntimeLines.ParseAll(files, rerr);
                foreach (var file in ProbeRuntimeFiles())
                    foreach (RuntimeTemplate template in ProbeRuntimeTable.Parse(file.Value, file.Key, rerr).Templates)
                    {
                        RuntimeTemplate old = allRuntimeTemplates.Find(t => RuntimeLines.CanonicalId(t.Id) == RuntimeLines.CanonicalId(template.Id));
                        if (old != null && ModOf(old.Where) != null && ModOf(template.Where) == null) continue;
                        if (old != null) allRuntimeTemplates.Remove(old);
                        allRuntimeTemplates.Add(template);
                    }
                lastRuntimeErrors = rerr.Count;
                ApplyModMutes();
                for (int i = 0; i < rerr.Count && i < 10; i++) Log.Warn("Lonapse runtime: " + rerr[i]);
                if (rerr.Count > 10) Log.Warn("Lonapse runtime: " + (rerr.Count - 10) + " more error(s)");
                // per mod: the templates of its files
                modTemplateCounts.Clear();
                foreach (RuntimeTemplate t in allRuntimeTemplates)
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
                Log.Info("Lonapse: " + (allRuntimeTemplates.Count - modCount) + " runtime voice templates from " + own + " general file(s) and Phrases/Probe");
                if (modFiles.Count > 0)
                    Log.Info("Lonapse: " + modCount + " mod templates from " + modFiles.Count + " file(s): " + string.Join(", ", per.ToArray())
                             + (mutedMods.Count > 0 ? ", muted by the player: " + string.Join(", ", new List<string>(mutedMods).ToArray()) : ""));
            }
            catch (Exception e)
            {
                Log.Warn("Lonapse Phrases/Crew: read failed: " + e.Message);
                runtimeTemplates = new List<RuntimeTemplate>();
                allRuntimeTemplates = runtimeTemplates;
            }
        }

        /// <summary>Templates per mod folder (other mods' Lonapse/*.tsv), for the log and the Mods tab.</summary>
        public static readonly SortedDictionary<string, int> modTemplateCounts = new SortedDictionary<string, int>(StringComparer.Ordinal);

        /// <summary>The mod folder of a template from another mod ("ExampleMod/Lonapse/lines.tsv:3" gives
        /// "ExampleMod"), null for a Lonapse file.</summary>
        public static string ModOf(string where)
        {
            if (string.IsNullOrEmpty(where)) return null;
            int i = where.IndexOf("/Lonapse/", StringComparison.Ordinal);
            return i > 0 ? where.Substring(0, i) : null;
        }

        /// <summary>The mods of the Compat folders that are installed, for the Mods tab.</summary>
        public static readonly HashSet<string> compatMods = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>The .tsv files of Compat/&lt;Assembly&gt;/ whose assembly is loaded, named like the files of that
        /// mod (&lt;Assembly&gt;/Lonapse/file.tsv).</summary>
        static List<KeyValuePair<string, IList<string>>> CompatLineFiles()
        {
            var r = new List<KeyValuePair<string, IList<string>>>();
            compatMods.Clear();
            string dir = Path.Combine(KSPUtil.ApplicationRootPath, CompatRel);
            if (!Directory.Exists(dir)) return r;
            string[] mods = Directory.GetDirectories(dir);
            Array.Sort(mods, StringComparer.Ordinal);
            foreach (string m in mods)
            {
                string mod = Path.GetFileName(m);
                try
                {
                    if (!AssemblyLoaded(mod)) continue;
                    string[] paths = Directory.GetFiles(m, "*.tsv");
                    Array.Sort(paths, StringComparer.Ordinal);
                    foreach (string f in paths)
                        r.Add(new KeyValuePair<string, IList<string>>(mod + "/Lonapse/" + Path.GetFileName(f), File.ReadAllLines(f, System.Text.Encoding.UTF8)));
                    if (paths.Length > 0) { compatMods.Add(mod); Log.Info("Lonapse: " + mod + " is installed, its lines from Lonapse load"); }
                }
                catch (Exception e) { Log.Warn("Lonapse: lines for " + mod + " not read: " + e.Message); }
            }
            return r;
        }

        /// <summary>A loaded plugin with this assembly (file) name. KSP's own name for it is the KSPAssembly title.</summary>
        public static bool AssemblyLoaded(string name)
        {
            foreach (AssemblyLoader.LoadedAssembly la in AssemblyLoader.loadedAssemblies)
                if (la != null && la.assembly != null && la.assembly.GetName().Name == name) return true;
            return false;
        }

        /// <summary>The .tsv files in GameData/&lt;Mod&gt;/Lonapse/ of every other mod, sorted by mod and name. A mod
        /// folder that cannot be read is skipped.</summary>
        /// <summary>The probe line files of other mods: GameData/&lt;Mod&gt;/Lonapse/Probe/*.tsv, same format as
        /// Phrases/Extra (a core column that names a lineset from LONAPSE_PROBE_LINES). Added after the Lonapse rows.
        /// Returns the number of files read.</summary>
        static List<KeyValuePair<string, IList<string>>> ProbeRuntimeFiles()
        {
            var files = new List<KeyValuePair<string, IList<string>>>();
            string gd = Path.Combine(KSPUtil.ApplicationRootPath, "GameData");
            if (!Directory.Exists(gd)) return files;
            var roots = new List<string> { Path.Combine(gd, "Lonapse") };
            string[] mods = Directory.GetDirectories(gd); Array.Sort(mods, StringComparer.Ordinal);
            foreach (string mod in mods) if (Path.GetFileName(mod) != "Lonapse") roots.Add(mod);
            foreach (string root in roots)
            {
                bool own = Path.GetFileName(root) == "Lonapse";
                string dir = own ? Path.Combine(root, "Phrases/Probe") : Path.Combine(root, "Lonapse/Probe");
                if (!Directory.Exists(dir)) continue;
                string[] paths = Directory.GetFiles(dir, "*.tsv"); Array.Sort(paths, StringComparer.Ordinal);
                foreach (string path in paths)
                {
                    string where = own ? "Probe/" + Path.GetFileName(path)
                        : Path.GetFileName(root) + "/Lonapse/Probe/" + Path.GetFileName(path);
                    files.Add(new KeyValuePair<string, IList<string>>(where, File.ReadAllLines(path, System.Text.Encoding.UTF8)));
                }
            }
            return files;
        }

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

        private static readonly Queue<KeyValuePair<string, AudioClip>> runtimeUrls = new Queue<KeyValuePair<string, AudioClip>>();
        private const int RuntimeKeep = 64;

        /// <summary>Experimental (runtime voice): a clip made at runtime for a row that has no file, so Clip(e) finds
        /// it like any other. Only the last RuntimeKeep stay, older ones are destroyed.</summary>
        public static void SetRuntimeClip(PhraseEntry e, AudioClip clip)
        {
            if (e == null || clip == null) return;
            string url = e.ClipUrl(PhraseTable.FixedGender(e.Role) ?? "m");
            e.PreparedSpeech = true;
            clips[url] = clip;
            runtimeUrls.Enqueue(new KeyValuePair<string, AudioClip>(url, clip));
            while (runtimeUrls.Count > RuntimeKeep)
            {
                var old = runtimeUrls.Dequeue();
                AudioClip current;
                if (clips.TryGetValue(old.Key, out current) && current == old.Value) clips.Remove(old.Key);
                if (old.Value != null) UnityEngine.Object.Destroy(old.Value);
            }
        }

        /// <summary>Prepare direct speech paths (countdown, callouts and the final MC call).</summary>
        public static void Render(PhraseEntry e, bool female, Action<AudioClip> ready)
        {
            if (e == null || e.SubtitleOnly) { ready(null); return; }
            string kind = RuntimeDelivery.KindFor(e.Role == "mc" ? "mc" : female ? "f" : "m", e.Band, e.Severity, e.Event);
            if (kind == null || RuntimeVoice.Missing(kind) != null) { ready(null); return; }
            string spanish = e.RuntimeSpanish ?? e.SpanishRtVersion(0) ?? e.Spanish;
            RuntimeVoice.Shared.Request(kind, spanish, result => ready(result.clip));
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

        /// <summary>True for a probe row. It has no clip, only its subtitle.</summary>
        public static bool SubtitleOnly(PhraseEntry e)
        {
            return e != null && e.SubtitleOnly;
        }

        /// <summary>The speaker role of a row (crew mc probe), null for null.</summary>
        public static string SpeakerRole(PhraseEntry e)
        {
            return e == null ? null : e.Role;
        }

        /// <summary>Beep mood of a probe row (happy offended ...), empty for other rows.</summary>
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
                if (e.HasBody && !e.SubtitleOnly && Clip(e, false) == null && (runtimeCanSpeak == null || !runtimeCanSpeak(e)))
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
