using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using PowerHelper.Abstractions;

namespace PowerHelper.Windows;

/// <summary>
/// Controls Lenovo's battery conservation mode through the Energy Management driver
/// (\.\EnergyDrv) using the same private IOCTL Lenovo Vantage uses.
/// </summary>
public sealed class WindowsBatteryConservationController : IBatteryConservationController
{
    private const string DevicePath = @"\\.\EnergyDrv";
    private const uint IoctlBatteryChargeMode = 0x831020F8;
    private const uint QueryModeCode = 0xFF;
    private const uint SetNormalCode = 0x05;
    private const uint SetConservationCode = 0x03;
    private const uint PrepareCode = 0x08;

    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint OpenExisting = 3;

    public WindowsBatteryConservationController()
    {
        Support = DetectSupport();
    }

    public CapabilitySupport Support { get; }

    public bool IsEnabled() => TryReadConservationMode(out var enabled) && enabled;

    public bool SetEnabled(bool enabled)
    {
        if (!Support.IsSupported || !TryOpenDriver(out var handle))
        {
            return false;
        }

        using (handle)
        {
            // Lenovo Legion Toolkit sends a two-step sequence for reliable transitions.
            return enabled
                ? SendCode(handle, PrepareCode) && SendCode(handle, SetConservationCode)
                : SendCode(handle, SetNormalCode) && SendCode(handle, PrepareCode);
        }
    }

    private static CapabilitySupport DetectSupport()
    {
        if (!TryOpenDriver(out var handle))
        {
            return CapabilitySupport.Unavailable(
                @"Not available — Lenovo's Energy Management driver (\.\EnergyDrv) isn't installed.");
        }

        using (handle)
        {
            return SendCode(handle, QueryModeCode, out _)
                ? CapabilitySupport.Supported
                : CapabilitySupport.Unavailable(
                    @"Not available — Lenovo's Energy Management driver is present, but this model doesn't expose battery conservation mode.");
        }
    }

    private static bool TryReadConservationMode(out bool enabled)
    {
        enabled = false;

        if (!TryOpenDriver(out var handle))
        {
            return false;
        }

        using (handle)
        {
            if (!SendCode(handle, QueryModeCode, out var rawState))
            {
                return false;
            }

            enabled = (rawState & 0x20) != 0;
            return true;
        }
    }

    private static bool TryOpenDriver(out SafeFileHandle handle)
    {
        handle = CreateFile(
            DevicePath,
            GenericRead | GenericWrite,
            FileShareRead | FileShareWrite,
            IntPtr.Zero,
            OpenExisting,
            0,
            IntPtr.Zero);

        return !handle.IsInvalid;
    }

    private static bool SendCode(SafeFileHandle handle, uint code) => SendCode(handle, code, out _);

    private static bool SendCode(SafeFileHandle handle, uint code, out uint response)
    {
        response = 0;
        uint bytesReturned;
        return DeviceIoControl(
            handle,
            IoctlBatteryChargeMode,
            ref code,
            (uint)Marshal.SizeOf<uint>(),
            out response,
            (uint)Marshal.SizeOf<uint>(),
            out bytesReturned,
            IntPtr.Zero);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(
        SafeFileHandle hDevice,
        uint dwIoControlCode,
        ref uint lpInBuffer,
        uint nInBufferSize,
        out uint lpOutBuffer,
        uint nOutBufferSize,
        out uint lpBytesReturned,
        IntPtr lpOverlapped);
}
