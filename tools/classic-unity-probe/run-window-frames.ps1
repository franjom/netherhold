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
    [switch]$AllocationTrace,
    [switch]$PortalIntroduction,
    [switch]$OmitArrivalEffect,
    [switch]$ReleasePlayer,
    [switch]$CheckFrameBudget,
    [ValidateSet('Default', 'Direct3D11', 'Direct3D12', 'Vulkan')]
    [string]$GraphicsApi = 'Default',
    [string]$PresentMon,
    [switch]$CompareGraphicsApis,
    [switch]$ArrivalAllocationStacks,
    [switch]$NoArrivalScreenshot,
    [int]$PresentationTraceSeconds = 60,
    [ValidateSet('auto', 'fps', 'diagnostic-gpu', 'cpu', 'native-gpu')]
    [string]$TraceMode = 'auto',
    [string]$ExistingPlayer
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path "$PSScriptRoot/../..").Path
$graphicsLease = & "$PSScriptRoot/acquire-graphics-probe.ps1" -RepositoryRoot $repo
try {
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
Copy-Item -LiteralPath "$PSScriptRoot/Level8MotionProbe.cs" -Destination "$projectPath/Assets/DK2/Level8MotionProbe.cs" -Force
Copy-Item -LiteralPath "$PSScriptRoot/ClassicRotationProfileReport.cs" -Destination "$projectPath/Assets/DK2/Editor/ClassicRotationProfileReport.cs" -Force
Copy-Item -LiteralPath "$PSScriptRoot/PortalIntroductionProfile.cs" -Destination "$projectPath/Assets/DK2/PortalIntroductionProfile.cs" -Force
Copy-Item -LiteralPath "$PSScriptRoot/PortalCpuProfileReport.cs" -Destination "$projectPath/Assets/DK2/Editor/PortalCpuProfileReport.cs" -Force
if ($PortalIntroduction) {
    $editorAssemblyPath = "$projectPath/Assets/DK2/Editor/DK2.Unity.Editor.asmdef"
    $editorAssembly = Get-Content -LiteralPath $editorAssemblyPath -Raw | ConvertFrom-Json
    $editorAssembly.references = @(@($editorAssembly.references) + @('Unity.RenderPipelines.Universal.Runtime', 'Unity.RenderPipelines.Core.Runtime') | Select-Object -Unique)
    [IO.File]::WriteAllText($editorAssemblyPath, ($editorAssembly | ConvertTo-Json -Depth 10), [Text.UTF8Encoding]::new($false))
}
$env:DK2_DIR = $Install
$env:DK2_WINDOW_ROTATION_PROBE = if ($RotationCapture) { '1' } else { '0' }
$env:DK2_PORTAL_INTRO_PROFILE = if ($PortalIntroduction) { '1' } else { '0' }
$env:DK2_PROBE_RELEASE = if ($ReleasePlayer) { '1' } else { '0' }
$env:DK2_PROBE_COMPARE_APIS = if ($CompareGraphicsApis) { '1' } else { '0' }
$captureMode = if ($TraceMode -eq 'auto') { if ($CheckFrameBudget) { 'fps' } else { 'diagnostic-gpu' } } else { $TraceMode }
$env:DK2_PROBE_CAPTURE_MODE = $captureMode
if ($captureMode -eq 'cpu' -and (!$PortalIntroduction -or $ReleasePlayer)) { throw 'CPU tracing requires a Development portal introduction player.' }
if ($ArrivalAllocationStacks -and $captureMode -ne 'cpu') { throw 'Arrival allocation stacks require CPU tracing.' }
if ($CheckFrameBudget -and $captureMode -ne 'fps') { throw 'FPS qualification requires lightweight fps trace mode.' }
if ($CheckFrameBudget -and !$PresentMon) {
    $PresentMon = Join-Path $repo 'build/profiling-tools/PresentMon-2.6.0-x64.exe'
    if (!(Test-Path -LiteralPath $PresentMon)) { throw 'Run tools/classic-unity-probe/install-presentmon.ps1 first.' }
}
if ($CheckFrameBudget -and (!$PortalIntroduction -or !$ReleasePlayer -or $OmitArrivalEffect)) {
    throw '-CheckFrameBudget requires -PortalIntroduction -ReleasePlayer and the full effect.'
}
Copy-Item -LiteralPath "$PSScriptRoot/PortalIntroductionProfile.cs" -Destination "$run/probe-source.cs"
if ($captureMode -eq 'cpu') { Copy-Item -LiteralPath "$PSScriptRoot/PortalCpuProfileReport.cs" -Destination "$run/cpu-report-source.cs" }
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
function Run-ProbeProcess([string]$Executable, [string[]]$Arguments, [int]$TimeoutSeconds, [bool]$HideWindows, [bool]$VisibleWindow = $false, [bool]$TracePresentation = $false) {
    $quoted = foreach ($argument in $Arguments) {
        if ($argument.Contains('"')) { throw 'Embedded quotes in process argument.' }
        '"' + $argument + '"'
    }
    $windowStyle = if ($VisibleWindow) { 'Normal' } else { 'Hidden' }
    $monitor = $null
    $traceSession = $null
    if ($TracePresentation -and $PresentMon) {
        $processName = [IO.Path]::GetFileNameWithoutExtension($Executable)
        if (@(Get-Process -Name $processName -ErrorAction SilentlyContinue).Count -ne 0) {
            throw "Another $processName player is running; a trace must target only this benchmark."
        }
        $traceSession = 'DK2Profile-' + [guid]::NewGuid().ToString('N')
        $monitorArguments = @('--process_name', [IO.Path]::GetFileName($Executable), '--output_file', "$run/presentmon.csv",
            '--qpc_time', '--no_console_stats', '--no_track_input',
            '--timed', $PresentationTraceSeconds.ToString(), '--terminate_after_timed', '--session_name', $traceSession)
        if ($captureMode -in @('fps', 'cpu')) { $monitorArguments += '--no_track_gpu' }
        $monitorQuoted = foreach ($argument in $monitorArguments) { '"' + $argument + '"' }
        $monitor = Start-Process -FilePath $PresentMon -ArgumentList ($monitorQuoted -join ' ') -WindowStyle Hidden -PassThru `
            -RedirectStandardOutput "$run/presentmon.log" -RedirectStandardError "$run/presentmon-errors.log"
        $null = $monitor.Handle
        Get-FileHash -LiteralPath $PresentMon -Algorithm SHA256 | Select-Object Path, Hash | Export-Csv "$run/presentmon-hash.csv" -NoTypeInformation
        Set-Content -LiteralPath "$run/presentmon-arguments.txt" -Value ($monitorQuoted -join ' ')
        $ready = [Diagnostics.Stopwatch]::StartNew()
        while ($true) {
            & logman query $traceSession -ets *> "$run/presentmon-session.txt"
            if ($LASTEXITCODE -eq 0) { break }
            if ($monitor.HasExited -or $ready.Elapsed.TotalSeconds -gt 5) {
                & logman stop $traceSession -ets *> "$run/presentmon-stop.txt"
                if (!$monitor.HasExited) { $monitor.Kill() }
                throw "Presentation trace was not ready; see $run/presentmon-errors.log"
            }
            Start-Sleep -Milliseconds 100
        }
    }
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
            if ($monitor) { & logman stop $traceSession -ets *> "$run/presentmon-stop.txt" }
            throw "Probe PID $($process.Id) exceeded $TimeoutSeconds seconds; see $run."
        }
    }
    if ($HideWindows) { Set-Content -LiteralPath "$run/window-hiding.txt" -Value "OwnProcessOnly=True`nShowWindowCommand=0 (SW_HIDE only)`nMatchingWindowHideCalls=$hideCalls" }
    if ($monitor) {
        $traceWait = [Diagnostics.Stopwatch]::StartNew()
        while (!$monitor.WaitForExit(1000)) {
            if ($traceWait.Elapsed.TotalSeconds -gt $PresentationTraceSeconds + 10) {
                & logman stop $traceSession -ets *> "$run/presentmon-stop.txt"
                $monitor.Kill()
                throw "Presentation trace exceeded its timer; see $run/presentmon-errors.log"
            }
        }
        $monitor.Refresh()
        [IO.File]::AppendAllText("$run/metadata.txt", "PresentMonProcessId=$($process.Id)`nPresentMonExitCode=$($monitor.ExitCode)`nPresentationTraceMode=$captureMode`n")
        if ($monitor.ExitCode -ne 0) { throw "Presentation trace failed; see $run/presentmon-errors.log" }
    }
    if ($process.ExitCode -eq 2 -and (Test-Path -LiteralPath "$run/frames.csv")) {
        throw "Rendering verification FAILED; both view diagnostics retained at $run. Counters are not rendered frame performance."
    }
    if ($process.ExitCode -ne 0) { throw "Probe PID $($process.Id) exited $($process.ExitCode); see $run." }
}
$player = if ($ExistingPlayer) { [IO.Path]::GetFullPath($ExistingPlayer) } else { Join-Path $run 'player/ClassicWindowFrame.exe' }
if (!$ExistingPlayer) {
    Run-ProbeProcess $Unity @('-batchmode', '-quit', '-nographics', '-projectPath', $projectPath,
        '-executeMethod', 'DK2.Unity.Editor.ClassicWindowFrameBuild.Build', '-windowProbeBuild', $player,
        '-logFile', "$run/build.log") 600 $false
}
if (!(Test-Path -LiteralPath $player)) { throw "Player was not built; see $run/build.log." }
Get-FileHash -LiteralPath $player -Algorithm SHA256 | Select-Object Path, Hash | Export-Csv "$run/player-hash.csv" -NoTypeInformation
$managed = Join-Path ([IO.Path]::GetDirectoryName($player)) (([IO.Path]::GetFileNameWithoutExtension($player)) + '_Data/Managed')
Get-ChildItem -LiteralPath $managed -File | Where-Object { $_.Name -eq 'Assembly-CSharp.dll' -or $_.Name -like 'DK2*.dll' } | Get-FileHash -Algorithm SHA256 | Select-Object Path, Hash | Export-Csv "$run/player-managed-hashes.csv" -NoTypeInformation
Set-Content -LiteralPath "$run/window-mode.txt" -Value "VisiblePlayerExplicitOptIn=$($VisiblePlayer.IsPresent)"
$playerArguments = @('-screen-fullscreen', '0', '-screen-width', $(if ($PortalIntroduction) { '1920' } else { '1280' }), '-screen-height', $(if ($PortalIntroduction) { '1080' } else { '720' }),
    '-windowProbeOutput', $run, '-logFile', "$run/player.log")
if ($AllocationTrace) { $playerArguments += '-rotationAllocationTrace' }
if ($OmitArrivalEffect) { $playerArguments += '-omitArrivalEffect' }
if ($CheckFrameBudget -or $NoArrivalScreenshot) { $playerArguments += '-noArrivalScreenshot' }
if ($captureMode -eq 'fps') { $playerArguments += '-fpsQualification' }
if ($captureMode -eq 'native-gpu') { $playerArguments += '-nativeGpuDiagnostic' }
if ($captureMode -eq 'cpu') { $playerArguments += @('-arrivalCpuProfile', '-noArrivalScreenshot') }
if ($ArrivalAllocationStacks) { $playerArguments += '-arrivalAllocationStacks' }
if ($GraphicsApi -eq 'Direct3D11') { $playerArguments += '-force-d3d11' }
if ($GraphicsApi -eq 'Direct3D12') { $playerArguments += '-force-d3d12' }
if ($GraphicsApi -eq 'Vulkan') { $playerArguments += '-force-vulkan' }
Run-ProbeProcess $player $playerArguments 240 (!$VisiblePlayer.IsPresent) $VisiblePlayer.IsPresent $true
if (!(Test-Path -LiteralPath "$run/frames.csv")) { throw "No completed windowed capture; see $run/player.log." }
if ($captureMode -eq 'cpu') {
    Run-ProbeProcess $Unity @('-batchmode', '-nographics', '-projectPath', $projectPath,
        '-executeMethod', 'DK2.Unity.Editor.PortalCpuProfileReport.Run', '-arrivalProfile', "$run/arrival-cpu.raw",
        '-logFile', "$run/cpu-report.log") 240 $false
}
if ($AllocationTrace) {
    Run-ProbeProcess $Unity @('-batchmode', '-nographics', '-projectPath', $projectPath,
        '-executeMethod', 'DK2.Unity.Editor.ClassicRotationProfileReport.Run',
        '-rotationProfile', "$run/allocations.raw", '-rotationReport', "$run/allocation-report.json",
        '-logFile', "$run/report.log") 180 $false
}
Write-Output "Windowed capture artifacts (check RenderingVerified): $run"
if ($CheckFrameBudget) {
    $report = python "$PSScriptRoot/frame_budget.py" $run
    $budgetExit = $LASTEXITCODE
    [IO.File]::WriteAllText("$run/frame-budget.json", ([string]::Join("`n", $report) + "`n"), [Text.UTF8Encoding]::new($false))
    $report | Write-Output
    if ($budgetExit -ne 0) { throw "Frame budget failed or capture invalid; see $run/frame-budget.json" }
}
} finally {
    $graphicsLease.Dispose()
}
