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

BeforeAll {
    . (Join-Path $PSScriptRoot '../.jenkins/Docker.ps1')

    function New-TestResourceName {
        return "docker-godot-contract-$([guid]::NewGuid().ToString('N'))"
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
}

Describe "Image command contract [$Os, $Image]" {
    It 'declares the Jenkins Docker Pipeline-compatible command metadata' {
        $result = Get-DockerCommandResult -Context $Context -Arguments @('image', 'inspect', $Image)
        $result.ExitCode | Should -Be 0

        $inspection = @($result.Output | Out-String | ConvertFrom-Json)
        $inspection.Count | Should -Be 1
        $entrypointProperty = $inspection[0].Config.PSObject.Properties['Entrypoint']
        $entrypoint = $null -eq $entrypointProperty ? @() : @($entrypointProperty.Value)

        $entrypoint.Count | Should -Be 0
        ConvertTo-Json -Compress -InputObject @($inspection[0].Config.Cmd) |
            Should -Be '["godot","help"]'
    }

    It 'allows the Jenkins keeper command to replace Godot startup' {
        $container = New-TestResourceName
        $keeper = $Os -eq 'windows' ? 'cmd.exe' : 'cat'

        try {
            Invoke-Docker -Context $Context -Arguments @(
                'run', '--detach', '--tty', '--name', $container, $Image, $keeper
            )

            $inspection = Invoke-DockerOutput -Context $Context -Arguments @('container', 'inspect', $container) |
                ConvertFrom-Json
            $inspection.State.Running | Should -BeTrue
            ConvertTo-Json -Compress -InputObject @($inspection.Config.Cmd) |
                Should -Be (ConvertTo-Json -Compress -InputObject @($keeper))

            if ($Os -eq 'windows') {
                $result = Get-DockerCommandResult -Context $Context -Arguments @(
                    'container', 'exec', $container, 'cmd.exe', '/S', '/C', 'echo docker-pipeline-keeper-ok'
                )
            } else {
                $result = Get-DockerCommandResult -Context $Context -Arguments @(
                    'container', 'exec', $container, 'sh', '-c', 'printf docker-pipeline-keeper-ok'
                )
            }

            $result.ExitCode | Should -Be 0
            ($result.Output | Out-String) | Should -Match 'docker-pipeline-keeper-ok'
        } finally {
            Remove-TestContainer -Container $container
        }
    }
}
