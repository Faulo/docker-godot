using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace Godot;

static class ProcessRunner {
    public static int Run(string executable, IEnumerable<string> arguments, bool requireSuccess) {
        using var process = Process.Start(CreateStartInfo(executable, arguments)) ?? throw new InvalidOperationException("failed to start " + executable);

        process.WaitForExit();
        if (requireSuccess && process.ExitCode != 0) {
            throw new InvalidOperationException(Path.GetFileName(executable) + " exited with code " + process.ExitCode);
        }

        return process.ExitCode;
    }

    internal static ProcessStartInfo CreateStartInfo(string executable, IEnumerable<string> arguments) {
        var start = new ProcessStartInfo { FileName = executable, UseShellExecute = false, WorkingDirectory = Environment.CurrentDirectory };
        foreach (string argument in arguments) {
            start.ArgumentList.Add(argument);
        }

        return start;
    }
}