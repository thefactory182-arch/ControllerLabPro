param([switch]$SkipPublish)
$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
dotnet restore (Join-Path $projectRoot 'ControllerLabPro.slnx')
dotnet build (Join-Path $projectRoot 'ControllerLabPro.slnx') -c Release --no-restore
if (-not $SkipPublish) {
    $publishDir = Join-Path $projectRoot 'artifacts\publish'
    dotnet publish (Join-Path $projectRoot 'src\ControllerLabPro\ControllerLabPro.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -o $publishDir
    Write-Host "Published ControllerLab Pro to $publishDir"
}
