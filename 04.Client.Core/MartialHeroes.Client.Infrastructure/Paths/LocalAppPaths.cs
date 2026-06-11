namespace MartialHeroes.Client.Infrastructure.Paths;

/// <summary>
/// Resolves well-known paths under the user's application-data directory.
/// <para>
/// All paths are rooted at <c>%APPDATA%\MartialHeroes</c> on Windows and the
/// platform equivalent on other OSes via
/// <see cref="Environment.SpecialFolder.ApplicationData"/>.
/// Never hard-codes drive letters or OS-specific separators.
/// </para>
/// </summary>
public static class LocalAppPaths
{
    private const string AppFolderName = "MartialHeroes";

    /// <summary>
    /// The root application-data directory for Martial Heroes.
    /// Created on first access if it does not exist.
    /// </summary>
    public static string AppDataRoot
    {
        get
        {
            var root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                AppFolderName);
            Directory.CreateDirectory(root);
            return root;
        }
    }

    /// <summary>Full path to the settings SQLite database file.</summary>
    public static string SettingsDbPath =>
        Path.Combine(AppDataRoot, "settings.db");

    /// <summary>Full path to the offline item-cache SQLite database file.</summary>
    public static string ItemCacheDbPath =>
        Path.Combine(AppDataRoot, "item_cache.db");

    /// <summary>
    /// Directory where the player's macro files (<c>*.mhm</c>) are stored.
    /// Created on first access if it does not exist.
    /// </summary>
    public static string MacrosDirectory
    {
        get
        {
            var dir = Path.Combine(AppDataRoot, "macros");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    /// <summary>
    /// Returns a <c>Microsoft.Data.Sqlite</c> connection string for the given
    /// file path. Pass <c>:memory:</c> for in-memory (test) databases.
    /// </summary>
    public static string ConnectionStringFor(string dbPath) =>
        $"Data Source={dbPath}";
}