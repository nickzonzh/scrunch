namespace Scrunch;

internal static class AppPaths
{
    // A process-only override supports testing the exact shipping binary without
    // opening personal notes. Never set this as a user/machine environment variable.
    internal static string? OverrideDirectory => Environment.GetEnvironmentVariable("SCRUNCH_DATA_DIRECTORY") is { Length: > 0 } path
        ? Path.GetFullPath(path) : null;
    internal static string DataDirectory
    {
        get
        {
            if (OverrideDirectory is { } isolated) return isolated;
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string destination = Path.Combine(local, "Scrunch");
            // Compatibility identifier only: never rename this lookup.
            StorageMigration.Migrate(Path.Combine(local, "Noot"), destination);
            return destination;
        }
    }
    internal static string InstanceKey => OverrideDirectory is { } path
        ? "Scrunch.Test." + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(path.TrimEnd(Path.DirectorySeparatorChar).ToUpperInvariant())))
        : "Scrunch.Resident.v1";
}
