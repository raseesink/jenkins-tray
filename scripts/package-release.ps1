<# Publishes the self-contained Windows x64 distribution. Requires PowerShell 7 and .NET 10. #>
[CmdletBinding()]
param([string]$repoRoot = (Split-Path $PSScriptRoot -Parent))
$ErrorActionPreference = 'Stop'
Push-Location $repoRoot
try {
    $env:AVALONIA_TELEMETRY_OPTOUT = '1'
    dotnet restore src/JenkinsTray.Windows/JenkinsTray.Windows.csproj --locked-mode
    if ($LASTEXITCODE -ne 0) { throw 'Locked restore failed.' }
    dotnet publish src/JenkinsTray.Windows/JenkinsTray.Windows.csproj -c Release -r win-x64 --self-contained true --no-restore --disable-build-servers -p:PublishTrimmed=false -o artifacts/win-x64
    if ($LASTEXITCODE -ne 0) { throw 'Windows publish failed.' }
    if (!(Test-Path artifacts/win-x64/JenkinsTray.exe)) { throw 'Published executable is missing.' }
    Compress-Archive -Path artifacts/win-x64/* -DestinationPath artifacts/JenkinsTray-win-x64.zip -Force
    Write-Host 'Created artifacts/JenkinsTray-win-x64.zip'
} finally { Pop-Location }
