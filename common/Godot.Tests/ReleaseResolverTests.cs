using System;
using System.Linq;
using DockerGodot;
using NUnit.Framework;

namespace Godot.Tests;

public sealed class ReleaseResolverTests {
    [Test]
    public void ParsesGodotTagsFromJsonProperties() {
        const string json = """
            [
              { "name": "ignored", "tag_name": "4.3-stable" },
              { "tag_name": "4.3.2-stable" },
              { "tag_name": "4.4-rc1" }
            ]
            """;

        var tags = ReleaseResolver.ParseGodotTags(json);

        Assert.That(tags, Is.EqualTo(new[] { "4.3-stable", "4.3.2-stable", "4.4-rc1" }));
    }

    [Test]
    public void FiltersUnstableAndUnselectedGodotReleases() {
        var selector = VersionSelector.Parse("VERSION", "4.3");

        var releases = ReleaseResolver.ParseGodotReleases(
            new[] { "4.3-stable", "4.3.2-stable", "4.3.3-rc1", "4.4-stable", "not-a-version" },
            selector);

        Assert.That(releases.Select(release => release.version), Is.EqualTo(new[] { new Version(4, 3, 0), new Version(4, 3, 2) }));
    }

    [Test]
    public void FindsDistinctMatchingBlenderSeries() {
        const string html = "<a>Blender4.2/</a><a>Blender4.2/</a><a>Blender4.3/</a><a>Blender5.0/</a>";
        var selector = VersionSelector.Parse("VERSION", "4");

        var series = ReleaseResolver.ParseBlenderSeries(html, selector);

        Assert.That(series, Is.EqualTo(new[] { "Blender4.2/", "Blender4.3/" }));
    }

    [TestCase(false, "blender-4.2.1-linux-x64.tar.xz", "4.2.1")]
    [TestCase(true, "blender-4.2.3-windows-x64.zip", "4.2.3")]
    public void ParsesPlatformSpecificBlenderReleases(bool windows, string listing, string expected) {
        var platform = CreatePlatform(windows);
        var selector = VersionSelector.Parse("VERSION", "4.2");

        var releases = ReleaseResolver.ParseBlenderReleases(listing, "Blender4.2/", selector, platform);
        Assert.That(releases, Has.Count.EqualTo(1));
        var release = releases.Single();

        Assert.That(release.version, Is.EqualTo(new Version(expected)));
    }

    static PlatformInfo CreatePlatform(bool windows) {
        return new PlatformInfo(windows, "godot", "templates", "blender", "state", "data", "settings");
    }
}
