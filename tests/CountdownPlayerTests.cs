// Exercise the production countdown player with GameDatabase-owned recordings and no Piper implementation.
using System;
using System.Collections.Generic;
using Lonapse;

class CountdownPlayerTests
{
    static void Check(bool value, string message)
    {
        if (!value) throw new Exception(message);
        Console.WriteLine("PASS  " + message);
    }

    static void Main()
    {
        var host = new LonapseAddon();
        var vessel = new Vessel();
        FlightGlobals.ActiveVessel = vessel;
        for (int step = 1; step <= LaunchCountdown.Liftoff + 1; step++)
        {
            string key = step <= LaunchCountdown.Liftoff ? LaunchCountdown.ClipKey(step) : LaunchCountdown.AbortKey;
            PhraseClips.table.entries.Add(key, new PhraseEntry { ClipKey = key });
            GameDatabase.Instance.audio.Add(LaunchCountdown.AudioUrl(key), new UnityEngine.AudioClip { name = key });
        }
        var player = new CountdownPlayer(host);
        Check(player.WhyNot(vessel) == null, "countdown recordings are ready immediately without Piper");
        player.Start(vessel);
        Check(player.Running, "recorded countdown starts without a preparation wait");
        for (int i = 0; i < LaunchCountdown.Liftoff; i++) player.Update(i == 0 ? 0.6f : 1f, vessel);
        bool ordered = host.control.played.Count == LaunchCountdown.Liftoff;
        for (int i = 0; i < host.control.played.Count; i++) ordered &= host.control.played[i] == LaunchCountdown.ClipKey(i + 1);
        Check(ordered && !player.Running, "all ten recorded numbers and liftoff play in order");
        host.control.played.Clear();
        host.settings.countdownDown = true;
        player.Start(vessel); player.Update(0.6f, vessel); player.Stop();
        Check(host.control.played.Count == 2 && host.control.played[0] == "countdown_10"
              && host.control.played[1] == LaunchCountdown.AbortKey, "down-count and cancellation use their recordings");
        int lookups = GameDatabase.Instance.lookups;
        FlightGlobals.ActiveVessel = new Vessel();
        Check(player.WhyNot(FlightGlobals.ActiveVessel) == null && GameDatabase.Instance.lookups == lookups + 12,
              "switching vessels reloads recording references without destroying shared clips");
        player.Dispose();
        player = new CountdownPlayer(host);
        Check(player.WhyNot(FlightGlobals.ActiveVessel) == null, "scene changes leave GameDatabase recordings available");
        string missing = LaunchCountdown.AudioUrl("countdown_05");
        GameDatabase.Instance.audio.Remove(missing);
        PhraseClips.SceneGeneration++;
        Check(player.WhyNot(FlightGlobals.ActiveVessel) == "missing countdown recording countdown_05",
              "missing countdown assets identify the exact recording");
        player.Dispose();
    }
}

namespace UnityEngine
{
    public class AudioClip { public string name; }
    public static class Object { public static void Destroy(object value) { throw new Exception("Destroyed a GameDatabase recording"); } }
}
namespace KSP.UI.Screens { public static class StageManager { public static void ActivateNextStage() { } } }
public class Vessel
{
    public enum Situations { PRELAUNCH, FLYING }
    public Guid id = Guid.NewGuid();
    public Situations situation = Situations.PRELAUNCH;
}
public static class FlightGlobals { public static Vessel ActiveVessel; }
public static class ScreenMessages { public static void PostScreenMessage(string message, float duration) { } }
public class GameDatabase
{
    public static readonly GameDatabase Instance = new GameDatabase();
    public readonly Dictionary<string, UnityEngine.AudioClip> audio = new Dictionary<string, UnityEngine.AudioClip>();
    public int lookups;
    public UnityEngine.AudioClip GetAudioClip(string url) { lookups++; UnityEngine.AudioClip clip; return audio.TryGetValue(url, out clip) ? clip : null; }
}
namespace Lonapse
{
    public class LonapseAddon
    {
        public bool CanStartAudio = true;
        public string gateReason = "open";
        public readonly Settings settings = new Settings();
        public readonly ChatterDirector director = new ChatterDirector();
        public readonly VoiceChannel control = new VoiceChannel();
        public readonly VoiceChannel[] crew = new VoiceChannel[0];
        public VoiceChannel clickCh;
        public Subtitles subtitles;
    }
    public class Settings
    {
        public bool radioEnabled = true, countdownAutoStage, countdownDown, voiceCompressor, subtitles;
        public float voiceBoostDb, calloutVolume = 1f;
    }
    public class ChatterDirector
    {
        public float clock, holdUntil;
        public PhraseEntry ResolveSpeechLine(PhraseEntry entry) { return entry; }
        public void ClearQueue() { }
    }
    public class PhraseEntry { public string ClipKey; }
    public class PhraseTable
    {
        public readonly Dictionary<string, PhraseEntry> entries = new Dictionary<string, PhraseEntry>();
        public PhraseEntry Get(string key) { PhraseEntry entry; return entries.TryGetValue(key, out entry) ? entry : null; }
    }
    public static class PhraseClips
    {
        public static bool Loaded = true;
        public static int SceneGeneration;
        public static readonly PhraseTable table = new PhraseTable();
        public static string Subtitle(PhraseEntry entry) { return entry.ClipKey; }
    }
    public enum RadioVoice { Control }
    public class VoiceChannel
    {
        public readonly List<string> played = new List<string>();
        public bool Busy, compressor;
        public float duration = 0.6f, boost, baseDistortion, extraDistortion, dropout;
        public string phraseKey;
        public void Play(UnityEngine.AudioClip clip, float volume, float pitch, bool loop, float delay) { played.Add(clip.name); }
        public void SetBand(float high, float low) { }
        public void SetRadioVoice(RadioVoice voice) { }
        public void Stop() { }
        public void FadeOutOnce(float duration) { }
    }
    public class Subtitles { public void Show(string who, string text, float duration, float hold, bool radio) { } }
    public static class SubtitleLayout { public const float DefaultHold = 2f; }
    public static class MixMath { public static float DbToLin(float db) { return 1f; } }
    public static class ClipLibrary { public static UnityEngine.AudioClip Next(string folder, Random random) { return null; } }
    public static class Log
    {
        public static void Info(string message) { }
        public static void Warn(string message) { }
    }
}
