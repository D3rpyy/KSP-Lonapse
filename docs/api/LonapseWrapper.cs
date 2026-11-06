// LonapseWrapper: call the Lonapse API from your mod without a dependency on Lonapse.
// Copy this file into your mod and change the namespace to your own. Public domain (CC0): use it any way you like.
//
// Without Lonapse installed, Available is false and every call returns false. Your mod loads and runs as normal.
// This file uses only KSP and .NET types, so your DLL never references Lonapse.dll.
//
//   if (LonapseWrapper.Available)
//       LonapseWrapper.Trigger("yourmod.repair_done", vessel, kerbal,
//           new Dictionary<string, double> { { "x_parts", 3 } },
//           new Dictionary<string, string> { { "x_tool", "la llave|the wrench" } });
//
// The lines go in GameData/YourMod/Lonapse/*.tsv. See docs/API.md in the Lonapse repository.
using System;
using System.Collections.Generic;
using System.Reflection;

namespace YourMod
{
    public static class LonapseWrapper
    {
        static bool searched;
        static MethodInfo trigger, hasLines, cancel;
        static PropertyInfo available, apiVersion;

        static void Find()
        {
            if (searched) return;
            searched = true;
            try
            {
                foreach (AssemblyLoader.LoadedAssembly a in AssemblyLoader.loadedAssemblies)
                {
                    if (a == null || a.assembly == null || a.name != "Lonapse") continue;
                    Type t = a.assembly.GetType("Lonapse.LonapseApi", false);
                    if (t == null) continue;
                    trigger = t.GetMethod("Trigger", BindingFlags.Public | BindingFlags.Static, null,
                        new[] { typeof(string), typeof(Vessel), typeof(ProtoCrewMember),
                                typeof(IDictionary<string, double>), typeof(IDictionary<string, string>) }, null);
                    hasLines = t.GetMethod("HasLines", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(string) }, null);
                    cancel = t.GetMethod("Cancel", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(string) }, null);
                    available = t.GetProperty("Available", BindingFlags.Public | BindingFlags.Static);
                    apiVersion = t.GetProperty("ApiVersion", BindingFlags.Public | BindingFlags.Static);
                    break;
                }
            }
            catch (Exception) { trigger = null; hasLines = null; cancel = null; available = null; apiVersion = null; }
        }

        /// <summary>Lonapse is installed (any state). Use Available to know if it can talk now.</summary>
        public static bool Installed { get { Find(); return trigger != null; } }

        /// <summary>The API version of the installed Lonapse, 0 when it is not installed.</summary>
        public static int ApiVersion
        {
            get
            {
                Find();
                try { return apiVersion != null ? (int)apiVersion.GetValue(null, null) : 0; }
                catch (Exception) { return 0; }
            }
        }

        /// <summary>Lonapse is running in flight and enabled.</summary>
        public static bool Available
        {
            get
            {
                Find();
                try { return available != null && (bool)available.GetValue(null, null); }
                catch (Exception) { return false; }
            }
        }

        /// <summary>Something happened. eventId is "yourmod.event", the on= of your lines. vessel null: the active
        /// one. kerbal null: Lonapse picks. numbers and names fill your x_ slots, a name is "english" or
        /// "spanish|english". True when Lonapse accepted the event. It never throws.</summary>
        public static bool Trigger(string eventId, Vessel vessel, ProtoCrewMember kerbal,
                                   IDictionary<string, double> numbers = null, IDictionary<string, string> names = null)
        {
            Find();
            if (trigger == null) return false;
            try { return (bool)trigger.Invoke(null, new object[] { eventId, vessel, kerbal, numbers, names }); }
            catch (Exception) { return false; }
        }

        /// <summary>Drops your event when it still waits for the radio, for example when the problem is solved
        /// before Lonapse said it. A line that already plays is not cut. True when one was waiting.</summary>
        public static bool Cancel(string eventId)
        {
            Find();
            if (cancel == null) return false;
            try { return (bool)cancel.Invoke(null, new object[] { eventId }); }
            catch (Exception) { return false; }
        }

        /// <summary>Lines for this event are loaded in Lonapse (after the first flight scene).</summary>
        public static bool HasLines(string eventId)
        {
            Find();
            if (hasLines == null) return false;
            try { return (bool)hasLines.Invoke(null, new object[] { eventId }); }
            catch (Exception) { return false; }
        }
    }
}
