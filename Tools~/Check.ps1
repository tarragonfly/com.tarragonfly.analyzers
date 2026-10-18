<#
.SYNOPSIS
Builds, tests and checks TarragonFly.Analyzers in one command.

.DESCRIPTION
Runs three steps. A failed build stops there; the other steps run even if an earlier one failed.
  1. Build.ps1: Release build, with the DLL copied into Analyzers/ when it changed.
  2. dotnet test: the analyzer tests.
  3. Parity: runs the corpus in Source~/parity/corpus/ through the TF rules and the oracle analyzers
     still in the Unity project (Assets/Plugins/MicrosoftUnityAnalyzers)
     and writes Source~/parity/out/parity.md. With -Project it runs Analyze.ps1 on the Unity project
     first (close the project in Unity) and adds those findings to the report.

The Unity project is -ProjectPath if given, else the project the package sits in
(<project>/Packages/<package>), else the current directory if that's a Unity project. Without one,
parity still runs, just with no oracles to compare against. Differences in the parity report don't
fail the check; they get sorted out rule by rule.

Exit codes: 0 every step passed, 1 a step failed.

.EXAMPLE
& Packages/com.tarragonfly.analyzers/Tools~/Check.ps1

.EXAMPLE
& Tools~/Check.ps1 -ProjectPath C:\Projects\MyGame -Project
#>
[CmdletBinding()]
param(
    # Unity project that supplies the oracle analyzers and, with -Project, the findings.
    [string] $ProjectPath,
    # Also run Analyze.ps1 on the Unity project and compare its findings.
    [switch] $Project
)

$ErrorActionPreference = 'Stop'
$package = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $package 'Source~/TarragonFly.Analyzers.sln'
$parityTool = Join-Path $package 'Source~/tools/Parity/bin/Release/net8.0/TarragonFly.Analyzers.Parity.dll'
$report = Join-Path $package 'Source~/parity/out/parity.md'
$failed = New-Object System.Collections.Generic.List[string]

function Test-UnityProject([string] $Path) {
    return $Path -and (Test-Path -LiteralPath (Join-Path $Path 'ProjectSettings/ProjectVersion.txt'))
}

if ($ProjectPath) {
    $ProjectPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($ProjectPath)
    if (-not (Test-UnityProject $ProjectPath)) { throw "-ProjectPath $ProjectPath is not a Unity project." }
}
elseif (Test-UnityProject (Split-Path -Parent (Split-Path -Parent $package))) { $ProjectPath = Split-Path -Parent (Split-Path -Parent $package) }
elseif (Test-UnityProject (Get-Location).Path) { $ProjectPath = (Get-Location).Path }
if ($Project -and -not $ProjectPath) { throw '-Project needs a Unity project; pass -ProjectPath.' }

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
    Write-Warning 'Build failed; skipping the other steps.'
    exit 1
}

Invoke-Step 'Tests' { & dotnet test $solution -c Release --no-build -nologo -v q }

$parityArguments = @('--out', $report, '--corpus', (Join-Path $package 'Source~/parity/corpus'))
if ($ProjectPath) {
    foreach ($oracle in @('Assets/Plugins/MicrosoftUnityAnalyzers/Microsoft.Unity.Analyzers.dll')) {
        $path = Join-Path $ProjectPath $oracle
        if (Test-Path -LiteralPath $path) { $parityArguments += @('--oracle', $path) }
    }
}
else {
    Write-Warning 'No Unity project found; parity runs without the oracle analyzers.'
}
if ($Project) {
    $analyzeOutput = Join-Path $ProjectPath 'Logs/analyzers'
    Invoke-Step 'Analyze project' { & (Join-Path $PSScriptRoot 'Analyze.ps1') -ProjectPath $ProjectPath -OutputDir $analyzeOutput }
    $parityArguments += @('--findings', (Join-Path $analyzeOutput 'findings.json'))
}
Invoke-Step 'Parity' { & dotnet $parityTool @parityArguments }

if ($failed.Count -gt 0) {
    Write-Warning "Failed: $($failed -join ', ')"
    exit 1
}
Write-Host 'All checks passed.'
exit 0
