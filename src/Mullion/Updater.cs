using System.IO;
using System.Net;
using System.Net.Http;
using Mullion.Core;

namespace Mullion;

/// <summary>What a look at GitHub found.</summary>
/// <param name="Available">A version later than the one running, or null.</param>
/// <param name="Error">Why the look failed, in words for the user, or null when it worked.</param>
internal sealed record UpdateCheck(ReleaseInfo? Available, string? Error);

/// <summary>Asks GitHub for the latest published version, and installs it.</summary>
internal sealed class Updater
{
    // Declared before the client below, which names the version in what it sends.
    public static Version CurrentVersion { get; } = typeof(App).Assembly.GetName().Version ?? new Version(0, 0, 0);

    private static readonly HttpClient Http = CreateClient();

    /// <summary>
    /// True when the running executable is the large one that carries the .NET
    /// runtime inside it. The small one is well under a megabyte.
    /// </summary>
    public static bool IsSelfContained
    {
        get
        {
            try
            {
                return Environment.ProcessPath is { } path && new FileInfo(path).Length > 10 * 1024 * 1024;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }
    }

    public async Task<UpdateCheck> CheckAsync()
    {
        try
        {
            using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            using var response = await Http.GetAsync(UpdateFeed.LatestReleaseUrl, cancel.Token).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                // Nothing has been published, or not where everyone can see it.
                return new UpdateCheck(null, "No published version was found on GitHub.");
            }

            if (!response.IsSuccessStatusCode)
            {
                return new UpdateCheck(null, $"GitHub did not answer the question (it said {(int)response.StatusCode}). Try again later.");
            }

            string json = await response.Content.ReadAsStringAsync(cancel.Token).ConfigureAwait(false);
            var release = UpdateFeed.Parse(json);
            return release is not null && UpdateFeed.IsNewer(release.Version, CurrentVersion)
                ? new UpdateCheck(release, null)
                : new UpdateCheck(null, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            Log.Trace($"update check failed: {ex.GetType().Name}: {ex.Message}");
            return new UpdateCheck(null, "GitHub could not be reached. Check your internet connection and try again.");
        }
    }

    /// <summary>
    /// Downloads a version and puts it in the place of the running executable.
    /// The caller restarts Mullion. Returns what went wrong, in words for the
    /// user, or null when the new version is in place.
    /// </summary>
    public async Task<string?> InstallAsync(ReleaseInfo release)
    {
        if (Environment.ProcessPath is not { } executable)
        {
            return "Mullion could not find its own file.";
        }

        var asset = UpdateFeed.PickAsset(release, IsSelfContained);
        if (asset is null)
        {
            return $"Version {release.Version.ToString(3)} does not include the file this copy needs. Download it from the releases page instead.";
        }

        string download = UpdateInstaller.DownloadPathFor(executable);
        try
        {
            using var cancel = new CancellationTokenSource(TimeSpan.FromMinutes(10));
            using (var response = await Http.GetAsync(asset.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancel.Token).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                await using var file = File.Create(download);
                await response.Content.CopyToAsync(file, cancel.Token).ConfigureAwait(false);
            }

            if (!UpdateInstaller.Matches(download, asset))
            {
                File.Delete(download);
                return "The download does not match what was published, so it was not installed.";
            }

            UpdateInstaller.Swap(executable, download);
            return null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            Log.Write("Downloading the new version failed.", ex);
            TryDelete(download);
            return "The new version could not be downloaded. Check your internet connection and try again.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Write("Installing the new version failed.", ex);
            TryDelete(download);
            return "Mullion could not write the new version into its folder. Download it from the releases page instead.";
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient();

        // GitHub refuses requests that do not say what program is asking. Nothing
        // about the user or the PC is sent.
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"Mullion/{CurrentVersion.ToString(3)}");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }
}
