using System;
using System.Linq;
using NUnit.Framework;

namespace Godot.Tests;

public sealed class ProcessRunnerTests {
    [Test]
    public void PreservesArgumentsWithoutManualQuoting() {
        string[] arguments = ["plain", "with spaces", "a\"quote", @"trailing\\", string.Empty];

        var start = ProcessRunner.CreateStartInfo("tool", arguments);

        Assert.That(start.ArgumentList.Cast<string>(), Is.EqualTo(arguments));
        Assert.That(start.UseShellExecute, Is.False);
        Assert.That(start.WorkingDirectory, Is.EqualTo(Environment.CurrentDirectory));
    }
}