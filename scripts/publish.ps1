param(
    [string]$Configuration = "Release",
    [switch]$SkipTests
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

if (-not $SkipTests) {
    Write-Host "Running tests..."
    dotnet test .\tests\TaskbarBanner.Core.Tests\TaskbarBanner.Core.Tests.csproj -c $Configuration
    if ($LASTEXITCODE -ne 0) { throw "Tests failed." }
}

Write-Host "Publishing single-file executable..."
dotnet publish .\src\TaskbarBanner.App\TaskbarBanner.App.csproj `
    -c $Configuration `
    -r win-x64 `
    --self-contained `
    -p:PublishSingleFile=true `
    -o .\artifacts\publish\win-x64
if ($LASTEXITCODE -ne 0) { throw "Publish failed." }

Write-Host ""
Write-Host "Done: artifacts\publish\win-x64\TaskbarBanner.App.exe"
