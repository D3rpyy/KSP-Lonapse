// Compact bitmap glyph data, independent of Unity so bundle coverage is checked offline.
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace Lonapse
{
    public static class RpmUnicodeFontData
    {
        public static Dictionary<int, byte[]> Read(Stream source)
        {
            using (var zip = new GZipStream(source, CompressionMode.Decompress))
            using (var reader = new BinaryReader(zip))
            {
                if (Encoding.ASCII.GetString(reader.ReadBytes(5)) != "LNRF1") throw new InvalidDataException("Invalid RPM font header");
                int count = reader.ReadInt32();
                if (count < 1 || count > 200000) throw new InvalidDataException("Invalid RPM font size");
                var result = new Dictionary<int, byte[]>(count);
                for (int i = 0; i < count; i++)
                {
                    int code = reader.ReadInt32(), width = reader.ReadByte();
                    if (width != 8 && width != 16) throw new InvalidDataException("Invalid RPM glyph width");
                    byte[] bits = reader.ReadBytes(width * 2);
                    if (bits.Length != width * 2) throw new EndOfStreamException("Incomplete RPM glyph");
                    result.Add(code, bits);
                }
                return result;
            }
        }
    }
}
