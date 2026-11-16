using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mullion.Core;

/// <summary>Reads and writes <see cref="Settings"/> as a JSON file.</summary>
public static class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>
    /// Loads settings, falling back to defaults when the file is missing. A file that
    /// cannot be parsed is kept as "*.bad" so nothing the user wrote is lost.
    /// </summary>
    public static Settings Load(string path)
    {
        Settings? settings = null;

        if (File.Exists(path))
        {
            try
            {
                settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(path), Options);
            }
            catch (JsonException)
            {
                TryKeepBadFile(path);
            }
        }

        settings ??= new Settings();
        settings.Normalize();
        return settings;
    }

    /// <summary>Writes settings through a temporary file so a crash cannot leave a half-written file.</summary>
    public static void Save(string path, Settings settings)
    {
        settings.Normalize();
        string json = JsonSerializer.Serialize(settings, Options);
        string temp = path + ".tmp";
        File.WriteAllText(temp, json);
        File.Move(temp, path, overwrite: true);
    }

    private static void TryKeepBadFile(string path)
    {
        try
        {
            File.Move(path, path + ".bad", overwrite: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
