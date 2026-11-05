<#
.SYNOPSIS
    Summarises a probe run's capture.raw (run.ps1 -CaptureFrom/-CaptureTo) into capture-report.txt:
    main-thread self time and self GC allocation per sample path, frames with and without a tick.
    Uses the probe's scratch project, in a batchmode Editor. See README.md.
#>
param(
    [Parameter(Mandatory)] [string]$RunDirectory,
    [string]$Unity = 'C:/Program Files/Unity 6000.6.0f1/Editor/Unity.exe',
    [string]$OutputRoot = 'D:/DungeonKeeperRemake/build/game-unity-probe'
)
$ErrorActionPreference = 'Stop'
$project = Join-Path $OutputRoot 'project'
if (!(Test-Path -LiteralPath "$project/.game-probe")) { throw "$project is not the probe's scratch project; run run.ps1 first" }
$run = (Resolve-Path -LiteralPath $RunDirectory).Path
if (!(Test-Path -LiteralPath "$run/capture.raw")) { throw "$run has no capture.raw" }
Copy-Item -LiteralPath "$PSScriptRoot/CaptureReport.cs" -Destination "$project/Assets/DK2/Editor/CaptureReport.cs" -Force
$arguments = @('-batchmode', '-quit', '-projectPath', $project, '-executeMethod', 'DK2.Unity.Editor.CaptureReport.Run',
    '-captureDir', $run, '-logFile', "$run/capture-report.log") | ForEach-Object { '"' + $_ + '"' }
$process = Start-Process -FilePath $Unity -ArgumentList ($arguments -join ' ') -WindowStyle Hidden -PassThru
if (!$process.WaitForExit(30 * 60 * 1000)) { $process.Kill(); throw "Timed out; see $run/capture-report.log" }
if (!(Test-Path -LiteralPath "$run/capture-report.txt")) { throw "No report; see $run/capture-report.log" }
Get-Content -LiteralPath "$run/capture-report.txt"
