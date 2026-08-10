using System.Diagnostics;

namespace PowerHelper.Windows;

/// <summary>
/// Runs a short-lived CLI helper (pnputil, schtasks, ...) to completion and reports whether
/// it succeeded. Shared because draining stdout/stderr before waiting is not optional: leaving
/// them unread has caused later calls in the same process to silently stop taking effect (see
/// WindowsPowerProfileController, where this was diagnosed against powercfg.exe).
/// </summary>
internal static class ProcessRunner
{
    public static bool RunAndWait(ProcessStartInfo startInfo)
    {
        startInfo.UseShellExecute = false;
        startInfo.CreateNoWindow = true;
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;

        using var process = Process.Start(startInfo);
        if (process is null)
        {
            return false;
        }

        process.StandardOutput.ReadToEnd();
        process.StandardError.ReadToEnd();
        process.WaitForExit();
        return process.ExitCode == 0;
    }
}
