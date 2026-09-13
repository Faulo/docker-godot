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

# PowerShell
ARG POWERSHELL_MAJOR=7

ADD https://api.github.com/repos/PowerShell/PowerShell/releases?per_page=100 C:/powershell-releases.json

RUN $releases = Get-Content C:/powershell-releases.json -Raw | ConvertFrom-Json; \
    $release = $releases | \
      Where-Object { -not $_.draft -and -not $_.prerelease -and $_.tag_name -match ('^v' + $env:POWERSHELL_MAJOR + '\.\d+\.\d+$') } | \
      Sort-Object { [version]$_.tag_name.Substring(1) } | \
      Select-Object -Last 1; \
    if (-not $release) { throw ('No stable PowerShell ' + $env:POWERSHELL_MAJOR + ' release found') }; \
    $powershellVersion = $release.tag_name.Substring(1); \
    $archiveName = 'PowerShell-' + $powershellVersion + '-win-x64.zip'; \
    $archive = 'C:/' + $archiveName; \
    $asset = $release.assets | Where-Object name -eq $archiveName; \
    $hashesAsset = $release.assets | Where-Object name -eq 'hashes.sha256'; \
    if (-not $asset -or -not $hashesAsset) { throw ('PowerShell ' + $powershellVersion + ' release assets are incomplete') }; \
    curl.exe -fsSL $asset.browser_download_url -o $archive; \
    if ($LASTEXITCODE -ne 0) { throw 'Failed to download PowerShell' }; \
    curl.exe -fsSL $hashesAsset.browser_download_url -o C:/powershell-hashes; \
    if ($LASTEXITCODE -ne 0) { throw 'Failed to download PowerShell hashes' }; \
    $hashLine = Get-Content C:/powershell-hashes | Where-Object { $_ -match ('\*' + [regex]::Escape($archiveName) + '$') }; \
    if (-not $hashLine) { throw ('No checksum found for ' + $archiveName) }; \
    $expectedHash = ($hashLine -split '\s+')[0]; \
    $actualHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $archive).Hash; \
    if ($actualHash -ne $expectedHash) { throw ('Checksum verification failed for ' + $archiveName) }; \
    $destination = 'C:/Program Files/PowerShell/7'; \
    New-Item -ItemType Directory -Force $destination | Out-Null; \
    Expand-Archive -LiteralPath $archive -DestinationPath $destination; \
    Remove-Item -LiteralPath $archive,C:/powershell-hashes,C:/powershell-releases.json; \
    $machinePath = [Environment]::GetEnvironmentVariable('Path', 'Machine'); \
    [Environment]::SetEnvironmentVariable('Path', ($destination + ';' + $machinePath), 'Machine'); \
    $env:Path = $destination + ';' + $env:Path; \
    pwsh -NoLogo -NoProfile -Command \
        'if ($PSVersionTable.PSVersion.Major -ne [int]$env:POWERSHELL_MAJOR) { exit 1 }'; \
    if ($LASTEXITCODE -ne 0) { throw 'PowerShell smoke check failed' }

VOLUME ["C:/godot/binaries", "C:/godot/export_templates", "C:/blender"]

HEALTHCHECK --interval=30s --timeout=10s --start-period=2h --retries=3 \
    CMD ["godot.exe", "health"]

CMD ["godot.exe"]
