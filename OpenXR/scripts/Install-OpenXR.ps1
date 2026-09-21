param([string]$GameDirectory = $PSScriptRoot)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$game=[IO.Path]::GetFullPath($GameDirectory)
if(Get-Process valheim -ErrorAction SilentlyContinue) { throw 'Save and quit Valheim before installing OpenXR.' }
$manifest=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'openxr-manifest.json') -Raw | ConvertFrom-Json
$payload=Join-Path $PSScriptRoot 'openxr'
function Inside([string]$root,[string]$relative) {
    $prefix=[IO.Path]::GetFullPath($root).TrimEnd('\')+'\'
    $path=[IO.Path]::GetFullPath((Join-Path $root $relative))
    if(!$path.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase)){throw 'Invalid OpenXR payload path.'}
    $path
}
foreach($file in $manifest.files) {
    $source=Inside $payload $file.path
    if(!(Test-Path -LiteralPath $source) -or (Get-FileHash -LiteralPath $source).Hash -ne $file.sha256){throw "OpenXR payload verification failed: $($file.path)"}
}
$changed=@($manifest.files | Where-Object {
    $target=Inside $game $_.path
    !(Test-Path -LiteralPath $target) -or (Get-FileHash -LiteralPath $target).Hash -ne $_.sha256
})
if(!$changed.Count){
    # A manually updated payload may already match while its receipt is absent
    # or still uses an older schema. Publish the manifest we just verified.
    $manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $game 'nikami-openxr.json') -Encoding UTF8
    Write-Host 'Nikami native OpenXR is current.'
    return
}
$backup=Join-Path (Split-Path $game -Parent) ('Nikami-Backups\OpenXR\'+(Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
$existed=@{}
foreach($file in $changed) {
    $target=Inside $game $file.path
    $existed[$file.path]=Test-Path -LiteralPath $target
    if($existed[$file.path]) {
        $copy=Inside $backup $file.path
        New-Item -ItemType Directory -Path (Split-Path $copy -Parent) -Force | Out-Null
        Copy-Item -LiteralPath $target -Destination $copy
        if((Get-FileHash -LiteralPath $target).Hash -ne (Get-FileHash -LiteralPath $copy).Hash){throw 'OpenXR backup failed.'}
    }
}
try {
    foreach($file in $changed) {
        $target=Inside $game $file.path
        New-Item -ItemType Directory -Path (Split-Path $target -Parent) -Force | Out-Null
        Copy-Item -LiteralPath (Inside $payload $file.path) -Destination $target -Force
        if((Get-FileHash -LiteralPath $target).Hash -ne $file.sha256){throw 'OpenXR installation verification failed.'}
    }
    $manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $game 'nikami-openxr.json') -Encoding UTF8
    Write-Host 'Installed Nikami native OpenXR. Uses your active OpenXR runtime.'
} catch {
    foreach($file in $changed) {
        $target=Inside $game $file.path
        if($existed[$file.path]) { Copy-Item -LiteralPath (Inside $backup $file.path) -Destination $target -Force }
        elseif(Test-Path -LiteralPath $target) { Remove-Item -LiteralPath $target }
    }
    throw
}
