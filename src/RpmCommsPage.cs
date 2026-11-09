// Comms page for RasterPropMonitor screens: the Lonapse subtitles as a radio log on an IVA monitor.
// No reference to RPM at all. RPM finds this module by its class name through a PAGEHANDLER block
// (Patches/Lonapse_RPM.cfg, only with RPM installed) and calls ShowComms by reflection.
// It needs the signature string (int, int): screen width and height in characters.
// Without RPM nothing ever creates this module.
// The text is only rebuilt when a line comes or goes, or a subtitle leaves the screen and turns dim.
using System;

namespace Lonapse
{
    public class RpmCommsPage : InternalModule
    {
        // from the PAGEHANDLER block, empty gives "----- Comms -----" across the screen
        [KSPField] public string pageTitle = string.Empty;
        // for monitors with a text overlay (ASET ALCOR): the size of the free area and the overlay rows above it.
        // 0 uses the size RPM passes in.
        [KSPField] public int screenWidth = 0;
        [KSPField] public int screenHeight = 0;
        [KSPField] public int topRows = 0;

        private readonly RpmLine[] lines = new RpmLine[Subtitles.LogCap];
        private string response = string.Empty;
        private int lastVersion = -1, lastLive = -1, lastW = -1, lastH = -1, lastFlags = -1;
        private Subtitles lastSource;
        private bool failed;

        public string ShowComms(int width, int height)
        {
            if (failed) return response;
            try
            {
                if (screenWidth > 0) width = Math.Min(width, screenWidth);
                if (screenHeight > 0) height = Math.Min(height, screenHeight);
                LonapseAddon host = LonapseAddon.Instance;
                Settings s = host != null ? host.settings : null;
                Subtitles subs = host != null ? host.subtitles : null;
                int flags = s == null || subs == null ? 0 : !s.subtitles ? 1 : 2 + (s.subtitleSpeaker ? 4 : 0) + (s.subtitleColors ? 8 : 0);

                int count = 0, live = 0;
                if ((flags & 2) != 0)
                {
                    count = subs.CopyLog(lines);
                    for (int i = 0; i < count; i++) if (lines[i].live) live |= 1 << i;
                }
                int version = subs != null ? subs.LogVersion : -1;
                if (version == lastVersion && live == lastLive && flags == lastFlags && width == lastW
                    && height == lastH && ReferenceEquals(subs, lastSource))
                    return response;

                lastVersion = version; lastLive = live; lastFlags = flags; lastW = width; lastH = height;
                lastSource = subs;
                string page = flags == 1
                    ? RpmPageText.Off(width, pageTitle)
                    : RpmPageText.Build(lines, count, width, height, pageTitle, (flags & 4) != 0, (flags & 8) != 0);
                response = RpmPageText.Pad(page, topRows);
            }
            catch (Exception e)
            {
                // once, so a broken page never fills the log every refresh
                failed = true;
                Log.Exception("RPM comms page", e);
            }
            return response;
        }
    }
}
