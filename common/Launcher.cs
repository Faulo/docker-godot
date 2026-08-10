using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

internal static class Launcher
{
    public static int Main(string[] args)
    {
        try
        {
            var command = Path.GetFileNameWithoutExtension(Process.GetCurrentProcess().MainModule.FileName).ToLowerInvariant();
            if (command != "godot" && command != "blender")
            {
                Console.Error.WriteLine("docker-godot: launcher must be named godot.exe or blender.exe");
                return 1;
            }

            var setup = new ProcessStartInfo
            {
                FileName = @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe",
                Arguments = BuildArguments(new[]
                {
                    "-ExecutionPolicy", "Bypass", "-NonInteractive", "-NoLogo", "-NoProfile",
                    "-File", @"C:\docker-godot\setup.ps1", command
                }),
                RedirectStandardOutput = true,
                UseShellExecute = false
            };
            string executable;
            using (var setupProcess = Process.Start(setup))
            {
                executable = setupProcess.StandardOutput.ReadToEnd().Trim();
                setupProcess.WaitForExit();
                if (setupProcess.ExitCode != 0)
                {
                    return setupProcess.ExitCode;
                }
            }

            if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable))
            {
                Console.Error.WriteLine("docker-godot: setup returned an invalid executable path");
                return 1;
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = executable,
                Arguments = BuildArguments(args),
                UseShellExecute = false,
                WorkingDirectory = Environment.CurrentDirectory
            };
            using (var process = Process.Start(startInfo))
            {
                process.WaitForExit();
                return process.ExitCode;
            }
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("docker-godot: " + exception.Message);
            return 1;
        }
    }

    private static string BuildArguments(IEnumerable<string> arguments)
    {
        return string.Join(" ", arguments.Select(QuoteArgument));
    }

    private static string QuoteArgument(string argument)
    {
        if (argument.Length > 0 && !argument.Any(character => char.IsWhiteSpace(character) || character == '"'))
        {
            return argument;
        }

        var result = new StringBuilder("\"");
        var backslashes = 0;
        foreach (var character in argument)
        {
            if (character == '\\')
            {
                backslashes++;
            }
            else if (character == '"')
            {
                result.Append('\\', backslashes * 2 + 1).Append('"');
                backslashes = 0;
            }
            else
            {
                result.Append('\\', backslashes).Append(character);
                backslashes = 0;
            }
        }

        result.Append('\\', backslashes * 2).Append('"');
        return result.ToString();
    }
}
