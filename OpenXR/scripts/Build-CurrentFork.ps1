param(
    [Parameter(Mandatory)][string]$ValheimDir,
    [string]$BaseVHVRArchive,
    [string]$OpenXRPackage,
    [string]$Destination,
    [string]$UpstreamCommit = 'd78db84588c166431652c6fc50378e62b876079c',
    [string]$ReleaseVersion = '0.3.2'
)
$ErrorActionPreference = 'Stop'
$openxrRoot = Split-Path $PSScriptRoot -Parent
$repoRoot = Split-Path $openxrRoot -Parent
$forkPolicy = Get-Content -LiteralPath (Join-Path $openxrRoot 'FORK-CHANGES.json') -Raw | ConvertFrom-Json
$allowedChanges = @($forkPolicy.changes.path)
function AssertPluginVersion([string]$PluginSource, [string]$ExpectedVersion) {
    $attribute = [regex]::Match([IO.File]::ReadAllText($PluginSource), '\[BepInPlugin\("nikami\.openxr",\s*"[^"]+",\s*"([^"]+)"\)\]')
    if (!$attribute.Success -or $attribute.Groups[1].Value -ne $ExpectedVersion) { throw 'ReleaseVersion must match the OpenXR plugin version in source.' }
}
AssertPluginVersion (Join-Path $openxrRoot 'src/nikami-openxr/OpenXRPlugin.cs') $ReleaseVersion
$cache = Join-Path $env:LOCALAPPDATA 'VHVR-OpenXR/dependencies'
if (!$Destination) { $Destination = Join-Path $openxrRoot 'dist/current-fork' }
if (!$BaseVHVRArchive) {
    $BaseVHVRArchive = Join-Path $cache 'vhvr-v0.10.5.zip'
    if (!(Test-Path -LiteralPath $BaseVHVRArchive)) {
        New-Item -ItemType Directory -Path $cache -Force | Out-Null
        Invoke-WebRequest 'https://github.com/brandonmousseau/vhvr-mod/releases/download/v0.10.5/vhvr.zip' -OutFile $BaseVHVRArchive -UseBasicParsing
    }
}
if ((Get-FileHash -LiteralPath $BaseVHVRArchive).Hash -ne 'D7BCE3CD3663F0B8AF2A3044050A63F7A5417B409431E3363A744C2DC2D41003') { throw 'Expected the official VHVR v0.10.5 dependency archive.' }
if (!$OpenXRPackage) {
    $unityCache = Join-Path $cache 'Unity-OpenXR-1.16.1'
    $OpenXRPackage = Join-Path $unityCache 'package'
    if (!(Test-Path -LiteralPath (Join-Path $OpenXRPackage 'package.json'))) {
        New-Item -ItemType Directory -Path $unityCache -Force | Out-Null
        $unityArchive = Join-Path $unityCache 'openxr.tgz'
        Invoke-WebRequest 'https://download.packages.unity.com/com.unity.xr.openxr/-/com.unity.xr.openxr-1.16.1.tgz' -OutFile $unityArchive -UseBasicParsing
        if ((Get-FileHash -LiteralPath $unityArchive -Algorithm SHA1).Hash -ne 'EF0033A586BF33CB236BFE77CB661FE1B2882748') { throw 'Unity package checksum failed.' }
        & tar -xf $unityArchive -C $unityCache
        if ($LASTEXITCODE) { throw 'Unity package extraction failed.' }
    }
}
$unityMetadata = Get-Content -LiteralPath (Join-Path $OpenXRPackage 'package.json') -Raw | ConvertFrom-Json
if ($unityMetadata.name -ne 'com.unity.xr.openxr' -or $unityMetadata.version -ne '1.16.1') { throw 'Expected Unity OpenXR 1.16.1.' }
if (Test-Path -LiteralPath (Join-Path $repoRoot '.git')) {
    $sourceCommit = (& git -C $repoRoot rev-parse $UpstreamCommit).Trim()
    if ($LASTEXITCODE) { throw 'Upstream commit is unavailable in this checkout.' }
    & git -C $repoRoot merge-base --is-ancestor $sourceCommit HEAD
    if ($LASTEXITCODE) { throw 'Checkout does not contain the selected upstream revision.' }
    $upstreamChanges = @(& git -C $repoRoot diff --name-only $sourceCommit -- ValheimVRMod Unity/ValheimVR/Assets/SteamVR Unity/ValheimVR/Assets/SteamVR_Input Unity/ValheimVR/Assets/AssetBundles Unity/ValheimVR/Assets/StreamingAssets/SteamVR)
    if ($upstreamChanges | Where-Object { $_ -notin $allowedChanges }) { throw 'Unexpected changes outside the documented backend refactor.' }
    $upstreamChanges = $allowedChanges
} else {
    # The corresponding source ZIP has no Git history; its recorded revision
    # and exact adapter/correction source hashes provide the archive contract.
    $savedSource = Get-Content -LiteralPath (Join-Path $openxrRoot 'SOURCE-PROVENANCE.json') -Raw | ConvertFrom-Json
    if ($savedSource.upstreamCommit -ne $UpstreamCommit) { throw 'Source archive revision differs from the selected build revision.' }
    $sourceCommit = $savedSource.upstreamCommit
    foreach ($entry in $savedSource.sources) {
        if ((Get-FileHash -LiteralPath (Join-Path $openxrRoot ('src/nikami-openxr/' + $entry.name))).Hash -ne $entry.sha256) { throw ('Source archive hash mismatch: ' + $entry.name) }
    }
    foreach ($entry in $savedSource.forkCorrections) {
        if ((Get-FileHash -LiteralPath (Join-Path $repoRoot $entry.path)).Hash -ne $entry.sha256) { throw ('Source archive correction mismatch: ' + $entry.path) }
    }
    foreach ($entry in $savedSource.sharedSources) {
        if ((Get-FileHash -LiteralPath (Join-Path $repoRoot $entry.path)).Hash -ne $entry.sha256) { throw ('Shared source archive hash mismatch: ' + $entry.path) }
    }
    $upstreamChanges = @($savedSource.forkCorrections.path)
}
$payload = Join-Path $Destination 'openxr'
New-Item -ItemType Directory -Path $payload -Force | Out-Null
$manifestFiles = [ordered]@{}
function CopyPayload([string]$Source, [string]$Relative) {
    $target = [IO.Path]::GetFullPath((Join-Path $payload $Relative))
    if (!$target.StartsWith([IO.Path]::GetFullPath($payload).TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Payload target outside intended destination.' }
    New-Item -ItemType Directory -Path (Split-Path $target -Parent) -Force | Out-Null
    Copy-Item -LiteralPath $Source -Destination $target -Force
    $manifestFiles[$Relative.Replace('\','/')] = $target
}
function AssertPayloadFiles([string]$PayloadRoot, [System.Collections.IDictionary]$ExpectedFiles) {
    $prefix = [IO.Path]::GetFullPath($PayloadRoot).TrimEnd('\') + '\'
    $expected = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($relative in $ExpectedFiles.Keys) { [void]$expected.Add($relative.Replace('\', '/')) }
    foreach ($file in Get-ChildItem -LiteralPath $PayloadRoot -File -Recurse -Force) {
        $relative = $file.FullName.Substring($prefix.Length).Replace('\', '/')
        if (!$expected.Contains($relative)) { throw ('Unmanifested payload file; use a clean build destination: ' + $relative) }
    }
}
# Use only redistributed VHVR runtime dependencies. Owned Valheim assemblies,
# BepInEx loader files, existing user settings and QA helpers never enter here.
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead($BaseVHVRArchive)
try {
    foreach ($entry in $archive.Entries) {
        if (!$entry.Name -or !($entry.FullName.StartsWith('Valheim_Data/') -or $entry.FullName.StartsWith('BepInEx/plugins/'))) { continue }
        $target = [IO.Path]::GetFullPath((Join-Path $payload $entry.FullName))
        if (!$target.StartsWith([IO.Path]::GetFullPath($payload).TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Archive path escapes payload.' }
        New-Item -ItemType Directory -Path (Split-Path $target -Parent) -Force | Out-Null
        [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $target, $true)
        $manifestFiles[$entry.FullName] = $target
    }
} finally { $archive.Dispose() }
& dotnet build (Join-Path $openxrRoot 'tools/VHVR.Gameplay/VHVR.Gameplay.csproj') -c Release "-p:ValheimDir=$ValheimDir" "-p:VHVRSourceRoot=$repoRoot" --nologo -v:q -clp:ErrorsOnly
if ($LASTEXITCODE) { throw 'Current VHVR gameplay/controller build failed.' }
$managed = Join-Path $openxrRoot 'dist/build-managed'
New-Item -ItemType Directory -Path $managed -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $openxrRoot 'tools/SteamVR.Runtime/bin/Release/net472/SteamVR.dll') -Destination $managed -Force
Copy-Item -LiteralPath (Join-Path $openxrRoot 'tools/SteamVR.Actions/bin/Release/net472/SteamVR_Actions.dll') -Destination $managed -Force
Copy-Item -LiteralPath (Join-Path $openxrRoot 'tools/VHVR.Gameplay/bin/Release/net472/ValheimVRMod.dll') -Destination $managed -Force
& dotnet build (Join-Path $openxrRoot 'src/nikami-openxr/nikami-openxr.csproj') -c Release "-p:ValheimDir=$ValheimDir" "-p:GameplayManagedDir=$managed" "-p:OpenXRPackage=$OpenXRPackage" -p:ImportDirectoryBuildTargets=false --nologo -v:q -clp:ErrorsOnly
if ($LASTEXITCODE) { throw 'Current XR companion build failed.' }
CopyPayload (Join-Path $openxrRoot 'tools/VHVR.Gameplay/bin/Release/net472/ValheimVRMod.dll') 'BepInEx/plugins/ValheimVRMod.dll'
CopyPayload (Join-Path $managed 'SteamVR.dll') 'Valheim_Data/Managed/SteamVR.dll'
CopyPayload (Join-Path $managed 'SteamVR_Actions.dll') 'Valheim_Data/Managed/SteamVR_Actions.dll'
foreach ($file in Get-ChildItem -LiteralPath (Join-Path $repoRoot 'Unity/ValheimVR/Assets/AssetBundles') -File | Where-Object {$_.Extension -ne '.meta'}) { CopyPayload $file.FullName ('Valheim_Data/StreamingAssets/' + $file.Name) }
foreach ($file in Get-ChildItem -LiteralPath (Join-Path $repoRoot 'Unity/ValheimVR/Assets/StreamingAssets/SteamVR') -Filter '*.json' -File) { CopyPayload $file.FullName ('Valheim_Data/StreamingAssets/SteamVR/' + $file.Name) }
CopyPayload (Join-Path $openxrRoot 'src/nikami-openxr/bin/Release/netstandard2.1/Nikami.OpenXR.dll') 'BepInEx/plugins/Nikami.OpenXR/Nikami.OpenXR.dll'
CopyPayload (Join-Path $openxrRoot 'tools/OpenXR.Runtime/bin/Release/netstandard2.1/Unity.XR.OpenXR.dll') 'Valheim_Data/Managed/Unity.XR.OpenXR.dll'
CopyPayload (Join-Path $OpenXRPackage 'Runtime/windows/x64/UnityOpenXR.dll') 'Valheim_Data/Plugins/x86_64/UnityOpenXR.dll'
CopyPayload (Join-Path $OpenXRPackage 'RuntimeLoaders/windows/x64/openxr_loader.dll') 'Valheim_Data/Plugins/x86_64/openxr_loader.dll'
CopyPayload (Join-Path $OpenXRPackage 'Runtime/UnitySubsystemsManifest.json') 'Valheim_Data/UnitySubsystems/UnityOpenXR/UnitySubsystemsManifest.json'
AssertPayloadFiles $payload $manifestFiles
$sources = foreach ($file in Get-ChildItem -LiteralPath (Join-Path $openxrRoot 'src/nikami-openxr') -Filter '*.cs' -File) { [pscustomobject]@{name=$file.Name;sha256=(Get-FileHash -LiteralPath $file.FullName).Hash} }
$corrections = foreach ($path in $upstreamChanges) { [pscustomobject]@{path=$path;sha256=(Get-FileHash -LiteralPath (Join-Path $repoRoot $path)).Hash;reason=($forkPolicy.changes | Where-Object {$_.path -eq $path}).reason} }
$sharedSources = foreach ($file in Get-ChildItem -LiteralPath (Join-Path $openxrRoot 'src/shared-gameplay') -Filter '*.cs' -File) { [pscustomobject]@{path=('OpenXR/src/shared-gameplay/' + $file.Name);sha256=(Get-FileHash -LiteralPath $file.FullName).Hash} }
$provenance = [ordered]@{upstreamCommit=$sourceCommit;adapter_sha256=(Get-FileHash -LiteralPath (Join-Path $payload 'BepInEx/plugins/Nikami.OpenXR/Nikami.OpenXR.dll')).Hash;gameplay_sha256=(Get-FileHash -LiteralPath (Join-Path $payload 'BepInEx/plugins/ValheimVRMod.dll')).Hash;forkCorrections=@($corrections);sources=@($sources);sharedSources=@($sharedSources)}
[IO.File]::WriteAllText((Join-Path $openxrRoot 'SOURCE-PROVENANCE.json'), (($provenance | ConvertTo-Json -Depth 6).Replace("`r`n", "`n") + "`n"), (New-Object Text.UTF8Encoding($false)))
Copy-Item -LiteralPath (Join-Path $openxrRoot 'SOURCE-PROVENANCE.json') -Destination $Destination -Force
$files = foreach ($relative in $manifestFiles.Keys) { [pscustomobject]@{path=$relative;sha256=(Get-FileHash -LiteralPath $manifestFiles[$relative]).Hash} }
$manifestJson = [ordered]@{releaseTag=('openxr-v' + $ReleaseVersion);adapterVersion=$ReleaseVersion;backends=@('openxr','openvr');defaultBackend='openxr';unityOpenXR='1.16.1';testedValheim='1.0.16';upstreamCommit=$sourceCommit;upstreamChannel='master';baseDependencyRelease='v0.10.5';sourceManifestSha256=(Get-FileHash -LiteralPath (Join-Path $openxrRoot 'SOURCE-PROVENANCE.json')).Hash;files=@($files)} | ConvertTo-Json -Depth 6
[IO.File]::WriteAllText((Join-Path $Destination 'openxr-manifest.json'), ($manifestJson.Replace("`r`n", "`n") + "`n"), (New-Object Text.UTF8Encoding($false)))
$licenses = Join-Path $Destination 'openxr-licenses'
New-Item -ItemType Directory -Path $licenses -Force | Out-Null
foreach ($name in @('LICENSE.md', 'Third Party Notices.md')) {
    $notice = Join-Path $OpenXRPackage $name
    if (Test-Path -LiteralPath $notice) { Copy-Item -LiteralPath $notice -Destination $licenses -Force }
}
Write-Output ('Built current fork payload: ' + $files.Count + ' files, upstream ' + $sourceCommit)
