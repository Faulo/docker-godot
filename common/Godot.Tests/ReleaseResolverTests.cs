using System;
using System.Linq;
using NUnit.Framework;

namespace Godot.Tests;

public sealed class ReleaseResolverTests {
    [Test]
    public void ParsesDistinctStableGodotTagsFromArchivePage() {
        const string html = """
                            <a href="/download/archive/4.3-stable/">4.3-stable</a>
                            <a href="/download/archive/4.3.2-stable/">4.3.2-stable</a>
                            <a href="/download/archive/4.4-rc1/">4.4-rc1</a>
                            """;

        var tags = ReleaseResolver.ParseGodotArchiveTags(html);

        Assert.That(tags, Is.EqualTo(["4.3-stable", "4.3.2-stable"]));
    }

    [Test]
    public void FiltersUnstableAndUnselectedGodotReleases() {
        var selector = VersionSelector.Parse("VERSION", "4.3");

        var releases = ReleaseResolver.ParseGodotReleases(
            ["4.3-stable", "4.3.2-stable", "4.3.3-rc1", "4.4-stable", "not-a-version"],
            selector);

        Assert.That(releases.Select(release => release.version), Is.EqualTo([new Version(4, 3, 0), new Version(4, 3, 2)]));
    }

    [Test]
    public void FindsDistinctMatchingBlenderSeries() {
        const string html = "<a>Blender4.2/</a><a>Blender4.2/</a><a>Blender4.3/</a><a>Blender5.0/</a>";
        var selector = VersionSelector.Parse("VERSION", "4");

        var series = ReleaseResolver.ParseBlenderSeries(html, selector);

        Assert.That(series, Is.EqualTo(["Blender4.2/", "Blender4.3/"]));
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

    static PlatformInfo CreatePlatform(bool windows) => new(windows, "godot", "templates", "blender", "state", "data", "settings");
}