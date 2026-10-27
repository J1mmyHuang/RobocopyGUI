param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    # Build the original WinForms version (single-file EXE) instead of the WinUI 3 app.
    [switch]$Classic
)

$ErrorActionPreference = "Stop"
$sdkVersion = & dotnet --version
if ($LASTEXITCODE -ne 0 -or -not $sdkVersion.StartsWith("10.")) {
    throw ".NET 10 SDK is required to build RobocopyGUI. Detected: $sdkVersion"
}

$publishRoot = Join-Path $PSScriptRoot "publish"

if ($Classic) {
    $project = Join-Path $PSScriptRoot "RobocopyGui.csproj"
    $output = Join-Path $publishRoot "classic"

    dotnet publish $project -c $Configuration -r win-x64 --self-contained false `
        -p:PublishSingleFile=true -p:EnableWindowsTargeting=true -o $output

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed with exit code $LASTEXITCODE."
    }

    Write-Host "Build complete: $output\RobocopyGUI.exe" -ForegroundColor Green
    return
}

$project = Join-Path $PSScriptRoot "RobocopyGui.WinUI\RobocopyGui.WinUI.csproj"
$output = Join-Path $publishRoot "RobocopyGUI"
$archive = Join-Path $publishRoot "RobocopyGUI-win-x64.zip"

# Start from an empty folder so files left by an older build don't end up in the archive.
if (Test-Path $output) {
    Remove-Item $output -Recurse -Force
}

dotnet publish $project -c $Configuration -r win-x64 --self-contained false `
    -p:DebugType=None -p:DebugSymbols=false -o $output

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

# The archive keeps the RobocopyGUI folder as its root, so extracting it doesn't scatter files.
Compress-Archive -Path $output -DestinationPath $archive -Force

Write-Host "Build complete: $output\RobocopyGUI.exe" -ForegroundColor Green
Write-Host "Release archive: $archive" -ForegroundColor Green
