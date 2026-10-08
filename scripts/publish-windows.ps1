param(
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$Runtime = 'win-x64',
    [string]$Output = 'artifacts\BatomonTracker'
)

$project = Join-Path $PSScriptRoot '..\src\BatomonTracker.App\BatomonTracker.App.csproj'
$outputPath = Join-Path $PSScriptRoot "..\$Output"

dotnet publish $project `
    --configuration Release `
    --runtime $Runtime `
    --self-contained true `
    --output $outputPath `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None `
    -p:DebugSymbols=false

if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

Write-Host "Published Batomon Tracker to $outputPath"
