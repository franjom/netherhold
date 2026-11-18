# Copyright (C) 2026 Franjo Misetic
# SPDX-License-Identifier: GPL-3.0-or-later
# See LICENSE in the project root for the full license text.

# The performance recipe (migration plan step 3o, godot/README.md "Measuring performance"): runs InGameHud.tscn
# with --perf-report for each level and window size, in real time on the pinned editor binary, from a save made
# after the level's opening camera path, along one fixed camera script, with the pointer held at the window's
# centre by --pointer-path (the OS cursor is never read or moved). Each run writes <Out>/<Label>-<level>-<size>-r<n>.json
# with the machine's CPU load during the run added. Compare two labels with godot/perf_compare.py.
#   powershell -File godot/perf.ps1 -Out <dir> -Label classic
#   powershell -File godot/perf.ps1 -Out <dir> -Label perpixel -Extra --light-perpixel -State <same state dir>
[CmdletBinding(PositionalBinding = $false)]
param(
    [Parameter(Mandatory = $true)] [string] $Out,
    [string] $Label = 'run',
    [string[]] $Levels = @('Level1', 'Level7'),
    [string[]] $Sizes = @('1920x1080', '1024x576'),
    [int] $Frames = 1800,
    [int] $WarmUp = 120,
    [long] $Seed = 1,
    [int] $Repeat = 1,
    # The saves are made at this turn, after Level1's (turn 120) and Level7's (turn 110) opening camera paths.
    [int] $SaveTick = 200,
    # Profile, settings, saves and hi-scores; reuse one between builds so both start from the same saves. A new
    # temporary folder, removed at the end, when not given.
    [string] $State,
    # Tiles east and south of the start focus, by frame: still to 600, a loop to 1500, still to the end (1920).
    [string] $Camera = '0:0,0;600:0,0;825:8,0;1050:8,8;1275:0,8;1500:0,0',
    # Extra scene arguments: '--light-perpixel' or '--a,--b'; an option and its value as
    # '--name value' (a value with spaces cannot go through powershell -File).
    [string[]] $Extra = @(),
    # Skips building the C# assembly first (the run uses whatever the last build left).
    [switch] $SkipBuild,
    [string] $InstallRoot = $(if ($env:DK2_GODOT_ROOT) { $env:DK2_GODOT_ROOT } else { Join-Path $env:LOCALAPPDATA 'Programs\Godot' })
)
$ErrorActionPreference = 'Stop'
# powershell -File passes a comma list as one string.
$Levels = @($Levels | ForEach-Object { $_ -split ',' } | Where-Object { $_ })
$Sizes = @($Sizes | ForEach-Object { $_ -split ',' } | Where-Object { $_ })
# Extra splits only before a '--', so a value such as 0:5,5 keeps its comma; '--name value' becomes two arguments.
$Extra = @($Extra | ForEach-Object { $_ -split ',(?=--)' } | ForEach-Object { $_.Trim() -split '\s+', 2 } | Where-Object { $_ })
# The label starts each report's name and is matched by perf_compare.py: no '-' or path characters.
if ($Label -notmatch '^[A-Za-z0-9_.]+$') { throw "Label '$Label' must be letters, digits, '_' or '.'." }
if ($Frames -le 0 -or $WarmUp -lt 0) { throw 'Frames must be positive and WarmUp not negative.' }
$pin = Get-Content -Raw (Join-Path $PSScriptRoot 'godot-version.json') | ConvertFrom-Json
# The windowed binary, not the console wrapper, so the process sampled for CPU is the game; its log goes to --log-file.
$exe = Join-Path (Join-Path $InstallRoot $pin.editor.version) $pin.editor.windowsExe
if (-not (Test-Path $exe)) { throw "Godot $($pin.editor.version) is not installed at $exe. Run godot/setup.ps1 first." }
$project = Join-Path $PSScriptRoot 'DK2.Godot'
$repo = Split-Path -Parent $PSScriptRoot
New-Item -ItemType Directory -Force $Out | Out-Null
$Out = (Resolve-Path $Out).Path
$ownState = -not $State
if ($ownState) { $State = Join-Path ([IO.Path]::GetTempPath()) ('dk2-perf-' + [guid]::NewGuid().ToString('N')) }
New-Item -ItemType Directory -Force (Join-Path $State 'saves') | Out-Null
$State = (Resolve-Path $State).Path
if (-not $SkipBuild) {
    # The editor binary loads the Debug assembly this build writes; a stale one would measure old code.
    & dotnet build (Join-Path $project 'DK2.Godot.csproj') -c Debug -v q -nologo | Out-Host
    if ($LASTEXITCODE -ne 0) { throw 'The C# build failed.' }
}
$script:child = $null
$common = @('--seed', "$Seed", '--profile', (Join-Path $State 'profile.dat'), '--settings', (Join-Path $State 'settings.ini'),
    '--saves', (Join-Path $State 'saves'), '--hiscores', (Join-Path $State 'hiscores.dat'))

function Format-Arguments([string[]] $arguments) {
    # Windows PowerShell does not quote -ArgumentList elements.
    ($arguments | ForEach-Object { if ($_ -match '[\s;]') { '"' + $_ + '"' } else { $_ } }) -join ' '
}

function New-CpuCounter {
    try {
        $counter = New-Object System.Diagnostics.PerformanceCounter('Processor', '% Processor Time', '_Total')
        [void]$counter.NextValue()   # the first read is always 0
        return $counter
    } catch { return $null }
}

function Get-Git([string[]] $arguments) {
    try { return (& git -C $repo --no-optional-locks @arguments 2>$null) } catch { return $null }
}

function Read-CpuLoad($counter) {
    if ($counter) { return [double]$counter.NextValue() }
    return [double](Get-CimInstance Win32_Processor | Measure-Object -Property LoadPercentage -Average).Average
}

function Stop-Child {
    if ($script:child -and -not $script:child.HasExited) { Stop-Process -Id $script:child.Id -Force -ErrorAction SilentlyContinue }
    $script:child = $null
}

function New-Save([string] $level) {
    $save = Join-Path $State "saves\perf$level.dk2g"
    # The marker is written only once the game has logged the save as done; a save without it may be partial.
    $marker = "$save.ok"
    if ((Test-Path $save) -and (Test-Path $marker)) { return }
    Remove-Item $save, $marker -ErrorAction SilentlyContinue
    Write-Host "Making the $level save at turn $SaveTick (headless, real time)..."
    $log = Join-Path $Out "save-$level.log"
    $arguments = @('--headless', '--path', $project, 'res://InGameHud.tscn', '--log-file', $log, '--',
        '--level', $level) + $common + @('--save-as', "perf$level", '--save-tick', "$SaveTick")
    $script:child = Start-Process -FilePath $exe -ArgumentList (Format-Arguments $arguments) -WorkingDirectory $repo -PassThru -WindowStyle Hidden
    $deadline = (Get-Date).AddMinutes(5)
    $saved = $false
    while (-not $saved -and -not $script:child.HasExited -and (Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 500
        $saved = (Test-Path $log) -and (Select-String -Path $log -Pattern "DK2\.Godot: saved .*perf$level\.dk2g" -Quiet)
    }
    Stop-Child
    if (-not $saved -or -not (Test-Path $save)) { throw "No complete save for $level; see $log." }
    Set-Content -Path $marker -Value $SaveTick
}

function Invoke-Run([string] $level, [string] $size, [int] $round) {
    $name = "$Label-$level-$size-r$round"
    $report = Join-Path $Out "$name.json"
    $width, $height = $size -split 'x'
    $centre = "0:$([int]$width / 2),$([int]$height / 2)"
    $arguments = @('--path', $project, 'res://InGameHud.tscn', '--log-file', (Join-Path $Out "$name.log"), '--',
        '--level', $level) + $common + @('--load', "perf$level", '--perf-report', $report, '--perf-frames', "$Frames",
        '--perf-warmup', "$WarmUp", '--perf-size', $size, '--perf-camera', $Camera, '--pointer-path', $centre) + $Extra
    if (Test-Path $report) { Remove-Item $report }
    $counter = New-CpuCounter
    $before = @(1..3 | ForEach-Object { Start-Sleep -Milliseconds 300; Read-CpuLoad $counter })
    $script:child = Start-Process -FilePath $exe -ArgumentList (Format-Arguments $arguments) -WorkingDirectory $repo -PassThru
    $process = $script:child
    $cores = [Environment]::ProcessorCount
    $total = New-Object System.Collections.Generic.List[double]
    $other = New-Object System.Collections.Generic.List[double]
    $lastCpu = [TimeSpan]::Zero; $lastTime = Get-Date
    $deadline = (Get-Date).AddMinutes(10)
    while (-not $process.HasExited -and (Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 1000
        try { $process.Refresh(); $cpu = $process.TotalProcessorTime } catch { break }
        $now = Get-Date
        $own = 100.0 * ($cpu - $lastCpu).TotalMilliseconds / (($now - $lastTime).TotalMilliseconds * $cores)
        $lastCpu = $cpu; $lastTime = $now
        $load = Read-CpuLoad $counter
        $total.Add($load); $other.Add([Math]::Max(0, $load - $own))
    }
    if (-not $process.HasExited) { Stop-Child; throw "$name did not finish in 10 minutes." }
    $script:child = $null
    if (-not (Test-Path $report)) { throw "$name wrote no report; see $(Join-Path $Out "$name.log")." }
    $json = Get-Content -Raw $report | ConvertFrom-Json
    if ($json.info.resolution -ne $size) { Write-Warning "$name rendered at $($json.info.resolution), not $size." }
    $machine = [ordered]@{
        cpuLoadBeforePercent = [Math]::Round(($before | Measure-Object -Average).Average, 1)
        cpuLoadMeanPercent = if ($total.Count) { [Math]::Round(($total | Measure-Object -Average).Average, 1) } else { $null }
        cpuLoadMaxPercent = if ($total.Count) { [Math]::Round(($total | Measure-Object -Maximum).Maximum, 1) } else { $null }
        otherLoadMeanPercent = if ($other.Count) { [Math]::Round(($other | Measure-Object -Average).Average, 1) } else { $null }
        loadSamples = $total.Count
        commit = Get-Git @('rev-parse', '--short', 'HEAD')
        changedFiles = @(Get-Git @('status', '--porcelain')).Count
        label = $Label
    }
    $json | Add-Member -NotePropertyName machine -NotePropertyValue ([pscustomobject]$machine)
    [IO.File]::WriteAllText($report, ($json | ConvertTo-Json -Depth 8), (New-Object System.Text.UTF8Encoding($false)))
    $frame = $json.phases.all.frameMs
    Write-Host ([string]::Format([Globalization.CultureInfo]::InvariantCulture,
        '{0}: frame p50 {1:0.00} ms p99 {2:0.00} ms, GPU p50 {3:0.00} ms; other CPU load {4}%', $name, $frame.p50,
        $frame.p99, $json.phases.all.renderGpuMs.p50, $machine.otherLoadMeanPercent))
}

try {
    foreach ($level in $Levels) { New-Save $level }
    for ($round = 1; $round -le $Repeat; $round++) {
        foreach ($level in $Levels) { foreach ($size in $Sizes) { Invoke-Run $level $size $round } }
    }
} finally {
    Stop-Child
    if ($ownState) { Remove-Item -Recurse -Force $State -ErrorAction SilentlyContinue }
}
