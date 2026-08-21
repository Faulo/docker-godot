using System;

namespace DockerGodot;

static class Program {
    static int Main(string[] arguments) {
        try {
            var setup = new RuntimeSetup(PlatformInfo.current, new ReleaseResolver(DownloadClient.shared), DownloadClient.shared);
            if (arguments.Length == 1 && string.Equals(arguments[0], "health", StringComparison.OrdinalIgnoreCase)) {
                return setup.IsHealthy() ? 0 : 1;
            }

            string executable = setup.PrepareGodot();
            if (PlatformInfo.current.isWindows) {
                GodotCommandLine.ValidateImportProject(arguments, Environment.CurrentDirectory);
            }
            return ProcessRunner.Run(executable, arguments, false);
        } catch (Exception exception) {
            Console.Error.WriteLine("docker-godot: " + exception.Message);
            return 1;
        }
    }
}
