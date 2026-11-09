// Probe mode: probe core keys, beep folders and the sleep hysteresis.
// Pure code: no Unity or KSP references, so it compiles under DC_OFFLINE_TEST.
using System;
using System.Collections.Generic;

namespace Lonapse
{
    /// <summary>Probe core characters and the beep folders that belong to them.</summary>
    public static class ProbeCores
    {
        public const string Stayputnik = "stayputnik", Okto = "okto", Hecs = "hecs", RoveMate = "rovemate", Snob = "snob", Generic = "generic";
        // own cores for parts that had the stayputnik or snob character before
        public const string Qbe = "qbe", Mk2Drone = "mk2drone", Mpo = "mpo", Mtm = "mtm";

        /// <summary>All core keys. Generic is last. The order matches <see cref="DisplayNames"/>.</summary>
        public static readonly string[] Keys = { Stayputnik, Okto, Hecs, RoveMate, Snob, Qbe, Mk2Drone, Mpo, Mtm, Generic };

        /// <summary>Display names per key, same order as <see cref="Keys"/>.</summary>
        public static readonly string[] DisplayNames = { "Stayputnik", "OKTO", "HECS", "RoveMate", "Probe", "QBE", "Drone Core", "MPO", "MTM", "Probe" };

        /// <summary>Beep moods. Each core has one folder per mood.</summary>
        public static readonly string[] Moods = { "happy", "offended", "curious", "scared", "sleepy", "proud", "bored" };

        /// <summary>Default wake margin above probeDrowsyFrac (fraction of the EC capacity).</summary>
        public const float WakeMargin = 0.05f;

        // normalised stock part names (lowercase, '.' replaced by '_') to core keys
        private static readonly Dictionary<string, string> Map = new Dictionary<string, string>
        {
            { "probecoresphere", Stayputnik }, { "probecoresphere_v2", Stayputnik },
            { "probecorecube", Qbe },
            { "probecoreocto", Okto }, { "probecoreocto_v2", Okto }, { "probecoreocto2", Okto }, { "probecoreocto2_v2", Okto },
            { "probecorehex", Hecs }, { "probecorehex_v2", Hecs }, { "hecs2_probecore", Hecs },
            { "roverbody", RoveMate }, { "roverbody_v2", RoveMate },
            { "probestacksmall", Snob }, { "probestacklarge", Snob },
            { "mk2dronecore", Mk2Drone }, { "mpoprobe", Mpo }, { "mtmstage", Mtm },
        };

        /// <summary>Lowercase, trimmed, '.' replaced by '_'. KSP turns '_' in a cfg name into '.' in part.name.</summary>
        public static string Normalise(string partName)
        {
            if (partName == null) return "";
            return partName.Trim().ToLowerInvariant().Replace('.', '_');
        }

        /// <summary>Core key for a part name. Unknown, null or empty names give "generic".</summary>
        public static string KeyFor(string partName)
        {
            string k;
            return Map.TryGetValue(Normalise(partName), out k) ? k : Generic;
        }

        /// <summary>True when the part name is one of the stock cores with a known character.</summary>
        public static bool IsKnown(string partName)
        {
            return Map.ContainsKey(Normalise(partName));
        }

        /// <summary>
        /// Core key of a vessel. <paramref name="names"/> holds the probe core part names in root-first order.
        /// Returns the key of the first known core, "generic" when none is known, null when the list is null or empty.
        /// </summary>
        public static string Pick(IList<string> names)
        {
            if (names == null || names.Count == 0) return null;
            for (int i = 0; i < names.Count; i++)
                if (IsKnown(names[i])) return KeyFor(names[i]);
            return Generic;
        }

        /// <summary>Display name for a core key or a lineset id ("Probe" for snob, generic and unknown keys).</summary>
        public static string DisplayName(string key)
        {
            int i = Array.IndexOf(Keys, key);
            if (i >= 0) return DisplayNames[i];
            string n;
            return key != null && linesets.TryGetValue(key, out n) ? n : "Probe";
        }

        // ---- linesets: lines of a mod or a player, next to the stock cores ----
        // A lineset is an id in the core column of the phrase table. It is registered from a LONAPSE_PROBE_LINES node
        // before the table and the voices are read, so a row or a voice can name it.
        private static readonly Dictionary<string, string> linesets = new Dictionary<string, string>();
        public const int MaxLinesetIdLength = 32;

        /// <summary>A lineset id: a to z, 0 to 9 and _, at most 32 characters, not "any" and not a stock core key.</summary>
        public static bool ValidLinesetId(string id)
        {
            if (string.IsNullOrEmpty(id) || id.Length > MaxLinesetIdLength || id == "any" || Array.IndexOf(Keys, id) >= 0) return false;
            for (int i = 0; i < id.Length; i++)
            {
                char c = id[i];
                if (!((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_')) return false;
            }
            return true;
        }

        /// <summary>
        /// Registers a lineset from the values of a LONAPSE_PROBE_LINES node (name, displayName). The id is lowercased.
        /// False with a message in <paramref name="errors"/> when the id is bad. A second node with the same id keeps
        /// the first and reports it. A missing displayName gives "Probe".
        /// </summary>
        public static bool RegisterLineset(IDictionary<string, string> values, List<string> errors)
        {
            string id;
            if (values == null || !values.TryGetValue("name", out id) || string.IsNullOrEmpty(id = id.Trim().ToLowerInvariant()))
            {
                if (errors != null) errors.Add("lineset without a name");
                return false;
            }
            if (!ValidLinesetId(id))
            {
                if (errors != null) errors.Add("lineset '" + id + "': the name must be a to z, 0 to 9 and _, at most " + MaxLinesetIdLength
                                               + " characters, and not 'any' or a stock core");
                return false;
            }
            if (linesets.ContainsKey(id))
            {
                if (errors != null) errors.Add("lineset '" + id + "' is registered twice, the first one stays");
                return false;
            }
            string dn;
            linesets[id] = values.TryGetValue("displayName", out dn) && !string.IsNullOrEmpty(dn.Trim()) ? dn.Trim() : "Probe";
            return true;
        }

        public static bool IsLineset(string id) { return id != null && linesets.ContainsKey(id); }

        /// <summary>True for a stock core key or a registered lineset: a value the lines of a voice can name.</summary>
        public static bool IsLinesKey(string key)
        {
            return key != null && (Array.IndexOf(Keys, key) >= 0 || linesets.ContainsKey(key));
        }

        /// <summary>The stock keys, then the registered linesets in name order. For the window.</summary>
        public static string[] LinesKeys()
        {
            var ids = new List<string>(linesets.Keys);
            ids.Sort(StringComparer.Ordinal);
            string[] r = new string[Keys.Length + ids.Count];
            Array.Copy(Keys, r, Keys.Length);
            ids.CopyTo(r, Keys.Length);
            return r;
        }

        /// <summary>Forgets every lineset. For tests.</summary>
        public static void ClearLinesets() { linesets.Clear(); }

        /// <summary>Cores that have their own sound and name but no lines of their own yet. Until their scenes are
        /// written (phrase table core column) they borrow the neutral generic lines. Remove a key here once its lines
        /// exist.</summary>
        public static readonly string[] BorrowGenericLines = { };

        /// <summary>The phrase table lines a stock voice uses: its own key, or generic for a core in
        /// <see cref="BorrowGenericLines"/>.</summary>
        public static string DefaultLines(string key)
        {
            return Array.IndexOf(BorrowGenericLines, key) >= 0 ? Generic : key;
        }

        /// <summary>The data stream beep while an antenna sends (built by build_clips.py).</summary>
        public const string DataFolder = "beeps/probe_data";

        /// <summary>Beep folder of one core and mood: beeps/probe_&lt;core&gt;_&lt;mood&gt;.</summary>
        public static string Folder(string core, string mood)
        {
            return "beeps/probe_" + core + "_" + mood;
        }

        /// <summary>All probe beep folders (10 cores x 7 moods, core-major).</summary>
        public static string[] Folders()
        {
            string[] r = new string[Keys.Length * Moods.Length];
            int n = 0;
            for (int c = 0; c < Keys.Length; c++)
                for (int m = 0; m < Moods.Length; m++)
                    r[n++] = Folder(Keys[c], Moods[m]);
            return r;
        }
    }

    public enum ProbeSleepState { Awake = 0, Drowsy = 1, Asleep = 2 }

    /// <summary>
    /// Sleep hysteresis on the electric charge fraction. Drowsy below drowsyFrac, asleep below sleepFrac.
    /// It only goes back to awake above drowsyFrac + margin, so the probe does not flicker at a threshold.
    /// A forced sleep (core without power or hibernating) is asleep at once and wakes by the same rule.
    /// </summary>
    public class ProbeSleep
    {
        public ProbeSleepState State = ProbeSleepState.Awake;

        public bool Asleep { get { return State == ProbeSleepState.Asleep; } }
        public bool Drowsy { get { return State == ProbeSleepState.Drowsy; } }

        public void Reset() { State = ProbeSleepState.Awake; }

        public ProbeSleepState Update(double ecFrac, float drowsyFrac, float sleepFrac, float margin, bool forceAsleep)
        {
            if (double.IsNaN(ecFrac)) ecFrac = 1.0;
            if (sleepFrac > drowsyFrac) sleepFrac = drowsyFrac;
            if (margin < 0f) margin = 0f;
            double wake = drowsyFrac + margin;

            if (forceAsleep) { State = ProbeSleepState.Asleep; return State; }
            switch (State)
            {
                case ProbeSleepState.Awake:
                    if (ecFrac < sleepFrac) State = ProbeSleepState.Asleep;
                    else if (ecFrac < drowsyFrac) State = ProbeSleepState.Drowsy;
                    break;
                case ProbeSleepState.Drowsy:
                    if (ecFrac < sleepFrac) State = ProbeSleepState.Asleep;
                    else if (ecFrac > wake) State = ProbeSleepState.Awake;
                    break;
                default:
                    // asleep: stay asleep until there is real power again
                    if (ecFrac > wake) State = ProbeSleepState.Awake;
                    break;
            }
            return State;
        }

        public ProbeSleepState Update(double ecFrac, float drowsyFrac, float sleepFrac)
        {
            return Update(ecFrac, drowsyFrac, sleepFrac, ProbeCores.WakeMargin, false);
        }
    }
}
