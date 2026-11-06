// Loads the Piper voices once, when a save opens (the Space Center), and keeps them for the whole game session.
// Loading a model takes all cores for a moment. At the flight start that cut the audio, at the Space Center nobody
// hears it. The flight addon uses RuntimeVoice.Shared and finds the voices running.
using System;
using UnityEngine;

namespace Lonapse
{
    [KSPAddon(KSPAddon.Startup.SpaceCentre, true)]
    public sealed class RuntimeVoiceHost : MonoBehaviour
    {
        /// <summary>Every voice a flight often needs: Mission Control calm, tense, panic and callout, and the calm and
        /// tense voice of both genders. Crew panic lines are recorded clips, crew callouts load when one is needed.</summary>
        public static readonly string[] SessionKinds = { "mc", "mc.tense", "mc.mc_panic", "mc.callout", "m", "m.tense", "f", "f.tense" };

        private void Awake()
        {
            DontDestroyOnLoad(gameObject);
            Log.SetMainThread();
        }

        private void Start()
        {
            try
            {
                Settings s = Settings.Load();
                if (!s.runtimeVoice) { Log.Info("Runtime voice is off: no Piper voices loaded at the save start"); return; }
                string why = RuntimeVoice.Missing("mc");
                if (why != null) { Log.Info("Runtime voice: no voices loaded at the save start (" + why + ")"); return; }
                RuntimeVoice v = RuntimeVoice.Shared;
                foreach (string kind in SessionKinds) v.Warm(kind);
                Log.Info("Runtime voice: loading " + SessionKinds.Length + " Piper voices for this game session");
            }
            catch (Exception e) { Log.Exception("RuntimeVoiceHost.Start", e); }
        }

        private void Update()
        {
            Log.Flush();
            try { RuntimeVoice.Shared.Tick(); }
            catch (Exception e) { Log.Exception("RuntimeVoiceHost.Tick", e); }
        }

        private void OnApplicationQuit()
        {
            try { RuntimeVoice.DisposeShared(); } catch (Exception) { }
        }
    }
}
