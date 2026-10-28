<#
.SYNOPSIS
Builds and tests TarragonFly.Analyzers in one command.

.DESCRIPTION
Runs three steps. A failed build stops there.
  1. Build.ps1: Release build, with the DLL copied into Analyzers/ when it changed.
  2. Test-Analyze.ps1: replay reporting and removed-settings checks.
  3. dotnet test: the analyzer tests.

Exit codes: 0 every step passed, 1 a step failed.

.EXAMPLE
& Packages/com.tarragonfly.analyzers/Tools~/Check.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$package = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $package 'Source~/TarragonFly.Analyzers.sln'
$failed = New-Object System.Collections.Generic.List[string]

function Invoke-Step([string] $Name, [scriptblock] $Step) {
    Write-Host "== $Name"
    $global:LASTEXITCODE = 0
    try { & $Step }
    catch {
        Write-Warning "${Name}: $($_.Exception.Message)"
        $failed.Add($Name)
        return
    }
    if ($LASTEXITCODE -ne 0) { $failed.Add($Name) }
}

Invoke-Step 'Build' { & (Join-Path $PSScriptRoot 'Build.ps1') }
if ($failed.Count -gt 0) {
    Write-Warning 'Build failed; skipping the tests.'
    exit 1
}

Invoke-Step 'Replay script tests' { & (Join-Path $PSScriptRoot 'Test-Analyze.ps1') }
Invoke-Step 'Tests' { & dotnet test $solution -c Release --no-build -nologo -v q }

if ($failed.Count -gt 0) {
    Write-Warning "Failed: $($failed -join ', ')"
    exit 1
}
Write-Host 'All checks passed.'
exit 0
