using Microsoft.Win32;

namespace Scrunch;

internal sealed record StartupState(bool Available, bool Enabled, string Message, bool BlockedByWindows = false);

internal static class StartupRegistration
{
    internal const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    internal const string ValueName = "Scrunch";
    private const string InstallKey = @"Software\Scrunch";
    private const string ApprovalKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    internal static string Command(string executable) => $"\"{executable}\" --startup";

    private static bool IsInstalled(string executable)
    {
        using var key = Registry.CurrentUser.OpenSubKey(InstallKey);
        return key?.GetValue("InstallLocation") is string directory &&
            string.Equals(Path.GetFullPath(executable), Path.Combine(Path.GetFullPath(directory), "Scrunch.exe"), StringComparison.OrdinalIgnoreCase);
    }

    internal static StartupState Read()
    {
        try
        {
            var executable = Environment.ProcessPath!;
            if (!IsInstalled(executable)) return new(false, false, "Install Scrunch to enable start at sign-in.");
            using var run = Registry.CurrentUser.OpenSubKey(RunKey);
            bool registered = string.Equals(run?.GetValue(ValueName) as string, Command(executable), StringComparison.OrdinalIgnoreCase);
            using var approval = Registry.CurrentUser.OpenSubKey(ApprovalKey);
            // Task Manager can disable an existing Run entry independently. Read its
            // state; never rewrite Explorer's private approval data to override a user.
            bool disabledByWindows = approval?.GetValue(ValueName) is byte[] bytes && bytes.Length > 0 && bytes[0] is 3 or 7;
            return new(!disabledByWindows || !registered, registered && !disabledByWindows, disabledByWindows
                ? registered
                    ? "Disabled in Windows Startup Apps. Enable Scrunch there, or remove its entry below."
                    : "Windows previously disabled Scrunch. Turn this on to register it, then enable it in Windows Startup Apps."
                : "Restores your notes quietly, with Scrunch in the tray.", disabledByWindows && registered);
        }
        catch (Exception error) when (error is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        { return new(false, false, "Could not read Windows startup settings. " + error.Message); }
    }

    internal static void SetEnabled(bool enabled)
    {
        var executable = Environment.ProcessPath!;
        if (!IsInstalled(executable)) throw new InvalidOperationException("Install Scrunch before changing start at sign-in.");
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
        if (enabled) key.SetValue(ValueName, Command(executable), RegistryValueKind.String);
        else key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
