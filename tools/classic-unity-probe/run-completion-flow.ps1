# Copyright (C) 2026 Franjo Misetic
# SPDX-License-Identifier: GPL-3.0-or-later
# See LICENSE in the project root for the full license text.

param(
    [string]$Unity = 'C:/Program Files/Unity 6000.6.0f1/Editor/Unity.exe',
    [string]$EvidenceRoot = 'D:/DK2Evidence/runs/unity-input-2026-10-29-completion',
    [string]$ScratchProject = '',
    [switch]$EditorGui
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path "$PSScriptRoot/../..").Path
$id = (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8)
$run = Join-Path $repo ('build/cf-' + [Guid]::NewGuid().ToString('N').Substring(0, 6))
$project = Join-Path $run 'p'
if ($ScratchProject) {
    $project = (Resolve-Path -LiteralPath $ScratchProject).Path
    $prefix = [IO.Path]::GetFullPath((Join-Path $repo 'build')) + [IO.Path]::DirectorySeparatorChar
    if (!$project.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) -or !(Test-Path -LiteralPath "$project/.completion-flow-probe")) {
        throw 'Reuse is limited to marked completion projects beneath this worktree build directory.'
    }
    $run = Split-Path -Parent $project
} elseif (Test-Path -LiteralPath $run) { throw 'A fresh scratch directory is required.' }
$evidence = Join-Path $EvidenceRoot $id
$state = Join-Path $run "state-$id"
if ((Test-Path -LiteralPath $evidence) -or (Test-Path -LiteralPath $state)) { throw 'Fresh evidence and private state directories are required.' }
$lease = & "$PSScriptRoot/acquire-graphics-probe.ps1" -RepositoryRoot $repo
$oldOutput = $env:DK2_COMPLETION_OUTPUT; $oldState = $env:DK2_COMPLETION_STATE; $oldJournal = $env:DK2_INPUT_JOURNAL
$sourceCommitAtCopy = git -C $repo rev-parse HEAD
$sourceChangesAtCopy = @(git -C $repo status --short)
try {
    New-Item -ItemType Directory -Path $project -Force | Out-Null
    New-Item -ItemType Directory -Path "$state/Saves" | Out-Null
    New-Item -ItemType Directory -Path "$evidence/reports" | Out-Null
    New-Item -ItemType Directory -Path "$evidence/journals" | Out-Null
    foreach ($folder in @('Assets', 'Packages', 'ProjectSettings')) {
        Copy-Item -LiteralPath "$repo/unity/DK2.Unity/$folder" -Destination $project -Recurse -Force
    }
    $plugins = Join-Path $project 'Assets/Plugins/DK2'
    & "$repo/unity/copy-runtime-assemblies.ps1" -RepositoryRoot $repo -Destination $plugins -Configuration Release
    $assemblies = @(Get-ChildItem -LiteralPath $plugins -Filter *.dll)
    if ($assemblies.Count -ne 9 -or !(Test-Path -LiteralPath "$plugins/NLayer.dll")) { throw 'Nine runtime assemblies including NLayer are required.' }
    $font = "$project/Assets/Resources/Fonts/DungeonKeeperII.ttf"
    if (!(Test-Path -LiteralPath $font)) {
        $referenceFont = 'D:/DungeonKeeperRemake/unity/DK2.Unity/Assets/Resources/Fonts/DungeonKeeperII.ttf'
        if (!(Test-Path -LiteralPath $referenceFont)) { throw 'The replacement DK2 font is missing.' }
        New-Item -ItemType Directory -Path (Split-Path -Parent $font) -Force | Out-Null
        Copy-Item -LiteralPath $referenceFont -Destination $font
    }
    Copy-Item -LiteralPath "$PSScriptRoot/CompletionFlowProbe.cs" -Destination "$project/Assets/DK2/Editor/CompletionFlowProbe.cs" -Force
    $witness = @'
// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using UnityEngine;
namespace DK2.Unity
{
    public sealed class CompletionGuiWitness : MonoBehaviour
    {
        public static Action Repainted;
        private void OnGUI() => Repainted?.Invoke();
    }
}
'@
    [IO.File]::WriteAllText("$project/Assets/DK2/CompletionGuiWitness.cs", $witness, [Text.UTF8Encoding]::new($false))
    New-Item -ItemType File -Path "$project/.completion-flow-probe" -Force | Out-Null
    $env:DK2_COMPLETION_OUTPUT = "$evidence/reports"; $env:DK2_COMPLETION_STATE = $state; $env:DK2_INPUT_JOURNAL = "$evidence/journals"
    $log = Join-Path $run "unity-$id.log"
    $arguments = @('-projectPath', $project, '-executeMethod', 'DK2.Unity.Editor.CompletionFlowProbe.Run', '-logFile', $log)
    if (!$EditorGui) { $arguments = @('-batchmode') + $arguments }
    $quoted = foreach ($argument in $arguments) { '"' + $argument + '"' }
    $process = Start-Process -FilePath $Unity -ArgumentList ($quoted -join ' ') -WindowStyle Hidden -PassThru
    Write-Output "Completion PID=$($process.Id); scratch=$project; state=$state; evidence=$evidence"
    $timer = [Diagnostics.Stopwatch]::StartNew()
    while (!$process.WaitForExit(10000)) {
        if ($timer.Elapsed.TotalMinutes -gt 30) { $process.Kill(); throw "Probe import/runtime timeout: $log" }
        Write-Output "Completion probe running: $log"
    }
    $processResult = $process.ExitCode
    $inputs = @(Get-ChildItem -LiteralPath "$project/Assets/DK2" -Recurse -File | Where-Object { $_.Extension -in @('.cs', '.shader', '.hlsl', '.compute', '.uss') })
    $metadata = [PSCustomObject]@{
        utc = [DateTime]::UtcNow.ToString('O'); unityExitCode = $processResult; scratch = $project; state = $state; log = $log
        sourceCommitAtCopy = $sourceCommitAtCopy; sourceChangesAtCopy = $sourceChangesAtCopy
        sourceCommitAtExit = (git -C $repo rev-parse HEAD); sourceChangesAtExit = @(git -C $repo status --short)
        assemblies = @($assemblies | ForEach-Object { [PSCustomObject]@{ path = $_.Name; sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash } })
        sources = @($inputs | ForEach-Object { [PSCustomObject]@{ path = $_.FullName.Substring($project.Length + 1); sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash } })
        evidence = @(Get-ChildItem -LiteralPath $evidence -Recurse -File | ForEach-Object { [PSCustomObject]@{ path = $_.FullName.Substring($evidence.Length + 1); sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash } })
    }
    $bytes = [Text.Encoding]::UTF8.GetBytes(($metadata | ConvertTo-Json -Depth 10))
    $file = [IO.File]::Open((Join-Path $evidence 'source-and-evidence-hashes.json'), [IO.FileMode]::CreateNew, [IO.FileAccess]::Write)
    try { $file.Write($bytes, 0, $bytes.Length) } finally { $file.Dispose() }
    if ($processResult -ne 0) { throw "Completion probe failed ($processResult): $log; all partial evidence preserved at $evidence" }
    Select-String -LiteralPath $log -Pattern 'Completion flow|movie:|Input journal:'
} finally { $env:DK2_COMPLETION_OUTPUT = $oldOutput; $env:DK2_COMPLETION_STATE = $oldState; $env:DK2_INPUT_JOURNAL = $oldJournal; $lease.Dispose() }
