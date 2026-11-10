using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Lonapse
{
    /// <summary>Category runtime tables can also contain explicitly ordered scenes. The director's scene
    /// selectors retain their event, personality and lineset semantics; recordings are not part of this format.</summary>
    public static class RuntimeSceneTable
    {
        public static bool IsSceneFile(IList<string> rows)
        {
            if (rows == null) return false;
            if (DialogueTable.IsUnified(rows)) return true;
            int kind = -1;
            foreach (string row in rows)
            {
                if (string.IsNullOrEmpty(row) || row.StartsWith("#")) continue;
                string[] f = row.Split('\t');
                if (kind < 0) { kind = Array.IndexOf(f, "kind"); if (kind < 0) return false; continue; }
                if (kind < f.Length && (f[kind] == "scene" || f[kind] == "probe")) return true;
            }
            return false;
        }

        public static List<string> TemplateRows(IList<string> rows)
        {
            if (DialogueTable.IsUnified(rows)) return new List<string>(rows);
            var result = new List<string>();
            int kind = -1;
            foreach (string row in rows)
            {
                if (string.IsNullOrEmpty(row) || row.StartsWith("#")) continue;
                string[] f = row.Split('\t');
                if (kind < 0) { kind = Array.IndexOf(f, "kind"); result.Add(row); continue; }
                if (kind >= f.Length || (f[kind] != "scene" && f[kind] != "probe")) result.Add(row);
            }
            return result;
        }

        /// <summary>Resolve optional scene columns and legacy inline selectors. Shared scene metadata is
        /// inherited from the first row of each scene/body; speaker, order, beep and conditions are per line.</summary>
        public static IList<string> Expand(IList<string> rows)
        {
            string[] keys = { "id", "kind", "when", "spanish", "english", "scene", "seq", "event", "role", "severity", "band", "gender", "phase", "cause", "trait", "persona", "topic", "core", "beep", "body", "conditions" };
            string[] shared = { "event", "severity", "band", "phase", "cause", "trait", "persona", "topic", "core" };
            var expanded = new List<string> { string.Join("\t", keys) };
            var heads = new Dictionary<string, Dictionary<string, string>>();
            Dictionary<string, int> columns = null;
            int width = 0;
            foreach (string row in rows)
            {
                if (string.IsNullOrWhiteSpace(row) || row.TrimStart().StartsWith("#")) continue;
                string[] f = row.Split('\t');
                if (columns == null)
                {
                    columns = new Dictionary<string, int>(); width = f.Length;
                    for (int i = 0; i < f.Length; i++) columns[f[i].Trim()] = i;
                    continue;
                }
                if (f.Length != width) { expanded.Add(row); continue; }
                Func<string, string> get = key => columns.ContainsKey(key) ? f[columns[key]].Trim() : "";
                if (get("kind") != "scene" && get("kind") != "probe") continue;
                var explicitValues = new Dictionary<string, string>();
                var predicates = new List<string>();
                string[] conditions = new string[get("english").Split('|').Length];
                foreach (string token in get("when").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    int at = token.IndexOf('='); string key = at > 0 ? token.Substring(0, at) : "";
                    int variant;
                    if (key.StartsWith("v") && int.TryParse(key.Substring(1), out variant) && variant > 0 && variant <= conditions.Length)
                        conditions[variant-1] = token.Substring(at+1).Replace(',', ' ');
                    else if (Array.IndexOf(keys, key) >= 5) explicitValues[key] = token.Substring(at+1);
                    else predicates.Add(token);
                }
                // Named columns take precedence over the legacy inline form, and blank cells use defaults/inheritance.
                foreach (string key in keys) if (Array.IndexOf(keys, key) >= 5 && get(key).Length > 0) explicitValues[key] = get(key);
                var values = new Dictionary<string, string>();
                foreach (string key in keys) values[key] = get(key);
                values["scene"] = explicitValues.ContainsKey("scene") ? explicitValues["scene"] : get("id");
                values["seq"] = "1"; values["event"] = "chatter"; values["role"] = "crew";
                values["severity"] = "chatter"; values["band"] = "calm"; values["gender"] = "both";
                values["beep"] = ""; values["body"] = explicitValues.ContainsKey("body") ? explicitValues["body"] : "";
                foreach (string key in new[] { "phase", "cause", "trait", "persona", "topic", "core" }) values[key] = "any";
                string group = values["kind"] + ":" + values["scene"] + ":" + values["body"];
                Dictionary<string, string> head;
                if (heads.TryGetValue(group, out head)) foreach (string key in shared) values[key] = head[key];
                foreach (var pair in explicitValues) values[pair.Key] = pair.Value;
                if (!heads.ContainsKey(group)) heads[group] = new Dictionary<string, string>(values);
                values["when"] = string.Join(" ", RuntimeLines.ConditionTokens(string.Join(" ", predicates.ToArray()), get).ToArray());
                for (int n = 0; n < conditions.Length; n++) if (conditions[n] == null) conditions[n] = "";
                values["conditions"] = explicitValues.ContainsKey("conditions") ? explicitValues["conditions"] : string.Join(" | ", conditions);
                var result = new List<string>(); foreach (string key in keys) result.Add(values[key]);
                expanded.Add(string.Join("\t", result.ToArray()));
            }
            return expanded;
        }

        public static List<string> Compile(IList<string> rows, string where, List<string> errors)
        {
            if (DialogueTable.IsUnified(rows)) return DialogueTable.Parse(rows, where, errors).SceneRows();
            rows = Expand(rows);
            var result = new List<string>();
            string[] keys = { "clip_key", "event", "severity", "band", "role", "scene", "seq", "gender", "phase", "cause", "trait", "persona", "topic", "core", "beep", "spanish", "english", "spanish_rt", "body", "runtime", "when" };
            result.Add(string.Join("\t", keys));
            Dictionary<string, int> columns = null;
            for (int i = 0; i < rows.Count; i++)
            {
                if (string.IsNullOrEmpty(rows[i]) || rows[i].StartsWith("#")) continue;
                string[] f = rows[i].Split('\t');
                if (columns == null)
                {
                    columns = new Dictionary<string, int>();
                    for (int n = 0; n < f.Length; n++) columns[f[n]] = n;
                    foreach (string key in new[] { "id", "kind", "when", "spanish", "english", "scene", "seq", "event", "role" })
                        if (!columns.ContainsKey(key)) { errors.Add(where + ": missing " + key); return result; }
                    continue;
                }
                if (f.Length != columns.Count) { errors.Add(where + ":" + (i+1) + ": wrong column count"); continue; }
                Func<string, string> get = key => columns.ContainsKey(key) ? f[columns[key]].Trim() : "";
                if (get("kind") == "probe") continue;
                if (get("kind") != "scene") { errors.Add(where + ":" + (i+1) + ": expected kind=scene or probe"); continue; }
                // The predicate is checked by the director against current runtime values.
                var predicateErrors = new List<string>();
                RuntimeLines.Parse(new List<string> { "id\tkind\twhen\tspanish\tenglish", "condition_check\treport\t" + get("when") + "\tHola.\tHello." }, predicateErrors);
                if (predicateErrors.Count > 0) { errors.Add(where + ":" + (i+1) + ": " + string.Join("; ", predicateErrors.ToArray())); continue; }
                string[] es = get("spanish").Split('|'), en = get("english").Split('|'), conditions = get("conditions").Split('|');
                bool beep = get("role") == "probe";
                if ((!beep && es.Length != en.Length) || (get("conditions").Length > 0 && conditions.Length != en.Length))
                { errors.Add(where + ":" + (i+1) + ": Spanish, English and conditions must have paired alternatives"); continue; }
                for (int n = 0; n < en.Length; n++)
                    if (n < conditions.Length && conditions[n].Trim().Length > 0) en[n] = "[" + conditions[n].Trim() + "] " + en[n].Trim();
                var line = new string[keys.Length];
                for (int n = 0; n < keys.Length; n++) line[n] = get(keys[n]);
                line[0] = get("id");
                line[19] = "1";
                // Internal compatibility with the existing scene model. Paired speech is resolved before queueing.
                line[15] = beep ? "" : Regex.Replace(es[0].Trim(), @"\{(?!body\})[^}]+\}", "valor");
                line[16] = string.Join(" | ", en);
                line[17] = beep ? "" : get("spanish");
                result.Add(string.Join("\t", line));
            }
            return result;
        }
    }
}
