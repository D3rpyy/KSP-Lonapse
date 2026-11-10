// One volume for the radio beds, bursts and crackle. Pure code, also used by settings migration.
using System;

namespace Lonapse
{
    public static class RadioNoise
    {
        public const float DefaultVolume = 0.5f;
        public const float CrewBed = 0.42426407f;
        public const float ControlBed = 0.21213203f;

        public static float Gain(float volume)
        {
            return MixMath.ClampVolume(volume) / DefaultVolume;
        }

        public static float FromLegacy(float volume, float amount)
        {
            volume = DcMath.Clamp01(DcMath.Finite(volume));
            amount = DcMath.Clamp01(DcMath.Finite(amount));
            return DcMath.Clamp01(volume * (float)Math.Sqrt(amount / DefaultVolume));
        }

        public static float ControlLevel(float baseline, float signalStatic, float weakVolume)
        {
            return DcMath.Clamp01(baseline + ControlBed + signalStatic * weakVolume);
        }
    }
}
