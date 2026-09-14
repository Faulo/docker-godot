ARG OS_BASE=ltsc2019

# Keep the SDK stage small enough for Hyper-V-isolated CI runners.
FROM mcr.microsoft.com/dotnet/sdk:9.0-nanoserver-1809 AS build

WORKDIR C:/solution

ENV NUGET_PACKAGES=C:/nuget/packages

COPY . .

RUN dotnet publish \
        --configuration Release \
        --runtime win-x64 \
        --self-contained true \
        --property:PublishSingleFile=true \
        --property:UseAppHost=true \
        --property:DebugType=None

FROM mcr.microsoft.com/windows:${OS_BASE}

SHELL ["C:/Windows/System32/WindowsPowerShell/v1.0/powershell", "-ExecutionPolicy", "Bypass", "-NonInteractive", "-NoProfile", "-Command", "$ErrorActionPreference = 'Stop'; $ProgressPreference = 'SilentlyContinue';"]

USER ContainerAdministrator
WORKDIR C:/godot

COPY --from=build C:/solution/common/Godot/bin/Release/net9.0/win-x64/publish/ C:/godot/

RUN $machinePath = [Environment]::GetEnvironmentVariable('Path', 'Machine'); \
    [Environment]::SetEnvironmentVariable('Path', ('C:\godot;' + $machinePath), 'Machine')

# Chocolatey packages
ARG CHOCOLATEY_VERSION=1.4.0

# Chocolatey 2 requires .NET Framework 4.8, which the full LTSC 2019 image
# cannot add without a reboot. Chocolatey 1 keeps the full Windows DLL surface.
RUN $env:chocolateyVersion = $env:CHOCOLATEY_VERSION; \
    Invoke-Expression ((New-Object System.Net.WebClient).DownloadString('https://chocolatey.org/install.ps1')); \
    if (-not (Get-Command choco -ErrorAction SilentlyContinue)) { throw 'Chocolatey installation failed' }

COPY windows/godot.nuspec C:/package/godot.nuspec

RUN choco pack C:/package/godot.nuspec --output-directory C:/package --limit-output; \
    if ($LASTEXITCODE -ne 0) { throw ('choco pack godot failed with exit code {0}' -f $LASTEXITCODE) }; \
    choco install godot-container --yes --no-progress --limit-output --source 'C:/package;https://community.chocolatey.org/api/v2/'; \
    if ($LASTEXITCODE -notin 0, 1641, 3010) { throw ('choco install godot-container failed with exit code {0}' -f $LASTEXITCODE) }; \
    Remove-Item -Recurse -Force C:/package; \
    $env:Path = [Environment]::GetEnvironmentVariable('Path', 'Machine') + ';' + [Environment]::GetEnvironmentVariable('Path', 'User'); \
    pwsh.exe -NoLogo -NoProfile -Command '$PSVersionTable.PSVersion'; \
    if ($LASTEXITCODE -ne 0) { throw 'PowerShell smoke check failed' }

VOLUME ["C:/godot/binaries", "C:/godot/export_templates", "C:/blender"]

HEALTHCHECK --interval=30s --timeout=10s --start-period=2h --retries=3 \
    CMD ["godot.exe", "health"]

CMD ["godot.exe"]
