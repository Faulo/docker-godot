[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateRange(1, [int]::MaxValue)]
    [int] $MajorVersion
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$installed = Get-Module -ListAvailable -Name Pester |
    Where-Object { $_.Version.Major -eq $MajorVersion } |
    Sort-Object Version -Descending |
    Select-Object -First 1

if ($null -eq $installed) {
    $minimumVersion = [Version]::new($MajorVersion, 0, 0)
    $maximumVersion = [Version]::new($MajorVersion, 999, 999)
    Install-Module -Name Pester `
        -MinimumVersion $minimumVersion `
        -MaximumVersion $maximumVersion `
        -Scope CurrentUser `
        -Force `
        -SkipPublisherCheck
}
