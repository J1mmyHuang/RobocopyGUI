param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$project = Join-Path $PSScriptRoot "RobocopyGui.csproj"
$output = Join-Path $PSScriptRoot "publish"

dotnet publish $project -c $Configuration -r win-x64 --self-contained false `
    -p:PublishSingleFile=true -p:EnableWindowsTargeting=true -o $output

Write-Host "Build complete: $output\RobocopyGui.exe" -ForegroundColor Green
