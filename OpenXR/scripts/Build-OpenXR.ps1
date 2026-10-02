param(
    [string]$ValheimDir = 'D:\SteamLibrary\steamapps\common\Valheim',
    [string]$OpenXRPackage,
    [string]$Destination,
    [string]$BaseVHVRArchive,
    [string]$UpstreamCommit,
    [string]$ReleaseVersion
)
$ErrorActionPreference = 'Stop'
# Preserve the old entry point; build the matching gameplay and both providers
# through the same source/provenance checks as every current-fork build.
$buildArguments = @{} + $PSBoundParameters
$buildArguments.ValheimDir = $ValheimDir
if (!$Destination) {
    $buildArguments.Destination = Join-Path (Split-Path $PSScriptRoot -Parent) 'dist/openxr-runtime'
}
& (Join-Path $PSScriptRoot 'Build-CurrentFork.ps1') @buildArguments
