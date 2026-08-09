using System.Runtime.InteropServices;
using PowerHelper.Abstractions;
using PowerHelper.Core;

namespace PowerHelper.Windows;

/// <summary>
/// Reads/sets the primary display's refresh rate via the standard Win32 display-settings
/// API (not a vendor-specific mechanism - EnumDisplaySettingsEx/ChangeDisplaySettingsEx are
/// what Windows' own Display Settings page uses). Only targets the primary display; if an
/// external monitor is ever set as primary this would throttle that instead of the internal
/// panel, since telling internal from external reliably needs the newer CCD/QueryDisplayConfig
/// API, which is more P/Invoke surface than this feature currently justifies.
/// </summary>
public sealed class WindowsRefreshRateController : IRefreshRateController
{
    private const int EnumCurrentSettings = -1;
    private const int DispChangeSuccessful = 0;
    private const int NativeRefreshRateFallback = 60;

    public WindowsRefreshRateController()
    {
        var supported = GetSupportedFrequencies();

        NativeHertz = supported.Length > 0 ? supported[^1] : NativeRefreshRateFallback;

        // One reported mode means there is nothing to throttle to. Reporting that up front
        // is the difference between a disabled switch that explains itself and one that
        // looks broken when flipping it does nothing.
        Support = supported.Length > 1
            ? CapabilitySupport.Supported
            : CapabilitySupport.Unavailable("Not available — your display only reports a single refresh rate.");
    }

    public CapabilitySupport Support { get; }

    public int NativeHertz { get; }

    public bool ThrottleToLowRate() => TrySetFrequency(PowerHelperEngine.ThrottledRefreshRate);

    public bool RestoreNativeRate() => TrySetFrequency(NativeHertz);

    private static int[] GetSupportedFrequencies()
    {
        var current = CreateDevMode();
        if (!EnumDisplaySettingsEx(null, EnumCurrentSettings, ref current, 0))
        {
            return [];
        }

        var frequencies = new SortedSet<int>();
        var mode = CreateDevMode();
        var i = 0;
        while (EnumDisplaySettingsEx(null, i++, ref mode, 0))
        {
            if (Matches(mode, current))
            {
                frequencies.Add(mode.dmDisplayFrequency);
            }
        }

        return [.. frequencies];
    }

    private static bool TrySetFrequency(int hertz)
    {
        var current = CreateDevMode();
        if (!EnumDisplaySettingsEx(null, EnumCurrentSettings, ref current, 0))
        {
            return false;
        }

        if (current.dmDisplayFrequency == hertz)
        {
            return true;
        }

        var mode = CreateDevMode();
        var i = 0;
        while (EnumDisplaySettingsEx(null, i++, ref mode, 0))
        {
            if (Matches(mode, current) && mode.dmDisplayFrequency == hertz)
            {
                // dwFlags=0 applies the mode to the current session only (no CDS_UPDATEREGISTRY),
                // so it reverts to the user's saved default on reboot rather than overwriting it.
                return ChangeDisplaySettingsEx(null, ref mode, IntPtr.Zero, 0, IntPtr.Zero) == DispChangeSuccessful;
            }
        }

        return false;
    }

    private static bool Matches(DEVMODE a, DEVMODE b) =>
        a.dmPelsWidth == b.dmPelsWidth && a.dmPelsHeight == b.dmPelsHeight && a.dmBitsPerPel == b.dmBitsPerPel;

    private static DEVMODE CreateDevMode()
    {
        var mode = new DEVMODE();
        mode.dmSize = (short)Marshal.SizeOf<DEVMODE>();
        return mode;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct DEVMODE
    {
        private const int CCHDEVICENAME = 32;
        private const int CCHFORMNAME = 32;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCHDEVICENAME)]
        public string dmDeviceName;
        public short dmSpecVersion;
        public short dmDriverVersion;
        public short dmSize;
        public short dmDriverExtra;
        public uint dmFields;
        public int dmPositionX;
        public int dmPositionY;
        public int dmDisplayOrientation;
        public int dmDisplayFixedOutput;
        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCHFORMNAME)]
        public string dmFormName;
        public short dmLogPixels;
        public int dmBitsPerPel;
        public int dmPelsWidth;
        public int dmPelsHeight;
        public int dmDisplayFlags;
        public int dmDisplayFrequency;
        public int dmICMMethod;
        public int dmICMIntent;
        public int dmMediaType;
        public int dmDitherType;
        public int dmReserved1;
        public int dmReserved2;
        public int dmPanningWidth;
        public int dmPanningHeight;
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool EnumDisplaySettingsEx(string? lpszDeviceName, int iModeNum, ref DEVMODE lpDevMode, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int ChangeDisplaySettingsEx(string? lpszDeviceName, ref DEVMODE lpDevMode, IntPtr hwnd, uint dwflags, IntPtr lParam);
}
