// Line memory (0.6.0, user report: players hear the same lines too often). A shuffle bag that works with the
// changing lists the director picks from: lines that were never heard come first, then only the quarter of the
// list that was heard longest ago. With a fixed list every line plays once before any line repeats, and a line
// never comes back within about three quarters of the list. Kept between game sessions (PluginData/played.txt).
// Pure code: no Unity or KSP references, so it compiles under DC_OFFLINE_TEST.
using System;
using System.Collections.Generic;
using System.Globalization;

namespace Lonapse
{
    public sealed class SceneMemory
    {
        private readonly Dictionary<string, long> last = new Dictionary<string, long>();
        private long seq;

        /// <summary>Most scene ids kept. The oldest are forgotten first, so they count as new again. Raised from 4000
        /// for the scene variants: the lines of a scene with alternatives are kept too (LineKey).</summary>
        public const int MaxEntries = 20000;
        /// <summary>Prefix of a line id in the memory, so a clip key never mixes with a scene id.</summary>
        public const string LinePrefix = "line:";

        public static string LineKey(PhraseEntry e) { return e == null ? null : LinePrefix + e.ClipKey; }

        /// <summary>
        /// Scene variants: picks one line of the options (the base row and its alternatives for one seq) and remembers
        /// it. A line never heard comes first (at random among those), else the line heard longest ago. So every
        /// version of a line plays once before one comes back. Null or empty gives null.
        /// </summary>
        public PhraseEntry TakeLine(IList<PhraseEntry> options, Random rng)
        {
            if (options == null || options.Count == 0) return null;
            PhraseEntry p = null;
            if (options.Count == 1) p = options[0];
            else
            {
                var fresh = new List<PhraseEntry>();
                for (int i = 0; i < options.Count; i++) if (LastSeq(LineKey(options[i])) < 0) fresh.Add(options[i]);
                if (fresh.Count > 0) p = fresh[rng != null ? rng.Next(fresh.Count) : 0];
                else
                {
                    p = options[0];
                    for (int i = 1; i < options.Count; i++)
                        if (LastSeq(LineKey(options[i])) < LastSeq(LineKey(p))) p = options[i];
                }
            }
            Stamp(LineKey(p));
            return p;
        }
        /// <summary>Prefix of a slot value in the memory (procedural talk): "slot:thing:helmet".</summary>
        public const string SlotPrefix = "slot:";

        /// <summary>
        /// Procedural talk: picks one value of a slot pool and remembers it, with the same rule as TakeLine (a value
        /// never used first, else the one used longest ago). So "Who took my {thing}?" goes through the whole pool
        /// before a thing comes back. Null or empty gives null.
        /// </summary>
        public string TakeValue(string pool, IList<string> values, Random rng)
        {
            if (values == null || values.Count == 0) return null;
            string p;
            if (values.Count == 1) p = values[0];
            else
            {
                var fresh = new List<string>();
                for (int i = 0; i < values.Count; i++) if (LastSeq(SlotPrefix + pool + ":" + values[i]) < 0) fresh.Add(values[i]);
                if (fresh.Count > 0) p = fresh[rng != null ? rng.Next(fresh.Count) : 0];
                else
                {
                    p = values[0];
                    for (int i = 1; i < values.Count; i++)
                        if (LastSeq(SlotPrefix + pool + ":" + values[i]) < LastSeq(SlotPrefix + pool + ":" + p)) p = values[i];
                }
            }
            Stamp(SlotPrefix + pool + ":" + p);
            return p;
        }

        /// <summary>After every line was heard: pick from this share of the list, the ones heard longest ago.</summary>
        public const int OldestShareDivisor = 4;

        public int Count { get { return last.Count; } }
        public long Sequence { get { return seq; } }

        /// <summary>When the scene was last picked (a counter, not a time), -1 for never.</summary>
        public long LastSeq(string id)
        {
            long s;
            return id != null && last.TryGetValue(id, out s) ? s : -1;
        }

        public void Stamp(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            last[id] = ++seq;
            if (last.Count > MaxEntries) Trim(MaxEntries * 9 / 10);
        }

        public void Clear()
        {
            last.Clear();
            seq = 0;
        }

        /// <summary>
        /// The part of <paramref name="list"/> to pick from: the scenes never heard when there are any, else the
        /// quarter heard longest ago (at least one). The order of the list is kept. Null or empty gives empty.
        /// </summary>
        public List<PhraseScene> Freshest(IList<PhraseScene> list)
        {
            var r = new List<PhraseScene>();
            if (list == null || list.Count == 0) return r;
            for (int i = 0; i < list.Count; i++) if (LastSeq(list[i].Id) < 0) r.Add(list[i]);
            if (r.Count > 0) return r;
            int k = Math.Max(1, (list.Count + OldestShareDivisor - 1) / OldestShareDivisor);
            var sorted = new List<PhraseScene>(list);
            sorted.Sort((a, b) => LastSeq(a.Id).CompareTo(LastSeq(b.Id)));
            long cutoff = LastSeq(sorted[k - 1].Id);
            for (int i = 0; i < list.Count; i++) if (LastSeq(list[i].Id) <= cutoff) r.Add(list[i]);
            return r;
        }

        /// <summary>
        /// Picks from the freshest part at random and remembers the pick. <paramref name="avoidId"/> (the scene
        /// that played last) is left out when there is another choice. A single scene is returned as it is.
        /// </summary>
        public PhraseScene Take(IList<PhraseScene> list, Random rng, string avoidId)
        {
            if (list == null || list.Count == 0) return null;
            PhraseScene p;
            if (list.Count == 1) p = list[0];
            else
            {
                IList<PhraseScene> from = list;
                if (avoidId != null)
                {
                    var without = new List<PhraseScene>(list.Count);
                    for (int i = 0; i < list.Count; i++) if (list[i].Id != avoidId) without.Add(list[i]);
                    if (without.Count > 0) from = without;
                }
                List<PhraseScene> fresh = Freshest(from);
                p = fresh[rng != null ? rng.Next(fresh.Count) : 0];
            }
            Stamp(p.Id);
            return p;
        }

        // ------------------------------------------------ file

        /// <summary>One line per scene, "id seq", oldest first, after a "# Lonapse line memory" header.</summary>
        public List<string> Serialize()
        {
            var items = new List<KeyValuePair<string, long>>(last);
            items.Sort((a, b) => a.Value.CompareTo(b.Value));
            var r = new List<string>(items.Count + 1) { "# Lonapse line memory: scene id and when it was last picked. Delete this file to start fresh." };
            foreach (var kv in items) r.Add(kv.Key + " " + kv.Value.ToString(CultureInfo.InvariantCulture));
            return r;
        }

        /// <summary>Reads Serialize output. Bad lines are skipped. The counter continues after the highest value.</summary>
        public static SceneMemory Parse(IEnumerable<string> lines)
        {
            SceneMemory m = new SceneMemory();
            if (lines == null) return m;
            foreach (string raw in lines)
            {
                if (raw == null) continue;
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                int sp = line.LastIndexOf(' ');
                long v;
                if (sp <= 0 || !long.TryParse(line.Substring(sp + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out v) || v < 0) continue;
                string id = line.Substring(0, sp).Trim();
                if (id.Length == 0) continue;
                m.last[id] = v;
                if (v > m.seq) m.seq = v;
            }
            if (m.last.Count > MaxEntries) m.Trim(MaxEntries);
            return m;
        }

        private void Trim(int keep)
        {
            var items = new List<KeyValuePair<string, long>>(last);
            items.Sort((a, b) => b.Value.CompareTo(a.Value));   // newest first
            last.Clear();
            for (int i = 0; i < items.Count && i < keep; i++) last[items[i].Key] = items[i].Value;
        }
    }
}
