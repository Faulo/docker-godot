ARG OS_BASE=ltsc2019

# Keep the SDK stage small enough for Hyper-V-isolated CI runners.
FROM mcr.microsoft.com/dotnet/sdk:9.0-nanoserver-1809 AS build

WORKDIR C:/solution

ENV NUGET_PACKAGES=C:/nuget/packages

COPY . .

RUN dotnet publish --runtime win-x64

FROM faulo/choco:windows-${OS_BASE}

SHELL ["C:/Windows/System32/WindowsPowerShell/v1.0/powershell", "-ExecutionPolicy", "Bypass", "-NonInteractive", "-NoProfile", "-Command", "$ErrorActionPreference = 'Stop'; $ProgressPreference = 'SilentlyContinue';"]

USER ContainerAdministrator
WORKDIR C:/godot

COPY --from=build C:/solution/common/Godot/bin/Release/net9.0/win-x64/publish/ C:/godot/

# hadolint shell=powershell
RUN $machinePath = [Environment]::GetEnvironmentVariable('Path', 'Machine'); \
    [Environment]::SetEnvironmentVariable('Path', ('C:\godot;' + $machinePath), 'Machine')

COPY windows/godot.nuspec C:/package/godot.nuspec

# hadolint shell=powershell
RUN choco-install C:/package/godot.nuspec; \
    if ($LASTEXITCODE -ne 0) { throw ('choco-install godot failed with exit code {0}' -f $LASTEXITCODE) }; \
    Remove-Item -LiteralPath C:/package -Recurse -Force

# The shared base declares PATH, which takes precedence over installer registry updates.
ENV PATH="C:\\godot;C:\\Program Files\\PowerShell\\7;${PATH}"

# hadolint shell=powershell
RUN pwsh.exe -NoLogo -NoProfile -Command 'if ($PSVersionTable.PSVersion.Major -ne 7) { exit 1 }; $PSVersionTable.PSVersion'; \
    if ($LASTEXITCODE -ne 0) { throw 'PowerShell smoke check failed' }

VOLUME ["C:/godot/binaries", "C:/godot/export_templates", "C:/blender"]

HEALTHCHECK --interval=30s --timeout=10s --start-period=2h --retries=3 \
    CMD ["godot.exe", "health"]

CMD ["godot", "help"]
