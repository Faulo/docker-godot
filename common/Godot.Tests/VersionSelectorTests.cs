using System;
using NUnit.Framework;

namespace Godot.Tests;

public sealed class VersionSelectorTests {
    [TestCase("4", "4.6.2", true)]
    [TestCase("4", "5.0.0", false)]
    [TestCase("4.3", "4.3.0", true)]
    [TestCase("4.3", "4.4.0", false)]
    [TestCase("4.3.1", "4.3.1", true)]
    [TestCase("4.3.1", "4.3.2", false)]
    public void MatchesSelectedVersionPrefix(string value, string candidate, bool expected) {
        var selector = VersionSelector.Parse("VERSION", value);

        Assert.That(selector.Matches(new Version(candidate)), Is.EqualTo(expected));
    }

    [TestCase("")]
    [TestCase(" ")]
    [TestCase("4.")]
    [TestCase(".4")]
    [TestCase("4.3.1.2")]
    [TestCase("v4")]
    [TestCase("4.-1")]
    [TestCase("999999999999999999999")]
    public void RejectsInvalidSelector(string value) {
        var exception = Assert.Throws<InvalidOperationException>(() => VersionSelector.Parse("VERSION", value))!;

        Assert.That(exception.Message, Does.Contain("VERSION"));
    }

    [Test]
    public void PreservesCanonicalSelectorText() {
        var selector = VersionSelector.Parse("VERSION", "4.03.001");

        Assert.That(selector.ToString(), Is.EqualTo("4.3.1"));
    }
}