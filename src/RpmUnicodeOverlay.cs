// RPM background handler. Unicode glyphs occupy reserved cells behind its ordinary bitmap text.
// Uses RPM's loaded font shader, with no assembly dependency or changes to other monitor pages.
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Lonapse
{
    public class RpmUnicodeOverlay : InternalModule
    {
        [KSPField] public string backgroundTextureURL = "JSI/RasterPropMonitor/Library/Textures/bg01";
        static Dictionary<int, byte[]> font;
        static bool fontFailed;
        readonly List<RpmUnicodeGlyph> glyphs = new List<RpmUnicodeGlyph>();
        readonly Dictionary<int, Rect> atlasRects = new Dictionary<int, Rect>();
        Texture2D atlas, background;
        Material material;
        int columns, rows;
        bool failed;
        const int AtlasSize = 1024, Tile = 18, TilesPerRow = AtlasSize / Tile;

        public string SetPage(string page, int width, int height)
        {
            columns = Math.Max(1, width); rows = Math.Max(1, height);
            if (failed) return page;
            try
            {
                string masked = RpmUnicodeText.Mask(page, glyphs);
                if (glyphs.Count == 0) return page;
                if (!EnsureFont()) { glyphs.Clear(); return page; }
                return masked;
            }
            catch (Exception e)
            {
                failed = true; glyphs.Clear();
                Log.Exception("RPM Unicode text", e);
                return page;
            }
        }

        bool EnsureFont()
        {
            if (fontFailed) return false;
            if (font == null)
            {
                try
                {
                    using (var stream = File.OpenRead(Path.Combine(KSPUtil.ApplicationRootPath, "GameData/Lonapse/Fonts/unicode.bin")))
                        font = RpmUnicodeFontData.Read(stream);
                    Log.Info("RPM Unicode font: loaded " + font.Count + " glyphs");
                }
                catch (Exception e) { fontFailed = true; Log.Exception("RPM Unicode font", e); return false; }
            }
            if (material == null)
            {
                Shader shader = RpmShaderLoader.Load() as Shader;
                if (shader == null) shader = Shader.Find("RPM/FontShader");
                if (shader == null) throw new InvalidOperationException("RPM font shader is not loaded");
                material = new Material(shader);
                atlas = new Texture2D(AtlasSize, AtlasSize, TextureFormat.RGBA32, false);
                atlas.filterMode = FilterMode.Point;
                atlas.wrapMode = TextureWrapMode.Clamp;
                material.mainTexture = atlas;
            }
            return true;
        }

        Rect Glyph(int code, ref bool changed)
        {
            Rect rect;
            if (atlasRects.TryGetValue(code, out rect)) return rect;
            byte[] bits;
            if (!font.TryGetValue(code, out bits)) bits = font['?'];
            int width = bits.Length / 2, index = atlasRects.Count;
            int x = (index % TilesPerRow) * Tile + 1, y = (index / TilesPerRow) * Tile + 1;
            var pixels = new Color32[16 * 16];
            for (int row = 0; row < 16; row++)
                for (int col = 0; col < width; col++)
                {
                    bool on = (bits[row * (width / 8) + col / 8] & (0x80 >> (col % 8))) != 0;
                    pixels[(15 - row) * 16 + col] = new Color32(255, 255, 255, on ? (byte)255 : (byte)0);
                }
            atlas.SetPixels32(x, y, 16, 16, pixels);
            rect = new Rect((float)x / AtlasSize, (float)y / AtlasSize, (float)width / AtlasSize, 16f / AtlasSize);
            atlasRects.Add(code, rect); changed = true;
            return rect;
        }

        public bool RenderUnicode(RenderTexture screen, float cameraAspect)
        {
            RenderTexture previous = RenderTexture.active;
            GL.PushMatrix();
            try
            {
                RenderTexture.active = screen;
                GL.LoadPixelMatrix(0, screen.width, screen.height, 0);
                GL.Clear(true, true, Color.black);
                if (background == null) background = GameDatabase.Instance.GetTexture(backgroundTextureURL, false);
                if (background != null) Graphics.DrawTexture(new Rect(0, 0, screen.width, screen.height), background);
                if (failed || glyphs.Count == 0 || material == null) return true;
                // Reserve enough slots for the entire page before uploading or drawing any glyphs.
                if (atlasRects.Count + glyphs.Count > TilesPerRow * TilesPerRow) atlasRects.Clear();
                bool changed = false;
                foreach (RpmUnicodeGlyph glyph in glyphs) Glyph(glyph.code, ref changed);
                if (changed) atlas.Apply(false, false);
                float cellWidth = (float)screen.width / columns, cellHeight = (float)screen.height / rows;
                if (!material.SetPass(0)) throw new InvalidOperationException("RPM font shader pass could not be set");
                GL.Begin(GL.QUADS);
                foreach (RpmUnicodeGlyph glyph in glyphs)
                {
                    if (glyph.column >= columns || glyph.row >= rows) continue;
                    Rect uv = atlasRects[glyph.code];
                    float x = glyph.column * cellWidth, y = glyph.row * cellHeight, w = glyph.cells * cellWidth;
                    uint color = glyph.color;
                    // RPM's font shader doubles vertex alpha.
                    GL.Color(new Color((color >> 24) / 255f, ((color >> 16) & 255) / 255f,
                                       ((color >> 8) & 255) / 255f, (color & 255) / 510f));
                    GL.TexCoord2(uv.xMin, uv.yMax); GL.Vertex3(x, y, 0);
                    GL.TexCoord2(uv.xMax, uv.yMax); GL.Vertex3(x + w, y, 0);
                    GL.TexCoord2(uv.xMax, uv.yMin); GL.Vertex3(x + w, y + cellHeight, 0);
                    GL.TexCoord2(uv.xMin, uv.yMin); GL.Vertex3(x, y + cellHeight, 0);
                }
                GL.End();
                return true;
            }
            catch (Exception e)
            {
                if (!failed) Log.Exception("RPM Unicode rendering", e);
                failed = true;
                return true;
            }
            finally { GL.PopMatrix(); RenderTexture.active = previous; }
        }

        void OnDestroy()
        {
            if (material != null) UnityEngine.Object.Destroy(material);
            if (atlas != null) UnityEngine.Object.Destroy(atlas);
        }
    }
}
