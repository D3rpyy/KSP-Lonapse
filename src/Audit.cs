// Ring buffer of the last 16 audit lines (reason of the last chosen line), for the debug overlay.
// Pure C#, no Unity. Thread-safe through a lock.
using System;

namespace Lonapse
{
    public static class Audit
    {
        public const int Capacity = 16;

        static readonly string[] buffer = new string[Capacity];
        static int next;
        static int count;
        static readonly object sync = new object();

        public static void Add(string line)
        {
            lock (sync)
            {
                buffer[next] = line ?? "";
                next = (next + 1) % Capacity;
                if (count < Capacity) count++;
            }
        }

        // Newest line, or null when empty.
        public static string Last
        {
            get
            {
                lock (sync)
                {
                    if (count == 0) return null;
                    return buffer[(next - 1 + Capacity) % Capacity];
                }
            }
        }

        // Up to 16 lines, newest first.
        public static string[] Recent
        {
            get
            {
                lock (sync)
                {
                    string[] result = new string[count];
                    for (int i = 0; i < count; i++)
                        result[i] = buffer[(next - 1 - i + Capacity * 2) % Capacity];
                    return result;
                }
            }
        }
    }
}
