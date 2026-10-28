[CmdletBinding()]
param([string] $TempRoot = [IO.Path]::GetTempPath())

Set-StrictMode -Version 3
$ErrorActionPreference = 'Stop'
$scriptPath = Join-Path $PSScriptRoot 'Analyze.ps1'
$tokens = $null
$parseErrors = $null
$syntax = [Management.Automation.Language.Parser]::ParseFile($scriptPath, [ref]$tokens, [ref]$parseErrors)
if ($parseErrors.Count -gt 0) { throw ($parseErrors | Out-String) }
$names = @('Test-ReportedDiagnostic', 'Test-FailingFinding', 'Get-MissingAssemblySettings', 'Test-ImportedPath')
foreach ($name in $names) {
    $function = $syntax.Find({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name }, $true)
    if ($null -eq $function) { throw "Missing function: $name" }
    . ([scriptblock]::Create($function.Extent.Text))
}

function Assert-Equal($Actual, $Expected, [string] $Check) {
    if ($Actual -ne $Expected) { throw "${Check}: expected '$Expected', got '$Actual'." }
}

$changed = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
Assert-Equal (Test-ReportedDiagnostic 'note' 'Assets/Player.cs' $null) $true 'Informational diagnostic'
Assert-Equal (Test-ReportedDiagnostic 'error' 'Assets/Player.cs' $null) $false 'Compiler errors are listed separately'
Assert-Equal (Test-ReportedDiagnostic 'none' 'Assets/Player.cs' $null) $false 'Hidden diagnostic'
Assert-Equal (Test-ReportedDiagnostic 'warning' 'Assets/Player.cs' $changed) $false 'Empty changed-file set'
[void]$changed.Add('Assets/Player.cs')
Assert-Equal (Test-ReportedDiagnostic 'warning' 'Assets/Player.cs' $changed) $true 'Changed source'
Assert-Equal (Test-ReportedDiagnostic 'warning' 'Assets/Other.cs' $changed) $false 'Unchanged source'
Assert-Equal (Test-FailingFinding 'note') $false 'Info findings never fail the run'
Assert-Equal (Test-FailingFinding 'warning') $true 'Warnings fail the run'

$scratch = Join-Path $TempRoot ([guid]::NewGuid().ToString('N'))
try {
    [void][IO.Directory]::CreateDirectory((Join-Path $scratch 'Assets/Game'))
    [void][IO.Directory]::CreateDirectory((Join-Path $scratch 'Packages/Local'))
    $since = [datetime]::UtcNow.AddMinutes(-5)
    $paths = @('Assets/csc.rsp', 'Assets/Game/Shared.asmref', 'Packages/Local/Game.asmdef', 'Assets/Game/Player.cs', 'Packages/Hidden~/Ignored.asmref')
    $missing = @(Get-MissingAssemblySettings $paths $scratch $since)
    Assert-Equal $missing.Count 3 'Deleted assembly settings'
    [IO.File]::WriteAllText((Join-Path $scratch 'Assets/csc.rsp'), '')
    Assert-Equal @(Get-MissingAssemblySettings $paths $scratch $since).Count 2 'Existing response file'
    (Get-Item -LiteralPath (Join-Path $scratch 'Assets/Game')).LastWriteTimeUtc = $since.AddMinutes(-1)
    (Get-Item -LiteralPath (Join-Path $scratch 'Packages/Local')).LastWriteTimeUtc = $since.AddMinutes(-1)
    Assert-Equal @(Get-MissingAssemblySettings $paths $scratch $since).Count 0 'Unity already checked the removal'
    [IO.File]::WriteAllText((Join-Path $scratch 'Assets/Game/Player.cs'), '')
    Assert-Equal @(Get-MissingAssemblySettings @('Assets/Game/Shared.asmref') $scratch $since).Count 0 'A saved file explains the folder change'
    Assert-Equal @(Get-MissingAssemblySettings @('Assets/Game/Shared.asmref') $scratch $since -AfterCompile).Count 1 'Removal committed after the compile'
}
finally {
    if (Test-Path -LiteralPath $scratch) { [IO.Directory]::Delete($scratch, $true) }
}
Write-Host 'Replay script tests passed.'
