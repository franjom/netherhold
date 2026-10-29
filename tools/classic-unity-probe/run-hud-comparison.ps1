# Copyright (C) 2026 Franjo Misetic
# SPDX-License-Identifier: GPL-3.0-or-later
# See LICENSE in the project root for the full license text.

param(
    [string]$Unity = 'C:/Program Files/Unity/Hub/Editor/6000.6.0f1/Editor/Unity.exe',
    [string]$EvidenceRoot = 'D:/DK2Evidence/captures/hud-2026-10-29',
    [string]$ScratchProject = '',
    [switch]$FullMapOnly
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path "$PSScriptRoot/../..").Path
$id = (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8)
$run = Join-Path $repo ("build/hc-" + [Guid]::NewGuid().ToString('N').Substring(0, 6))
$project = Join-Path $run 'p'
$output = Join-Path $EvidenceRoot $id
if ($ScratchProject) {
    $project = (Resolve-Path -LiteralPath $ScratchProject).Path
    $prefix = [IO.Path]::GetFullPath((Join-Path $repo 'build')) + [IO.Path]::DirectorySeparatorChar
    if (!$project.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) -or !(Test-Path -LiteralPath "$project/.hud-comparison-probe")) {
        throw 'Reuse requires a marked HUD probe project inside this worktree build directory.'
    }
    $run = Split-Path -Parent $project
} elseif (Test-Path -LiteralPath $run) { throw 'A fresh scratch directory is required.' }
if (Test-Path -LiteralPath $output) { throw 'A fresh evidence directory is required.' }
$lease = & "$PSScriptRoot/acquire-graphics-probe.ps1" -RepositoryRoot $repo
$previous = $env:DK2_HUD_CAPTURE_OUTPUT
try {
    New-Item -ItemType Directory -Path $project -Force | Out-Null
    New-Item -ItemType Directory -Path $output | Out-Null
    foreach ($folder in @('Assets', 'Packages', 'ProjectSettings')) {
        Copy-Item -LiteralPath "$repo/unity/DK2.Unity/$folder" -Destination $project -Recurse -Force
    }
    $plugins = Join-Path $project 'Assets/Plugins/DK2'
    New-Item -ItemType Directory -Path $plugins -Force | Out-Null
    $fingerprints = @()
    foreach ($assembly in @('Core', 'Assets', 'Data.Model', 'Data', 'Pathfinding', 'Simulation', 'Presentation', 'Art')) {
        $dll = "$repo/src/DK2.$assembly/bin/Release/netstandard2.1/DK2.$assembly.dll"
        if (!(Test-Path -LiteralPath $dll)) { throw "Build Release first: missing $dll" }
        Copy-Item -LiteralPath $dll -Destination $plugins -Force
        $fingerprints += [PSCustomObject]@{ path = "DK2.$assembly.dll"; sha256 = (Get-FileHash -LiteralPath $dll -Algorithm SHA256).Hash }
    }
    $font = "$project/Assets/Resources/Fonts/DungeonKeeperII.ttf"
    if (!(Test-Path -LiteralPath $font)) {
        $mainFont = 'D:/DungeonKeeperRemake/unity/DK2.Unity/Assets/Resources/Fonts/DungeonKeeperII.ttf'
        if (!(Test-Path -LiteralPath $mainFont)) { throw 'The requested DK2 replacement font is missing.' }
        New-Item -ItemType Directory -Path (Split-Path $font) -Force | Out-Null
        Copy-Item -LiteralPath $mainFont -Destination $font
    }
    Copy-Item -LiteralPath "$PSScriptRoot/HudComparisonProbe.cs" -Destination "$project/Assets/DK2/Editor/HudComparisonProbe.cs" -Force
    Copy-Item -LiteralPath "$PSScriptRoot/FrontEndProbeComposite.shader" -Destination "$project/Assets/DK2/Editor/FrontEndProbeComposite.shader" -Force
    $asmPath = "$project/Assets/DK2/Editor/DK2.Unity.Editor.asmdef"
    $asm = Get-Content -LiteralPath $asmPath -Raw | ConvertFrom-Json
    $asm.references = @(@($asm.references) + @('Unity.RenderPipelines.Universal.Runtime', 'Unity.RenderPipelines.Core.Runtime') | Select-Object -Unique)
    [IO.File]::WriteAllText($asmPath, ($asm | ConvertTo-Json -Depth 10), [Text.UTF8Encoding]::new($false))
    New-Item -ItemType File -Path "$project/.hud-comparison-probe" -Force | Out-Null
    $env:DK2_HUD_CAPTURE_OUTPUT = $output
    $log = Join-Path $run "unity-$id.log"
    $entry = if ($FullMapOnly) { 'DK2.Unity.Editor.HudComparisonProbe.RunFullMap' } else { 'DK2.Unity.Editor.HudComparisonProbe.Run' }
    $arguments = @('-batchmode', '-projectPath', $project, '-executeMethod', $entry, '-logFile', $log)
    $quoted = foreach ($argument in $arguments) { '"' + $argument + '"' }
    $process = Start-Process -FilePath $Unity -ArgumentList ($quoted -join ' ') -WindowStyle Hidden -PassThru
    Write-Output "HUD probe PID=$($process.Id); scratch=$run; evidence=$output"
    while (!$process.WaitForExit(10000)) { Write-Output "HUD probe running: $log" }
    if ($process.ExitCode -ne 0) { throw "HUD probe failed ($($process.ExitCode)): $log; partial evidence preserved at $output" }
    $sourceHashes = Get-ChildItem -LiteralPath "$project/Assets/DK2" -Recurse -File | Where-Object { $_.Extension -in @('.cs', '.shader', '.hlsl', '.compute', '.uss') } | ForEach-Object {
        [PSCustomObject]@{ path = $_.FullName.Substring($project.Length + 1); sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
    }
    $manifest = [PSCustomObject]@{ sourceCommit = (git -C $repo rev-parse HEAD); sourceDirty = @(git -C $repo status --short); scratch = $run;
        createdUtc = [DateTime]::UtcNow.ToString('O'); assemblies = $fingerprints; sources = @($sourceHashes);
        images = @(Get-ChildItem -LiteralPath $output -Filter *.png | ForEach-Object { [PSCustomObject]@{ path = $_.Name; sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash } }) }
    $bytes = [Text.Encoding]::UTF8.GetBytes(($manifest | ConvertTo-Json -Depth 10))
    $file = [IO.File]::Open((Join-Path $output 'source-and-image-hashes.json'), [IO.FileMode]::CreateNew, [IO.FileAccess]::Write)
    try { $file.Write($bytes, 0, $bytes.Length) } finally { $file.Dispose() }
    Select-String -LiteralPath $log -Pattern 'HUD capture|HUD comparison verification'
} finally { $env:DK2_HUD_CAPTURE_OUTPUT = $previous; $lease.Dispose() }
