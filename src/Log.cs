using System;
using System.Collections.Generic;
using UnityEngine;

namespace Lonapse
{
    public static class Log
    {
        public const string Prefix = "[Lonapse] ";
        public static bool verbose;

        private static readonly Dictionary<string, float> lastBySite = new Dictionary<string, float>();
        private static readonly Dictionary<string, int> suppressedBySite = new Dictionary<string, int>();
        private static readonly HashSet<string> onceKeys = new HashSet<string>();

        public static void Info(string msg) { UnityEngine.Debug.Log(Prefix + msg); }
        public static void Warn(string msg) { UnityEngine.Debug.LogWarning(Prefix + msg); }
        public static void Error(string msg) { UnityEngine.Debug.LogError(Prefix + msg); }

        public static void Debug(string msg)
        {
            if (verbose) UnityEngine.Debug.Log(Prefix + "[debug] " + msg);
        }

        /// <summary>Logs an info line only the first time the key is seen in this game session.</summary>
        public static void InfoOnce(string key, string msg)
        {
            if (onceKeys.Add(key)) Info(msg);
        }

        public static void WarnOnce(string key, string msg)
        {
            if (onceKeys.Add(key)) Warn(msg);
        }

        /// <summary>Exception from a Unity callback. At most one log line per 10 s per site.</summary>
        public static void Exception(string site, Exception e)
        {
            float now = Time.realtimeSinceStartup;
            float last;
            if (lastBySite.TryGetValue(site, out last) && now - last < 10f)
            {
                int n;
                suppressedBySite.TryGetValue(site, out n);
                suppressedBySite[site] = n + 1;
                return;
            }
            int suppressed;
            suppressedBySite.TryGetValue(site, out suppressed);
            suppressedBySite[site] = 0;
            lastBySite[site] = now;
            string extra = suppressed > 0 ? " (" + suppressed + " similar suppressed)" : "";
            // Formatting the exception can throw itself (broken runtime state). Never let that escape
            // into the KSP event that called us.
            string text;
            try { text = e == null ? "null" : e.ToString(); }
            catch { try { text = e.GetType().Name; } catch { text = "unprintable exception"; } }
            try { UnityEngine.Debug.LogError(Prefix + "Exception in " + site + extra + ": " + text); }
            catch { }
        }
    }
}
