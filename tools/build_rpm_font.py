#!/usr/bin/env python3
"""Bundle Cyrillic and Han bitmap glyphs from GNU Unifont 17.0.05 and Jigmo 20250912.

Download https://unifoundry.com/pub/unifont/unifont-17.0.05/font-builds/unifont_all-17.0.05.hex.gz
Download https://kamichikoichi.github.io/jigmo/Jigmo-20250912.zip
Run python3 tools/build_rpm_font.py /path/to/unifont_all-17.0.05.hex.gz /path/to/Jigmo-20250912.zip
Needs Pillow and fontTools. Unifont glyphs retain OFL 1.1, Jigmo additions are CC0 1.0.
"""
import argparse
import gzip
import hashlib
import io
from pathlib import Path
import re
import struct
import zipfile

from PIL import Image, ImageDraw, ImageFont
from fontTools.ttLib import TTFont


def included(code):
    return (code == 0x3F or 0x0400 <= code <= 0x052F or 0x1C80 <= code <= 0x1C8F
            or 0x2DE0 <= code <= 0x2DFF or 0xA640 <= code <= 0xA69F
            or 0x3000 <= code <= 0x303F or 0x3400 <= code <= 0x4DBF
            or 0x4E00 <= code <= 0x9FFF or 0xF900 <= code <= 0xFAFF
            or 0xFF00 <= code <= 0xFFEF or 0x20000 <= code <= 0x3347F)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("hex_gz", type=Path)
    parser.add_argument("jigmo_zip", type=Path)
    args = parser.parse_args()
    if hashlib.sha256(args.hex_gz.read_bytes()).hexdigest() != "7b182454966046d35482469b979edce7d262fab5c53c2180e9b1fbb5d0b5e574":
        parser.error("expected unifont_all-17.0.05.hex.gz (SHA256 mismatch)")
    if hashlib.sha256(args.jigmo_zip.read_bytes()).hexdigest() != "5744c7386d129475d87607ca66d043c8793c65448adeaedc921b6931890e5d0b":
        parser.error("expected Jigmo-20250912.zip (SHA256 mismatch)")
    glyphs = {}
    for line in gzip.decompress(args.hex_gz.read_bytes()).decode("ascii").splitlines():
        code, hex_bits = line.split(":")
        code = int(code, 16)
        if included(code):
            bits = bytes.fromhex(hex_bits)
            assert len(bits) in (16, 32)
            glyphs[code] = bits
    repo = Path(__file__).resolve().parent.parent
    generated = (repo / "src/ChinesePronunciation.Data.cs").read_text(encoding="utf-8")
    wanted = {int(a or b, 16) for a, b in re.findall(r"\\(?:u([0-9A-F]{4})|U([0-9A-F]{8}))", generated)}
    with zipfile.ZipFile(args.jigmo_zip) as archive:
        for entry in archive.namelist():
            if not entry.lower().endswith(".ttf"):
                continue
            raw = archive.read(entry)
            cmap = TTFont(io.BytesIO(raw)).getBestCmap()
            face = ImageFont.truetype(io.BytesIO(raw), 16)
            for code in sorted((wanted - glyphs.keys()) & cmap.keys()):
                char = chr(code)
                left, top, right, bottom = face.getbbox(char)
                image = Image.new("L", (max(16, right - left), max(16, bottom - top)))
                ImageDraw.Draw(image).text((-left, -top), char, font=face, fill=255)
                if image.width > 16 or image.height > 16:
                    image.thumbnail((16, 16))
                cell = Image.new("L", (16, 16))
                cell.paste(image, ((16 - image.width) // 2, (16 - image.height) // 2))
                bits = bytearray()
                for y in range(16):
                    row = sum(1 << (15 - x) for x in range(16) if cell.getpixel((x, y)) >= 64)
                    bits += row.to_bytes(2, "big")
                glyphs[code] = bytes(bits)
    missing = wanted - glyphs.keys()
    if missing:
        raise ValueError("Mandarin characters missing from font " + str(sorted(missing)[:10]))
    result = bytearray(b"LNRF1" + struct.pack("<I", len(glyphs)))
    for code, bits in sorted(glyphs.items()):
        # Add one pixel to each stroke's right edge to match RPM's bold bitmap text.
        # Work row by row so strokes cannot spill into the next row or character cell.
        row_bytes = len(bits) // 16
        bold = bytearray()
        for offset in range(0, len(bits), row_bytes):
            row = int.from_bytes(bits[offset:offset + row_bytes], "big")
            bold += (row | (row >> 1)).to_bytes(row_bytes, "big")
        result += struct.pack("<IB", code, len(bits) // 2) + bold
    out = repo / "GameData/Lonapse/Fonts/unicode.bin"
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_bytes(gzip.compress(result, mtime=0))
    print("Bundled %d glyphs in %s (%d bytes)" % (len(glyphs), out, out.stat().st_size))


if __name__ == "__main__":
    main()
