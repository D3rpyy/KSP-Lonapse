// Amplitude modulation on the audio thread. Only volatile floats are shared with the main thread.
using System;
using UnityEngine;

namespace Lonapse
{
    public class TremoloDsp : MonoBehaviour
    {
        public volatile float depth;       // 0..1
        public volatile float rate = 5.5f; // Hz
        public volatile float sampleRate = 48000f;
        private double phase;              // audio thread only

        private void OnAudioFilterRead(float[] data, int channels)
        {
            float d = depth;
            if (d <= 0.0005f || data == null || channels <= 0) return;
            if (d > 1f) d = 1f;
            float sr = sampleRate;
            if (sr < 8000f) sr = 48000f;
            double step = 2.0 * Math.PI * rate / sr;
            double ph = phase;
            int frames = data.Length / channels;
            int idx = 0;
            for (int f = 0; f < frames; f++)
            {
                float m = 1f - d * 0.5f * (1f + (float)Math.Sin(ph));
                for (int c = 0; c < channels; c++) data[idx++] *= m;
                ph += step;
            }
            if (ph > 2.0 * Math.PI) ph %= 2.0 * Math.PI;
            phase = ph;
        }
    }
}
