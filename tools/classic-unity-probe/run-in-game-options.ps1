# Copyright (C) 2026 Franjo Misetic
# SPDX-License-Identifier: GPL-3.0-or-later
# See LICENSE in the project root for the full license text.

param([string]$Unity = 'C:/Program Files/Unity 6000.6.0f1/Editor/Unity.exe', [string]$CachedProject = '')
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path "$PSScriptRoot/../..").Path
$run = Join-Path $repo ('build/igo-' + (Get-Date -Format 'HHmmss'))
if (Test-Path -LiteralPath $run) { throw 'Unique output directory required for each in-game Options run.' }
New-Item -ItemType Directory -Path $run | Out-Null
if ($CachedProject) {
    $project = (Resolve-Path -LiteralPath $CachedProject).Path
    $allowed = [IO.Path]::GetFullPath((Join-Path $repo 'build/igo-'))
    if (!$project.StartsWith($allowed, [StringComparison]::OrdinalIgnoreCase) -or !(Test-Path -LiteralPath "$project/.in-game-options-probe")) { throw 'Only an isolated in-game Options project can be refreshed.' }
    if (Get-CimInstance Win32_Process -Filter "name='Unity.exe'" | Where-Object { $_.CommandLine -like "*$project*" }) { throw 'Close the isolated editor before refreshing it.' }
    Copy-Item -Path "$repo/unity/DK2.Unity/Assets/*" -Destination "$project/Assets" -Recurse -Force
} else {
    $project = Join-Path $run 'p'
    New-Item -ItemType Directory -Path $project | Out-Null
    foreach ($folder in @('Assets', 'Packages', 'ProjectSettings')) {
        Copy-Item -LiteralPath "$repo/unity/DK2.Unity/$folder" -Destination $project -Recurse
    }
}
$plugins = Join-Path $project 'Assets/Plugins/DK2'
New-Item -ItemType Directory -Path $plugins -Force | Out-Null
foreach ($assembly in @('Core', 'Assets', 'Data.Model', 'Data', 'Pathfinding', 'Simulation', 'Presentation', 'Art')) {
    Copy-Item -LiteralPath "$repo/src/DK2.$assembly/bin/Release/netstandard2.1/DK2.$assembly.dll" -Destination $plugins -Force
}
Copy-Item -LiteralPath "$PSScriptRoot/InGameOptionsProbe.cs" -Destination "$project/Assets/DK2/Editor/InGameOptionsProbe.cs"
[IO.File]::WriteAllText("$project/.in-game-options-probe", $run)
$log = Join-Path $run 'unity.log'
$arguments = @('-batchmode', '-projectPath', $project, '-executeMethod', 'DK2.Unity.Editor.InGameOptionsProbe.Run', '-logFile', $log)
$quoted = foreach ($argument in $arguments) { '"' + $argument + '"' }
$process = Start-Process -FilePath $Unity -ArgumentList ($quoted -join ' ') -WindowStyle Hidden -PassThru
Write-Output "In-game Options probe PID=$($process.Id); output=$run"
while (!$process.WaitForExit(10000)) { Write-Output "In-game Options probe running: $log" }
if ($process.ExitCode -ne 0) { throw "In-game Options probe failed ($($process.ExitCode)): $log" }
Select-String -LiteralPath $log -Pattern 'verification passed'
