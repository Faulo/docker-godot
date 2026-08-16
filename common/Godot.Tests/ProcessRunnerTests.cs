using System;
using System.Diagnostics;
using System.Linq;
using Xunit;

public sealed class ProcessRunnerTests {
    [Fact]
    public void PreservesArgumentsWithoutManualQuoting() {
        string[] arguments = { "plain", "with spaces", "a\"quote", @"trailing\\", string.Empty };

        var start = ProcessRunner.CreateStartInfo("tool", arguments);

        Assert.Equal(arguments, start.ArgumentList.Cast<string>());
        Assert.False(start.UseShellExecute);
        Assert.Equal(Environment.CurrentDirectory, start.WorkingDirectory);
    }
}
