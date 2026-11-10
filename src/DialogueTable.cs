using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Lonapse
{
    public sealed class DialogueAlternative
    {
        public string Id, Spanish, English, When;
        public int Prefer;
    }

    /// <summary>One line in an ordered exchange; all authored alternatives use the same condition engine.</summary>
    public sealed class DialogueLine
    {
        public readonly Dictionary<string, string> Fields = new Dictionary<string, string>();
        public readonly List<DialogueAlternative> Alternatives = new List<DialogueAlternative>();
        public readonly HashSet<string> Common = new HashSet<string>();
        public string Id { get { return Get("line"); } }
        public string Get(string key) { string value; return Fields.TryGetValue(key, out value) ? value : ""; }
        public bool Flight { get { return Get("trigger").StartsWith("flight.", StringComparison.Ordinal); } }
        public bool Probe { get { return Get("trigger") == "probe"; } }
        public string Event { get { return Flight ? Get("trigger").Substring(7) : Get("trigger"); } }
        public string LegacyRole { get { return Get("role") == "partner" ? "crew" : Get("role"); } }

        public RuntimeTemplate Template(string kind)
        {
            var t = new RuntimeTemplate(Id, kind, "", "") { Dialogue = this, Role = Get("role") };
            t.Versions.Clear();
            foreach (var a in Alternatives) t.Versions.Add(new[] { a.Spanish, a.English });
            foreach (string token in Common) DialogueTable.AddFacts(t, token);
            return t;
        }

        public List<int> Matching(Dictionary<string, double> nums, Dictionary<string, string[]> names)
        {
            var matches = new List<int>(); int best = int.MinValue;
            for (int i = 0; i < Alternatives.Count; i++)
            {
                var a = Alternatives[i]; bool eligible = true;
                var situations = new List<string>();
                foreach (string token in DialogueTable.Tokens(a.When))
                {
                    if (Array.IndexOf(RuntimeLines.Situations, token) >= 0) { situations.Add(token); continue; }
                    if (!DialogueTable.Holds(token, nums)) { eligible = false; break; }
                }
                if (situations.Count > 0 && !situations.Exists(token => DialogueTable.Holds(token, nums))) eligible = false;
                string es, en;
                if (!eligible || !RuntimeLines.FillText(a.Spanish, a.English, nums, names, out es, out en)) continue;
                if (a.Prefer < best) continue;
                if (a.Prefer > best) { matches.Clear(); best = a.Prefer; }
                matches.Add(i);
            }
            return matches;
        }
    }

    /// <summary>Canonical Crew/Probe authoring: one alternative per row, independent of the event scheduler.</summary>
    public sealed class DialogueTable
    {
        public static readonly string[] Columns = { "id", "line", "scene", "seq", "speaker", "trigger", "channel", "on", "to", "when", "prefer", "spanish", "english", "band", "type", "gender", "phase", "cause", "job", "trait", "topic", "core", "beep", "body", "who", "cooldown", "priority", "registration" };
        // Keep the scheduler's field names private to the adapter; old mod tables remain readable.
        public static string FieldKey(string key) { return key == "speaker" ? "role" : key == "type" ? "severity" : key == "job" ? "trait" : key == "trait" ? "persona" : key; }
        public static string SpeakerRole(string speaker) { return speaker == "kerbal_a" ? "crew" : speaker == "kerbal_b" ? "partner" : speaker == "mission_control" ? "mc" : speaker; }
        static readonly Regex ActorCheck = new Regex(@"^(job|trait)_([ab])(!=|=)([a-z_]+)$");
        static string Condition(string token)
        {
            Match m = ActorCheck.Match(token);
            return m.Success ? m.Groups[2].Value + m.Groups[3].Value + m.Groups[4].Value : token;
        }
        static readonly string[] Shared = { "trigger", "channel", "on", "band", "severity", "phase", "cause", "trait", "persona", "topic", "core", "who", "cooldown", "priority" };
        static readonly Regex Check = new Regex(@"^([a-z][a-z0-9_]*)(<=|>=|<|>|=)(-?[0-9]+(?:\.[0-9]+)?)$");
        static readonly Dictionary<string, DialogueLine> registry = new Dictionary<string, DialogueLine>();
        public readonly List<DialogueLine> Lines = new List<DialogueLine>();
        public static DialogueLine Find(string id) { DialogueLine line; return id != null && registry.TryGetValue(id, out line) ? line : null; }
        public static void Clear() { registry.Clear(); }
        public static string[] Tokens(string text) { return (text ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries); }
        public static bool IsUnified(IList<string> rows)
        {
            if (rows == null) return false;
            foreach (string row in rows)
                if (!string.IsNullOrWhiteSpace(row) && !row.TrimStart().StartsWith("#")) return Array.IndexOf(row.Split('\t'), "trigger") >= 0;
            return false;
        }
        public static void AddFacts(RuntimeTemplate t, string token)
        {
            token = Condition(token);
            if (Array.IndexOf(RuntimeLines.Situations, token) >= 0) { t.Situations.Add(token); return; }
            if (token.StartsWith("a=")) { t.TraitA = t.TraitA == null ? token.Substring(2) : t.TraitA + " " + token.Substring(2); return; }
            if (token.StartsWith("b=")) { t.TraitB = t.TraitB == null ? token.Substring(2) : t.TraitB + " " + token.Substring(2); return; }
            Match m = Check.Match(token);
            if (m.Success) t.Checks.Add(new KeyValuePair<string, KeyValuePair<string, double>>(m.Groups[1].Value,
                new KeyValuePair<string, double>(m.Groups[2].Value, double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture))));
        }
        public static bool Holds(string token, Dictionary<string, double> nums)
        {
            token = Condition(token);
            if (token == "any") return true;
            double value;
            if (Array.IndexOf(SubtitleSlots.Conditions, token) >= 0 || Array.IndexOf(RuntimeLines.Situations, token) >= 0)
                return nums != null && nums.TryGetValue("tag_" + token, out value) && value > 0;
            if (token.StartsWith("a=") || token.StartsWith("b="))
                return nums != null && nums.TryGetValue("job_" + token[0] + "_" + token.Substring(2), out value) && value > 0;
            if (token.StartsWith("a!=") || token.StartsWith("b!="))
                return nums != null && nums.TryGetValue("job_" + token[0] + "_" + token.Substring(3), out value) && value == 0;
            Match m = Check.Match(token);
            if (!m.Success || nums == null || !nums.TryGetValue(m.Groups[1].Value, out value) || double.IsNaN(value) || double.IsInfinity(value)) return false;
            value = RuntimeLines.Rounded(m.Groups[1].Value, value);
            double wanted = double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
            switch (m.Groups[2].Value) { case "<": return value < wanted; case "<=": return value <= wanted; case ">": return value > wanted; case ">=": return value >= wanted; default: return value == wanted; }
        }
        static bool ValidCondition(string token)
        {
            Match actor = ActorCheck.Match(token);
            if (actor.Success) return Array.IndexOf(actor.Groups[1].Value == "job" ? Persona.TraitKeys : Persona.PersonaKeys, actor.Groups[4].Value) >= 0;
            if (token == "any" || Array.IndexOf(SubtitleSlots.Conditions, token) >= 0 || Array.IndexOf(RuntimeLines.Situations, token) >= 0) return true;
            if (Regex.IsMatch(token, @"^[ab]=[a-z_]+$"))
                return Array.IndexOf(Persona.TraitKeys, token.Substring(2)) >= 0 || Array.IndexOf(Persona.PersonaKeys, token.Substring(2)) >= 0;
            Match m = Check.Match(token);
            return m.Success && (Slots.IsNumber(m.Groups[1].Value) || Slots.IsModSlot(m.Groups[1].Value));
        }
        public static DialogueTable Parse(IList<string> rows, string where, List<string> errors)
        {
            if (errors == null) errors = new List<string>();
            var doc = new DialogueTable(); var lines = new Dictionary<string, DialogueLine>();
            var heads = new Dictionary<string, Dictionary<string, string>>(); var ids = new HashSet<string>();
            Dictionary<string, int> columns = null; int width = 0, number = 0;
            foreach (string row in rows)
            {
                number++;
                if (string.IsNullOrWhiteSpace(row) || row.TrimStart().StartsWith("#")) continue;
                string[] fields = row.Split('\t');
                if (columns == null)
                {
                    columns = new Dictionary<string, int>(); width = fields.Length;
                    bool modern = Array.IndexOf(fields, "speaker") >= 0 || Array.IndexOf(fields, "job") >= 0;
                    for (int i = 0; i < fields.Length; i++)
                    {
                        string key = modern ? FieldKey(fields[i]) : fields[i];
                        if (columns.ContainsKey(key)) errors.Add(where + ": duplicate column " + fields[i]); else columns[key] = i;
                    }
                    foreach (string key in new[] { "id", "line", "scene", "seq", "role", "trigger", "when", "spanish", "english" })
                        if (!columns.ContainsKey(key)) { errors.Add(where + ": missing " + key); return doc; }
                    continue;
                }
                string at = where + ":" + number;
                if (fields.Length != width) { errors.Add(at + ": wrong column count"); continue; }
                Func<string, string> get = key => columns.ContainsKey(key) ? fields[columns[key]].Trim() : "";
                string id = get("id"), lineId = get("line"), scene = get("scene"); int seq, prefer;
                if (!PhraseTable.IsKey(id) || !PhraseTable.IsKey(lineId) || !PhraseTable.IsKey(scene) || !ids.Add(id)
                    || !int.TryParse(get("seq"), out seq) || seq < 1 || !int.TryParse(get("prefer").Length > 0 ? get("prefer") : "0", out prefer))
                { errors.Add(at + ": invalid/duplicate identity, sequence or preference"); continue; }
                var values = new Dictionary<string, string>(); foreach (string key in Columns) values[FieldKey(key)] = get(FieldKey(key));
                values["role"] = SpeakerRole(values["role"]);
                string group = scene + ":" + get("body"); Dictionary<string, string> head;
                if (heads.TryGetValue(group, out head)) foreach (string key in Shared) if (values[key].Length == 0) values[key] = head[key];
                foreach (string key in new[] { "phase", "cause", "trait", "persona", "topic", "core" }) if (values[key].Length == 0) values[key] = "any";
                values["phase"] = FlightPhase.Normalize(values["phase"]);
                if (values["channel"].Length == 0) values["channel"] = "auto";
                if (values["band"].Length == 0) values["band"] = "calm";
                if (values["severity"].Length == 0) values["severity"] = "chatter";
                if (values["gender"].Length == 0) values["gender"] = "both";
                string trigger = values["trigger"];
                if (trigger.Length == 0 || (!trigger.StartsWith("flight.") && trigger != "probe" && Array.IndexOf(RuntimeLines.Kinds, trigger) < 0)
                    || (trigger.StartsWith("flight.") && Array.IndexOf(PhraseTable.Events, trigger.Substring(7)) < 0))
                { errors.Add(at + ": unknown trigger " + trigger); continue; }
                bool metadata = trigger == "meta" || RuntimeLines.IsRegKind(trigger);
                if (Array.IndexOf(new[] { "auto", "radio", "intercom" }, values["channel"]) < 0
                    || Array.IndexOf(PhraseTable.Bands, values["band"]) < 0 || Array.IndexOf(PhraseTable.Severities, values["severity"]) < 0)
                { errors.Add(at + ": invalid channel or delivery"); continue; }
                if (!metadata && (values["role"] == "mc" && values["channel"] == "intercom"
                    || trigger == "chat" && values["role"] != "crew" && values["role"] != "partner"))
                { errors.Add(at + ": speaker is incompatible with this channel/trigger"); continue; }
                if (!metadata && Array.IndexOf(new[] { "crew", "partner", "mc", "probe" }, values["role"]) < 0)
                { errors.Add(at + ": invalid speaker"); continue; }
                if (get("spanish").Contains("|") || get("english").Contains("|") || get("when").Contains("|"))
                { errors.Add(at + ": one alternative per row; pipe-separated alternatives are not supported"); continue; }
                bool valid = true;
                var slotSets = new List<HashSet<string>>();
                foreach (string text in new[] { get("spanish"), get("english") })
                {
                    var slots = new HashSet<string>();
                    foreach (Match slot in Regex.Matches(text, @"\{([a-z][a-z0-9_]*)\}"))
                    {
                        string key = slot.Groups[1].Value; slots.Add(key);
                        if (!Slots.IsReserved(key)) { errors.Add(at + ": unknown variable " + key); valid = false; }
                    }
                    if (Regex.Replace(text, @"\{([a-z][a-z0-9_]*)\}", "").IndexOfAny(new[] { '{', '}' }) >= 0)
                    { errors.Add(at + ": malformed variable"); valid = false; }
                    slotSets.Add(slots);
                }
                if (values["role"] != "probe" && !slotSets[0].SetEquals(slotSets[1])) { errors.Add(at + ": spoken/subtitle variables disagree"); valid = false; }
                foreach (string token in Tokens(get("when"))) if (!ValidCondition(token)) { errors.Add(at + ": invalid condition " + token); valid = false; }
                if (!valid) continue;
                if (!metadata && (get("english").Length == 0 || (values["role"] != "probe" && get("spanish").Length == 0)))
                { errors.Add(at + ": missing spoken text or subtitle"); continue; }
                if (values["role"] == "probe" && (get("spanish").Length > 0 || Array.IndexOf(ProbeCores.Moods, values["beep"]) < 0))
                { errors.Add(at + ": invalid beep row"); continue; }
                DialogueLine line;
                if (!lines.TryGetValue(lineId, out line))
                {
                    line = new DialogueLine(); foreach (var pair in values) line.Fields[pair.Key] = pair.Value;
                    lines[lineId] = line; doc.Lines.Add(line);
                }
                else
                {
                    foreach (string key in new[] { "scene", "seq", "role", "trigger", "channel", "on", "to", "band", "severity", "gender", "phase", "cause", "trait", "persona", "topic", "core", "beep", "body", "who", "cooldown", "priority", "registration" })
                        if (line.Get(key) != values[key]) { errors.Add(at + ": alternatives disagree on " + key); valid = false; }
                    if (!valid) continue;
                }
                line.Alternatives.Add(new DialogueAlternative { Id = id, Spanish = get("spanish"), English = get("english"), When = get("when"), Prefer = prefer });
                if (!heads.ContainsKey(group)) heads[group] = values;
            }
            var positions = new HashSet<string>(); var sequences = new Dictionary<string, List<int>>();
            foreach (var line in doc.Lines)
            {
                string group = line.Get("trigger") + ":" + line.Get("scene") + ":" + line.Get("body");
                string position = group + ":" + line.Get("seq");
                if (!positions.Add(position)) errors.Add(where + ": multiple line identities at " + position);
                // Flight scenes can share body-independent lines across category files; the scene loader combines them.
                if (!line.Flight)
                {
                    List<int> sequence;
                    if (!sequences.TryGetValue(group, out sequence)) sequences[group] = sequence = new List<int>();
                    sequence.Add(int.Parse(line.Get("seq")));
                }
                if (line.Alternatives.Count == 0) continue;
                foreach (string token in Tokens(line.Alternatives[0].When)) line.Common.Add(token);
                foreach (var alt in line.Alternatives) line.Common.IntersectWith(Tokens(alt.When));
                // Situation tokens are ORed: a shared token is not a mandatory guard when the OR sets differ.
                var situations = new HashSet<string>();
                foreach (string token in Tokens(line.Alternatives[0].When))
                    if (Array.IndexOf(RuntimeLines.Situations, token) >= 0) situations.Add(token);
                foreach (var alt in line.Alternatives)
                {
                    var other = new HashSet<string>();
                    foreach (string token in Tokens(alt.When)) if (Array.IndexOf(RuntimeLines.Situations, token) >= 0) other.Add(token);
                    if (!situations.SetEquals(other)) { foreach (string token in RuntimeLines.Situations) line.Common.Remove(token); break; }
                }
                if ((line.Get("trigger") == "meta" || RuntimeLines.IsRegKind(line.Get("trigger"))) && line.Alternatives.Count != 1)
                    errors.Add(where + ": registration/settings records cannot have speech alternatives");
                registry[line.Id] = line;
            }
            foreach (var group in sequences)
            {
                group.Value.Sort();
                for (int i = 0; i < group.Value.Count; i++)
                    if (group.Value[i] != i + 1) { errors.Add(where + ": seq must be consecutive from 1 in " + group.Key); break; }
            }
            return doc;
        }

        public static List<int> Resolve(DialogueLine line, IDictionary<string, string> values, IDictionary<string, string> per,
            IDictionary<string, string[]> spokenNames, ICollection<string> tags, out Dictionary<string, double> nums, out Dictionary<string, string[]> names)
        {
            nums = new Dictionary<string, double>(); names = new Dictionary<string, string[]>();
            if (values != null) foreach (var pair in values)
            {
                double number;
                if (double.TryParse(pair.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out number) &&
                    (Slots.IsNumber(pair.Key) || Slots.IsModSlot(pair.Key) || pair.Key.StartsWith("tag_") || pair.Key.StartsWith("job_"))) nums[pair.Key] = number;
                else PutName(names, spokenNames, pair.Key, pair.Value);
            }
            if (per != null) foreach (var pair in per) PutName(names, null, pair.Key, pair.Value);
            if (tags != null) foreach (string tag in tags) nums["tag_" + tag] = 1;
            return line.Matching(nums, names);
        }
        static void PutName(Dictionary<string, string[]> names, IDictionary<string, string[]> spokenNames, string key, string value)
        {
            if (string.IsNullOrEmpty(value)) return;
            string[] pair;
            if (spokenNames != null && spokenNames.TryGetValue(key, out pair) && pair.Length > 1 && pair[1] == value) { names[key] = pair; return; }
            string speech = key == "body" || key == "here" ? RuntimeLines.BodySpoken(value.StartsWith("the ") ? value.Substring(4) : value)
                : RuntimeSpeech.Respell(RuntimeLines.CleanName(value));
            names[key] = new[] { speech, value };
        }

        // Event schedulers retain their timing rules, but receive the same parsed lines/alternatives.
        public List<string> SceneRows()
        {
            var rows = new List<string> { "clip_key\tevent\tseverity\tband\trole\tscene\tseq\tgender\tphase\tcause\ttrait\tpersona\ttopic\tcore\tbeep\tspanish\tenglish\tspanish_rt\tbody\truntime\tdialogue" };
            foreach (var line in Lines)
            {
                if (!line.Flight) continue;
                var a = line.Alternatives[0];
                rows.Add(string.Join("\t", new[] { line.Id, line.Event, line.Get("severity"), line.Get("band"), line.LegacyRole, line.Get("scene"), line.Get("seq"), line.Get("gender"), line.Get("phase"), line.Get("cause"), line.Get("trait"), line.Get("persona"), line.Get("topic"), line.Get("core"), line.Get("beep"), line.LegacyRole == "probe" ? "" : Regex.Replace(a.Spanish, @"\{[^}]+\}", "valor"), a.English, line.LegacyRole == "probe" ? "" : a.Spanish, line.Get("body"), "1", line.Id }));
            }
            return rows;
        }
        public List<string> TemplateRows()
        {
            var rows = new List<string> { "id\tkind\ton\tto\twho\tcooldown\tpriority\twhen\tspanish\tenglish\tscene\tseq\trole" };
            foreach (var line in Lines)
            {
                if (line.Flight || line.Probe) continue;
                var conditions = new List<string>();
                foreach (string token in line.Common)
                {
                    if (ActorCheck.IsMatch(token))
                    {
                        string condition = Condition(token);
                        if (condition.IndexOf("!=", StringComparison.Ordinal) < 0 && (line.Get("trigger") == "chat" || line.Get("trigger") == "science_talk" && condition.StartsWith("a="))) conditions.Add(condition);
                    }
                    else if (Array.IndexOf(SubtitleSlots.Conditions, token) < 0 || Array.IndexOf(RuntimeLines.Situations, token) >= 0) conditions.Add(token);
                }
                bool grouped = line.Get("trigger") == "chat" || line.Get("trigger") == "science_talk";
                bool first = line.Get("seq") == "1";
                string who = line.Get("who");
                if (line.Get("trigger") == "science_talk") who = first && line.Get("role") == "mc" ? "mc" : "";
                if (grouped && !first) conditions.Clear();
                if (line.Get("registration").Length > 0) conditions.Add("id=" + line.Get("registration"));
                var a = line.Alternatives[0];
                rows.Add(string.Join("\t", new[] { line.Id, line.Get("trigger"), grouped && !first ? "" : line.Get("on"), line.Get("to"), who, line.Get("cooldown"), line.Get("priority"), string.Join(" ", conditions.ToArray()), a.Spanish, a.English, grouped ? line.Get("scene") : "", grouped ? line.Get("seq") : "", line.Get("role") }));
            }
            return rows;
        }
        public void Attach(IList<RuntimeTemplate> templates, int start)
        {
            var map = new Dictionary<string, DialogueLine>(); foreach (var line in Lines) map[line.Id] = line;
            for (int i = start; i < templates.Count; i++)
            {
                DialogueLine line;
                if (!map.TryGetValue(templates[i].Id, out line)) continue;
                var t = templates[i]; t.Dialogue = line; t.Role = line.Get("role"); t.Versions.Clear();
                foreach (var a in line.Alternatives) t.Versions.Add(new[] { a.Spanish, a.English });
            }
        }
        public ProbeRuntimeTable ProbeScenes()
        {
            var result = new ProbeRuntimeTable(); var groups = new Dictionary<string, List<DialogueLine>>();
            foreach (var line in Lines) if (line.Probe)
            { List<DialogueLine> group; if (!groups.TryGetValue(line.Get("scene"), out group)) groups[line.Get("scene")] = group = new List<DialogueLine>(); group.Add(line); }
            foreach (var group in groups.Values)
            {
                group.Sort((a,b) => int.Parse(a.Get("seq")).CompareTo(int.Parse(b.Get("seq"))));
                DialogueLine head = group[0], spoken = group.Find(l => l.LegacyRole != "probe");
                if (spoken == null) continue;
                var template = spoken.Template("event"); template.Id = head.Get("scene");
                if (spoken != head) foreach (string token in head.Common) AddFacts(template, token);
                template.ProbeBand = head.Get("band"); template.ProbePhase = head.Get("phase");
                template.ProbeLines = new List<ProbeRuntimeLine>();
                foreach (string topic in Tokens(head.Get("on"))) template.On.Add(topic);
                if (head.Get("core") != "any") template.Checks.Add(new KeyValuePair<string, KeyValuePair<string, double>>("x_core_" + head.Get("core"), new KeyValuePair<string, double>("=", 1)));
                foreach (var line in group)
                    template.ProbeLines.Add(new ProbeRuntimeLine { Role = line.LegacyRole, Mood = line.Get("beep"), English = line.Alternatives[0].English,
                        Spanish = line.Alternatives[0].Spanish, Speech = line.Template("report") });
                result.Templates.Add(template);
            }
            return result;
        }
    }
}
