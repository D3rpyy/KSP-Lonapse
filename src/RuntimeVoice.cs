// Experimental (runtime voice): speaks lines at runtime with the standalone Piper binary. One Piper process per voice,
// started once and kept running (the model loads once), fed one JSON line per sentence on stdin. Piper writes a WAV
// and prints its path on stdout. A reader thread turns that WAV into samples (RuntimeSpeech: Kerbal shift, level
// shaping), and Tick() on the main thread makes the AudioClip, because Unity only allows that there.
// Files: GameData/Lonapse/PluginData/Runtime/<windows|linux|macos>/piper(.exe) with its libraries and espeak-ng-data,
// and PluginData/Runtime/voices/<voice>.onnx(.json). KSP under Proton/Wine is the Windows player, so it uses windows/.
// It must be under PluginData: KSP loads every .dll elsewhere in GameData as a plugin, and the native Piper DLLs
// make that loader throw, which stops the game loading.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using UnityEngine;

namespace Lonapse
{
    public sealed class RuntimeVoice : IDisposable
    {
        public sealed class Result
        {
            public AudioClip clip;
            public string error;
            public float queueToReadyMs, piperMs;
            public string spoken;
        }

        sealed class Job
        {
            public string id, spoken, wavPath;
            public double pitchSt;
            public Action<Result> done;
            public Stopwatch sw;
            public float piperMs;
            public float[] samples;
            public string error;
        }

        sealed class Worker
        {
            public RuntimeDelivery delivery;
            public Process proc;
            public Stream stdin;
            public Thread reader, errReader;
            public readonly Queue<Job> pending = new Queue<Job>();
            public volatile bool dead;
            public string lastErr;
        }

        readonly Dictionary<string, Worker> workers = new Dictionary<string, Worker>();
        readonly Queue<Job> finished = new Queue<Job>();
        readonly object gate = new object();
        // requests go to one dispatcher thread: starting Piper takes about half a second, the game must not wait
        readonly Queue<KeyValuePair<string, Job>> requests = new Queue<KeyValuePair<string, Job>>();
        readonly AutoResetEvent wake = new AutoResetEvent(false);
        Thread dispatcher;
        int counter;
        volatile bool disposed;

        // paths and platform are read once on the main thread (Unity calls), the dispatcher thread uses the copies
        static string root, tempDir, platform;

        /// <summary>Main thread: reads the KSP root and the player platform. The constructor calls it.</summary>
        public static void Init()
        {
            if (root != null) return;
            tempDir = Path.Combine(KSPUtil.ApplicationRootPath, "GameData/Lonapse/PluginData/RuntimeTemp");
            switch (Application.platform)
            {
                case RuntimePlatform.LinuxPlayer: platform = "linux"; break;
                case RuntimePlatform.OSXPlayer: platform = "macos"; break;
                default: platform = "windows"; break;
            }
            root = Path.Combine(KSPUtil.ApplicationRootPath, "GameData/Lonapse/PluginData/Runtime");
        }

        public RuntimeVoice() { Init(); }

        public static string Root { get { Init(); return root; } }
        public static string TempDir { get { Init(); return tempDir; } }

        /// <summary>Folder of the Piper binary for this player: windows (also under Proton/Wine), linux, macos.
        /// KSP on a Mac is an x64 build, so macos holds the x64 binary (Rosetta on Apple silicon).</summary>
        public static string Platform { get { Init(); return platform; } }

        public static string Binary
        {
            get { return Path.Combine(Path.Combine(Root, Platform), Platform == "windows" ? "piper.exe" : "piper"); }
        }

        public static string VoicePath(string voice) { return Path.Combine(Path.Combine(Root, "voices"), voice + ".onnx"); }

        /// <summary>Why runtime voice cannot work here, null when the files are there.</summary>
        static readonly Dictionary<string, KeyValuePair<float, string>> missingCache = new Dictionary<string, KeyValuePair<float, string>>();

        public static string Missing(string kind)
        {
            float now = Environment.TickCount / 1000f; // not a Unity call: the dispatcher thread asks too
            KeyValuePair<float, string> c;
            lock (missingCache)
                if (missingCache.TryGetValue(kind, out c) && now - c.Key < 5f) return c.Value;
            string why = MissingNow(kind);
            lock (missingCache) missingCache[kind] = new KeyValuePair<float, string>(now, why);
            return why;
        }

        static string MissingNow(string kind)
        {
            if (!File.Exists(Binary)) return "no Piper binary at " + Binary;
            RuntimeDelivery d = RuntimeDelivery.For(kind);
            if (d == null) return "unknown speaker kind " + kind;
            if (!File.Exists(VoicePath(d.Voice))) return "no voice " + VoicePath(d.Voice);
            if (!File.Exists(VoicePath(d.Voice) + ".json")) return "no voice config " + VoicePath(d.Voice) + ".json";
            return null;
        }

        /// <summary>Starts the Piper process of a kind (m, f, mc) in the background and speaks one short line it throws
        /// away, so the model is loaded before the first real line.</summary>
        public void Warm(string kind)
        {
            if (Missing(kind) == null) Request(kind, "Hola.", null);
        }

        /// <summary>
        /// Speaks a Spanish template (names marked with RuntimeSpeech.MarkName) in the voice of a kind. done runs on
        /// the main thread (from Tick) with the clip, or with an error. done null: a warm-up line, no clip is made.
        /// Never blocks: the Piper process starts and gets the line on the dispatcher thread.
        /// </summary>
        public void Request(string kind, string spanishTemplate, Action<Result> done)
        {
            var job = new Job { id = "rt" + Interlocked.Increment(ref counter), spoken = RuntimeSpeech.Spoken(spanishTemplate),
                                done = done, sw = Stopwatch.StartNew() };
            if (disposed) return;
            lock (requests)
            {
                requests.Enqueue(new KeyValuePair<string, Job>(kind, job));
                if (dispatcher == null)
                {
                    dispatcher = new Thread(Dispatch) { IsBackground = true, Name = "Lonapse Piper dispatch" };
                    dispatcher.Start();
                }
            }
            wake.Set();
        }

        void Dispatch()
        {
            while (!disposed)
            {
                KeyValuePair<string, Job> r;
                bool have;
                lock (requests) { have = requests.Count > 0; r = have ? requests.Dequeue() : default(KeyValuePair<string, Job>); }
                if (!have) { wake.WaitOne(1000); continue; }
                Send(r.Key, r.Value);
            }
        }

        void Send(string kind, Job job)
        {
            string why = Missing(kind);
            Worker w = null;
            if (why == null) why = GetWorker(kind, out w);
            if (why != null)
            {
                job.error = why;
                lock (gate) finished.Enqueue(job);
                return;
            }
            job.pitchSt = w.delivery.PitchSt;
            job.wavPath = Path.Combine(TempDir, job.id + "_" + kind + ".wav").Replace('\\', '/');
            string json = "{\"text\": " + RuntimeSpeech.Json(job.spoken) + ", \"output_file\": " + RuntimeSpeech.Json(job.wavPath) + "}\n";
            byte[] bytes = new UTF8Encoding(false).GetBytes(json);
            try
            {
                lock (w.pending) w.pending.Enqueue(job);
                w.stdin.Write(bytes, 0, bytes.Length);
                w.stdin.Flush();
            }
            catch (Exception e)
            {
                w.dead = true;
                lock (w.pending) if (w.pending.Count > 0 && w.pending.Peek() == job) w.pending.Dequeue();
                job.error = "write to Piper failed: " + e.Message;
                lock (gate) finished.Enqueue(job);
            }
        }

        string GetWorker(string kind, out Worker w)
        {
            lock (workers) return GetWorkerLocked(kind, out w);
        }

        string GetWorkerLocked(string kind, out Worker w)
        {
            if (workers.TryGetValue(kind, out w) && !w.dead && w.proc != null && !w.proc.HasExited) return null;
            if (w != null) Stop(w);
            RuntimeDelivery d = RuntimeDelivery.For(kind);
            w = new Worker { delivery = d };
            try
            {
                Directory.CreateDirectory(TempDir);
                string bin = Binary, dir = Path.GetDirectoryName(bin);
                if (Platform != "windows") MakeExecutable(bin);
                var psi = new ProcessStartInfo(bin)
                {
                    Arguments = "--model \"" + VoicePath(d.Voice) + "\" --json-input --quiet"
                        + " --length_scale " + d.LengthScale.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture)
                        + " --noise_scale " + d.NoiseScale.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture)
                        + " --noise_w " + d.NoiseW.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture)
                        + " --sentence_silence 0.12"
                        + " --espeak_data \"" + Path.Combine(dir, "espeak-ng-data") + "\"",
                    WorkingDirectory = dir,
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                };
                if (Platform == "linux") psi.EnvironmentVariables["LD_LIBRARY_PATH"] = dir;
                if (Platform == "macos") psi.EnvironmentVariables["DYLD_LIBRARY_PATH"] = dir;
                var sw = Stopwatch.StartNew();
                w.proc = Process.Start(psi);
                w.stdin = w.proc.StandardInput.BaseStream;
                Worker ww = w;
                w.reader = new Thread(() => ReadLoop(ww)) { IsBackground = true, Name = "Lonapse Piper " + kind };
                w.reader.Start();
                w.errReader = new Thread(() => ErrLoop(ww)) { IsBackground = true, Name = "Lonapse Piper err " + kind };
                w.errReader.Start();
                workers[kind] = w;
                Log.Info("Runtime voice: started Piper for " + kind + " (" + d.Voice + ", length scale "
                         + d.LengthScale.ToString("0.000") + ") in " + sw.ElapsedMilliseconds + " ms");
                return null;
            }
            catch (Exception e)
            {
                w.dead = true;
                return "Piper did not start: " + e.Message;
            }
        }

        static void MakeExecutable(string path)
        {
            try
            {
                var p = Process.Start(new ProcessStartInfo("chmod", "+x \"" + path + "\"") { UseShellExecute = false, CreateNoWindow = true });
                if (p != null) p.WaitForExit(2000);
            }
            catch (Exception) { }
        }

        void ReadLoop(Worker w)
        {
            try
            {
                StreamReader so = w.proc.StandardOutput;
                string line;
                while (!disposed && (line = so.ReadLine()) != null)
                {
                    line = line.Trim();
                    if (line.Length == 0) continue;
                    Job job;
                    lock (w.pending) job = w.pending.Count > 0 ? w.pending.Dequeue() : null;
                    if (job == null) continue;
                    job.piperMs = (float)job.sw.Elapsed.TotalMilliseconds;
                    try
                    {
                        byte[] wav = File.ReadAllBytes(job.wavPath);
                        job.samples = RuntimeSpeech.FromPiperWav(wav, job.pitchSt);
                        if (job.samples == null) job.error = "Piper WAV had no speech";
                    }
                    catch (Exception e) { job.error = "reading the Piper WAV failed: " + e.Message; }
                    try { File.Delete(job.wavPath); } catch (Exception) { }
                    lock (gate) finished.Enqueue(job);
                }
            }
            catch (Exception e) { w.lastErr = e.Message; }
            w.dead = true;
            // whatever is still waiting will not come
            lock (w.pending)
                while (w.pending.Count > 0)
                {
                    Job j = w.pending.Dequeue();
                    j.error = "Piper stopped" + (w.lastErr != null ? ": " + w.lastErr : "");
                    lock (gate) finished.Enqueue(j);
                }
        }

        void ErrLoop(Worker w)
        {
            try
            {
                string line;
                while (!disposed && (line = w.proc.StandardError.ReadLine()) != null)
                    if (line.Trim().Length > 0) w.lastErr = line.Trim();
            }
            catch (Exception) { }
        }

        /// <summary>Main thread, every frame: makes the clips of finished lines and calls their done.</summary>
        public void Tick()
        {
            while (true)
            {
                Job j;
                lock (gate) { if (finished.Count == 0) return; j = finished.Dequeue(); }
                var r = new Result { error = j.error, spoken = j.spoken, piperMs = j.piperMs };
                if (j.error == null && j.samples != null && j.done != null) // no clip for a warm-up line
                {
                    r.clip = AudioClip.Create(j.id, j.samples.Length, 1, RuntimeSpeech.OutRate, false);
                    r.clip.SetData(j.samples, 0);
                }
                r.queueToReadyMs = (float)j.sw.Elapsed.TotalMilliseconds;
                if (r.error != null) Log.Warn("Runtime voice: " + r.error);
                try { if (j.done != null) j.done(r); }
                catch (Exception e) { Log.Warn("Runtime voice: callback failed: " + e.Message); }
            }
        }

        static void Stop(Worker w)
        {
            w.dead = true;
            try { if (w.stdin != null) w.stdin.Close(); } catch (Exception) { }
            try { if (w.proc != null && !w.proc.HasExited && !w.proc.WaitForExit(500)) w.proc.Kill(); } catch (Exception) { }
        }

        public void Dispose()
        {
            disposed = true;
            wake.Set();
            lock (workers)
            {
                foreach (Worker w in workers.Values) Stop(w);
                workers.Clear();
            }
        }
    }
}
