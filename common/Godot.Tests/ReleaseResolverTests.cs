using System;
using System.Linq;
using Xunit;

public sealed class ReleaseResolverTests {
    [Fact]
    public void ParsesGodotTagsFromJsonProperties() {
        const string json = """
            [
              { "name": "ignored", "tag_name": "4.3-stable" },
              { "tag_name": "4.3.2-stable" },
              { "tag_name": "4.4-rc1" }
            ]
            """;

        var tags = ReleaseResolver.ParseGodotTags(json);

        Assert.Equal(new[] { "4.3-stable", "4.3.2-stable", "4.4-rc1" }, tags);
    }

    [Fact]
    public void FiltersUnstableAndUnselectedGodotReleases() {
        var selector = VersionSelector.Parse("VERSION", "4.3");

        var releases = ReleaseResolver.ParseGodotReleases(
            new[] { "4.3-stable", "4.3.2-stable", "4.3.3-rc1", "4.4-stable", "not-a-version" },
            selector);

        Assert.Equal(new[] { new Version(4, 3, 0), new Version(4, 3, 2) }, releases.Select(release => release.version));
    }

    [Fact]
    public void FindsDistinctMatchingBlenderSeries() {
        const string html = "<a>Blender4.2/</a><a>Blender4.2/</a><a>Blender4.3/</a><a>Blender5.0/</a>";
        var selector = VersionSelector.Parse("VERSION", "4");

        var series = ReleaseResolver.ParseBlenderSeries(html, selector);

        Assert.Equal(new[] { "Blender4.2/", "Blender4.3/" }, series);
    }

    [Theory]
    [InlineData(false, "blender-4.2.1-linux-x64.tar.xz", "4.2.1")]
    [InlineData(true, "blender-4.2.3-windows-x64.zip", "4.2.3")]
    public void ParsesPlatformSpecificBlenderReleases(bool windows, string listing, string expected) {
        var platform = CreatePlatform(windows);
        var selector = VersionSelector.Parse("VERSION", "4.2");

        var release = Assert.Single(ReleaseResolver.ParseBlenderReleases(listing, "Blender4.2/", selector, platform));

        Assert.Equal(new Version(expected), release.version);
    }

    static PlatformInfo CreatePlatform(bool windows) {
        return new PlatformInfo(windows, "godot", "templates", "blender", "state", "data", "settings");
    }
}
