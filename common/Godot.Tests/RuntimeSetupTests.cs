using System;
using System.IO;
using NUnit.Framework;

public sealed class RuntimeSetupTests {
    [Test]
    public void IsUnhealthyWithoutReadyState() {
        using var directory = new TemporaryDirectory();
        var setup = CreateSetup(directory.path);

        Assert.That(setup.IsHealthy(), Is.False);
    }

    [Test]
    public void IsHealthyWhenEveryReadyPathExists() {
        using var directory = new TemporaryDirectory();
        string first = directory.Write("first", string.Empty);
        string second = directory.Write("second", string.Empty);
        File.WriteAllLines(Path.Combine(directory.path, "ready"), new[] { first, second });
        var setup = CreateSetup(directory.path);

        Assert.That(setup.IsHealthy(), Is.True);
    }

    [Test]
    public void IsUnhealthyWhenAReadyPathIsMissing() {
        using var directory = new TemporaryDirectory();
        string existing = directory.Write("existing", string.Empty);
        File.WriteAllLines(Path.Combine(directory.path, "ready"), new[] { existing, Path.Combine(directory.path, "missing") });
        var setup = CreateSetup(directory.path);

        Assert.That(setup.IsHealthy(), Is.False);
    }

    [TestCase("4", false)]
    [TestCase("4.3", false)]
    [TestCase("4.3.0", true)]
    public void OnlyExactSelectorsCanUseCache(string value, bool expected) {
        using var directory = new TemporaryDirectory();
        string executable = directory.Write("executable", string.Empty);
        var selector = VersionSelector.Parse("VERSION", value);

        bool actual = RuntimeSetup.CanUseCache(selector, new[] { selector.ToString(), executable }, 1);

        Assert.That(actual, Is.EqualTo(expected));
    }

    [Test]
    public void RejectsCacheWithMissingArtifact() {
        using var directory = new TemporaryDirectory();
        var selector = VersionSelector.Parse("VERSION", "4.3.0");

        bool actual = RuntimeSetup.CanUseCache(selector, new[] { selector.ToString(), Path.Combine(directory.path, "missing") }, 1);

        Assert.That(actual, Is.False);
    }

    static RuntimeSetup CreateSetup(string stateRoot) {
        var platform = new PlatformInfo(false, "godot", "templates", "blender", stateRoot, "data", "settings");
        return new RuntimeSetup(platform, new UnusedReleaseResolver(), new UnusedDownloadClient());
    }

    sealed class UnusedReleaseResolver : IReleaseResolver {
        public GodotRelease ResolveGodot(VersionSelector selector) {
            throw new NotSupportedException();
        }

        public BlenderRelease ResolveBlender(VersionSelector selector, PlatformInfo platform) {
            throw new NotSupportedException();
        }
    }

    sealed class UnusedDownloadClient : IDownloadClient {
        public string ReadText(string uri, bool github) {
            throw new NotSupportedException();
        }

        public void Save(string uri, string destination) {
            throw new NotSupportedException();
        }
    }
}
