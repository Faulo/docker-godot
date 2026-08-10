param(
    [Parameter(Mandatory = $true, Position = 0)]
    [ValidateSet('godot', 'blender', 'health')]
    [string] $Operation
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$GodotRoot = 'C:\godot\binaries'
$TemplateRoot = 'C:\godot\export_templates'
$BlenderRoot = 'C:\blender'
$StateRoot = 'C:\run\docker-godot'

function Write-Log([string] $Message) {
    [Console]::Error.WriteLine("docker-godot: $Message")
}

function Assert-Selector([string] $Name, [string] $Value) {
    if ($Value -notmatch '^\d+(\.\d+){0,2}$') {
        throw "$Name must contain one to three numeric components, got: $Value"
    }
}

function Test-Selector([version] $Candidate, [string] $Selector) {
    $parts = $Selector.Split('.')
    if ($Candidate.Major -ne [int] $parts[0]) {
        return $false
    }
    if ($parts.Length -ge 2 -and $Candidate.Minor -ne [int] $parts[1]) {
        return $false
    }
    if ($parts.Length -eq 3 -and $Candidate.Build -ne [int] $parts[2]) {
        return $false
    }
    return $true
}

function Resolve-Godot([string] $Selector) {
    $candidates = @()
    for ($page = 1; $page -le 10; $page++) {
        try {
            $releases = @(Invoke-RestMethod -UseBasicParsing -Headers @{
                Accept = 'application/vnd.github+json'
                'User-Agent' = 'docker-godot'
            } -Uri "https://api.github.com/repos/godotengine/godot-builds/releases?per_page=100&page=$page")
        } catch {
            throw "failed to query Godot releases: $($_.Exception.Message)"
        }

        foreach ($release in $releases) {
            if ($release.tag_name -match '^(\d+)\.(\d+)(?:\.(\d+))?-stable$') {
                $patch = if ($Matches[3]) { [int] $Matches[3] } else { 0 }
                $version = [version]::new([int] $Matches[1], [int] $Matches[2], $patch)
                if (Test-Selector $version $Selector) {
                    $candidates += [pscustomobject]@{ Version = $version; Tag = $release.tag_name }
                }
            }
        }
        if ($candidates.Count -gt 0) {
            break
        }
        if ($releases.Count -lt 100) {
            break
        }
    }

    $resolved = $candidates | Sort-Object Version | Select-Object -Last 1
    if (-not $resolved) {
        throw "no stable Godot release matches GODOT_VERSION=$Selector"
    }
    return $resolved
}

function Resolve-Blender([string] $Selector) {
    $major = $Selector.Split('.')[0]
    try {
        $rootListing = (Invoke-WebRequest -UseBasicParsing 'https://download.blender.org/release/').Content
    } catch {
        throw "failed to query Blender releases: $($_.Exception.Message)"
    }

    $seriesNames = [regex]::Matches($rootListing, "Blender$major\.(\d+)/") |
        ForEach-Object { $_.Value } | Sort-Object -Unique
    $candidates = @()
    foreach ($series in $seriesNames) {
        if ($series -notmatch '^Blender\d+\.(\d+)/$') {
            continue
        }
        if ($Selector.Split('.').Length -ge 2 -and [int] $Matches[1] -ne [int] $Selector.Split('.')[1]) {
            continue
        }
        try {
            $listing = (Invoke-WebRequest -UseBasicParsing "https://download.blender.org/release/$series").Content
        } catch {
            throw "failed to query Blender series $series`: $($_.Exception.Message)"
        }
        $versions = [regex]::Matches($listing, 'blender-(\d+\.\d+\.\d+)-windows-x64\.zip') |
            ForEach-Object { [version] $_.Groups[1].Value } | Sort-Object -Unique
        foreach ($version in $versions) {
            if (Test-Selector $version $Selector) {
                $candidates += [pscustomobject]@{ Version = $version; Series = $series }
            }
        }
    }

    $resolved = $candidates | Sort-Object Version | Select-Object -Last 1
    if (-not $resolved) {
        throw "no stable Blender release matches BLENDER_VERSION=$Selector"
    }
    return $resolved
}

function Save-Download([string] $Uri, [string] $Destination) {
    for ($attempt = 1; $attempt -le 5; $attempt++) {
        & curl.exe -fsSL --http1.1 --continue-at - $Uri -o $Destination
        if ($LASTEXITCODE -eq 0) {
            return
        }
        if ($attempt -eq 5) {
            throw "download failed after $attempt attempts: $Uri"
        }
        Write-Log "download interrupted; resuming attempt $($attempt + 1) of 5"
        Start-Sleep -Seconds ([Math]::Min(2 * $attempt, 10))
    }
}

function Assert-Checksum(
    [string] $Archive,
    [string] $Sums,
    [ValidateSet('SHA256', 'SHA512')]
    [string] $Algorithm
) {
    $filename = [IO.Path]::GetFileName($Archive)
    $escaped = [regex]::Escape($filename)
    $length = if ($Algorithm -eq 'SHA512') { 128 } else { 64 }
    $match = [regex]::Match([IO.File]::ReadAllText($Sums), "(?m)^([0-9a-fA-F]{$length})\s+\*?$escaped`r?$")
    if (-not $match.Success) {
        throw "missing $Algorithm checksum for $filename"
    }
    $actual = (Get-FileHash -Algorithm $Algorithm -LiteralPath $Archive).Hash
    if ($actual -ne $match.Groups[1].Value) {
        throw "$Algorithm checksum mismatch for $filename"
    }
}

function Invoke-WithLock([string] $Root, [scriptblock] $Action) {
    [IO.Directory]::CreateDirectory($Root) > $null
    $lockPath = Join-Path $Root '.docker-godot.lock'
    $lock = $null
    while (-not $lock) {
        try {
            $lock = [IO.File]::Open($lockPath, 'OpenOrCreate', 'ReadWrite', 'None')
        } catch [IO.IOException] {
            Start-Sleep -Milliseconds 250
        }
    }
    try {
        & $Action
    } finally {
        $lock.Dispose()
    }
}

function Install-Godot {
    $selector = $env:GODOT_VERSION
    if ([string]::IsNullOrWhiteSpace($selector)) {
        throw 'GODOT_VERSION is required'
    }
    Assert-Selector 'GODOT_VERSION' $selector
    if ([int] $selector.Split('.')[0] -ne 4) {
        throw 'only standard Godot 4 releases are currently supported'
    }
    $resolved = Resolve-Godot $selector
    $installId = $resolved.Tag -replace '-', '.'
    $installDirectory = Join-Path $GodotRoot $installId
    $templateDirectory = Join-Path $TemplateRoot $installId
    $executable = Join-Path $installDirectory "Godot_v$($resolved.Tag)_win64_console.exe"

    if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
        Write-Log "installing Godot $($resolved.Version) ($installId)"
        Get-ChildItem -LiteralPath $GodotRoot -Directory -Filter ".$installId.*" |
            Remove-Item -Recurse -Force
        $temporary = Join-Path $GodotRoot ".$installId.$([guid]::NewGuid().ToString('N'))"
        [IO.Directory]::CreateDirectory($temporary) > $null
        try {
            $filename = "Godot_v$($resolved.Tag)_win64.exe.zip"
            $archive = Join-Path $temporary $filename
            $sums = Join-Path $temporary 'SHA512-SUMS.txt'
            $baseUrl = "https://github.com/godotengine/godot-builds/releases/download/$($resolved.Tag)"
            Save-Download "$baseUrl/$filename" $archive
            Save-Download "$baseUrl/SHA512-SUMS.txt" $sums
            Assert-Checksum $archive $sums SHA512
            $unpacked = Join-Path $temporary 'unpacked'
            Expand-Archive -LiteralPath $archive -DestinationPath $unpacked
            Move-Item -LiteralPath $unpacked -Destination $installDirectory
        } finally {
            Remove-Item -LiteralPath $temporary -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    if (-not (Test-Path -LiteralPath (Join-Path $templateDirectory 'version.txt') -PathType Leaf)) {
        Write-Log "installing Godot export templates $installId"
        Get-ChildItem -LiteralPath $TemplateRoot -Directory -Filter ".$installId.*" |
            Remove-Item -Recurse -Force
        $temporary = Join-Path $TemplateRoot ".$installId.$([guid]::NewGuid().ToString('N'))"
        [IO.Directory]::CreateDirectory($temporary) > $null
        try {
            $filename = "Godot_v$($resolved.Tag)_export_templates.tpz"
            $archive = Join-Path $temporary ($filename -replace '\.tpz$', '.zip')
            $sums = Join-Path $temporary 'SHA512-SUMS.txt'
            $baseUrl = "https://github.com/godotengine/godot-builds/releases/download/$($resolved.Tag)"
            Save-Download "$baseUrl/$filename" $archive
            Save-Download "$baseUrl/SHA512-SUMS.txt" $sums
            # The checksum entry uses the original .tpz filename.
            $checksumArchive = Join-Path $temporary $filename
            Move-Item -LiteralPath $archive -Destination $checksumArchive
            Assert-Checksum $checksumArchive $sums SHA512
            $zipArchive = Join-Path $temporary 'templates.zip'
            Move-Item -LiteralPath $checksumArchive -Destination $zipArchive
            $unpacked = Join-Path $temporary 'unpacked'
            Expand-Archive -LiteralPath $zipArchive -DestinationPath $unpacked
            $templates = Join-Path $unpacked 'templates'
            if (-not (Test-Path -LiteralPath $templates -PathType Container)) {
                throw 'Godot template archive has an unexpected layout'
            }
            Move-Item -LiteralPath $templates -Destination $templateDirectory
        } finally {
            Remove-Item -LiteralPath $temporary -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    return $executable
}

function Install-Blender {
    $selector = $env:BLENDER_VERSION
    if ([string]::IsNullOrWhiteSpace($selector)) {
        throw 'BLENDER_VERSION is required'
    }
    Assert-Selector 'BLENDER_VERSION' $selector
    $resolved = Resolve-Blender $selector
    $version = $resolved.Version.ToString(3)
    $installDirectory = Join-Path $BlenderRoot $version
    $executable = Join-Path $installDirectory 'blender.exe'

    if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
        Write-Log "installing Blender $version"
        Get-ChildItem -LiteralPath $BlenderRoot -Directory -Filter ".$version.*" |
            Remove-Item -Recurse -Force
        $temporary = Join-Path $BlenderRoot ".$version.$([guid]::NewGuid().ToString('N'))"
        [IO.Directory]::CreateDirectory($temporary) > $null
        try {
            $filename = "blender-$version-windows-x64.zip"
            $archive = Join-Path $temporary $filename
            $sums = Join-Path $temporary "blender-$version.sha256"
            $baseUrl = "https://download.blender.org/release/$($resolved.Series)"
            Save-Download "$baseUrl/$filename" $archive
            Save-Download "$baseUrl/blender-$version.sha256" $sums
            Assert-Checksum $archive $sums SHA256
            $unpacked = Join-Path $temporary 'unpacked'
            Expand-Archive -LiteralPath $archive -DestinationPath $unpacked
            $source = Get-ChildItem -LiteralPath $unpacked -Directory | Select-Object -First 1
            if (-not $source -or -not (Test-Path -LiteralPath (Join-Path $source.FullName 'blender.exe'))) {
                throw 'Blender archive has an unexpected layout'
            }
            Move-Item -LiteralPath $source.FullName -Destination $installDirectory
        } finally {
            Remove-Item -LiteralPath $temporary -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    return $executable
}

function Write-Ready([string[]] $Paths) {
    [IO.Directory]::CreateDirectory($StateRoot) > $null
    $temporary = Join-Path $StateRoot "ready.$([guid]::NewGuid().ToString('N'))"
    [IO.File]::WriteAllLines($temporary, $Paths)
    Move-Item -LiteralPath $temporary -Destination (Join-Path $StateRoot 'ready') -Force
}

function Set-GodotBlenderPath([string] $GodotExecutable) {
    $installId = Split-Path (Split-Path $GodotExecutable -Parent) -Leaf
    $parts = $installId.Split('.')
    $major = [int] $parts[0]
    $minor = [int] $parts[1]
    $settingsRoot = Join-Path $env:APPDATA 'Godot'
    $settingsFile = $null
    for ($candidateMinor = $minor; $candidateMinor -ge 3; $candidateMinor--) {
        $candidate = Join-Path $settingsRoot "editor_settings-$major.$candidateMinor.tres"
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            $settingsFile = $candidate
            break
        }
    }
    $legacySettings = Join-Path $settingsRoot "editor_settings-$major.tres"
    if (-not $settingsFile -and ($minor -lt 3 -or (Test-Path -LiteralPath $legacySettings -PathType Leaf))) {
        $settingsFile = $legacySettings
    }
    if (-not $settingsFile) {
        $settingsFile = Join-Path $settingsRoot "editor_settings-$major.$minor.tres"
    }
    $setting = 'filesystem/import/blender/blender_path = "C:/Windows/blender.exe"'
    [IO.Directory]::CreateDirectory($settingsRoot) > $null
    $encoding = New-Object Text.UTF8Encoding($false)
    if (-not (Test-Path -LiteralPath $settingsFile -PathType Leaf)) {
        [IO.File]::WriteAllText($settingsFile, "[gd_resource type=`"EditorSettings`" format=3]`r`n`r`n[resource]`r`n$setting`r`n", $encoding)
        return
    }

    $contents = [IO.File]::ReadAllText($settingsFile)
    if ($contents -match '(?m)^filesystem/import/blender/blender_path = .*$') {
        $contents = [regex]::Replace(
            $contents,
            '(?m)^filesystem/import/blender/blender_path = .*$',
            $setting
        )
    } else {
        $contents = $contents.TrimEnd() + "`r`n$setting`r`n"
    }
    [IO.File]::WriteAllText($settingsFile, $contents, $encoding)
}

function Test-Health {
    $ready = Join-Path $StateRoot 'ready'
    if (-not (Test-Path -LiteralPath $ready -PathType Leaf)) {
        return $false
    }
    $paths = [IO.File]::ReadAllLines($ready)
    return $paths.Count -gt 0 -and -not ($paths | Where-Object { -not (Test-Path -LiteralPath $_ -PathType Leaf) })
}

try {
    switch ($Operation) {
        'godot' {
            $paths = @()
            if (-not [string]::IsNullOrWhiteSpace($env:BLENDER_VERSION)) {
                $paths += Invoke-WithLock $BlenderRoot { Install-Blender }
                [IO.Directory]::CreateDirectory($StateRoot) > $null
                [IO.File]::WriteAllLines((Join-Path $StateRoot 'blender'), @($env:BLENDER_VERSION, $paths[0]))
            }
            $godot = Invoke-WithLock $GodotRoot {
                Invoke-WithLock $TemplateRoot { Install-Godot }
            }
            if ($paths.Count -gt 0) {
                Set-GodotBlenderPath $godot
            }
            $paths += $godot
            Write-Ready $paths
            [Console]::Out.WriteLine($godot)
        }
        'blender' {
            $cachedBlender = Join-Path $StateRoot 'blender'
            $cachedValues = if (Test-Path -LiteralPath $cachedBlender -PathType Leaf) {
                @([IO.File]::ReadAllLines($cachedBlender))
            } else {
                @()
            }
            if ($cachedValues.Count -eq 2 -and $cachedValues[0] -eq $env:BLENDER_VERSION -and
                    (Test-Path -LiteralPath $cachedValues[1] -PathType Leaf)) {
                $blender = $cachedValues[1]
            } else {
                $blender = Invoke-WithLock $BlenderRoot { Install-Blender }
                [IO.Directory]::CreateDirectory($StateRoot) > $null
                [IO.File]::WriteAllLines($cachedBlender, @($env:BLENDER_VERSION, $blender))
            }
            Write-Ready @($blender)
            [Console]::Out.WriteLine($blender)
        }
        'health' {
            if (-not (Test-Health)) {
                exit 1
            }
        }
    }
} catch {
    Write-Log $_.Exception.Message
    exit 1
}
