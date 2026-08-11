ARG OS_BASE=1809
FROM mcr.microsoft.com/windows:${OS_BASE}

SHELL ["C:/Windows/System32/WindowsPowerShell/v1.0/powershell", "-ExecutionPolicy", "Bypass", "-NonInteractive", "-NoProfile", "-Command", "$ErrorActionPreference = 'Stop'; $ProgressPreference = 'SilentlyContinue';"]

USER ContainerAdministrator
WORKDIR C:/godot

COPY common/Program.cs C:/godot/Program.cs

RUN New-Item -ItemType Directory -Force C:/godot/binaries, C:/godot/export_templates, C:/blender, C:/run/docker-godot | Out-Null; \
    $godotData = 'C:/Users/ContainerAdministrator/AppData/Roaming/Godot'; \
    New-Item -ItemType Directory -Force $godotData | Out-Null; \
    & C:/Windows/Microsoft.NET/Framework64/v4.0.30319/csc.exe /nologo /optimize+ /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll '/out:C:\godot\docker-godot.exe' 'C:\godot\Program.cs'; \
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }; \
    Copy-Item C:/godot/docker-godot.exe C:/godot/godot.exe; \
    Copy-Item C:/godot/docker-godot.exe C:/godot/blender.exe; \
    $env:PATH = 'C:\godot;C:\Windows\system32;C:\Windows;C:\Windows\System32\WindowsPowerShell\v1.0'; \
    $ErrorActionPreference = 'Continue'; \
    & C:/godot/godot.exe 2>&1 | Out-Null; \
    $launcherExit = $LASTEXITCODE; \
    $ErrorActionPreference = 'Stop'; \
    if ($launcherExit -eq 0) { throw 'godot launcher unexpectedly succeeded without GODOT_VERSION' }

ENV PATH="C:\godot;C:\Windows\system32;C:\Windows;C:\Windows\System32\WindowsPowerShell\v1.0"

VOLUME ["C:/godot/binaries", "C:/godot/export_templates", "C:/blender"]

HEALTHCHECK --interval=30s --timeout=10s --start-period=2h --retries=3 \
    CMD ["C:/godot/docker-godot.exe", "health"]

CMD ["godot.exe"]
