using System.Security.Cryptography;
using System.Text.Json;

namespace Mullion.Core;

/// <summary>One downloadable file of a published version.</summary>
/// <param name="Name">The file's name, such as "Mullion.exe".</param>
/// <param name="DownloadUrl">Where to download it from.</param>
/// <param name="Size">Its size in bytes.</param>
/// <param name="Sha256">Its SHA-256 checksum in hexadecimal, when GitHub gives one.</param>
public sealed record ReleaseAsset(string Name, string DownloadUrl, long Size, string? Sha256);

/// <summary>A published version of Mullion.</summary>
public sealed record ReleaseInfo(Version Version, string Tag, string PageUrl, IReadOnlyList<ReleaseAsset> Assets);

/// <summary>Where new versions are published, and how to read what is there.</summary>
public static class UpdateFeed
{
    public const string LatestReleaseUrl = "https://api.github.com/repos/MRACHINI/Mullion/releases/latest";
    public const string ReleasesPage = "https://github.com/MRACHINI/Mullion/releases";

    /// <summary>The small executable, which needs the .NET runtime installed.</summary>
    public const string SmallAsset = "Mullion.exe";

    /// <summary>The large executable, which carries the runtime inside it.</summary>
    public const string FullAsset = "Mullion-full.exe";

    private const string DownloadPrefix = "/MRACHINI/Mullion/releases/download/";

    /// <summary>
    /// Reads GitHub's description of a release. Null for anything Mullion should
    /// not update to: a draft, a pre-release, or a tag that is not a version.
    /// </summary>
    public static ReleaseInfo? Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || Flag(root, "draft") || Flag(root, "prerelease"))
            {
                return null;
            }

            string? tag = Text(root, "tag_name");
            if (!TryParseVersion(tag, out var version))
            {
                return null;
            }

            var assets = new List<ReleaseAsset>();
            if (root.TryGetProperty("assets", out var list) && list.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in list.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.Object
                        && Text(item, "name") is { Length: > 0 } name
                        && Text(item, "browser_download_url") is { Length: > 0 } url
                        && item.TryGetProperty("size", out var size)
                        && size.TryGetInt64(out long bytes))
                    {
                        // GitHub writes the checksum as "sha256:<hexadecimal>".
                        string? digest = Text(item, "digest");
                        string? sha256 = digest is not null && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
                            ? digest["sha256:".Length..]
                            : null;
                        assets.Add(new ReleaseAsset(name, url, bytes, sha256));
                    }
                }
            }

            return new ReleaseInfo(version, tag!, Text(root, "html_url") ?? ReleasesPage, assets);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Reads a tag such as "v1.2.3" or "1.2". Tags with anything after the numbers are not versions.</summary>
    public static bool TryParseVersion(string? tag, out Version version)
    {
        version = new Version(0, 0, 0);
        if (string.IsNullOrWhiteSpace(tag))
        {
            return false;
        }

        string text = tag.Trim();
        if (text.StartsWith('v') || text.StartsWith('V'))
        {
            text = text[1..];
        }

        if (text.Length == 0 || text.Any(c => !char.IsAsciiDigit(c) && c != '.') || !Version.TryParse(text, out var parsed))
        {
            return false;
        }

        version = Normalize(parsed);
        return true;
    }

    /// <summary>True when a published version is later than the one running.</summary>
    public static bool IsNewer(Version published, Version running) => Normalize(published) > Normalize(running);

    /// <summary>The file of a release that replaces the running executable, or null when the release has none.</summary>
    /// <param name="release">The published version.</param>
    /// <param name="selfContained">True when the running executable is the large one.</param>
    public static ReleaseAsset? PickAsset(ReleaseInfo release, bool selfContained)
    {
        string name = selfContained ? FullAsset : SmallAsset;
        var asset = release.Assets.FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));
        return asset is not null && IsTrustedDownload(asset.DownloadUrl) ? asset : null;
    }

    /// <summary>True for a download that comes from Mullion's own releases on GitHub, over a secure connection.</summary>
    public static bool IsTrustedDownload(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase)
        && uri.AbsolutePath.StartsWith(DownloadPrefix, StringComparison.OrdinalIgnoreCase);

    // "1.2" and "1.2.0" are the same version; the fourth number is not used.
    private static Version Normalize(Version version) =>
        new(version.Major, version.Minor, Math.Max(0, version.Build));

    private static bool Flag(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}

/// <summary>Puts a downloaded executable in the place of the running one.</summary>
public static class UpdateInstaller
{
    /// <summary>Where the new version is downloaded to, next to the executable.</summary>
    public static string DownloadPathFor(string executable) => executable + ".new";

    /// <summary>Where the replaced version is kept until the new one has started.</summary>
    public static string OldPathFor(string executable) => executable + ".old";

    /// <summary>
    /// True when a downloaded file is the one the release describes: the same size,
    /// the same checksum when there is one, and a Windows program.
    /// </summary>
    public static bool Matches(string path, ReleaseAsset asset)
    {
        try
        {
            using var stream = File.OpenRead(path);
            if (stream.Length != asset.Size || stream.Length < 2 || stream.ReadByte() != 'M' || stream.ReadByte() != 'Z')
            {
                return false;
            }

            if (asset.Sha256 is null)
            {
                return true;
            }

            stream.Position = 0;
            string actual = Convert.ToHexString(SHA256.HashData(stream));
            return string.Equals(actual, asset.Sha256, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Replaces the executable with the downloaded file. Windows lets a running
    /// program be renamed but not overwritten, so the old one is moved aside
    /// first, and moved back if the new one cannot take its place.
    /// </summary>
    public static void Swap(string executable, string downloaded)
    {
        string old = OldPathFor(executable);
        File.Delete(old);
        File.Move(executable, old);

        try
        {
            File.Move(downloaded, executable);
        }
        catch
        {
            File.Move(old, executable);
            throw;
        }
    }

    /// <summary>Removes what an earlier update left behind. Safe to call at any time.</summary>
    public static void CleanUp(string executable)
    {
        foreach (string path in new[] { OldPathFor(executable), DownloadPathFor(executable) })
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Still in use by the copy that is closing; the next start removes it.
            }
        }
    }
}
