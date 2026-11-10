# Lonapse RPM glyphs

`unicode.bin` is the Lonapse RPM Glyphs bitmap subset. It supplies Cyrillic and Chinese glyphs on the Comms page,
including every character in the bundled Mandarin pronunciation dictionary. Other monitor pages retain their fonts.
The data loads only when the page displays Unicode text. A small texture holds only the glyphs currently needed.
Glyph strokes are expanded one pixel to the right to match RPM's bold text, within the original character cells.

The base glyphs come from GNU Unifont 17.0.05, used under the SIL Open Font License 1.1 in `OFL-1.1.txt`.
Source https://unifoundry.com/pub/unifont/unifont-17.0.05/font-builds/unifont_all-17.0.05.hex.gz.
This subset is a modified font and is distributed under the name Lonapse RPM Glyphs.

Rare Han characters missing from Unifont are rasterized from Jigmo 20250912 by Koichi Kamichi and GlyphWiki
contributors, dedicated to the public domain under CC0 1.0. See `Jigmo-CC0.txt`.
Source https://kamichikoichi.github.io/jigmo/Jigmo-20250912.zip.

Regenerate with `tools/build_rpm_font.py` using the two source downloads. The generator requires Pillow and fontTools.
The file is a gzip stream with an `LNRF1` header, a little-endian 32-bit glyph count, then records containing a 32-bit
Unicode code point, an 8-bit pixel width (8 or 16), and the 16 rows of packed bitmap pixels, most significant bit first.
