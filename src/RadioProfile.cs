// Radio profiles for station, ship and EVA transmissions (table 4.6). Pure C#, tested offline.
// The numbers are starting values, not measurements. They are tuned in the game.
using System;

namespace Lonapse
{
    public enum RadioContext { Ship, Station, Eva }

    public class RadioProfile
    {
        public RadioContext Context;
        public string Name;
        public float HighPassHz;
        public float LowPassHz;
        public float Distortion;
        // Clip keys (relative to the sounds folder) for opening and closing a transmission.
        public string OpenClip;
        public string CloseClip;
        // Background bed clip, or null for no bed.
        public string BedClip;
        public float BedGain;
    }

    public static class RadioProfiles
    {
        static readonly RadioProfile Ship = new RadioProfile
        {
            Context = RadioContext.Ship, Name = "Ship",
            HighPassHz = 300f, LowPassHz = 3600f, Distortion = 0.20f,
            OpenClip = "beeps/headset_open", CloseClip = "beeps/headset_close",
            BedClip = null, BedGain = 0f
        };

        static readonly RadioProfile Station = new RadioProfile
        {
            Context = RadioContext.Station, Name = "Station",
            HighPassHz = 350f, LowPassHz = 2800f, Distortion = 0.30f,
            OpenClip = "beeps/click_desk", CloseClip = "beeps/click_desk",
            BedClip = null, BedGain = 0f
        };

        static readonly RadioProfile Eva = new RadioProfile
        {
            Context = RadioContext.Eva, Name = "Eva",
            HighPassHz = 400f, LowPassHz = 2600f, Distortion = 0.25f,
            OpenClip = "beeps/headset_open", CloseClip = "beeps/headset_close",
            BedClip = "static/breath_loop", BedGain = 0.6f   // the loop itself sits at about -30 dBFS RMS
        };

        public static RadioProfile Get(RadioContext context)
        {
            switch (context)
            {
                case RadioContext.Station: return Station;
                case RadioContext.Eva: return Eva;
                default: return Ship;
            }
        }

        // Selection precedence: a forced profile ("station", "ship" or "eva", any case) wins.
        // Otherwise station wins over EVA, and EVA wins over ship. Any other forced value counts as "auto".
        public static RadioProfile Select(bool station, bool eva, string forced)
        {
            string f = forced == null ? "auto" : forced.Trim().ToLowerInvariant();
            if (f == "station") return Get(RadioContext.Station);
            if (f == "eva") return Get(RadioContext.Eva);
            if (f == "ship") return Get(RadioContext.Ship);
            if (station) return Get(RadioContext.Station);
            if (eva) return Get(RadioContext.Eva);
            return Get(RadioContext.Ship);
        }
    }
}
