using System.Security.Cryptography;
using Mullion.Core;

namespace Mullion.Core.Tests;

public sealed class UpdateTests : IDisposable
{
    private const string Download = "https://github.com/MRACHINI/Mullion/releases/download/v0.2.0/";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mullion-update-" + Guid.NewGuid().ToString("N"));

    public UpdateTests()
    {
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        Directory.Delete(_dir, recursive: true);
    }

    private static string Release(string tag = "v0.2.0", string flags = "", string digest = "") => $$"""
        {
          "tag_name": "{{tag}}",
          "html_url": "https://github.com/MRACHINI/Mullion/releases/tag/{{tag}}",
          {{flags}}
          "assets": [
            { "name": "Mullion.exe", "size": 500000, "browser_download_url": "{{Download}}Mullion.exe"{{digest}} },
            { "name": "Mullion-full.exe", "size": 66000000, "browser_download_url": "{{Download}}Mullion-full.exe" },
            { "name": "notes.txt", "size": 12 }
          ]
        }
        """;

    [Fact]
    public void A_release_is_read_with_its_version_and_files()
    {
        var release = UpdateFeed.Parse(Release());

        Assert.NotNull(release);
        Assert.Equal(new Version(0, 2, 0), release.Version);
        Assert.Equal("v0.2.0", release.Tag);
        Assert.Equal(["Mullion.exe", "Mullion-full.exe"], release.Assets.Select(asset => asset.Name));
        Assert.Equal(500000, release.Assets[0].Size);
        Assert.Null(release.Assets[0].Sha256);
    }

    [Fact]
    public void The_checksum_github_gives_is_kept()
    {
        var release = UpdateFeed.Parse(Release(digest: ", \"digest\": \"sha256:ABCDEF\""));

        Assert.Equal("ABCDEF", release!.Assets[0].Sha256);
    }

    [Theory]
    [InlineData("\"draft\": true,")]
    [InlineData("\"prerelease\": true,")]
    public void Drafts_and_pre_releases_are_not_updates(string flags)
    {
        Assert.Null(UpdateFeed.Parse(Release(flags: flags)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{ \"message\": \"Not Found\" }")]
    public void Anything_else_is_not_a_release(string json)
    {
        Assert.Null(UpdateFeed.Parse(json));
    }

    [Theory]
    [InlineData("v1.2.3", 1, 2, 3)]
    [InlineData("1.2.3", 1, 2, 3)]
    [InlineData("V2.0", 2, 0, 0)]
    [InlineData(" v0.10.1 ", 0, 10, 1)]
    public void Tags_are_read_as_versions(string tag, int major, int minor, int build)
    {
        Assert.True(UpdateFeed.TryParseVersion(tag, out var version));
        Assert.Equal(new Version(major, minor, build), version);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("v")]
    [InlineData("latest")]
    [InlineData("v1.2.3-beta")]
    [InlineData("v1")]
    public void Other_tags_are_not_versions(string? tag)
    {
        Assert.False(UpdateFeed.TryParseVersion(tag, out _));
    }

    [Theory]
    [InlineData("0.2.0", "0.1.0", true)]
    [InlineData("0.1.1", "0.1.0", true)]
    [InlineData("0.10.0", "0.9.0", true)]
    [InlineData("0.1.0", "0.1.0", false)]
    [InlineData("0.1", "0.1.0", false)]
    [InlineData("0.1.0", "0.1.0.0", false)]
    [InlineData("0.1.0", "0.2.0", false)]
    public void Only_a_later_version_is_newer(string published, string running, bool expected)
    {
        Assert.Equal(expected, UpdateFeed.IsNewer(Version.Parse(published), Version.Parse(running)));
    }

    [Fact]
    public void Each_kind_of_executable_gets_its_own_file()
    {
        var release = UpdateFeed.Parse(Release())!;

        Assert.Equal("Mullion.exe", UpdateFeed.PickAsset(release, selfContained: false)!.Name);
        Assert.Equal("Mullion-full.exe", UpdateFeed.PickAsset(release, selfContained: true)!.Name);
    }

    [Fact]
    public void A_release_without_the_file_offers_nothing()
    {
        var release = new ReleaseInfo(new Version(0, 2, 0), "v0.2.0", "", [new ReleaseAsset("Mullion.exe", Download + "Mullion.exe", 1, null)]);

        Assert.Null(UpdateFeed.PickAsset(release, selfContained: true));
    }

    [Theory]
    [InlineData("https://github.com/MRACHINI/Mullion/releases/download/v0.2.0/Mullion.exe", true)]
    [InlineData("https://GitHub.com/mrachini/mullion/releases/download/v0.2.0/Mullion.exe", true)]
    [InlineData("http://github.com/MRACHINI/Mullion/releases/download/v0.2.0/Mullion.exe", false)]
    [InlineData("https://github.com/someone-else/Mullion/releases/download/v0.2.0/Mullion.exe", false)]
    [InlineData("https://github.com.example.org/MRACHINI/Mullion/releases/download/v0.2.0/Mullion.exe", false)]
    [InlineData("https://example.org/MRACHINI/Mullion/releases/download/v0.2.0/Mullion.exe", false)]
    [InlineData("Mullion.exe", false)]
    public void Only_mullions_own_releases_are_downloaded(string url, bool expected)
    {
        Assert.Equal(expected, UpdateFeed.IsTrustedDownload(url));

        var release = new ReleaseInfo(new Version(0, 2, 0), "v0.2.0", "", [new ReleaseAsset("Mullion.exe", url, 1, null)]);
        Assert.Equal(expected, UpdateFeed.PickAsset(release, selfContained: false) is not null);
    }

    [Fact]
    public void A_download_must_be_the_published_size_and_a_program()
    {
        string file = Path.Combine(_dir, "download");
        File.WriteAllBytes(file, [(byte)'M', (byte)'Z', 1, 2, 3]);

        Assert.True(UpdateInstaller.Matches(file, new ReleaseAsset("Mullion.exe", "", 5, null)));
        Assert.False(UpdateInstaller.Matches(file, new ReleaseAsset("Mullion.exe", "", 6, null)));

        File.WriteAllText(file, "<html>");
        Assert.False(UpdateInstaller.Matches(file, new ReleaseAsset("Mullion.exe", "", 6, null)));
        Assert.False(UpdateInstaller.Matches(Path.Combine(_dir, "missing"), new ReleaseAsset("Mullion.exe", "", 6, null)));
    }

    [Fact]
    public void A_download_must_have_the_published_checksum()
    {
        string file = Path.Combine(_dir, "download");
        byte[] content = [(byte)'M', (byte)'Z', 1, 2, 3];
        File.WriteAllBytes(file, content);
        string checksum = Convert.ToHexString(SHA256.HashData(content));

        Assert.True(UpdateInstaller.Matches(file, new ReleaseAsset("Mullion.exe", "", 5, checksum.ToLowerInvariant())));
        Assert.False(UpdateInstaller.Matches(file, new ReleaseAsset("Mullion.exe", "", 5, new string('0', 64))));
    }

    [Fact]
    public void Swapping_puts_the_new_file_in_place_and_keeps_the_old_one_aside()
    {
        string exe = Path.Combine(_dir, "Mullion.exe");
        File.WriteAllText(exe, "old");
        File.WriteAllText(UpdateInstaller.OldPathFor(exe), "older, left from last time");
        File.WriteAllText(UpdateInstaller.DownloadPathFor(exe), "new");

        UpdateInstaller.Swap(exe, UpdateInstaller.DownloadPathFor(exe));

        Assert.Equal("new", File.ReadAllText(exe));
        Assert.Equal("old", File.ReadAllText(UpdateInstaller.OldPathFor(exe)));
        Assert.False(File.Exists(UpdateInstaller.DownloadPathFor(exe)));
    }

    [Fact]
    public void A_swap_that_fails_leaves_the_executable_where_it_was()
    {
        string exe = Path.Combine(_dir, "Mullion.exe");
        File.WriteAllText(exe, "old");

        Assert.ThrowsAny<IOException>(() => UpdateInstaller.Swap(exe, Path.Combine(_dir, "missing")));

        Assert.Equal("old", File.ReadAllText(exe));
    }

    [Fact]
    public void Cleaning_up_removes_what_an_update_left_behind()
    {
        string exe = Path.Combine(_dir, "Mullion.exe");
        File.WriteAllText(exe, "current");
        File.WriteAllText(UpdateInstaller.OldPathFor(exe), "old");
        File.WriteAllText(UpdateInstaller.DownloadPathFor(exe), "half a download");

        UpdateInstaller.CleanUp(exe);
        UpdateInstaller.CleanUp(exe);

        Assert.Equal(["Mullion.exe"], Directory.GetFiles(_dir).Select(Path.GetFileName));
    }
}
