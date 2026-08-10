ARG OS_BASE=1809
FROM mcr.microsoft.com/windows:${OS_BASE}

SHELL ["C:/Windows/System32/WindowsPowerShell/v1.0/powershell", "-ExecutionPolicy", "Bypass", "-NonInteractive", "-NoProfile", "-Command", "$ErrorActionPreference = 'Stop'; $ProgressPreference = 'SilentlyContinue';"]

USER ContainerAdministrator
WORKDIR C:/workspace

COPY common/setup-windows.ps1 C:/docker-godot/setup.ps1
COPY common/Launcher.cs C:/docker-godot/Launcher.cs

RUN New-Item -ItemType Directory -Force C:/godot/binaries, C:/godot/export_templates, C:/blender, C:/run/docker-godot | Out-Null; \
    $godotData = 'C:/Users/ContainerAdministrator/AppData/Roaming/Godot'; \
    New-Item -ItemType Directory -Force $godotData | Out-Null; \
    New-Item -ItemType Junction -Path "$godotData/export_templates" -Target C:/godot/export_templates | Out-Null; \
    & C:/Windows/Microsoft.NET/Framework64/v4.0.30319/csc.exe /nologo /warnaserror+ /optimize+ '/out:C:\Windows\godot.exe' 'C:\docker-godot\Launcher.cs'; \
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }; \
    Copy-Item C:/Windows/godot.exe C:/Windows/blender.exe; \
    $ErrorActionPreference = 'Continue'; \
    & C:/Windows/godot.exe 2>&1 | Out-Null; \
    $launcherExit = $LASTEXITCODE; \
    $ErrorActionPreference = 'Stop'; \
    if ($launcherExit -eq 0) { throw 'godot launcher unexpectedly succeeded without GODOT_VERSION' }; \
    Remove-Item C:/godot/binaries/.docker-godot.lock -Force

VOLUME ["C:/godot/binaries", "C:/godot/export_templates", "C:/blender"]

HEALTHCHECK --interval=30s --timeout=10s --start-period=2h --retries=3 \
    CMD ["powershell.exe", "-ExecutionPolicy", "Bypass", "-NonInteractive", "-NoLogo", "-NoProfile", "-File", "C:/docker-godot/setup.ps1", "health"]

CMD ["godot.exe"]
