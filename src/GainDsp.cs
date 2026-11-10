// Final output gain on the audio thread for every channel. A source volume cannot pass 1,
// so extra slider gain boosts here. Linear up to 0.8, then a smooth knee that never passes 1,
// the same curve as VoiceDsp. No allocation in OnAudioFilterRead.
using System;
using UnityEngine;

namespace Lonapse
{
    public class GainDsp : MonoBehaviour
    {
        public volatile float gain = 1f;     // linear, set from the main thread

        private void OnAudioFilterRead(float[] data, int channels)
        {
            float g = gain;
            if (data == null || g <= 1.001f) return;
            for (int i = 0; i < data.Length; i++)
            {
                float v = data[i] * g;
                float a = v < 0f ? -v : v;
                if (a > 0.8f)
                {
                    float y = 0.8f + 0.2f * (float)Math.Tanh((a - 0.8f) / 0.2f);
                    v = v < 0f ? -y : y;
                }
                data[i] = v;
            }
        }
    }
}
