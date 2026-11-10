using System;
using System.Reflection;
using Lonapse;

namespace UnityEngine { public class MonoBehaviour { } }

public static class GainDspTests
{
    static readonly MethodInfo Filter = typeof(GainDsp).GetMethod("OnAudioFilterRead", BindingFlags.NonPublic | BindingFlags.Instance);
    static void Require(bool ok, string name)
    {
        if (!ok) throw new Exception(name);
        Console.WriteLine("PASS  " + name);
    }
    static void Process(GainDsp dsp, float[] samples, int channels)
    {
        Filter.Invoke(dsp, new object[] { samples, channels });
    }
    public static int Main()
    {
        var dsp = new GainDsp();
        float[] untouched = { 0.1f, -0.2f, 0.7f };
        Process(dsp, untouched, 1);
        Require(untouched[0] == 0.1f && untouched[1] == -0.2f && untouched[2] == 0.7f,
                "output gain leaves existing unity-gain audio unchanged");
        dsp.gain = 4f;
        float[] quiet = { 0.05f, -0.1f };
        Process(dsp, quiet, 2);
        Require(Math.Abs(quiet[0] - 0.2f) < 1e-6 && Math.Abs(quiet[1] + 0.4f) < 1e-6,
                "stacked master and category boosts reach stereo samples above Unity's source-volume limit");
        dsp.gain = 16f;
        float[] loud = { 0.5f, -0.5f, 1f, -1f };
        Process(dsp, loud, 2);
        Require(Array.TrueForAll(loud, v => !float.IsNaN(v) && Math.Abs(v) <= 1f)
                && loud[0] > 0.8f && loud[1] < -0.8f,
                "high output gain uses the soft limiter without hard-clipping or changing polarity");
        dsp.gain = 2f;
        float[] whole = { 0.1f, -0.2f, 0.6f, -0.7f };
        float[] first = { 0.1f, -0.2f }, second = { 0.6f, -0.7f };
        Process(dsp, whole, 2); Process(dsp, first, 2); Process(dsp, second, 2);
        Require(whole[0] == first[0] && whole[1] == first[1] && whole[2] == second[0] && whole[3] == second[1],
                "output gain stays consistent across streaming block boundaries");
        return 0;
    }
}
