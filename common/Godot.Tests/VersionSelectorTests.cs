using System;
using Xunit;

public sealed class VersionSelectorTests {
    [Theory]
    [InlineData("4", "4.6.2", true)]
    [InlineData("4", "5.0.0", false)]
    [InlineData("4.3", "4.3.0", true)]
    [InlineData("4.3", "4.4.0", false)]
    [InlineData("4.3.1", "4.3.1", true)]
    [InlineData("4.3.1", "4.3.2", false)]
    public void MatchesSelectedVersionPrefix(string value, string candidate, bool expected) {
        var selector = VersionSelector.Parse("VERSION", value);

        Assert.Equal(expected, selector.Matches(new Version(candidate)));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("4.")]
    [InlineData(".4")]
    [InlineData("4.3.1.2")]
    [InlineData("v4")]
    [InlineData("4.-1")]
    [InlineData("999999999999999999999")]
    public void RejectsInvalidSelector(string value) {
        var exception = Assert.Throws<InvalidOperationException>(() => VersionSelector.Parse("VERSION", value));

        Assert.Contains("VERSION", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PreservesCanonicalSelectorText() {
        var selector = VersionSelector.Parse("VERSION", "4.03.001");

        Assert.Equal("4.3.1", selector.ToString());
    }
}
