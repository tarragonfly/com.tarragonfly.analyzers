<#
.SYNOPSIS
Runs the Roslyn analyzers of a Unity project without opening the editor.

.DESCRIPTION
Every script compile, Unity writes one compiler response file per assembly
(Library/Bee/artifacts/<dag>/<Assembly>.rsp). This script replays them with the editor's
own C# compiler, so you get the same analyzers, rule sets and defines Unity used. Findings
come out as SARIF plus a short summary.

It only writes to <OutputDir>; Library/ and the project files stay untouched. The reported
assemblies get rebuilt along with every editable assembly they depend on (anything outside
Library/PackageCache), in dependency order and against each other's fresh output. That way
edits made since Unity's last compile are checked across assembly boundaries too.

Some changes still need one Unity compile first: new or deleted .cs files, and edited
.asmdef, .asmref or csc.rsp files. Until Unity has compiled them, the run reports itself
incomplete (exit 1). New files in assemblies the editor leaves out on purpose (define
constraints or platforms) don't count. -OutputDir can't be inside Assets/ or Packages/,
since Unity would import the built DLLs as plugins.

A package installed from a "file:" path outside the project (a local clone next to it) is
compiled from absolute paths. Its files count as present when they exist on disk, and it gets
rebuilt like any other editable assembly. New files and asmdef edits inside it still aren't
noticed; see the known limits.

Exit codes:
  0  analysis complete, and no warnings remain or -FailOnFindings was not given
  1  analysis incomplete: an assembly did not compile, was skipped because a dependency
     did not compile, an analyzer crashed, failed to load or needs a newer compiler
     (AD0001, CS8032, CS8034, CS8784, CS8785, CS9057) in a reported assembly, a .cs file
     of a built assembly was added or deleted or any .asmdef, .asmref or csc.rsp file changed
     since Unity's last compile, or -Changed could not get the changed files from git. -Rule and -Changed never hide these.
  2  analysis complete, -FailOnFindings was given and warnings remain after the filters.
     Info findings (SARIF level 'note') are listed but don't fail the run.

What it promises: for normal work (editing, adding or deleting .cs files, editing assembly
settings, an analyzer crashing or failing to load) a run never calls itself complete when it
isn't. Other ways Unity's response files can go stale are known limits, listed below. When in
doubt, let Unity compile once (open the editor, or a batch run with -quit) and run again.

Known limits:
  - Removed compiler response files are found through git history and folder timestamps. A deleted
    untracked csc.rsp, one removed while git was unavailable, or an uncommitted removal in a folder
    where a file was saved since, needs a Unity compile first.
  - Not detected: Packages/manifest.json changes, Scripting Define Symbols or other
    ProjectSettings edits, and a different editor version than the one that last compiled.
  - In a "file:" package outside the project, only deleted .cs files are detected; added
    files and edited .asmdef, .asmref or csc.rsp files there need a Unity compile.
  - Assembly settings are compared by timestamp with Unity's last compile check, so a git
    checkout or a save without changes reads as incomplete until Unity checks again.
  - Define constraints are checked against Assembly-CSharp's defines. An asmdef's own
    version defines aren't evaluated for assemblies Unity didn't compile.

Tested with Windows PowerShell 5.1.

.EXAMPLE
& Packages/com.tarragonfly.analyzers/Tools~/Analyze.ps1

.EXAMPLE
& Packages/com.tarragonfly.analyzers/Tools~/Analyze.ps1 -Assembly ScenarioEngine.* -Rule TF1007,UNT*

.EXAMPLE
& Packages/com.tarragonfly.analyzers/Tools~/Analyze.ps1 -Changed -FailOnFindings
#>
[CmdletBinding()]
param(
    # Unity project root. Default: the current directory if it is a project, else the project above this script.
    [string] $ProjectPath,
    # Unity.exe or its Editor folder. Default: UNITY_EDITOR_PATH, then Unity Hub install folders.
    [string] $EditorPath = $env:UNITY_EDITOR_PATH,
    # Assembly names to report, wildcards allowed. Default: every assembly with sources under Assets/.
    [string[]] $Assembly = @(),
    # Rule IDs to report, wildcards allowed. Default: all.
    [string[]] $Rule = @(),
    # Report only findings in files changed against git HEAD, plus untracked files.
    [switch] $Changed,
    # Also report assemblies whose sources all live outside Assets/ (packages).
    [switch] $IncludePackages,
    # Extra analyzer DLLs to load. An existing analyzer with the same filename is replaced for this run.
    [string[]] $Analyzer = @(),
    # Extra Roslyn additional files, for example an analyzer settings file.
    [string[]] $AdditionalFile = @(),
    # Rule set that replaces each assembly's own one, for audits with more rules on.
    [string] $RuleSet,
    # Where SARIF, the summary and the build output go. Default: <project>/Logs/analyzers.
    [string] $OutputDir,
    # Exit with code 2 when any warning remains after the filters. Info findings don't count.
    [switch] $FailOnFindings
)

Set-StrictMode -Version 3
$ErrorActionPreference = 'Stop'
$utf8 = New-Object System.Text.UTF8Encoding($false)

# Diagnostics that mean an analyzer or source generator did not run to completion.
# AD0001 analyzer threw; CS8032 could not create it; CS8034 could not load the DLL; CS8784 and
# CS8785 source generator failed; CS9057 the DLL needs a newer compiler than this editor's.
$analyzerFailureIds = @('AD0001', 'CS8032', 'CS8034', 'CS8784', 'CS8785', 'CS9057')

function Test-ReportedDiagnostic([string] $Level, [string] $File, $ChangedFiles) {
    if ($Level -eq 'error' -or $Level -eq 'none') { return $false }
    return $null -eq $ChangedFiles -or $ChangedFiles.Contains($File)
}

# Info findings are worth listing, but too soft to fail a pre-commit check.
function Test-FailingFinding([string] $Level) {
    return $Level -eq 'warning' -or $Level -eq 'error'
}

# -AfterCompile is for deletions git dates after Unity's last compile. Any other deletion counts
# when its folder changed since then, unless a file saved there since could explain that change.
function Get-MissingAssemblySettings([string[]] $Paths, [string] $Project, [datetime] $Since, [switch] $AfterCompile) {
    foreach ($path in @($Paths | Sort-Object -Unique)) {
        if ($path -notmatch '^(Assets|Packages)/' -or $path -notmatch '(\.asmdef|\.asmref|(^|/)csc\.rsp)$' -or -not (Test-ImportedPath $path)) { continue }
        $fullPath = Join-Path $Project $path
        if (Test-Path -LiteralPath $fullPath) { continue }
        if ($AfterCompile) {
            [pscustomobject]@{ File = $path; Change = 'removed' }
            continue
        }
        $folder = Split-Path -Parent $fullPath
        while ($folder -and -not (Test-Path -LiteralPath $folder)) { $folder = Split-Path -Parent $folder }
        if (-not $folder -or (Get-Item -LiteralPath $folder).LastWriteTimeUtc -le $Since) { continue }
        if (@(Get-ChildItem -LiteralPath $folder -File | Where-Object { $_.LastWriteTimeUtc -gt $Since }).Count -gt 0) { continue }
        [pscustomobject]@{ File = $path; Change = 'removed' }
    }
}

# Runs git in the project folder and returns its output lines, or throws with git's own message.
function Get-GitLines([string[]] $Arguments) {
    $ErrorActionPreference = 'Continue'
    $output = [string[]]@(& git -C $project @Arguments 2>&1 | ForEach-Object { "$_" })
    $exitCode = $LASTEXITCODE
    $ErrorActionPreference = 'Stop'
    if ($exitCode -ne 0) {
        $reason = @($output | Where-Object { $_ -match '^(fatal|error):' } | Select-Object -First 1)
        if ($reason.Count -eq 0) { $reason = @($output | Select-Object -First 1) }
        throw "git $($Arguments -join ' ') failed (exit $exitCode): $($reason -join '')"
    }
    return $output
}

function Test-UnityProject([string] $Path) {
    return $Path -and (Test-Path -LiteralPath (Join-Path $Path 'ProjectSettings/ProjectVersion.txt'))
}

function Resolve-ProjectPath {
    if ($ProjectPath) {
        if (-not (Test-UnityProject $ProjectPath)) { throw "$ProjectPath is not a Unity project (no ProjectSettings/ProjectVersion.txt)." }
        return (Resolve-Path -LiteralPath $ProjectPath).Path
    }
    $here = (Get-Location).Path
    if (Test-UnityProject $here) { return $here }
    $dir = $PSScriptRoot
    while ($dir) {
        if (Test-UnityProject $dir) { return $dir }
        $parent = Split-Path -Parent $dir
        if ($parent -eq $dir) { break }
        $dir = $parent
    }
    throw 'Run this from a Unity project folder or pass -ProjectPath.'
}

function Resolve-EditorFolder([string] $Project) {
    $text = [IO.File]::ReadAllText((Join-Path $Project 'ProjectSettings/ProjectVersion.txt'))
    if ($text -notmatch '(?m)^m_EditorVersion: (\S+)') { throw 'ProjectVersion.txt has no editor version.' }
    $version = $Matches[1]
    $candidates = @()
    if ($EditorPath) { $candidates += $EditorPath }
    $hubPathFile = Join-Path $env:APPDATA 'UnityHub/secondaryInstallPath.json'
    if (Test-Path -LiteralPath $hubPathFile) {
        $hubRoot = ([IO.File]::ReadAllText($hubPathFile)).Trim().Trim('"') -replace '\\\\', '\'
        if ($hubRoot) { $candidates += (Join-Path $hubRoot "$version/Editor") }
    }
    $candidates += (Join-Path $env:ProgramFiles "Unity/Hub/Editor/$version/Editor")
    foreach ($candidate in $candidates) {
        $folder = $candidate
        if ($folder -like '*.exe') { $folder = Split-Path -Parent $folder }
        if (Test-Path -LiteralPath (Join-Path $folder 'Data')) { return @{ Folder = $folder; Version = $version } }
    }
    throw "Unity $version was not found. Set UNITY_EDITOR_PATH or pass -EditorPath."
}

function Find-Compiler([string] $EditorFolder) {
    $data = Join-Path $EditorFolder 'Data'
    # Unity 6000.4 and later ship a .NET SDK; 6000.0 to 6000.3 ship Roslyn and a separate runtime.
    $sdkCsc = Get-ChildItem -Path (Join-Path $data 'DotNetSdk/sdk') -Filter csc.dll -Recurse -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match '[\\/]Roslyn[\\/]bincore[\\/]' } | Select-Object -First 1
    if ($sdkCsc) { return @{ Dotnet = (Join-Path $data 'DotNetSdk/dotnet.exe'); Csc = $sdkCsc.FullName } }
    $roslynCsc = Join-Path $data 'DotNetSdkRoslyn/csc.dll'
    if (Test-Path -LiteralPath $roslynCsc) { return @{ Dotnet = (Join-Path $data 'NetCoreRuntime/dotnet.exe'); Csc = $roslynCsc } }
    throw "No C# compiler found under $data."
}

function Get-RelativePath([string] $Root, [string] $Path) {
    $full = [IO.Path]::GetFullPath($Path)
    $rootFull = [IO.Path]::GetFullPath($Root).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    if ($full.StartsWith($rootFull, [StringComparison]::OrdinalIgnoreCase)) { $full = $full.Substring($rootFull.Length) }
    return $full -replace '\\', '/'
}

function Read-ResponseFile([string] $File) {
    $lines = [IO.File]::ReadAllLines($File)
    $sources = @($lines | Where-Object { $_ -match '^"[^-].*\.cs"$' } | ForEach-Object { $_.Trim('"') -replace '\\', '/' })
    $refs = @($lines | Where-Object { $_ -match '^-r:"(.*)"$' } | ForEach-Object { $Matches[1] -replace '\\', '/' })
    return @{ Lines = $lines; Sources = $sources; References = $refs }
}

# -Filter *.rsp also matches .rsp2 on Windows; Unity's .rsp2 and .mvfrm.rsp files are not compiler inputs.
function Test-CompilerResponseFile($File) {
    return $File.Extension -eq '.rsp' -and $File.Name -notlike '*.mvfrm.rsp'
}

function Test-MatchesAnyPattern([string] $Value, [string[]] $Patterns) {
    if ($Patterns.Count -eq 0) { return $true }
    foreach ($p in $Patterns) { if ($Value -like $p) { return $true } }
    return $false
}

# Names of the assemblies Unity built in the same graph that this assembly references.
function Get-GraphReferenceNames($Info) {
    $names = @()
    foreach ($ref in $Info.References) {
        if ($ref -notlike "$dagRelative/*") { continue }
        $refName = [IO.Path]::GetFileName($ref) -replace '\.ref\.dll$', '' -replace '\.dll$', ''
        if ($compiledAssemblies.ContainsKey($refName)) { $names += $refName }
    }
    return $names
}

# Library/PackageCache only changes when Unity resolves packages, which recompiles them.
function Test-EditableAssembly($Info) {
    return @($Info.Sources | Where-Object { $_ -notlike 'Library/PackageCache/*' }).Count -gt 0
}

# Unity writes project files relative to the project and files of a "file:" package outside it
# with their absolute path, so a rooted source is one that lives outside the project.
function Get-SourceFullPath([string] $Source) {
    if ([IO.Path]::IsPathRooted($Source)) { return $Source }
    return Join-Path $project $Source
}

function Test-OutsideProject($Info) {
    return @($Info.Sources | Where-Object { -not [IO.Path]::IsPathRooted($_) }).Count -eq 0
}

function Get-SarifLocation($Result) {
    $location = @{ File = ''; Line = 0; Column = 0 }
    if ($Result.PSObject.Properties['locations'] -and @($Result.locations).Count -gt 0) {
        $physical = $Result.locations[0].physicalLocation
        $uri = [Uri]$physical.artifactLocation.uri
        if ($uri.IsAbsoluteUri) { $location.File = Get-RelativePath $project $uri.LocalPath } else { $location.File = "$uri" -replace '\\', '/' }
        $location.Line = [int]$physical.region.startLine
        if ($physical.region.PSObject.Properties['startColumn']) { $location.Column = [int]$physical.region.startColumn }
    }
    return $location
}

# Relative paths mean "relative to where the caller is". Resolve them now: .NET file calls use the
# process directory and the compiler runs from the project root, and neither is the caller's location.
function Get-AbsolutePath([string] $Path) {
    return $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Path)
}
function Resolve-ExistingFile([string] $Path, [string] $Parameter) {
    $full = Get-AbsolutePath $Path
    if (-not (Test-Path -LiteralPath $full -PathType Leaf)) { throw "${Parameter}: $Path was not found." }
    return $full
}

$project = Resolve-ProjectPath
$editor = Resolve-EditorFolder $project
$compiler = Find-Compiler $editor.Folder
if (-not $OutputDir) { $OutputDir = Join-Path $project 'Logs/analyzers' }
$OutputDir = Get-AbsolutePath $OutputDir
# Unity imports anything under Assets/ or Packages/, so the built DLLs would become plugins in the project.
foreach ($unityFolder in @('Assets', 'Packages')) {
    $root = (Join-Path $project $unityFolder).TrimEnd('\', '/')
    if ($OutputDir.TrimEnd('\', '/').Equals($root, [StringComparison]::OrdinalIgnoreCase) -or
        $OutputDir.StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "-OutputDir $OutputDir is inside $unityFolder/, where Unity would import the built DLLs as plugins. Use a folder such as Logs/ instead."
    }
}
if ($RuleSet) { $RuleSet = Resolve-ExistingFile $RuleSet '-RuleSet' }
$Analyzer = @($Analyzer | ForEach-Object { Resolve-ExistingFile $_ '-Analyzer' })
$AdditionalFile = @($AdditionalFile | ForEach-Object { Resolve-ExistingFile $_ '-AdditionalFile' })
$buildDir = Join-Path $OutputDir 'build'
$sarifDir = Join-Path $OutputDir 'sarif'
$logDir = Join-Path $OutputDir 'compiler'
foreach ($dir in @($buildDir, $sarifDir, $logDir)) { [void](New-Item -ItemType Directory -Force -Path $dir) }
Get-ChildItem -LiteralPath $sarifDir -Filter *.sarif | Remove-Item -Force
Get-ChildItem -LiteralPath $logDir -Filter *.txt | Remove-Item -Force

# Unity keeps one build graph per target; the editor one has UNITY_EDITOR defined.
$artifacts = Join-Path $project 'Library/Bee/artifacts'
if (-not (Test-Path -LiteralPath $artifacts)) { throw 'Library/Bee/artifacts is missing. Open the project in Unity once so it compiles.' }
$dag = Get-ChildItem -LiteralPath $artifacts -Directory -Filter *.dag | Where-Object {
    $probe = Get-ChildItem -LiteralPath $_.FullName -Filter *.rsp | Where-Object { Test-CompilerResponseFile $_ } | Select-Object -First 1
    $probe -and (Select-String -LiteralPath $probe.FullName -Pattern '^-define:UNITY_EDITOR$' -Quiet)
} | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $dag) { throw 'No editor compile found in Library/Bee/artifacts. Open the project in Unity once so it compiles.' }
$dagRelative = Get-RelativePath $project $dag.FullName

# Load every assembly Unity compiled, then pick the ones to report.
$compiledAssemblies = @{}
foreach ($rsp in Get-ChildItem -LiteralPath $dag.FullName -Filter *.rsp | Where-Object { Test-CompilerResponseFile $_ }) {
    $name = [IO.Path]::GetFileNameWithoutExtension($rsp.Name)
    $info = Read-ResponseFile $rsp.FullName
    $info.Name = $name
    $info.InAssets = @($info.Sources | Where-Object { $_ -like 'Assets/*' }).Count -gt 0
    $compiledAssemblies[$name] = $info
}
$reportedAssemblies = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
foreach ($info in $compiledAssemblies.Values) {
    if (($IncludePackages -or $info.InAssets) -and $info.Sources.Count -gt 0 -and (Test-MatchesAnyPattern $info.Name $Assembly)) {
        [void]$reportedAssemblies.Add($info.Name)
    }
}
if ($reportedAssemblies.Count -eq 0) { throw 'No assemblies matched.' }

# Rebuild every editable assembly the reported ones depend on, so they are checked against current code.
$assembliesToBuild = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
$assembliesToScan = New-Object System.Collections.Generic.Queue[string]
foreach ($name in $reportedAssemblies) { [void]$assembliesToBuild.Add($name); $assembliesToScan.Enqueue($name) }
while ($assembliesToScan.Count -gt 0) {
    $name = $assembliesToScan.Dequeue()
    foreach ($refName in (Get-GraphReferenceNames $compiledAssemblies[$name])) {
        if ($assembliesToBuild.Contains($refName) -or -not (Test-EditableAssembly $compiledAssemblies[$refName])) { continue }
        [void]$assembliesToBuild.Add($refName)
        $assembliesToScan.Enqueue($refName)
    }
}

# Files Unity has not compiled yet, and compiled files that are gone. Either way Unity's response
# files no longer match the code, so the assembly they belong to cannot be analyzed correctly.
$compiled = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
$compiledSourceOwner = @{}
foreach ($info in $compiledAssemblies.Values) {
    foreach ($s in $info.Sources) { [void]$compiled.Add($s); $compiledSourceOwner[$s] = $info.Name }
}

# Unity skips folders ending in ~ and hidden folders, so their asmdefs and scripts do not count.
function Test-ImportedPath([string] $RelativePath) {
    return $RelativePath -notmatch '(^|/)[^/]*~(/|$)' -and $RelativePath -notmatch '(^|/)\.[^/]+/'
}
$sourceRoots = @('Assets', 'Packages') | ForEach-Object { Join-Path $project $_ } | Where-Object { Test-Path -LiteralPath $_ }
$onDisk = @(Get-ChildItem -LiteralPath $sourceRoots -Filter *.cs -Recurse -File | ForEach-Object { Get-RelativePath $project $_.FullName } |
    Where-Object { Test-ImportedPath $_ })
$notCompiled = @($onDisk | Where-Object { -not $compiled.Contains($_) })
$missing = @($compiled | Where-Object { $_ -notlike 'Library/*' -and -not (Test-Path -LiteralPath (Get-SourceFullPath $_)) })

# Defines of the editor compile, for checking asmdef define constraints. Version defines that an
# asmdef adds for itself are not evaluated.
$editorDefines = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
$defineSource = $compiledAssemblies['Assembly-CSharp']
if (-not $defineSource) { $defineSource = @($compiledAssemblies.Values)[0] }
foreach ($line in $defineSource.Lines) { if ($line -match '^-define:(.+)$') { [void]$editorDefines.Add($Matches[1].Trim()) } }

# "A || B" passes when any part passes; "!A" passes when A is not defined.
function Test-DefineConstraint([string] $Constraint) {
    foreach ($part in ($Constraint -split '\|\|')) {
        $symbol = $part.Trim()
        if ($symbol.StartsWith('!')) { if (-not $editorDefines.Contains($symbol.Substring(1).Trim())) { return $true } }
        elseif ($editorDefines.Contains($symbol)) { return $true }
    }
    return $false
}

# Whether Unity's editor compile includes an assembly definition, given its platforms and define constraints.
function Test-CompiledInEditor($Definition) {
    $include = @(); $exclude = @(); $constraints = @()
    if ($Definition.PSObject.Properties['includePlatforms']) { $include = @($Definition.includePlatforms) }
    if ($Definition.PSObject.Properties['excludePlatforms']) { $exclude = @($Definition.excludePlatforms) }
    if ($Definition.PSObject.Properties['defineConstraints']) { $constraints = @($Definition.defineConstraints) }
    if ($include.Count -gt 0 -and $include -notcontains 'Editor') { return $false }
    if ($exclude -contains 'Editor') { return $false }
    foreach ($constraint in $constraints) { if (-not (Test-DefineConstraint $constraint)) { return $false } }
    return $true
}

# Which assembly a new file will join: the nearest .asmdef or .asmref above it, else Unity's
# predefined assemblies (Assembly-CSharp, -Editor, -firstpass, -Editor-firstpass).
$assemblyByFolder = @{}
$asmdefNames = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
$excludedFromEditor = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
$asmdefNameByGuid = @{}
# -Include is ignored with -LiteralPath in Windows PowerShell 5.1, so list each kind separately.
$assemblySettingFiles = @(@('*.asmdef', '*.asmref', 'csc.rsp') | ForEach-Object { Get-ChildItem -LiteralPath $sourceRoots -Filter $_ -Recurse -File } |
    Where-Object { $_.Name -notlike '*.meta' -and (Test-ImportedPath (Get-RelativePath $project $_.FullName)) })
foreach ($file in @($assemblySettingFiles | Where-Object { $_.Extension -eq '.asmdef' })) {
    $definition = [IO.File]::ReadAllText($file.FullName) | ConvertFrom-Json
    [void]$asmdefNames.Add($definition.name)
    $assemblyByFolder[(Get-RelativePath $project $file.DirectoryName)] = $definition.name
    # Version defines can satisfy a constraint, so trust Unity for any assembly it did compile.
    if (-not $compiledAssemblies.ContainsKey($definition.name) -and -not (Test-CompiledInEditor $definition)) {
        [void]$excludedFromEditor.Add($definition.name)
    }
    $meta = "$($file.FullName).meta"
    if ((Test-Path -LiteralPath $meta) -and ([IO.File]::ReadAllText($meta) -match '(?m)^guid: ([0-9a-f]+)')) { $asmdefNameByGuid[$Matches[1]] = $definition.name }
}
foreach ($file in @($assemblySettingFiles | Where-Object { $_.Extension -eq '.asmref' })) {
    $reference = ([IO.File]::ReadAllText($file.FullName) | ConvertFrom-Json).reference
    if ($reference -like 'GUID:*') { $reference = $asmdefNameByGuid[$reference.Substring(5)] }
    if ($reference) { $assemblyByFolder[(Get-RelativePath $project $file.DirectoryName)] = $reference }
}

# Assembly settings edited after Unity's last compile: Unity's response files then describe the old
# references, defines or file layout, so nothing built from them can be trusted. Unity rewrites
# <graph>-inputdata.json only when the compile inputs change, but touches <graph>.dag_fsmtime on every
# compile check, so the newest of these is when Unity last looked at the settings files.
$graphName = [IO.Path]::GetFileNameWithoutExtension($dag.Name)
$compileMarkers = @(
    @("Library/Bee/$graphName-inputdata.json", "Library/Bee/$graphName.dag_fsmtime") | ForEach-Object { Join-Path $project $_ } |
        Where-Object { Test-Path -LiteralPath $_ } | ForEach-Object { Get-Item -LiteralPath $_ }
) + @(Get-ChildItem -LiteralPath $dag.FullName -Filter *.rsp)
$lastUnityCompile = ($compileMarkers | Sort-Object LastWriteTimeUtc | Select-Object -Last 1).LastWriteTimeUtc
$changedAssemblySettings = New-Object System.Collections.Generic.List[object]
try {
    # HEAD as it was at Unity's last compile, from the reflog, so pulled commits count whatever their
    # dates. Without that entry, commit dates are the fallback.
    $compiledHead = @()
    try { $compiledHead = @(Get-GitLines @('rev-parse', '--verify', '--quiet', "HEAD@{$($lastUnityCompile.ToString('o'))}") | Where-Object { $_ -match '^[0-9a-f]{40,64}$' }) }
    catch { }
    if ($compiledHead.Count -gt 0) {
        $committed = @(Get-GitLines @('diff', '--name-only', '--relative', '--diff-filter=D', $compiledHead[-1], 'HEAD', '--', 'Assets', 'Packages'))
        foreach ($setting in @(Get-MissingAssemblySettings $committed $project $lastUnityCompile -AfterCompile)) { $changedAssemblySettings.Add($setting) }
        $undated = @()
    }
    else {
        $undated = @(Get-GitLines @('log', '--relative', "--since=$($lastUnityCompile.ToString('o'))", '--format=', '--name-only', '--diff-filter=D', '--', 'Assets', 'Packages'))
    }
    # Staged and unstaged deletions, which git can't date.
    $undated += @(Get-GitLines @('diff', '--name-only', '--relative', '--diff-filter=D', 'HEAD', '--', 'Assets', 'Packages'))
    foreach ($setting in @(Get-MissingAssemblySettings $undated $project $lastUnityCompile)) { $changedAssemblySettings.Add($setting) }
}
catch {
    Write-Warning 'Deleted compiler response files cannot be checked without git history. Let Unity compile after removing one.'
}
foreach ($file in $assemblySettingFiles) {
    if ($file.LastWriteTimeUtc -gt $lastUnityCompile) {
        $changedAssemblySettings.Add([pscustomobject]@{ File = (Get-RelativePath $project $file.FullName); Change = 'changed' })
    }
}
# An asmdef outside the project (a "file:" package) is never scanned, so it can't read as removed.
foreach ($info in $compiledAssemblies.Values) {
    if ($info.Name -notlike 'Assembly-CSharp*' -and (Test-EditableAssembly $info) -and -not (Test-OutsideProject $info) -and -not $asmdefNames.Contains($info.Name)) {
        $changedAssemblySettings.Add([pscustomobject]@{ File = "$($info.Name).asmdef"; Change = 'removed' })
    }
}
function Get-OwningAssembly([string] $File) {
    $folder = $File
    while ($folder.Contains('/')) {
        $folder = $folder.Substring(0, $folder.LastIndexOf('/'))
        if ($assemblyByFolder.ContainsKey($folder)) { return $assemblyByFolder[$folder] }
    }
    $firstpass = $File -like 'Assets/Plugins/*' -or $File -like 'Assets/Standard Assets/*' -or $File -like 'Assets/Pro Standard Assets/*'
    $editor = $File -match '/Editor/'
    if ($editor -and $firstpass) { return 'Assembly-CSharp-Editor-firstpass' }
    if ($editor) { return 'Assembly-CSharp-Editor' }
    if ($firstpass) { return 'Assembly-CSharp-firstpass' }
    return 'Assembly-CSharp'
}

# Only assemblies the folder guess can produce are compared. A source Unity put in a package's
# asmdef through an asmref, which the scan above can't resolve, would otherwise always read as moved.
$changedOwners = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
foreach ($source in $compiledSourceOwner.Keys) {
    if ($source -notmatch '^(Assets|Packages)/' -or -not (Test-Path -LiteralPath (Get-SourceFullPath $source))) { continue }
    $previous = $compiledSourceOwner[$source]
    if (-not $asmdefNames.Contains($previous) -and $previous -notlike 'Assembly-CSharp*') { continue }
    $owner = Get-OwningAssembly $source
    if ($owner -ne $previous -and $changedOwners.Add("$previous/$owner")) {
        $changedAssemblySettings.Add([pscustomobject]@{ File = $source; Change = "assembly changed from $previous to $owner" })
    }
}

# Out-of-date files count against the analysis when they belong to an assembly this run builds,
# or to an assembly Unity has not built yet whose name matches -Assembly.
$staleSourceFiles = New-Object System.Collections.Generic.List[object]
$staleElsewhere = New-Object System.Collections.Generic.List[object]
$staleCandidates = @($notCompiled | ForEach-Object { [pscustomobject]@{ File = $_; Assembly = (Get-OwningAssembly $_); Change = 'added' } }) +
    @($missing | ForEach-Object { [pscustomobject]@{ File = $_; Assembly = $compiledSourceOwner[$_]; Change = 'deleted' } })
foreach ($stale in $staleCandidates) {
    # Unity leaves these out on purpose (platform or define constraint), so a recompile would not add them.
    if ($stale.Change -eq 'added' -and $excludedFromEditor.Contains($stale.Assembly)) { continue }
    $inScope = $assembliesToBuild.Contains($stale.Assembly) -or
        (-not $compiledAssemblies.ContainsKey($stale.Assembly) -and (Test-MatchesAnyPattern $stale.Assembly $Assembly))
    if ($inScope) { $staleSourceFiles.Add($stale) } else { $staleElsewhere.Add($stale) }
}

# Order the build so each assembly compiles after the ones it references.
$buildOrder = New-Object System.Collections.Generic.List[string]
$visitState = @{}
function Add-ToBuildOrder([string] $Name) {
    if ($visitState[$Name] -eq 2) { return }
    if ($visitState[$Name] -eq 1) { throw "Assembly reference cycle at $Name." }
    $visitState[$Name] = 1
    foreach ($refName in (Get-GraphReferenceNames $compiledAssemblies[$Name])) {
        if ($assembliesToBuild.Contains($refName)) { Add-ToBuildOrder $refName }
    }
    $visitState[$Name] = 2
    $buildOrder.Add($Name)
}
foreach ($name in ($assembliesToBuild | Sort-Object)) { Add-ToBuildOrder $name }

$compileFailures = New-Object System.Collections.Generic.List[object]
$skippedAssemblies = New-Object System.Collections.Generic.List[object]
$compilerOutput = @{}
$notBuilt = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
$started = Get-Date
Push-Location -LiteralPath $project
try {
    foreach ($name in $buildOrder) {
        $info = $compiledAssemblies[$name]
        $blockedBy = @(Get-GraphReferenceNames $info | Where-Object { $notBuilt.Contains($_) })
        if ($blockedBy.Count -gt 0) {
            $skippedAssemblies.Add([pscustomobject]@{ Assembly = $name; BlockedBy = ($blockedBy -join ', ') })
            [void]$notBuilt.Add($name)
            continue
        }
        $lines = New-Object System.Collections.Generic.List[string]
        foreach ($line in $info.Lines) {
            if ($line -match '^-out:') { $lines.Add("-out:`"$buildDir/$name.dll`""); continue }
            if ($line -match '^-refout:') { $lines.Add("-refout:`"$buildDir/$name.ref.dll`""); continue }
            if ($line -match '^-errorlog:') { continue }
            if ($RuleSet -and $line -match '^-ruleset:') { continue }
            if ($Analyzer.Count -gt 0 -and $line -match '^-analyzer:(.+)$') {
                $existing = $Matches[1].Trim('"')
                if (@($Analyzer | Where-Object { [IO.Path]::GetFileName($_) -eq [IO.Path]::GetFileName($existing) }).Count -gt 0) { continue }
            }
            if ($line -match '^-r:"(.*)"$') {
                $refPath = $Matches[1] -replace '\\', '/'
                $refName = [IO.Path]::GetFileName($refPath) -replace '\.ref\.dll$', ''
                if ($refPath -like "$dagRelative/*" -and $assembliesToBuild.Contains($refName)) {
                    $lines.Add("-r:`"$buildDir/$refName.ref.dll`""); continue
                }
            }
            $lines.Add($line)
        }
        if ($RuleSet) { $lines.Add("-ruleset:`"$RuleSet`"") }
        foreach ($dll in $Analyzer) { $lines.Add("-analyzer:`"$dll`"") }
        foreach ($file in $AdditionalFile) { $lines.Add("-additionalfile:`"$file`"") }
        $lines.Add("-errorlog:`"$sarifDir/$name.sarif,version=2.1`"")
        $tempRsp = Join-Path $buildDir "$name.rsp"
        [IO.File]::WriteAllLines($tempRsp, $lines, $utf8)
        # Windows PowerShell turns native stderr lines into errors; keep them as plain output.
        $ErrorActionPreference = 'Continue'
        $output = [string[]]@(& $compiler.Dotnet exec $compiler.Csc -nologo "@$tempRsp" 2>&1 | ForEach-Object { "$_" })
        $exitCode = $LASTEXITCODE
        $ErrorActionPreference = 'Stop'
        $compilerOutput[$name] = $output
        [IO.File]::WriteAllLines((Join-Path $logDir "$name.txt"), $output, $utf8)
        if ($exitCode -ne 0) {
            # Errors with a file look like "path(1,2): error CS0535: ..."; command-line ones like "error CS0006: ...".
            $errors = @($output | Where-Object { $_ -match '(^|\s)error [A-Z]+[0-9]+:' })
            if ($errors.Count -eq 0) { $errors = @($output | Select-Object -First 5) }
            $compileFailures.Add([pscustomobject]@{ Assembly = $name; Errors = $errors })
            [void]$notBuilt.Add($name)
        }
    }
}
finally {
    Pop-Location
}
$elapsed = (Get-Date) - $started
$dependencyOnlyAssemblies = @($buildOrder | Where-Object { -not $reportedAssemblies.Contains($_) })

$changedFiles = $null
$changedFilesError = $null
if ($Changed) {
    # A failed git call must not leave an empty list, which would silently drop every finding.
    try {
        $listed = @(Get-GitLines @('diff', '--name-only', '--relative', 'HEAD')) + @(Get-GitLines @('ls-files', '--others', '--exclude-standard'))
        $changedFiles = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
        foreach ($f in $listed) { if ($f) { [void]$changedFiles.Add($f) } }
    }
    catch {
        $changedFilesError = "$($_.Exception.Message) Findings below are not limited to changed files."
    }
}

# Read findings from the reported assemblies. Analyzer failures are collected before any filter.
$findings = New-Object System.Collections.Generic.List[object]
$analyzerFailures = New-Object System.Collections.Generic.List[object]
$titles = @{}
$suppressedCount = 0
foreach ($name in $buildOrder) {
    if (-not $reportedAssemblies.Contains($name)) { continue }
    $sarifPath = Join-Path $sarifDir "$name.sarif"
    if (-not (Test-Path -LiteralPath $sarifPath)) {
        # No SARIF means the compiler stopped early; its console output still names analyzer failures.
        $pattern = '\b(' + ($analyzerFailureIds -join '|') + '):'
        foreach ($line in $compilerOutput[$name]) {
            if ($line -match $pattern) { $analyzerFailures.Add([pscustomobject]@{ Assembly = $name; Rule = $Matches[1]; Message = $line }) }
        }
        continue
    }
    $log = [IO.File]::ReadAllText($sarifPath) | ConvertFrom-Json
    foreach ($run in $log.runs) {
        $driver = $run.tool.driver
        if ($driver.PSObject.Properties['rules']) {
            foreach ($r in $driver.rules) {
                if ($r.PSObject.Properties['shortDescription'] -and -not $titles.ContainsKey($r.id)) { $titles[$r.id] = $r.shortDescription.text }
            }
        }
        if (-not $run.PSObject.Properties['results']) { continue }
        foreach ($result in $run.results) {
            if ($analyzerFailureIds -contains $result.ruleId) {
                $analyzerFailures.Add([pscustomobject]@{ Assembly = $name; Rule = $result.ruleId; Message = $result.message.text })
                continue
            }
            $level = 'warning'
            if ($result.PSObject.Properties['level']) { $level = $result.level }
            if (-not (Test-MatchesAnyPattern $result.ruleId $Rule)) { continue }
            $location = Get-SarifLocation $result
            if (-not (Test-ReportedDiagnostic $level $location.File $changedFiles)) { continue }
            if ($result.PSObject.Properties['suppressions'] -and @($result.suppressions).Count -gt 0) { $suppressedCount++; continue }
            $findings.Add([pscustomobject]@{
                Rule = $result.ruleId; Level = $level; Assembly = $name
                File = $location.File; Line = $location.Line; Column = $location.Column; Message = $result.message.text
            })
        }
    }
}
$analysisComplete = $compileFailures.Count -eq 0 -and $skippedAssemblies.Count -eq 0 -and $analyzerFailures.Count -eq 0 -and
    $staleSourceFiles.Count -eq 0 -and $changedAssemblySettings.Count -eq 0 -and -not $changedFilesError
# One crashing analyzer usually fails the same way on every symbol; show each distinct failure once.
$analyzerFailureGroups = @($analyzerFailures | Group-Object { "$($_.Assembly) $($_.Rule): $(($_.Message -split "`n")[0])" })

# Write the summary and the JSON reports.
$sorted = @($findings | Sort-Object File, Line, Column, Rule)
$byRule = @($sorted | Group-Object Rule | Sort-Object @{ Expression = 'Count'; Descending = $true }, Name)
$byAssembly = @($sorted | Group-Object Assembly | Sort-Object @{ Expression = 'Count'; Descending = $true }, Name)
$summary = New-Object System.Collections.Generic.List[string]
$summary.Add('# Analyzer findings')
$summary.Add('')
$summary.Add("- Project: $project")
$summary.Add("- Unity $($editor.Version), compiler $($compiler.Csc)")
$summary.Add("- Replayed: $dagRelative ($($reportedAssemblies.Count) reported assemblies, $($buildOrder.Count) built, $([int]$elapsed.TotalSeconds) s)")
if ($dependencyOnlyAssemblies.Count -gt 0) { $summary.Add("- Rebuilt only as current dependencies, not reported: $($dependencyOnlyAssemblies -join ', ')") }
if ($Rule.Count -gt 0) { $summary.Add("- Rules shown: $($Rule -join ', ')") }
if ($Changed -and -not $changedFilesError) { $summary.Add('- Only files changed against git HEAD') }
if ($changedFilesError) { $summary.Add("- Changed files: $changedFilesError") }
if ($analysisComplete) { $summary.Add('- Analysis: complete') }
else { $summary.Add('- Analysis: INCOMPLETE, see the sections below; findings may be missing') }
$summary.Add("- Findings: $($sorted.Count); suppressed in source: $suppressedCount")
if ($staleElsewhere.Count -gt 0) {
    $summary.Add("- Out of date outside this run's assemblies (open Unity to refresh): $(@($staleElsewhere | ForEach-Object { "$($_.File) ($($_.Change))" }) -join ', ')")
}
if ($changedAssemblySettings.Count -gt 0) {
    $summary.Add('')
    $summary.Add("## Assembly settings changed since Unity's last compile")
    $summary.Add('')
    $summary.Add("Unity's response files still use the old references and defines. Open Unity (or run a batch compile), then run again.")
    $summary.Add('')
    foreach ($s in $changedAssemblySettings) { $summary.Add("- $($s.File): $($s.Change)") }
}
if ($staleSourceFiles.Count -gt 0) {
    $summary.Add('')
    $summary.Add("## Files Unity has not compiled yet")
    $summary.Add('')
    $summary.Add('Open Unity (or run a batch compile) so its response files include these, then run again.')
    $summary.Add('')
    foreach ($s in $staleSourceFiles) { $summary.Add("- $($s.File): $($s.Change), in $($s.Assembly)") }
}
if ($analyzerFailures.Count -gt 0) {
    $summary.Add('')
    $summary.Add('## Analyzer failures')
    $summary.Add('')
    foreach ($g in $analyzerFailureGroups) { $summary.Add("- $($g.Name) (x$($g.Count))") }
}
if ($compileFailures.Count -gt 0) {
    $summary.Add('')
    $summary.Add('## Compile errors')
    $summary.Add('')
    foreach ($f in $compileFailures) { foreach ($e in $f.Errors) { $summary.Add("- $($f.Assembly): $e") } }
}
if ($skippedAssemblies.Count -gt 0) {
    $summary.Add('')
    $summary.Add('## Skipped assemblies')
    $summary.Add('')
    foreach ($s in $skippedAssemblies) { $summary.Add("- $($s.Assembly): needs $($s.BlockedBy), which did not compile") }
}
$summary.Add('')
$summary.Add('## By rule')
$summary.Add('')
$summary.Add('| Rule | Count | Title |')
$summary.Add('|---|---|---|')
foreach ($g in $byRule) {
    $title = ''
    if ($titles.ContainsKey($g.Name)) { $title = $titles[$g.Name] -replace '\|', '\|' }
    $summary.Add("| $($g.Name) | $($g.Count) | $title |")
}
$summary.Add('')
$summary.Add('## By assembly')
$summary.Add('')
$summary.Add('| Assembly | Count |')
$summary.Add('|---|---|')
foreach ($g in $byAssembly) { $summary.Add("| $($g.Name) | $($g.Count) |") }
$summary.Add('')
$summary.Add('## Findings')
$summary.Add('')
foreach ($f in $sorted) {
    $where = '(whole assembly)'
    if ($f.File) { $where = "``$($f.File):$($f.Line):$($f.Column)``" }
    $summary.Add("- $where $($f.Rule) in $($f.Assembly): $($f.Message)")
}
[IO.File]::WriteAllLines((Join-Path $OutputDir 'summary.md'), $summary, $utf8)
[IO.File]::WriteAllText((Join-Path $OutputDir 'findings.json'), (ConvertTo-Json -InputObject @($sorted) -Depth 3), $utf8)
$analysisErrors = [pscustomobject]@{
    AnalysisComplete = $analysisComplete
    ChangedFilesError = $changedFilesError
    ChangedAssemblySettings = $changedAssemblySettings.ToArray()
    StaleSourceFiles = $staleSourceFiles.ToArray()
    # Windows PowerShell 5.1 rejects @(List[object]) inside this literal; ToArray() works.
    AnalyzerFailures = $analyzerFailures.ToArray()
    CompileErrors = $compileFailures.ToArray()
    SkippedAssemblies = $skippedAssemblies.ToArray()
}
[IO.File]::WriteAllText((Join-Path $OutputDir 'analysis-errors.json'), (ConvertTo-Json -InputObject $analysisErrors -Depth 4), $utf8)

Write-Host "Reported $($reportedAssemblies.Count) assemblies ($($buildOrder.Count) built) with Unity $($editor.Version) in $([int]$elapsed.TotalSeconds) s."
foreach ($g in $byRule) { Write-Host ("  {0,-12} {1,5}" -f $g.Name, $g.Count) }
Write-Host "Findings: $($sorted.Count) (suppressed in source: $suppressedCount). Summary: $(Join-Path $OutputDir 'summary.md')"
foreach ($s in $changedAssemblySettings) { Write-Warning "$($s.File) was $($s.Change) since Unity's last compile; open Unity, then run again." }
foreach ($s in $staleSourceFiles) { Write-Warning "$($s.File) was $($s.Change) since Unity's last compile ($($s.Assembly)); open Unity, then run again." }
if ($staleElsewhere.Count -gt 0) { Write-Warning "$($staleElsewhere.Count) .cs file(s) outside this run's assemblies are out of date; open Unity to refresh." }
foreach ($g in $analyzerFailureGroups) { Write-Warning "Analyzer failure (x$($g.Count)) in $($g.Name)" }
foreach ($f in $compileFailures) { Write-Warning "$($f.Assembly) did not compile: $($f.Errors -join ' | ')" }
foreach ($s in $skippedAssemblies) { Write-Warning "$($s.Assembly) was skipped: it needs $($s.BlockedBy), which did not compile." }
if ($changedFilesError) { Write-Warning "-Changed: $changedFilesError" }
if (-not $analysisComplete) { exit 1 }
if ($FailOnFindings -and @($sorted | Where-Object { Test-FailingFinding $_.Level }).Count -gt 0) { exit 2 }
exit 0
