# Copyright (C) 2026 Franjo Misetic
# SPDX-License-Identifier: GPL-3.0-or-later
# See LICENSE in the project root for the full license text.

param(
    [string]$Unity = 'C:/Program Files/Unity/Hub/Editor/6000.6.0f1/Editor/Unity.exe',
    [string]$Install = 'E:/GogGames/Dungeon Keeper 2',
    [string]$Project = 'D:/DungeonKeeperRemake/build/step5-lighting-project',
    [string]$OutputRoot = 'D:/DungeonKeeperRemake/build/classic-window-frames',
    [switch]$VisiblePlayer,
    [switch]$RotationCapture,
    [switch]$AllocationTrace
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path "$PSScriptRoot/../..").Path
$projectPath = (Resolve-Path -LiteralPath $Project).Path
$allowedBuildRoot = [IO.Path]::GetFullPath('D:/DungeonKeeperRemake/build') + [IO.Path]::DirectorySeparatorChar
if (!$projectPath.StartsWith($allowedBuildRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'This probe only modifies an isolated project beneath D:/DungeonKeeperRemake/build.'
}
function Assert-NoLinkAncestors([string]$Path) {
    $current = [IO.Path]::GetFullPath($Path)
    while ($current) {
        if ((Test-Path -LiteralPath $current) -and ((Get-Item -LiteralPath $current -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw "Filesystem links are not permitted in scratch destinations: $current"
        }
        $parent = Split-Path -Parent $current
        if ($parent -eq $current) { break }
        $current = $parent
    }
}
Assert-NoLinkAncestors $projectPath
Assert-NoLinkAncestors $OutputRoot
$directories = New-Object 'System.Collections.Generic.Stack[string]'
$directories.Push($projectPath)
while ($directories.Count -gt 0) {
    foreach ($entry in Get-ChildItem -LiteralPath $directories.Pop() -Force) {
        if ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Filesystem link in scratch project: $($entry.FullName)" }
        if ($entry.PSIsContainer) { $directories.Push($entry.FullName) }
    }
}
if (!(Test-Path -LiteralPath "$projectPath/Assets/DK2/Scenes/Dungeon.unity")) { throw 'Prepared scratch project required.' }
$run = Join-Path $OutputRoot (Get-Date -Format 'yyyyMMdd-HHmmss')
New-Item -ItemType Directory -Path $run -Force | Out-Null
Set-Content -LiteralPath "$projectPath/.classic-window-frame-probe" -Value 'isolated windowed frame capture'
$manifestPath = "$projectPath/Packages/manifest.json"
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
foreach ($module in @('com.unity.modules.screencapture', 'com.unity.modules.imageconversion')) {
    $manifest.dependencies | Add-Member -NotePropertyName $module -NotePropertyValue '1.0.0' -Force
}
[IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Depth 20), (New-Object System.Text.UTF8Encoding($false)))
Copy-Item -LiteralPath "$PSScriptRoot/ClassicWindowFrameProbe.cs" -Destination "$projectPath/Assets/DK2/ClassicWindowFrameProbe.cs" -Force
Copy-Item -LiteralPath "$PSScriptRoot/ClassicWindowFrameBuild.cs" -Destination "$projectPath/Assets/DK2/Editor/ClassicWindowFrameBuild.cs" -Force
Copy-Item -LiteralPath "$PSScriptRoot/ClassicRotationProbe.cs" -Destination "$projectPath/Assets/DK2/ClassicRotationProbe.cs" -Force
Copy-Item -LiteralPath "$PSScriptRoot/ClassicRotationProfileReport.cs" -Destination "$projectPath/Assets/DK2/Editor/ClassicRotationProfileReport.cs" -Force
$env:DK2_DIR = $Install
$env:DK2_WINDOW_ROTATION_PROBE = if ($RotationCapture) { '1' } else { '0' }
if ($AllocationTrace -and !$RotationCapture) { throw '-AllocationTrace requires -RotationCapture.' }
git -C $repo rev-parse HEAD | Set-Content -LiteralPath "$run/commit.txt"
Get-ChildItem -LiteralPath "$projectPath/Assets/Plugins/DK2" -Filter '*.dll' |
    Get-FileHash -Algorithm SHA256 | Select-Object Path, Hash | Export-Csv "$run/assembly-hashes.csv" -NoTypeInformation
Get-ChildItem -LiteralPath "$projectPath/Assets/DK2" -Recurse -File |
    Where-Object { $_.Extension -in '.cs', '.shader', '.hlsl' } |
    Get-FileHash -Algorithm SHA256 | Select-Object Path, Hash | Export-Csv "$run/source-hashes.csv" -NoTypeInformation
if (-not ('ClassicProbeHiddenWindow' -as [type])) {
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class ClassicProbeHiddenWindow {
    private delegate bool Callback(IntPtr window, IntPtr data);
    [DllImport("user32.dll")] private static extern bool EnumWindows(Callback callback, IntPtr data);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);
    public static int HideOwnWindows(int processId) {
        int hidden = 0;
        EnumWindows((window, data) => {
            uint process; GetWindowThreadProcessId(window, out process);
            if (process == (uint)processId) { ShowWindow(window, 0); hidden++; }
            return true;
        }, IntPtr.Zero);
        return hidden;
    }
}
'@
}
function Run-ProbeProcess([string]$Executable, [string[]]$Arguments, [int]$TimeoutSeconds, [bool]$HideWindows, [bool]$VisibleWindow = $false) {
    $quoted = foreach ($argument in $Arguments) {
        if ($argument.Contains('"')) { throw 'Embedded quotes in process argument.' }
        '"' + $argument + '"'
    }
    $windowStyle = if ($VisibleWindow) { 'Normal' } else { 'Hidden' }
    $process = Start-Process -FilePath $Executable -ArgumentList ($quoted -join ' ') -WindowStyle $windowStyle -PassThru
    Write-Output "Started PID $($process.Id): $Executable"
    $timer = [Diagnostics.Stopwatch]::StartNew()
    $nextReport = 30
    $hideCalls = 0
    while (!$process.WaitForExit(500)) {
        if ($HideWindows) { $hideCalls += [ClassicProbeHiddenWindow]::HideOwnWindows($process.Id) }
        if ($timer.Elapsed.TotalSeconds -ge $nextReport) {
            Write-Output "PID $($process.Id) running $([int]$timer.Elapsed.TotalSeconds) seconds"
            $nextReport += 30
        }
        if ($timer.Elapsed.TotalSeconds -gt $TimeoutSeconds) {
            $process.Kill()
            throw "Probe PID $($process.Id) exceeded $TimeoutSeconds seconds; see $run."
        }
    }
    if ($HideWindows) { Set-Content -LiteralPath "$run/window-hiding.txt" -Value "OwnProcessOnly=True`nShowWindowCommand=0 (SW_HIDE only)`nMatchingWindowHideCalls=$hideCalls" }
    if ($process.ExitCode -eq 2 -and (Test-Path -LiteralPath "$run/frames.csv")) {
        throw "Rendering verification FAILED; both view diagnostics retained at $run. Counters are not rendered frame performance."
    }
    if ($process.ExitCode -ne 0) { throw "Probe PID $($process.Id) exited $($process.ExitCode); see $run." }
}
$player = Join-Path $run 'player/ClassicWindowFrame.exe'
Run-ProbeProcess $Unity @('-batchmode', '-quit', '-nographics', '-projectPath', $projectPath,
    '-executeMethod', 'DK2.Unity.Editor.ClassicWindowFrameBuild.Build', '-windowProbeBuild', $player,
    '-logFile', "$run/build.log") 600 $false
if (!(Test-Path -LiteralPath $player)) { throw "Player was not built; see $run/build.log." }
Set-Content -LiteralPath "$run/window-mode.txt" -Value "VisiblePlayerExplicitOptIn=$($VisiblePlayer.IsPresent)"
$playerArguments = @('-screen-fullscreen', '0', '-screen-width', '1280', '-screen-height', '720',
    '-windowProbeOutput', $run, '-logFile', "$run/player.log")
if ($AllocationTrace) { $playerArguments += '-rotationAllocationTrace' }
Run-ProbeProcess $player $playerArguments 240 (!$VisiblePlayer.IsPresent) $VisiblePlayer.IsPresent
if (!(Test-Path -LiteralPath "$run/frames.csv")) { throw "No completed windowed capture; see $run/player.log." }
if ($AllocationTrace) {
    Run-ProbeProcess $Unity @('-batchmode', '-nographics', '-projectPath', $projectPath,
        '-executeMethod', 'DK2.Unity.Editor.ClassicRotationProfileReport.Run',
        '-rotationProfile', "$run/allocations.raw", '-rotationReport', "$run/allocation-report.json",
        '-logFile', "$run/report.log") 180 $false
}
Write-Output "Windowed capture artifacts (check RenderingVerified): $run"
