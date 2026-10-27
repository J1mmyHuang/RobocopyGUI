param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$sdkVersion = & dotnet --version
if ($LASTEXITCODE -ne 0 -or -not $sdkVersion.StartsWith("10.")) {
    throw ".NET 10 SDK is required to build RobocopyGUI. Detected: $sdkVersion"
}

$project = Join-Path $PSScriptRoot "RobocopyGui.csproj"
$output = Join-Path $PSScriptRoot "publish"

dotnet publish $project -c $Configuration -r win-x64 --self-contained false `
    -p:PublishSingleFile=true -p:EnableWindowsTargeting=true -o $output

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

Write-Host "Build complete: $output\RobocopyGUI.exe" -ForegroundColor Green
