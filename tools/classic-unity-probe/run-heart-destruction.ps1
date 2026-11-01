# Copyright (C) 2026 Franjo Misetic
# SPDX-License-Identifier: GPL-3.0-or-later
# See LICENSE in the project root for the full license text.

param(
    [string]$Unity = 'C:/Program Files/Unity/Hub/Editor/6000.6.0f1/Editor/Unity.exe',
    [string]$Install = 'E:/GogGames/Dungeon Keeper 2',
    [string]$OutputRoot = 'D:/DungeonKeeperRemake/build/l7deathgpu'
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path "$PSScriptRoot/../..").Path
$graphicsLease = & "$PSScriptRoot/acquire-graphics-probe.ps1" -RepositoryRoot $repo
try {
    $project = Join-Path $OutputRoot 'project'
    if (Test-Path -LiteralPath $project) {
        if (!(Test-Path -LiteralPath "$project/.heart-destruction-probe")) {
            throw 'Refusing to modify an unmarked Unity project.'
        }
    } else {
        New-Item -ItemType Directory -Path $project -Force | Out-Null
        New-Item -ItemType File -Path "$project/.heart-destruction-probe" | Out-Null
    }
    foreach ($folder in @('Assets', 'Packages', 'ProjectSettings')) {
        Copy-Item -LiteralPath "$repo/unity/DK2.Unity/$folder" -Destination $project -Recurse -Force
    }
    $pluginDirectory = "$project/Assets/Plugins/DK2"
    New-Item -ItemType Directory -Path $pluginDirectory -Force | Out-Null
    foreach ($name in @('DK2.Core', 'DK2.Data.Model', 'DK2.Data', 'DK2.Assets',
        'DK2.Art', 'DK2.Pathfinding', 'DK2.Simulation', 'DK2.Presentation')) {
        $assembly = "$repo/src/$name/bin/Release/netstandard2.1/$name.dll"
        if (!(Test-Path -LiteralPath $assembly)) { throw "Build Release first: $assembly" }
        Copy-Item -LiteralPath $assembly -Destination $pluginDirectory -Force
    }
    $assets = Get-Content -LiteralPath "$repo/src/DK2.Data/obj/project.assets.json" -Raw | ConvertFrom-Json
    $target = ($assets.targets.PSObject.Properties | Select-Object -First 1).Value
    foreach ($library in $assets.libraries.PSObject.Properties) {
        if ($library.Value.type -ne 'package') { continue }
        $runtime = $target.PSObject.Properties[$library.Name].Value.runtime
        foreach ($entry in $runtime.PSObject.Properties) {
            if (!$entry.Name.EndsWith('.dll')) { continue }
            $dependency = $null
            foreach ($folder in $assets.packageFolders.PSObject.Properties.Name) {
                $candidate = Join-Path $folder ($library.Value.path + '/' + $entry.Name)
                if (Test-Path -LiteralPath $candidate) { $dependency = $candidate; break }
            }
            if (!$dependency) { throw "Missing restored runtime dependency: $($entry.Name)" }
            Copy-Item -LiteralPath $dependency -Destination $pluginDirectory -Force
        }
    }
    Copy-Item -LiteralPath "$PSScriptRoot/HeartDestructionProbe.cs" -Destination "$project/Assets/DK2/Editor/HeartDestructionProbe.cs" -Force
    $manifestPath = "$project/Packages/manifest.json"
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    $manifest.dependencies | Add-Member -NotePropertyName 'com.unity.modules.imageconversion' -NotePropertyValue '1.0.0' -Force
    [IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Depth 20), [Text.UTF8Encoding]::new($false))
    $assemblyPath = "$project/Assets/DK2/Editor/DK2.Unity.Editor.asmdef"
    $assembly = Get-Content -LiteralPath $assemblyPath -Raw | ConvertFrom-Json
    $assembly.references = @(@($assembly.references) + @('Unity.RenderPipelines.Universal.Runtime',
        'Unity.RenderPipelines.Core.Runtime') | Select-Object -Unique)
    [IO.File]::WriteAllText($assemblyPath, ($assembly | ConvertTo-Json -Depth 10), [Text.UTF8Encoding]::new($false))
    $run = Join-Path $OutputRoot (Get-Date -Format 'yyyyMMdd-HHmmss')
    New-Item -ItemType Directory -Path $run | Out-Null
    Get-ChildItem -LiteralPath $pluginDirectory -Filter '*.dll' | Get-FileHash -Algorithm SHA256 |
        Export-Csv -LiteralPath "$run/assembly-hashes.csv" -NoTypeInformation
    Get-FileHash -LiteralPath "$PSScriptRoot/HeartDestructionProbe.cs" -Algorithm SHA256 |
        Export-Csv -LiteralPath "$run/probe-hash.csv" -NoTypeInformation
    $env:DOTNET_PROCESSOR_COUNT = '2'
    $env:DK2_HEART_DEATH_OUTPUT = [IO.Path]::GetFullPath($run)
    $env:DK2_HEART_DEATH_INSTALL = $Install
    $arguments = @('-batchmode', '-force-d3d11', '-job-worker-count', '2', '-projectPath',
        [IO.Path]::GetFullPath($project), '-executeMethod', 'DK2.Unity.Editor.HeartDestructionProbe.Run',
        '-logFile', [IO.Path]::GetFullPath("$run/unity.log"))
    $quoted = foreach ($argument in $arguments) {
        if ($argument.Contains('"')) { throw 'Embedded quotes in process argument.' }
        '"' + $argument + '"'
    }
    $process = Start-Process -FilePath $Unity -ArgumentList ($quoted -join ' ') -WindowStyle Hidden -PassThru
    $deadline = (Get-Date).AddMinutes(10)
    while (!$process.WaitForExit(1000)) {
        if ((Get-Date) -gt $deadline) { $process.Kill(); throw "Probe timed out: $run" }
    }
    if ($process.ExitCode -ne 0) { throw "Probe failed ($($process.ExitCode)): $run" }
    $metrics = Get-Content -LiteralPath "$run/metrics.json" -Raw | ConvertFrom-Json
    if (!$metrics.passed) { throw "Acceptance failed: $run/metrics.json" }
    Select-String -LiteralPath "$run/unity.log" -Pattern 'Heart destruction verification passed'
    Write-Output $run
} finally {
    $graphicsLease.Dispose()
}
