param(
    [Parameter(Mandatory)]
    [string] $Namespace,

    [Parameter(Mandatory)]
    [string] $Name,

    [Parameter(Mandatory)]
    [string] $Variant,

    [Parameter(Mandatory)]
    [string] $Context,

    [Parameter(Mandatory)]
    [string] $Image,

    [Parameter(Mandatory)]
    [string] $Os,

    [Parameter(Mandatory)]
    [AllowEmptyCollection()]
    [string[]] $DockerRunArguments
)

BeforeDiscovery {
    $godotVersions = @('4.0', '4.1', '4.2', '4.3', '4.4', '4.5', '4.6', '4.7')
}

BeforeAll {
    . (Join-Path $PSScriptRoot '../.jenkins/Docker.ps1')

    $projectFixture = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../test-files/empty-project'))

    function New-TestResourceName {
        param(
            [Parameter(Mandatory)]
            [string] $Prefix
        )

        return "docker-godot-$Prefix-$([guid]::NewGuid().ToString('N'))"
    }

    function Remove-TestContainer {
        param(
            [string] $Container
        )

        if ([string]::IsNullOrWhiteSpace($Container)) {
            return
        }

        $result = Get-DockerCommandResult -Context $Context -Arguments @('container', 'inspect', $Container)
        if ($result.ExitCode -eq 0) {
            Invoke-Docker -Context $Context -Arguments @('container', 'rm', '--force', '--volumes', $Container)
        }
    }

    function Remove-TestVolume {
        param(
            [string] $Volume
        )

        if ([string]::IsNullOrWhiteSpace($Volume)) {
            return
        }

        $result = Get-DockerCommandResult -Context $Context -Arguments @('volume', 'inspect', $Volume)
        if ($result.ExitCode -eq 0) {
            Invoke-Docker -Context $Context -Arguments @('volume', 'rm', '--force', $Volume)
        }
    }

    function Wait-TestContainer {
        param(
            [Parameter(Mandatory)]
            [string] $Container,

            [Parameter(Mandatory)]
            [ValidateRange(1, [int]::MaxValue)]
            [int] $TimeoutSeconds
        )

        $stopwatch = [Diagnostics.Stopwatch]::StartNew()
        do {
            $state = Invoke-DockerOutput -Context $Context -Arguments @(
                'container', 'inspect', '--format', '{{.State.Running}} {{.State.ExitCode}}', $Container
            )
            $parts = $state.Split(' ', 2, [StringSplitOptions]::RemoveEmptyEntries)
            if ($parts[0] -eq 'false') {
                return [int] $parts[1]
            }

            Start-Sleep -Milliseconds 250
        } while ($stopwatch.Elapsed.TotalSeconds -lt $TimeoutSeconds)

        throw "Container $Container did not exit within $TimeoutSeconds seconds"
    }

    function Invoke-GodotContainer {
        param(
            [Parameter(Mandatory)]
            [string] $GodotVersion,

            [Parameter(Mandatory)]
            [string[]] $Command,

            [string[]] $AdditionalRunArguments = @(),

            [int] $TimeoutSeconds = 600,

            [switch] $Keep
        )

        $container = New-TestResourceName -Prefix 'container'
        $arguments = @('create', '--name', $container)
        $arguments += $DockerRunArguments
        $arguments += $AdditionalRunArguments
        $arguments += @('--env', "GODOT_VERSION=$GodotVersion", $Image)
        $arguments += $Command

        $completed = $false
        try {
            Invoke-Docker -Context $Context -Arguments $arguments
            Invoke-Docker -Context $Context -Arguments @('container', 'start', $container)
            $exitCode = Wait-TestContainer -Container $container -TimeoutSeconds $TimeoutSeconds
            $logs = Get-DockerCommandResult -Context $Context -Arguments @('container', 'logs', $container)

            $completed = $true
            return [pscustomobject] @{
                Container = $container
                ExitCode = $exitCode
                Output = $logs.Output
            }
        } finally {
            if (-not $Keep -or -not $completed) {
                Remove-TestContainer -Container $container
            }
        }
    }

    function Initialize-ProjectVolume {
        param(
            [Parameter(Mandatory)]
            [string] $Volume,

            [Parameter(Mandatory)]
            [string] $ProjectDirectory
        )

        $container = New-TestResourceName -Prefix 'fixture'
        $stagingDirectory = Join-Path ([IO.Path]::GetTempPath()) (New-TestResourceName -Prefix 'fixture')
        try {
            New-Item -ItemType Directory -Path $stagingDirectory | Out-Null
            Get-ChildItem -LiteralPath $projectFixture -File |
                Copy-Item -Destination $stagingDirectory
            $buildDirectory = Join-Path $stagingDirectory 'build'
            New-Item -ItemType Directory -Path $buildDirectory | Out-Null
            New-Item -ItemType File -Path (Join-Path $buildDirectory '.gdignore') | Out-Null

            Invoke-Docker -Context $Context -Arguments @('volume', 'create', $Volume)
            if ($Os -eq 'windows') {
                $keepAliveCommand = @('pwsh', '-NoLogo', '-NoProfile', '-NonInteractive', '-Command', 'Start-Sleep -Seconds 600')
            } else {
                $keepAliveCommand = @('sleep', '600')
            }
            $createArguments = @(
                'create', '--name', $container,
                '--mount', "type=volume,source=$Volume,target=$ProjectDirectory",
                '--workdir', $ProjectDirectory,
                $Image
            )
            $createArguments += $keepAliveCommand
            Invoke-Docker -Context $Context -Arguments $createArguments
            Invoke-Docker -Context $Context -Arguments @('container', 'start', $container)
            $fixtureDirectory = $Os -eq 'windows' ? 'C:/fixture' : '/fixture'
            if ($Os -eq 'windows') {
                Invoke-Docker -Context $Context -Arguments @(
                    'container', 'exec', $container,
                    'pwsh', '-NoLogo', '-NoProfile', '-NonInteractive', '-Command',
                    "New-Item -ItemType Directory -Path '$fixtureDirectory' -Force | Out-Null"
                )
            } else {
                Invoke-Docker -Context $Context -Arguments @('container', 'exec', $container, 'mkdir', '-p', $fixtureDirectory)
            }
            Invoke-Docker -Context $Context -Arguments @('container', 'cp', (Join-Path $stagingDirectory '.'), "${container}:${fixtureDirectory}")
            if ($Os -eq 'windows') {
                Invoke-Docker -Context $Context -Arguments @(
                    'container', 'exec', $container,
                    'pwsh', '-NoLogo', '-NoProfile', '-NonInteractive', '-Command',
                    "Copy-Item -Path '$fixtureDirectory/*' -Destination '$ProjectDirectory' -Recurse -Force"
                )
            } else {
                Invoke-Docker -Context $Context -Arguments @(
                    'container', 'exec', $container,
                    'cp', '--recursive', "$fixtureDirectory/.", "$ProjectDirectory/"
                )
            }
        } finally {
            Remove-TestContainer -Container $container
            if (Test-Path -LiteralPath $stagingDirectory) {
                Remove-Item -LiteralPath $stagingDirectory -Recurse -Force
            }
        }
    }
}

Describe "Godot runtime [$Os, $Image]" {
    It 'satisfies the platform runtime contract' {
        if ($Os -eq 'linux') {
            Invoke-Docker -Context $Context -RunArguments $DockerRunArguments -Arguments @(
                'run', '--rm', $Image,
                'grep', '--fixed-strings', 'VERSION_CODENAME=trixie', '/etc/os-release'
            )
        }

        if ($Os -eq 'windows') {
            $script = @'
$latestUrl = curl.exe -fsSL -o NUL -w '%{url_effective}' 'https://github.com/PowerShell/PowerShell/releases/latest'
if ($LASTEXITCODE -ne 0) { throw 'Failed to resolve the latest stable PowerShell release' }
$latestVersion = [version](([uri]$latestUrl).Segments[-1].TrimStart('v'))
if ($latestVersion.Major -ne 7) { throw "Latest stable PowerShell is not in major line 7: $latestVersion" }
if ($PSVersionTable.PSVersion -ne $latestVersion) { throw "Expected PowerShell $latestVersion, got $($PSVersionTable.PSVersion)" }
$chocolateyVersion = choco --version
if ($LASTEXITCODE -ne 0) { throw 'Chocolatey is not available' }
[version] $chocolateyVersion | Out-Null
'@
            Invoke-Docker -Context $Context -RunArguments $DockerRunArguments -Arguments @(
                'run', '--rm', $Image,
                'pwsh', '-NoLogo', '-NoProfile', '-NonInteractive', '-Command', $script
            )
        }
    }
}

Describe "Godot behavior [$Os, $Image]" {
    Context 'with GODOT_VERSION=<GodotVersion>' -ForEach @(
        $godotVersions | ForEach-Object { @{ GodotVersion = $_ } }
    ) {
        It 'installs and starts Godot' {
            $result = Invoke-GodotContainer -GodotVersion $GodotVersion -Command @('godot', '--version')
            $result.ExitCode | Should -Be 0
        }

        It 'rejects import outside a Godot project without hanging' {
            $workDirectory = $Os -eq 'windows' ? 'C:/empty-project' : '/empty-project'
            $result = Invoke-GodotContainer `
                -GodotVersion $GodotVersion `
                -Command @('godot', '--headless', '--verbose', '--quit', '--editor', '--import') `
                -AdditionalRunArguments @('--workdir', $workDirectory) `
                -TimeoutSeconds 10

            $result.ExitCode | Should -Not -Be 0
        }

        It 'imports the fixture and exports a Windows executable' {
            $projectDirectory = $Os -eq 'windows' ? 'C:/project' : '/project'
            $exportPath = $Os -eq 'windows' ? 'C:/project/build/empty-project.exe' : '/project/build/empty-project.exe'
            $volume = New-TestResourceName -Prefix 'project'
            $importContainer = $null
            $exportContainer = $null

            try {
                Initialize-ProjectVolume -Volume $volume -ProjectDirectory $projectDirectory

                $mount = @('--mount', "type=volume,source=$volume,target=$projectDirectory", '--workdir', $projectDirectory)
                $import = Invoke-GodotContainer `
                    -GodotVersion $GodotVersion `
                    -Command @('godot', '--headless', '--verbose', '--quit', '--editor', '--import') `
                    -AdditionalRunArguments $mount `
                    -Keep
                $importContainer = $import.Container

                $expectedImportExitCode = $Os -eq 'windows' -and $GodotVersion -in @('4.0', '4.1', '4.2') ? 1 : 0
                if ($import.ExitCode -ne $expectedImportExitCode) {
                    throw "Project import returned $($import.ExitCode), expected $expectedImportExitCode.`n$($import.Output | Out-String)"
                }

                $export = Invoke-GodotContainer `
                    -GodotVersion $GodotVersion `
                    -Command @('godot', '--headless', '--verbose', '--export-release', 'Windows Desktop', 'build/empty-project.exe') `
                    -AdditionalRunArguments $mount `
                    -Keep
                $exportContainer = $export.Container
                if ($export.ExitCode -ne 0) {
                    throw "Windows export returned $($export.ExitCode).`n$($export.Output | Select-Object -Last 50 | Out-String)"
                }

                if ($Os -eq 'windows') {
                    Invoke-Docker -Context $Context -RunArguments $DockerRunArguments -Arguments @(
                        'run', '--rm', '--mount', "type=volume,source=$volume,target=$projectDirectory", $Image,
                        'pwsh', '-NoLogo', '-NoProfile', '-NonInteractive', '-Command',
                        "if (-not (Test-Path -LiteralPath '$exportPath')) { exit 1 }"
                    )
                } else {
                    Invoke-Docker -Context $Context -RunArguments $DockerRunArguments -Arguments @(
                        'run', '--rm', '--mount', "type=volume,source=$volume,target=$projectDirectory", $Image,
                        'test', '-f', $exportPath
                    )
                }
            } finally {
                Remove-TestContainer -Container $importContainer
                Remove-TestContainer -Container $exportContainer
                Remove-TestVolume -Volume $volume
            }
        }
    }
}
