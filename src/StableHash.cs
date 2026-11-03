// FNV-1a 32 bit over UTF-8 bytes. Stable across runs and platforms (never use string.GetHashCode).
using System.Text;

namespace Lonapse
{
    public static class StableHash
    {
        public const uint OffsetBasis = 0x811C9DC5u;
        public const uint Prime = 0x01000193u;

        public static uint Fnv1a(string s)
        {
            uint h = OffsetBasis;
            byte[] bytes = Encoding.UTF8.GetBytes(s ?? string.Empty);
            for (int i = 0; i < bytes.Length; i++)
            {
                h ^= bytes[i];
                unchecked { h *= Prime; }
            }
            return h;
        }

        /// <summary>Hash of name + "#" + salt.</summary>
        public static uint Hash(string name, int salt)
        {
            return Fnv1a((name ?? string.Empty) + "#" + salt.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        /// <summary>Hash mapped to [0,1].</summary>
        public static double Unit(string name, int salt)
        {
            return Hash(name, salt) / 4294967295.0;
        }
    }
}
