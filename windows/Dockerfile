ARG OS_BASE=ltsc2019

FROM mcr.microsoft.com/dotnet/sdk:9.0-windowsservercore-ltsc2019 AS build

WORKDIR C:/source

COPY common/Godot/ .

RUN dotnet publish Godot.csproj \
        --configuration Release \
        --runtime win-x64 \
        --self-contained true \
        --output C:/out

FROM mcr.microsoft.com/windows:${OS_BASE}

ARG POWERSHELL_VERSION=7.6.4
ARG POWERSHELL_SHA256=80832551c52809301e6071c8bac977beb5a2f1ec953eb4db9f94deb953333793

SHELL ["C:/Windows/System32/WindowsPowerShell/v1.0/powershell", "-ExecutionPolicy", "Bypass", "-NonInteractive", "-NoProfile", "-Command", "$ErrorActionPreference = 'Stop'; $ProgressPreference = 'SilentlyContinue';"]

USER ContainerAdministrator
WORKDIR C:/godot

COPY --from=build C:/out/docker-godot.exe C:/godot/docker-godot.exe

# Let the daemon fetch this large file; legacy Windows container networking is
# prohibitively slow. The publisher checksum is verified before extraction.
ADD https://github.com/PowerShell/PowerShell/releases/download/v${POWERSHELL_VERSION}/PowerShell-${POWERSHELL_VERSION}-win-x64.zip C:/powershell.zip

# The ZIP package works on Server Core and avoids installer side effects.
RUN $archive = 'C:/powershell.zip'; \
    $actual = (Get-FileHash -Algorithm SHA256 $archive).Hash; \
    if ($actual -ne $env:POWERSHELL_SHA256) { throw "PowerShell SHA-256 mismatch: $actual" }; \
    $destination = 'C:/Program Files/PowerShell/7'; \
    New-Item -ItemType Directory -Force $destination | Out-Null; \
    Expand-Archive -LiteralPath $archive -DestinationPath $destination; \
    Remove-Item -LiteralPath $archive; \
    & "$destination/pwsh.exe" -NoLogo -NoProfile -Command 'if ($PSVersionTable.PSVersion.ToString() -ne $env:POWERSHELL_VERSION) { exit 1 }'; \
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

RUN New-Item -ItemType Directory -Force C:/godot/binaries, C:/godot/export_templates, C:/blender, C:/run/docker-godot | Out-Null; \
    $godotData = 'C:/Users/ContainerAdministrator/AppData/Roaming/Godot'; \
    New-Item -ItemType Directory -Force $godotData | Out-Null; \
    Copy-Item C:/godot/docker-godot.exe C:/godot/godot.exe; \
    Copy-Item C:/godot/docker-godot.exe C:/godot/blender.exe; \
    $env:PATH = 'C:\godot;C:\Windows\system32;C:\Windows;C:\Windows\System32\WindowsPowerShell\v1.0'; \
    $ErrorActionPreference = 'Continue'; \
    & C:/godot/godot.exe 2>&1 | Out-Null; \
    $launcherExit = $LASTEXITCODE; \
    $ErrorActionPreference = 'Stop'; \
    if ($launcherExit -eq 0) { throw 'godot launcher unexpectedly succeeded without GODOT_VERSION' }

ENV PATH="C:\godot;C:\Program Files\PowerShell\7;C:\Windows\system32;C:\Windows;C:\Windows\System32\WindowsPowerShell\v1.0"

VOLUME ["C:/godot/binaries", "C:/godot/export_templates", "C:/blender"]

HEALTHCHECK --interval=30s --timeout=10s --start-period=2h --retries=3 \
    CMD ["C:/godot/docker-godot.exe", "health"]

CMD ["godot.exe"]
