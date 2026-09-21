param(
    [string]$ValheimDir='D:\SteamLibrary\steamapps\common\Valheim',
    [string]$OpenXRPackage,
    [string]$Destination
)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
if(!$Destination){$Destination=Join-Path $repo 'dist\openxr-runtime'}
if(!$OpenXRPackage){
    $cache=Join-Path $env:LOCALAPPDATA 'Nikami\OpenXR\1.16.1'
    $OpenXRPackage=Join-Path $cache 'package'
    if(!(Test-Path -LiteralPath (Join-Path $OpenXRPackage 'package.json'))){
        New-Item -ItemType Directory -Path $cache -Force | Out-Null
        $archive=Join-Path $cache 'openxr.tgz'
        Invoke-WebRequest 'https://download.packages.unity.com/com.unity.xr.openxr/-/com.unity.xr.openxr-1.16.1.tgz' -OutFile $archive -UseBasicParsing
        if((Get-FileHash -LiteralPath $archive -Algorithm SHA1).Hash -ne 'EF0033A586BF33CB236BFE77CB661FE1B2882748'){throw 'Unity package checksum failed.'}
        tar -xf $archive -C $cache
        if($LASTEXITCODE){throw 'Unity package extraction failed.'}
    }
}
$metadata=Get-Content -LiteralPath (Join-Path $OpenXRPackage 'package.json') -Raw | ConvertFrom-Json
if($metadata.name -ne 'com.unity.xr.openxr' -or $metadata.version -ne '1.16.1'){throw 'Expected the official Unity OpenXR 1.16.1 package.'}
dotnet build (Join-Path $repo 'src\nikami-openxr\nikami-openxr.csproj') -c Release "-p:ValheimDir=$ValheimDir" "-p:OpenXRPackage=$OpenXRPackage" --nologo -v:q -clp:ErrorsOnly
if($LASTEXITCODE){throw 'OpenXR adapter build failed.'}
New-Item -ItemType Directory -Path $Destination -Force | Out-Null
$files=[ordered]@{
    'BepInEx/plugins/Nikami.OpenXR/Nikami.OpenXR.dll'=(Join-Path $repo 'src\nikami-openxr\bin\Release\netstandard2.1\Nikami.OpenXR.dll')
    'Valheim_Data/Managed/Unity.XR.OpenXR.dll'=(Join-Path $repo 'tools\OpenXR.Runtime\bin\Release\netstandard2.1\Unity.XR.OpenXR.dll')
    'Valheim_Data/Plugins/x86_64/UnityOpenXR.dll'=(Join-Path $OpenXRPackage 'Runtime\windows\x64\UnityOpenXR.dll')
    'Valheim_Data/Plugins/x86_64/openxr_loader.dll'=(Join-Path $OpenXRPackage 'RuntimeLoaders\windows\x64\openxr_loader.dll')
    'Valheim_Data/UnitySubsystems/UnityOpenXR/UnitySubsystemsManifest.json'=(Join-Path $OpenXRPackage 'Runtime\UnitySubsystemsManifest.json')
}
$entries=foreach($relative in $files.Keys){
    $target=Join-Path $Destination ('openxr\'+$relative)
    New-Item -ItemType Directory -Path (Split-Path $target -Parent) -Force | Out-Null
    Copy-Item -LiteralPath $files[$relative] -Destination $target -Force
    [pscustomobject]@{path=$relative;sha256=(Get-FileHash -LiteralPath $target).Hash}
}
@{adapterVersion='0.1.0';unityOpenXR='1.16.1';testedUpstream='v0.10.3';files=@($entries)} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $Destination 'openxr-manifest.json') -Encoding UTF8
$licenses=Join-Path $Destination 'openxr-licenses'
New-Item -ItemType Directory -Path $licenses -Force | Out-Null
foreach($name in @('LICENSE.md','Third Party Notices.md')) { if(Test-Path -LiteralPath (Join-Path $OpenXRPackage $name)){Copy-Item -LiteralPath (Join-Path $OpenXRPackage $name) -Destination $licenses -Force} }
Write-Output $Destination
