using System.Runtime.InteropServices;

namespace PowerHelper.Services;

/// <summary>
/// Reads/sets the primary display's refresh rate via the standard Win32 display-settings
/// API (not a Lenovo-specific mechanism - EnumDisplaySettingsEx/ChangeDisplaySettingsEx are
/// what Windows' own Display Settings page uses). Only targets the primary display; if an
/// external monitor is ever set as primary this would throttle that instead of the internal
/// panel, since telling internal vs external apart reliably needs the newer CCD/QueryDisplayConfig
/// API, which is more P/Invoke surface than this feature currently justifies.
/// </summary>
public sealed class RefreshRateService
{
    private const int EnumCurrentSettings = -1;
    private const uint DmPelsWidth = 0x80000;
    private const uint DmPelsHeight = 0x100000;
    private const uint DmBitsPerPel = 0x40000;
    private const uint DmDisplayFrequency = 0x400000;
    private const int DispChangeSuccessful = 0;

    public int? GetCurrentFrequency()
    {
        var mode = CreateDevMode();
        return EnumDisplaySettingsEx(null, EnumCurrentSettings, ref mode, 0) ? mode.dmDisplayFrequency : null;
    }

    public int[] GetSupportedFrequencies()
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

    public bool TrySetFrequency(int hertz)
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
