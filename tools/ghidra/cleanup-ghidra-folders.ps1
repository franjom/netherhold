<#
.SYNOPSIS
One-off: make D:\Ghidra\DK2_RE the one canonical Ghidra project and delete the old copies.

.DESCRIPTION
tools/ghidra/ghidra.py and mcp-serve.py clone the canonical project for every
read-only run and every MCP session, so the per-agent projects and the
persistent MCP copies are no longer used. This script, run by the owner once:

  1. refuses while pyghidra-mcp.exe, or a python/pythonw/java/javaw process whose
     command line names ghidra or pyghidra, is alive, or while any *.lock
     exists under the project folders or D:\Ghidra\mcp, naming each blocker;
  2. moves 'D:\Ghidra\ghidra projects\DK2_RE.rep' then '.gpr' to
     'D:\Ghidra\DK2_RE.*' (each skipped when its target already exists);
  3. moves the rest of 'D:\Ghidra\ghidra projects' to 'D:\Ghidra\other projects'
     and removes the then-empty folder;
  4. deletes the obsolete folders, asking for each (Remove-Item -Confirm);
  5. warns if the Ghidra install or the canonical project is missing, and
     prints the size of D:\Ghidra before and after.

.PARAMETER DryRun
Only print what would be done.

.EXAMPLE
powershell -ExecutionPolicy Bypass -File tools\ghidra\cleanup-ghidra-folders.ps1 -DryRun
#>
param([switch]$DryRun)

$ErrorActionPreference = 'Stop'
$root = 'D:\Ghidra'
$legacy = Join-Path $root 'ghidra projects'
$others = Join-Path $root 'other projects'
$obsolete = @(
    'D:\Ghidra\backup_projects_11',
    'D:\Ghidra\ghidra_11.0.1_PUBLIC',
    'D:\Ghidra\claude ghidra',
    'D:\Ghidra\ghidra projects codex-thunder',
    'D:\Ghidra\ghidra projects claude',
    'D:\Ghidra\mcp\claude',
    'D:\Ghidra\mcp\codex',
    'D:\Ghidra\mcp\check'
)
$required = @('D:\Ghidra\ghidra_12.1.4_PUBLIC', 'D:\Ghidra\DK2_RE.gpr', 'D:\Ghidra\DK2_RE.rep')

function Get-SizeGB([string]$path) {
    if (-not (Test-Path -LiteralPath $path)) { return 0 }
    $sum = (Get-ChildItem -LiteralPath $path -Recurse -Force -File -ErrorAction SilentlyContinue |
        Measure-Object -Property Length -Sum).Sum
    if ($null -eq $sum) { return 0 }
    return [Math]::Round($sum / 1GB, 2)
}

function Step([string]$text) {
    if ($DryRun) { Write-Host ('WOULD ' + $text) } else { Write-Host $text }
}

Write-Host ('D:\Ghidra before: {0} GB' -f (Get-SizeGB $root))

# 1. what holds the files: Ghidra JVMs, pyghidra-mcp, the Python launchers, and lock files
$blockers = @()
foreach ($p in @(Get-Process -Name 'pyghidra-mcp' -ErrorAction SilentlyContinue)) {
    $blockers += ('process pyghidra-mcp.exe ({0})' -f $p.Id)
}
$hosts = @(Get-CimInstance Win32_Process -ErrorAction SilentlyContinue |
    Where-Object { @('python.exe', 'pythonw.exe', 'javaw.exe', 'java.exe') -contains $_.Name.ToLower() -and $_.CommandLine -match 'ghidra|pyghidra' })
foreach ($p in $hosts) {
    $line = [string]$p.CommandLine
    if ($line.Length -gt 120) { $line = $line.Substring(0, 120) + '...' }
    $blockers += ('process {0} ({1}): {2}' -f $p.Name, $p.ProcessId, $line)
}
$lockRoots = @((Join-Path $root 'mcp'), $legacy)
foreach ($lockRoot in $lockRoots) {
    if (Test-Path -LiteralPath $lockRoot) {
        foreach ($f in @(Get-ChildItem -LiteralPath $lockRoot -Recurse -Force -File -Filter '*.lock' -ErrorAction SilentlyContinue)) {
            $blockers += ('lock ' + $f.FullName)
        }
    }
}
foreach ($f in @(Get-ChildItem -Path $root -Force -File -Filter 'DK2_RE.*' -ErrorAction SilentlyContinue)) {
    if ($f.Name -like '*.lock') { $blockers += ('lock ' + $f.FullName) }
}
if (Test-Path -LiteralPath (Join-Path $root 'DK2_RE.rep')) {
    foreach ($f in @(Get-ChildItem -LiteralPath (Join-Path $root 'DK2_RE.rep') -Recurse -Force -File -Filter '*.lock' -ErrorAction SilentlyContinue)) {
        $blockers += ('lock ' + $f.FullName)
    }
}
if ($blockers.Count -gt 0) {
    foreach ($b in $blockers) { Write-Host ('  blocked by ' + $b) }
    if (-not $DryRun) {
        Write-Error 'Close every Ghidra and MCP session first (a lock with no process alive is stale: delete it by hand).'
        exit 1
    }
    Write-Host 'NOTE a real run would refuse now, for the reasons above'
}

# 2. the canonical project: .rep first, so D:\Ghidra\DK2_RE.gpr never exists without its .rep
$stays = @()
foreach ($suffix in @('.rep', '.gpr')) {
    $source = Join-Path $legacy ('DK2_RE' + $suffix)
    $target = Join-Path $root ('DK2_RE' + $suffix)
    if (Test-Path -LiteralPath $target) {
        Write-Host ('skip: ' + $target + ' already exists')
        if (Test-Path -LiteralPath $source) { $stays += ('DK2_RE' + $suffix) }
    }
    elseif (-not (Test-Path -LiteralPath $source)) {
        Write-Host ('skip: no ' + $source)
    }
    else {
        Step ('move ' + $source + ' -> ' + $target)
        if (-not $DryRun) { Move-Item -LiteralPath $source -Destination $target }
    }
}

# 3. the other projects in the legacy folder
if (Test-Path -LiteralPath $legacy) {
    $rest = @(Get-ChildItem -LiteralPath $legacy -Force | Where-Object { $_.Name -notlike 'DK2_RE.*' })
    if ($rest.Count -gt 0) {
        if (-not (Test-Path -LiteralPath $others)) {
            Step ('create ' + $others)
            if (-not $DryRun) { New-Item -ItemType Directory -Path $others | Out-Null }
        }
        foreach ($item in $rest) {
            $target = Join-Path $others $item.Name
            if (Test-Path -LiteralPath $target) {
                Write-Host ('skip: ' + $target + ' already exists')
                $stays += $item.Name
                continue
            }
            Step ('move ' + $item.FullName + ' -> ' + $target)
            if (-not $DryRun) { Move-Item -LiteralPath $item.FullName -Destination $target }
        }
    }
    if ($DryRun) { $left = $stays } else { $left = @(@(Get-ChildItem -LiteralPath $legacy -Force) | ForEach-Object { $_.Name }) }
    if ($left.Count -eq 0) {
        Step ('remove the empty folder ' + $legacy)
        if (-not $DryRun) { Remove-Item -LiteralPath $legacy }
    }
    else {
        Write-Host ('keep: ' + $legacy + ' would still hold ' + ($left -join ', '))
    }
}

# 3b. keep a semantic index an old MCP copy built, so sessions need not embed again
$index = 'D:\Ghidra\mcp\index\DK2_RE-pyghidra-mcp'
if (-not (Test-Path -LiteralPath $index)) {
    foreach ($agent in @('claude', 'codex')) {
        $built = 'D:\Ghidra\mcp\' + $agent + '\DK2_RE-pyghidra-mcp'
        if (Test-Path -LiteralPath $built) {
            Step ('move ' + $built + ' -> ' + $index + ' (rebuild with mcp-serve.py --index-only if it was incomplete)')
            if (-not $DryRun) {
                New-Item -ItemType Directory -Path (Split-Path $index) -Force | Out-Null
                Move-Item -LiteralPath $built -Destination $index
            }
            break
        }
    }
}

# 4. the obsolete copies, one confirmation each
foreach ($path in $obsolete) {
    if (-not (Test-Path -LiteralPath $path)) {
        Write-Host ('gone: ' + $path)
        continue
    }
    Step ('delete ' + $path + ' (' + (Get-SizeGB $path) + ' GB)')
    if (-not $DryRun) { Remove-Item -LiteralPath $path -Recurse -Force -Confirm }
}

# 5. what must remain
foreach ($path in $required) {
    if (-not (Test-Path -LiteralPath $path)) {
        if ($DryRun) { Write-Host ('NOTE missing now (expected before the move): ' + $path) }
        else { Write-Warning ('missing: ' + $path) }
    }
}
if ($DryRun) {
    Write-Host 'dry run: nothing was changed'
}
else {
    Write-Host ('D:\Ghidra after: {0} GB' -f (Get-SizeGB $root))
}
