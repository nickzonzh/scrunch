using System.Security.Cryptography;
using System.Text;

namespace Scrunch;

internal static class TrayIdentity
{
    // Explorer binds unsigned notification GUIDs to an executable path. A stable
    // installation-derived GUID survives restarts/upgrades in place and lets a moved
    // portable build register without borrowing another build's tray preference.
    public static Guid ForExecutable(string path) => new(SHA256.HashData(Encoding.UTF8.GetBytes(
        "Scrunch.NotificationIcon.v1|" + Path.GetFullPath(path).ToUpperInvariant())).AsSpan(0, 16));
}
