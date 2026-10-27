# SPDX-FileCopyrightText: 2026 J1mmyHuang
# SPDX-License-Identifier: AGPL-3.0-or-later WITH AdditionRef-RobocopyGUI-Microsoft-Runtime-Exception

param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$sdkVersion = & dotnet --version
if ($LASTEXITCODE -ne 0 -or -not $sdkVersion.StartsWith("10.")) {
    throw ".NET 10 SDK is required to build RobocopyGUI. Detected: $sdkVersion"
}

$publishRoot = Join-Path $PSScriptRoot "publish"

# The license, its section 7 permission and the third-party notices travel with every build.
# (The classic WinForms UI is archived at tag classic-winforms; build it from there with .\build.ps1 -Classic.)
function Copy-LicenseFiles([string]$destination) {
    Copy-Item (Join-Path $PSScriptRoot "LICENSE"), (Join-Path $PSScriptRoot "LICENSE-EXCEPTION.txt"), (Join-Path $PSScriptRoot "THIRD-PARTY-NOTICES.md") $destination
    Copy-Item (Join-Path $PSScriptRoot "licenses") $destination -Recurse
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

Copy-LicenseFiles $output

# The archive keeps the RobocopyGUI folder as its root, so extracting it doesn't scatter files.
Compress-Archive -Path $output -DestinationPath $archive -Force

Write-Host "Build complete: $output\RobocopyGUI.exe" -ForegroundColor Green
Write-Host "Release archive: $archive" -ForegroundColor Green
