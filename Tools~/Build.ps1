<#
.SYNOPSIS
Builds TarragonFly.Analyzers in Release and copies the DLL into Analyzers/ for Unity.

.DESCRIPTION
Builds the whole solution (analyzer and tests) with the .NET SDK. The analyzer build is
deterministic, so the DLL only gets copied when its bytes change. Any new DLL makes Unity recompile
every assembly in the project, and that's slow enough to avoid when nothing changed.

Exit codes: 0 built, 1 build failed.

.EXAMPLE
& Packages/com.tarragonfly.analyzers/Tools~/Build.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$package = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $package 'Source~/TarragonFly.Analyzers.sln'

& dotnet build $solution -c Release -nologo -v q
if ($LASTEXITCODE -ne 0) {
    Write-Error 'Build failed.' -ErrorAction Continue
    exit 1
}

$built = Join-Path $package 'Source~/src/TarragonFly.Analyzers/bin/Release/netstandard2.0/TarragonFly.Analyzers.dll'
$installed = Join-Path $package 'Analyzers/TarragonFly.Analyzers.dll'
$sameBytes = (Test-Path -LiteralPath $installed) -and
    ((Get-FileHash -LiteralPath $built).Hash -eq (Get-FileHash -LiteralPath $installed).Hash)
if ($sameBytes) {
    Write-Host 'Analyzers/TarragonFly.Analyzers.dll is up to date.'
}
else {
    [void](New-Item -ItemType Directory -Force -Path (Split-Path -Parent $installed))
    Copy-Item -LiteralPath $built -Destination $installed -Force
    Write-Host 'Copied the new TarragonFly.Analyzers.dll into Analyzers/; Unity recompiles on its next refresh.'
}
exit 0
