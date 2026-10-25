# Copyright (C) 2026 Franjo Misetic
# SPDX-License-Identifier: GPL-3.0-or-later
# See LICENSE in the project root for the full license text.

param(
    [Parameter(Mandatory = $true)][string[]]$Profiles,
    [string]$Project = 'D:/DungeonKeeperRemake/build/tag-overlay-probe/project',
    [string]$Unity = 'C:/Program Files/Unity/Hub/Editor/6000.6.0f1/Editor/Unity.exe',
    [switch]$ResourcesOnly
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path "$PSScriptRoot/../..").Path
$lease = & "$PSScriptRoot/acquire-graphics-probe.ps1" -RepositoryRoot $repo
try {
    $projectPath = (Resolve-Path -LiteralPath $Project).Path
    $buildRoot = [IO.Path]::GetFullPath((Join-Path $repo 'build')) + [IO.Path]::DirectorySeparatorChar
    if (!$projectPath.StartsWith($buildRoot, [StringComparison]::OrdinalIgnoreCase) -or
        !(Test-Path -LiteralPath "$projectPath/.classic-window-frame-probe")) { throw 'Marked isolated build project required.' }
    Copy-Item -LiteralPath "$PSScriptRoot/PortalCpuProfileReport.cs" -Destination "$projectPath/Assets/DK2/Editor/PortalCpuProfileReport.cs" -Force
    foreach ($profile in $Profiles) {
        $raw = (Resolve-Path -LiteralPath $profile).Path
        $directory = Split-Path -Parent $raw
        Copy-Item -LiteralPath "$PSScriptRoot/PortalCpuProfileReport.cs" -Destination (Join-Path $directory 'resource-report-source.cs') -Force
        $method = if ($ResourcesOnly) { 'RunResources' } else { 'Run' }
        $arguments = @('-batchmode', '-nographics', '-projectPath', $projectPath, '-executeMethod',
            "DK2.Unity.Editor.PortalCpuProfileReport.$method", '-arrivalProfile', $raw, '-logFile', (Join-Path $directory 'resource-report.log'))
        $quoted = foreach ($argument in $arguments) {
            if ($argument.Contains('"')) { throw 'Embedded quote in process argument.' }
            '"' + $argument + '"'
        }
        $process = Start-Process -FilePath $Unity -ArgumentList ($quoted -join ' ') -WindowStyle Hidden -PassThru
        $null = $process.Handle
        $timer = [Diagnostics.Stopwatch]::StartNew()
        while (!$process.WaitForExit(1000)) {
            if ($timer.Elapsed.TotalSeconds -gt 240) { $process.Kill(); throw "CPU report timed out: $raw" }
        }
        if ($process.ExitCode -ne 0) { throw "CPU report failed: $directory/resource-report.log" }
        Write-Output "CPU report complete: $directory"
    }
} finally { $lease.Dispose() }
