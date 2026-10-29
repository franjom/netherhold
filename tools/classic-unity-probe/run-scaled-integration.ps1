# Copyright (C) 2026 Franjo Misetic
# SPDX-License-Identifier: GPL-3.0-or-later
# See LICENSE in the project root for the full license text.

param([string]$Unity = 'C:/Program Files/Unity 6000.6.0f1/Editor/Unity.exe', [string]$CachedProject = '')
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path "$PSScriptRoot/../..").Path
$run = Join-Path $repo ('build/isc-' + (Get-Date -Format 'HHmmss'))
if (Test-Path -LiteralPath $run) { throw 'Unique output directory required.' }
New-Item -ItemType Directory -Path $run | Out-Null
if ($CachedProject) {
    $project = (Resolve-Path -LiteralPath $CachedProject).Path
    $allowed = [IO.Path]::GetFullPath((Join-Path $repo 'build/isc-'))
    if (!$project.StartsWith($allowed, [StringComparison]::OrdinalIgnoreCase) -or !(Test-Path -LiteralPath "$project/.scaled-integration-probe")) { throw 'Only an isolated scaled project can be refreshed.' }
    if (Get-CimInstance Win32_Process -Filter "name='Unity.exe'" | Where-Object { $_.CommandLine -like "*$project*" }) { throw 'Close the isolated editor first.' }
    Copy-Item -Path "$repo/unity/DK2.Unity/Assets/*" -Destination "$project/Assets" -Recurse -Force
} else {
    $project = Join-Path $run 'p'
    New-Item -ItemType Directory -Path $project | Out-Null
    foreach ($folder in @('Assets', 'Packages', 'ProjectSettings')) { Copy-Item -LiteralPath "$repo/unity/DK2.Unity/$folder" -Destination $project -Recurse }
}
$plugins = Join-Path $project 'Assets/Plugins/DK2'
& "$repo/unity/copy-runtime-assemblies.ps1" -RepositoryRoot $repo -Destination $plugins -Configuration Release
Copy-Item -LiteralPath "$PSScriptRoot/ScaledIntegrationProbe.cs" -Destination "$project/Assets/DK2/Editor/ScaledIntegrationProbe.cs"
[IO.File]::WriteAllText("$project/.scaled-integration-probe", $run)
$log = Join-Path $run 'unity.log'
$arguments = @('-batchmode', '-projectPath', $project, '-executeMethod', 'DK2.Unity.Editor.ScaledIntegrationProbe.Run', '-logFile', $log)
$quoted = foreach ($argument in $arguments) { '"' + $argument + '"' }
$process = Start-Process -FilePath $Unity -ArgumentList ($quoted -join ' ') -WindowStyle Hidden -PassThru
Write-Output "Scaled integration PID=$($process.Id); output=$run"
while (!$process.WaitForExit(10000)) { Write-Output "Scaled integration running: $log" }
if ($process.ExitCode -ne 0) { throw "Scaled integration failed ($($process.ExitCode)): $log" }
Select-String -LiteralPath $log -Pattern 'verification passed'
