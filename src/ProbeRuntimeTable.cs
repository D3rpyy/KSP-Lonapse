using System;
using System.Collections.Generic;

namespace Lonapse
{
    public sealed class ProbeRuntimeLine
    {
        public string Role, Mood, Spanish, English;
        public RuntimeTemplate Speech;
    }

    /// <summary>One authoring format for fixed and contextual probe scenes. Fixed rows feed PhraseTable;
    /// contextual scenes compile to existing runtime predicates while retaining their authored speaker order.</summary>
    public sealed class ProbeRuntimeTable
    {
        public readonly List<string> SceneRows = new List<string>();
        public readonly List<RuntimeTemplate> Templates = new List<RuntimeTemplate>();
        public static bool CanFill(RuntimeTemplate template, Dictionary<string, double> nums, Dictionary<string, string[]> names)
        {
            if (template.ProbeLines == null) return true;
            foreach (var line in template.ProbeLines)
                if (line.Speech != null && line.Speech.Dialogue != null ? RuntimeLines.FillableVersions(line.Speech, nums, names).Count == 0 : line.Role != "probe" && (line.Speech == null || RuntimeLines.FillableVersions(line.Speech, nums, names).Count == 0)) return false;
            return true;
        }
        public static List<ProbeRuntimeLine> Fill(RuntimeTemplate template, Dictionary<string, double> nums,
            Dictionary<string, string[]> names, SceneMemory memory, Random rng)
        {
            if (template.ProbeLines == null || !CanFill(template, nums, names)) return null;
            var result = new List<ProbeRuntimeLine>();
            for (int i = 0; i < template.ProbeLines.Count; i++)
            {
                var line = template.ProbeLines[i];
                string es = "", en = line.English;
                if (line.Role != "probe" || line.Speech != null && line.Speech.Dialogue != null)
                {
                    var options = new List<string>();
                    foreach (int version in RuntimeLines.FillableVersions(line.Speech, nums, names)) options.Add(version.ToString());
                    int selected = int.Parse(memory.TakeValue("probe:" + template.Id + ":" + i, options, rng));
                    if (line.Role == "probe")
                    {
                        var alternative = line.Speech.Dialogue.Alternatives[selected];
                        if (!RuntimeLines.FillText(alternative.Spanish, alternative.English, nums, names, out es, out en)) return null;
                    }
                    else if (!RuntimeLines.Fill(line.Speech, selected, nums, names, out es, out en)) return null;
                }
                result.Add(new ProbeRuntimeLine { Role = line.Role, Mood = line.Mood, Spanish = es, English = en });
            }
            return result;
        }

        public static ProbeRuntimeTable Parse(IList<string> rows, string where, List<string> errors)
        {
            var result = new ProbeRuntimeTable();
            if (errors == null) errors = new List<string>();
            if (rows == null || rows.Count == 0) return result;
            if (DialogueTable.IsUnified(rows)) return DialogueTable.Parse(rows, where, errors).ProbeScenes();
            if (RuntimeSceneTable.IsSceneFile(rows))
            {
                rows = RuntimeSceneTable.Expand(rows);
                var headerColumns = new Dictionary<string, int>();
                string[] nativeHeader = rows[0].Split('\t');
                for (int i = 0; i < nativeHeader.Length; i++) headerColumns[nativeHeader[i]] = i;
                var legacy = new List<string> { "clip_key\tevent\tseverity\tband\trole\tscene\tseq\tgender\tphase\tcause\ttrait\tpersona\ttopic\tcore\tbeep\tspanish\tenglish\truntime_topic\twhen" };
                for (int i = 1; i < rows.Count; i++)
                {
                    if (string.IsNullOrEmpty(rows[i]) || rows[i].StartsWith("#")) continue;
                    string[] f = rows[i].Split('\t');
                    if (f.Length != nativeHeader.Length) { errors.Add(where + ": wrong column count"); continue; }
                    Func<string, string> get = key => headerColumns.ContainsKey(key) ? f[headerColumns[key]] : "";
                    if (get("kind") != "probe") continue;
                    var topics = new List<string>(); var predicates = new List<string>();
                    foreach (string token in get("when").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                        if (token.StartsWith("on=")) topics.Add(token.Substring(3)); else predicates.Add(token);
                    var values = new List<string>();
                    foreach (string key in legacy[0].Split('\t')) values.Add(key == "clip_key" ? get("id") : key == "runtime_topic" ? string.Join(" ", topics.ToArray()) : key == "when" ? string.Join(" ", predicates.ToArray()) : get(key));
                    legacy.Add(string.Join("\t", values.ToArray()));
                }
                if (legacy.Count == 1) return result;
                rows = legacy;
            }
            string[] header = rows[0].Split('\t');
            var columns = new Dictionary<string, int>();
            for (int i = 0; i < header.Length; i++) columns[header[i]] = i;
            foreach (string key in new[] { "clip_key", "scene", "seq", "event", "role", "spanish", "english" })
                if (!columns.ContainsKey(key)) { errors.Add(where + ": missing " + key); return result; }
            var groups = new Dictionary<string, List<string[]>>();
            for (int n = 1; n < rows.Count; n++)
            {
                if (string.IsNullOrEmpty(rows[n]) || rows[n].StartsWith("#")) continue;
                string[] fields = rows[n].Split('\t');
                if (fields.Length != header.Length) { errors.Add(where + ":" + (n + 1) + ": wrong column count"); return result; }
                string scene = fields[columns["scene"]];
                List<string[]> lines;
                if (!groups.TryGetValue(scene, out lines)) groups[scene] = lines = new List<string[]>();
                lines.Add(fields);
            }
            foreach (var group in groups)
            {
                Func<string[], string, string> get = (f, key) => columns.ContainsKey(key) ? f[columns[key]] : "";
                var lines = group.Value;
                bool good = true;
                foreach (var line in lines) { int seq; if (!int.TryParse(get(line, "seq"), out seq) || seq < 1) good = false; }
                if (!good) { errors.Add(where + ": " + group.Key + ": invalid sequence"); continue; }
                lines.Sort((a,b) => int.Parse(get(a,"seq")).CompareTo(int.Parse(get(b,"seq"))));
                string topics = get(lines[0], "runtime_topic");
                if (string.IsNullOrEmpty(topics))
                {
                    result.SceneRows.Add(rows[0]);
                    foreach (var line in lines) result.SceneRows.Add(string.Join("\t", line));
                    continue;
                }
                var script = new List<ProbeRuntimeLine>();
                string conditions = get(lines[0], "when");
                string band = get(lines[0], "band");
                string phase = FlightPhase.Normalize(get(lines[0], "phase"));
                if (string.IsNullOrEmpty(band)) band = "calm";
                if (Array.IndexOf(PhraseTable.Bands, band) < 0) good = false;
                if (!string.IsNullOrEmpty(phase) && phase != FlightPhase.Any && phase != FlightPhase.Serious && phase != FlightPhase.Idle && Array.IndexOf(FlightPhase.All, phase) < 0) good = false;
                if (topics.StartsWith("probe.stress_", StringComparison.Ordinal)) conditions += " x_panic=" + (band == "panic" ? "1" : "0");
                string core = get(lines[0], "core");
                if (!string.IsNullOrEmpty(core) && core != "any") conditions += " x_core_" + core + "=1";
                foreach (string topic in topics.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)) conditions += " on=" + topic;
                string spanish = null, english = null;
                for (int n = 0; n < lines.Count; n++)
                {
                    var line = lines[n]; string role = get(line, "role");
                    if (get(line,"seq") != (n+1).ToString() || (role != "mc" && role != "crew" && role != "probe")) good = false;
                    if (get(line,"core") != core || get(line,"band") != get(lines[0],"band") || FlightPhase.Normalize(get(line,"phase")) != phase) good = false;
                    string mood = get(line,"beep");
                    if (role == "probe" && Array.IndexOf(ProbeCores.Moods, mood) < 0) good = false;
                    if (n > 0 && (get(line,"runtime_topic").Length > 0 || get(line,"when").Length > 0)) good = false;
                    RuntimeTemplate speech = null;
                    if (role != "probe")
                    {
                        var validated = RuntimeLines.Parse(new List<string> { "id\tkind\twhen\tspanish\tenglish",
                            get(line,"clip_key") + "\tevent\t" + conditions.Trim() + "\t" + get(line,"spanish") + "\t" + get(line,"english") }, errors);
                        if (validated.Count != 1) good = false; else speech = validated[0];
                    }
                    script.Add(new ProbeRuntimeLine { Role = role, Mood = mood, Spanish = get(line,"spanish"), English = get(line,"english"), Speech = speech });
                    if (role != "probe" && spanish == null) { spanish = get(line,"spanish"); english = get(line,"english"); }
                }
                if (!good || spanish == null) { errors.Add(where + ": " + group.Key + ": invalid contextual scene"); continue; }
                var parsed = RuntimeLines.Parse(new List<string> { "id\tkind\twhen\tspanish\tenglish",
                    group.Key + "\tevent\t" + conditions.Trim() + "\t" + spanish + "\t" + english }, errors);
                if (parsed.Count != 1) continue;
                parsed[0].Where = where + ":" + group.Key;
                parsed[0].ProbeLines = script;
                parsed[0].ProbeBand = band; parsed[0].ProbePhase = phase;
                result.Templates.Add(parsed[0]);
            }
            return result;
        }
    }
}
