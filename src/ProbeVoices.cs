// Probe voices (0.6.0): which voices exist and which one a vessel uses.
// Voices come from three places, a later one replaces an earlier one with the same name:
//   stock (ProbeVoiceSpec.Stock), LONAPSE_PROBE_VOICE nodes in GameData, PROBE_VOICE nodes in settings.cfg.
// Pure code: no Unity or KSP references, so it compiles under DC_OFFLINE_TEST.
using System;
using System.Collections.Generic;

namespace Lonapse
{
    public enum ProbeVoiceSource { Stock = 0, Config = 1, Settings = 2 }

    public sealed class ProbeVoiceRegistry
    {
        private readonly Dictionary<string, ProbeVoiceSpec> voices = new Dictionary<string, ProbeVoiceSpec>();
        private readonly Dictionary<string, ProbeVoiceSource> sources = new Dictionary<string, ProbeVoiceSource>();
        private readonly Dictionary<string, ProbeVoiceSpec> stock = new Dictionary<string, ProbeVoiceSpec>();
        private readonly List<string> order = new List<string>();

        public ProbeVoiceRegistry()
        {
            foreach (ProbeVoiceSpec v in ProbeVoiceSpec.Stock())
            {
                stock[v.Name] = v;
                Add(v, ProbeVoiceSource.Stock);
            }
        }

        /// <summary>Stock voices, then CFG voices, then settings voices. Same order as the voice list in the window.</summary>
        public static ProbeVoiceRegistry Build(IList<ProbeVoiceSpec> config, IList<ProbeVoiceSpec> settings)
        {
            ProbeVoiceRegistry r = new ProbeVoiceRegistry();
            if (config != null) foreach (ProbeVoiceSpec v in config) r.Add(v, ProbeVoiceSource.Config);
            if (settings != null) foreach (ProbeVoiceSpec v in settings) r.Add(v, ProbeVoiceSource.Settings);
            return r;
        }

        public void Add(ProbeVoiceSpec v, ProbeVoiceSource source)
        {
            if (v == null || string.IsNullOrEmpty(v.Name)) return;
            if (!voices.ContainsKey(v.Name)) order.Add(v.Name);
            voices[v.Name] = v;
            sources[v.Name] = source;
        }

        /// <summary>The voice of that name, null when there is none.</summary>
        public ProbeVoiceSpec Get(string name)
        {
            ProbeVoiceSpec v;
            return name != null && voices.TryGetValue(name.Trim().ToLowerInvariant(), out v) ? v : null;
        }

        public ProbeVoiceSource SourceOf(string name)
        {
            ProbeVoiceSource s;
            return name != null && sources.TryGetValue(name, out s) ? s : ProbeVoiceSource.Stock;
        }

        /// <summary>Voice names in list order: stock first, then new names in the order they came in.</summary>
        public IList<string> Names { get { return order.AsReadOnly(); } }

        /// <summary>
        /// True when the voice sounds exactly like the stock voice of the same name. Then the prebuilt WAV clips
        /// play, and nothing is made in game.
        /// </summary>
        public bool UsesStockClips(ProbeVoiceSpec v)
        {
            ProbeVoiceSpec s;
            return v != null && stock.TryGetValue(v.Name, out s) && s.SameSound(v);
        }

        public bool IsStockName(string name) { return name != null && stock.ContainsKey(name); }

        /// <summary>The next name after <paramref name="name"/> in list order, wrapping around (step -1 goes back).</summary>
        public string Cycle(string name, int step)
        {
            if (order.Count == 0) return name;
            int i = order.IndexOf(name);
            if (i < 0) return order[0];
            return order[((i + step) % order.Count + order.Count) % order.Count];
        }

        /// <summary>A name like custom1, custom2 that is not taken yet.</summary>
        public string FreeName(string prefix)
        {
            for (int i = 1; ; i++)
            {
                string n = prefix + i;
                if (!voices.ContainsKey(n)) return n;
            }
        }
    }

    /// <summary>One probe core part of a vessel, in root-first order.</summary>
    public struct ProbeCorePart
    {
        public string partName;      // part.partInfo.name
        public string moduleVoice;   // ModuleLonapse.voice, null or empty without the module
        public int priority;         // ModuleLonapse.priority, 0 without the module
        public string displayName;   // ModuleLonapse.displayName, null or empty without one

        public ProbeCorePart(string partName, string moduleVoice, int priority)
            : this(partName, moduleVoice, priority, null) { }

        public ProbeCorePart(string partName, string moduleVoice, int priority, string displayName)
        {
            this.partName = partName; this.moduleVoice = moduleVoice; this.priority = priority;
            this.displayName = displayName;
        }
    }

    public static class ProbeVoicePick
    {
        public enum Why { None, Player, Module, StockPart, Generic }

        /// <summary>
        /// The subtitle name a part gives itself: the ModuleLonapse displayName of the core that decided the voice
        /// (<paramref name="coreIndex"/> from Resolve), trimmed. Null when that core has none. The name belongs to the
        /// part, so it holds for every voice the part gets, also one the player picked. Several parts can then share
        /// one voice and still have their own name.
        /// </summary>
        public static string ModuleName(IList<ProbeCorePart> cores, int coreIndex)
        {
            if (cores == null || coreIndex < 0 || coreIndex >= cores.Count) return null;
            string n = cores[coreIndex].displayName;
            return string.IsNullOrEmpty(n) || n.Trim().Length == 0 ? null : n.Trim();
        }

        /// <summary>
        /// The voice of a vessel. Per core part: a voice the player gave that part in the settings, else the
        /// ModuleLonapse voice, else the stock voice of a stock part. The core that decides: the first core with a
        /// player voice, else the module voice with the highest priority, else the first stock part (root first on
        /// ties). Nothing found: generic, from the first core. A voice name that does not exist is skipped.
        /// Returns null when the vessel has no probe core. <paramref name="coreIndex"/> is the deciding core.
        /// </summary>
        public static ProbeVoiceSpec Resolve(IList<ProbeCorePart> cores, IDictionary<string, string> playerParts,
                                             ProbeVoiceRegistry reg, out int coreIndex, out Why why)
        {
            coreIndex = -1;
            why = Why.None;
            if (cores == null || cores.Count == 0 || reg == null) return null;
            ProbeVoiceSpec v;
            if (playerParts != null)
                for (int i = 0; i < cores.Count; i++)
                {
                    string name;
                    if (playerParts.TryGetValue(ProbeCores.Normalise(cores[i].partName), out name) && (v = reg.Get(name)) != null)
                    {
                        coreIndex = i; why = Why.Player; return v;
                    }
                }
            int best = -1;
            ProbeVoiceSpec bestV = null;
            for (int i = 0; i < cores.Count; i++)
            {
                if (string.IsNullOrEmpty(cores[i].moduleVoice)) continue;
                ProbeVoiceSpec m = reg.Get(cores[i].moduleVoice);
                if (m == null) continue;
                if (best < 0 || cores[i].priority > cores[best].priority) { best = i; bestV = m; }
            }
            if (bestV != null)
            {
                // the stock cores get their own voice through Patches/Lonapse_StockCores.cfg: still "stock"
                coreIndex = best;
                why = ProbeCores.IsKnown(cores[best].partName) && ProbeCores.KeyFor(cores[best].partName) == cores[best].moduleVoice
                      ? Why.StockPart : Why.Module;
                return bestV;
            }
            for (int i = 0; i < cores.Count; i++)
                if (ProbeCores.IsKnown(cores[i].partName) && (v = reg.Get(ProbeCores.KeyFor(cores[i].partName))) != null)
                {
                    coreIndex = i; why = Why.StockPart; return v;
                }
            coreIndex = 0;
            why = Why.Generic;
            return reg.Get(ProbeCores.Generic) ?? new ProbeVoiceSpec();
        }
    }
}
