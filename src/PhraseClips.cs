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
                table = PhraseTable.Parse(new List<string>(File.ReadAllLines(path, System.Text.Encoding.UTF8)), errors);
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
                     + subtitleOnlyCount + " subtitle-only rows" + (missing > 0 ? ", " + missing + " missing" : "");
            Log.Info("Lonapse: " + status);
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
                if (!sc.Lines[i].SubtitleOnly && Clip(sc.Lines[i], false) == null) return false;
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
