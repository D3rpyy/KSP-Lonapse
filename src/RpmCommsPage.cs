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
        // index of the button in the monitor's globalButtons that starts or stops the countdown, -1 for none.
        // On the stock RPM MFD and the ALCOR MFDs 2 is ENTER.
        [KSPField] public int countdownButton = -1;
        [KSPField] public string countdownButtonName = "ENTER";
        // buttons to scroll the log: up to older lines, down to newer ones. -1 for none. 0 and 1 are UP and DOWN.
        [KSPField] public int scrollUpButton = -1;
        [KSPField] public int scrollDownButton = -1;

        private readonly RpmLine[] lines = new RpmLine[Subtitles.LogCap];
        private string response = string.Empty;
        private string displayResponse = string.Empty;
        private RpmUnicodeOverlay unicodeOverlay;
        private int lastVersion = -1, lastLive = -1, lastW = -1, lastH = -1, lastFlags = -1, lastCount = -1;
        private string footerStart, footerStop;
        private int scroll;            // how many of the newest lines are hidden below, 0 shows the newest
        private int lastAdded = -1, lastScroll = -1;
        private bool olderHidden;      // the last page left older lines out above, so UP has something to show
        private Subtitles lastSource;
        private bool failed;

        public string ShowComms(int width, int height)
        {
            if (failed) return displayResponse;
            try
            {
                int monitorWidth = width, monitorHeight = height;
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
                    if (lastAdded >= 0 && ReferenceEquals(subs, lastSource)) scroll = RpmPageText.KeepPlace(scroll, subs.LogAdded - lastAdded, count);
                    lastAdded = subs.LogAdded;
                }
                scroll = RpmPageText.ClampScroll(scroll, count);
                int cd = CountdownState(host);
                int version = subs != null ? subs.LogVersion : -1;
                if (version == lastVersion && live == lastLive && flags == lastFlags && width == lastW
                    && height == lastH && cd == lastCount && scroll == lastScroll && ReferenceEquals(subs, lastSource))
                    return displayResponse;

                lastVersion = version; lastLive = live; lastFlags = flags; lastW = width; lastH = height; lastCount = cd;
                lastScroll = scroll;
                lastSource = subs;
                if (footerStart == null)
                {
                    footerStart = "Start countdown (" + countdownButtonName + ")";
                    footerStop = "Stop countdown (" + countdownButtonName + ")";
                }
                string footer = cd == 2 ? footerStop : cd == 1 ? footerStart : null;
                if (scroll > 0) footer = scroll + (scroll == 1 ? " newer line" : " newer lines") + " below (DOWN)";
                // scrolled: the hidden newest lines are simply left out, so the line above them sits at the bottom
                string page = flags == 1
                    ? RpmPageText.Off(width, pageTitle)
                    : RpmPageText.Build(lines, count - scroll, width, height, pageTitle, (flags & 4) != 0, (flags & 8) != 0, footer,
                                        out olderHidden);
                if (flags == 1) olderHidden = false;
                response = RpmPageText.Pad(page, topRows);
                displayResponse = unicodeOverlay != null ? unicodeOverlay.SetPage(response, monitorWidth, monitorHeight) : response;
            }
            catch (Exception e)
            {
                // once, so a broken page never fills the log every refresh
                failed = true;
                Log.Exception("RPM comms page", e);
            }
            return displayResponse;
        }

        public void HandlerReferences(UnityEngine.MonoBehaviour pageHandler, UnityEngine.MonoBehaviour backgroundHandler)
        {
            var overlay = backgroundHandler as RpmUnicodeOverlay;
            if (ReferenceEquals(overlay, unicodeOverlay)) return;
            unicodeOverlay = overlay;
            lastVersion = -999;
        }

        /// <summary>RPM calls this for a button press while the page is shown (buttonClickMethod in the patch).</summary>
        public void ButtonProcessor(int buttonID)
        {
            if (buttonID < 0) return;
            Log.Info("RPM comms page: button " + buttonID);
            // up only while older lines are left out above, so the page never empties at the top of the log
            if (buttonID == scrollUpButton) { if (olderHidden) { scroll++; olderHidden = false; } return; }
            if (buttonID == scrollDownButton) { if (scroll > 0) scroll--; return; }
            if (buttonID != countdownButton) return;
            try
            {
                LonapseAddon host = LonapseAddon.Instance;
                if (host == null) return;
                Log.Info("Countdown button on an RPM screen");
                host.ToggleCountdown();
            }
            catch (Exception e) { Log.Exception("RPM countdown button", e); }
        }

        /// <summary>RPM calls this when the page is shown or left (pageActiveMethod). Back on the page: the newest lines.</summary>
        public void PageActive(bool active, int pageNumber)
        {
            if (active) scroll = 0;
        }

        // 0 no hint, 1 the countdown can start, 2 it is running
        private int CountdownState(LonapseAddon host)
        {
            if (countdownButton < 0 || host == null || host.countdown == null) return 0;
            if (host.countdown.Running) return 2;
            return host.countdown.WhyNot(FlightGlobals.ActiveVessel) == null ? 1 : 0;
        }
    }
}
