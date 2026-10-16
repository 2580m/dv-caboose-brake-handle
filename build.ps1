param(
    [string]$GameDir = 'W:\SteamLibrary\steamapps\common\Derail Valley',
    [switch]$Install
)
$ErrorActionPreference = 'Stop'
$projectDir = $PSScriptRoot
$env:DOTNET_CLI_HOME = Join-Path $projectDir '.build-home'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
if (!(Test-Path -LiteralPath (Join-Path $GameDir 'DerailValley_Data\Managed\UnityModManager\UnityModManager.dll'))) {
    throw 'GameDir must point to a Derail Valley installation with Unity Mod Manager.'
}
dotnet build (Join-Path $projectDir 'dv-caboose-break.csproj') -c Release "-p:GameDir=$GameDir" --ignore-failed-sources -v minimal
if ($LASTEXITCODE -ne 0) { throw 'Mod build failed.' }
dotnet build (Join-Path $projectDir 'tools\CompatibilityProbe.csproj') -c Release --ignore-failed-sources -v minimal
if ($LASTEXITCODE -ne 0) { throw 'Probe build failed.' }
$assembly = Join-Path $projectDir 'bin\Release\net48\DVCabooseBreak.dll'
dotnet (Join-Path $projectDir 'tools\bin\Release\net9.0\CompatibilityProbe.dll') $GameDir $assembly
if ($LASTEXITCODE -ne 0) { throw 'Compatibility/pneumatic verification failed.' }
$dist = Join-Path $projectDir 'dist'
$package = Join-Path $dist 'dv-caboose-break'
New-Item -ItemType Directory -Path $package -Force | Out-Null
Copy-Item -LiteralPath $assembly -Destination $package
Copy-Item -LiteralPath (Join-Path $projectDir 'package\Info.json') -Destination $package
Copy-Item -LiteralPath (Join-Path $projectDir 'README.md') -Destination $package
$version = (Get-Content -LiteralPath (Join-Path $projectDir 'package\Info.json') -Raw | ConvertFrom-Json).Version
$zip = Join-Path $dist "dv-caboose-break-$version.zip"
Compress-Archive -LiteralPath $package -DestinationPath $zip -Force
if ($Install) {
    $destination = Join-Path $GameDir 'Mods\dv-caboose-break'
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    foreach ($file in Get-ChildItem -LiteralPath $package -File) {
        Copy-Item -LiteralPath $file.FullName -Destination $destination
    }
    Write-Output "Installed: $destination"
}
Write-Output "Package: $zip"
