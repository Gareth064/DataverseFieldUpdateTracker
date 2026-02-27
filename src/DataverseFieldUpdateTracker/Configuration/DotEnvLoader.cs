namespace DataverseFieldUpdateTracker.Configuration;

/// <summary>
/// Simple .env file loader that sets environment variables.
/// Replaces the python-dotenv dependency without requiring an external NuGet package.
/// Reads KEY=VALUE pairs, strips quotes, ignores comment lines (#) and blank lines.
/// </summary>
public static class DotEnvLoader
{
    /// <summary>
    /// Loads the .env file from the current directory (or the provided path)
    /// and sets each entry as an environment variable if not already set.
    /// Silently no-ops if the file does not exist.
    /// </summary>
    public static void Load(string path = ".env")
    {
        if (!File.Exists(path)) return;

        foreach (var rawLine in File.ReadAllLines(path))
        {
            var line = rawLine.Trim();

            // Skip blank lines and comments
            if (string.IsNullOrEmpty(line) || line.StartsWith('#'))
                continue;

            var equalsIndex = line.IndexOf('=');
            if (equalsIndex < 1) continue;

            var key   = line[..equalsIndex].Trim();
            var value = line[(equalsIndex + 1)..].Trim();

            // Strip surrounding single or double quotes
            if (value.Length >= 2 &&
                ((value.StartsWith('"')  && value.EndsWith('"')) ||
                 (value.StartsWith('\'') && value.EndsWith('\''))))
            {
                value = value[1..^1];
            }

            // Only set if not already present in the environment
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(key)))
                Environment.SetEnvironmentVariable(key, value);
        }
    }
}
