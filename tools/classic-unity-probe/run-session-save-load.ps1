# Copyright (C) 2026 Franjo Misetic
# SPDX-License-Identifier: GPL-3.0-or-later
# See LICENSE in the project root for the full license text.

param([string]$Unity = 'C:/Program Files/Unity 6000.6.0f1/Editor/Unity.exe')
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path "$PSScriptRoot/../..").Path
$run = Join-Path $repo ('build/asl-' + (Get-Date -Format 'HHmmss'))
$project = Join-Path $run 'p'
if (Test-Path -LiteralPath $run) { throw 'Fresh project required for each save/load run.' }
New-Item -ItemType Directory -Path $project | Out-Null
foreach ($folder in @('Assets', 'Packages', 'ProjectSettings')) {
    Copy-Item -LiteralPath "$repo/unity/DK2.Unity/$folder" -Destination $project -Recurse
}
$plugins = Join-Path $project 'Assets/Plugins/DK2'
& "$repo/unity/copy-runtime-assemblies.ps1" -RepositoryRoot $repo -Destination $plugins
Copy-Item -LiteralPath "$PSScriptRoot/SessionSaveLoadProbe.cs" -Destination "$project/Assets/DK2/Editor/SessionSaveLoadProbe.cs"
New-Item -ItemType File -Path "$project/.session-save-load-probe" | Out-Null
$log = Join-Path $run 'unity.log'
$arguments = @('-batchmode', '-projectPath', $project, '-executeMethod', 'DK2.Unity.Editor.SessionSaveLoadProbe.Run', '-logFile', $log)
$quoted = foreach ($argument in $arguments) { '"' + $argument + '"' }
$process = Start-Process -FilePath $Unity -ArgumentList ($quoted -join ' ') -WindowStyle Hidden -PassThru
Write-Output "Save/load probe PID=$($process.Id); output=$run"
while (!$process.WaitForExit(10000)) { Write-Output "Save/load probe running: $log" }
if ($process.ExitCode -ne 0) { throw "Save/load probe failed ($($process.ExitCode)): $log" }
Select-String -LiteralPath $log -Pattern 'Session restore passed|verification passed'
