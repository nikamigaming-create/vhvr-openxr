param([string]$ScratchDirectory = (Join-Path $PSScriptRoot '../dist/payload-checks'))
$ErrorActionPreference = 'Stop'

# Run the builder's actual guard without building game assemblies or downloading dependencies.
$builder = Join-Path $PSScriptRoot '../scripts/Build-CurrentFork.ps1'
$tokens = $null
$parseErrors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile($builder, [ref]$tokens, [ref]$parseErrors)
if ($parseErrors.Count) { throw 'Build script failed to parse.' }
$guard = $ast.Find({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'AssertPayloadFiles' }, $true)
if (!$guard) { throw 'Missing production payload guard.' }
. ([scriptblock]::Create($guard.Extent.Text))
$versionGuard = $ast.Find({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'AssertPluginVersion' }, $true)
if (!$versionGuard) { throw 'Missing production version guard.' }
. ([scriptblock]::Create($versionGuard.Extent.Text))
$fixture = Join-Path $ScratchDirectory ([guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixture -Force | Out-Null
$pluginSource = Join-Path $fixture 'OpenXRPlugin.cs'
[IO.File]::WriteAllText($pluginSource, '[BepInPlugin("nikami.openxr", "VHVR Backends", "1.2.3")]')
AssertPluginVersion $pluginSource '1.2.3'
$versionRejected = $false
try { AssertPluginVersion $pluginSource '1.2.4' }
catch {
    if ($_.Exception.Message -ne 'ReleaseVersion must match the OpenXR plugin version in source.') { throw }
    $versionRejected = $true
}
if (!$versionRejected) { throw 'Builder accepted a mismatched plugin version.' }

$payload = Join-Path $fixture 'payload'
$plugin = Join-Path $payload 'BepInEx/plugins/expected.dll'
New-Item -ItemType Directory -Path (Split-Path $plugin -Parent) -Force | Out-Null
[IO.File]::WriteAllText($plugin, 'fixture')
$expected = [ordered]@{'bepinex/PLUGINS/expected.dll' = $plugin}
AssertPayloadFiles $payload $expected

$extra = Join-Path $payload 'BepInEx/plugins/stale.dll'
[IO.File]::WriteAllText($extra, 'not in manifest')
$rejected = $false
try { AssertPayloadFiles $payload $expected }
catch {
    if ($_.Exception.Message -notlike 'Unmanifested payload file*stale.dll') { throw }
    $rejected = $true
}
if (!$rejected) { throw 'Builder accepted an unmanifested plugin.' }
[IO.File]::SetAttributes($extra, [IO.FileAttributes]::Hidden)
$hiddenRejected = $false
try { AssertPayloadFiles $payload $expected }
catch {
    if ($_.Exception.Message -notlike 'Unmanifested payload file*stale.dll') { throw }
    $hiddenRejected = $true
}
if (!$hiddenRejected) { throw 'Builder accepted a hidden unmanifested plugin.' }
if (!(Test-Path -LiteralPath $plugin) -or !(Test-Path -LiteralPath $extra)) { throw 'Validation modified fixture files.' }
Write-Output 'PASS: matching version accepted; mismatched version rejected; listed/case-insensitive file accepted; stale and hidden plugins rejected; files preserved.'
