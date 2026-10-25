# Copyright (C) 2026 Franjo Misetic
# SPDX-License-Identifier: GPL-3.0-or-later
# See LICENSE in the project root for the full license text.

param([string]$Unity = 'C:/Program Files/Unity/Hub/Editor/6000.6.0f1/Editor/Unity.exe', [switch]$Menus)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path "$PSScriptRoot/../..").Path
$run = Join-Path $repo ('build/front-end-probe-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
$project = Join-Path $run 'project'
if (Test-Path -LiteralPath $run) { throw 'A fresh probe directory is required.' }
New-Item -ItemType Directory -Path $project | Out-Null
foreach ($folder in @('Assets', 'Packages', 'ProjectSettings')) {
    Copy-Item -LiteralPath "$repo/unity/DK2.Unity/$folder" -Destination $project -Recurse
}
$plugins = Join-Path $project 'Assets/Plugins/DK2'
New-Item -ItemType Directory -Path $plugins -Force | Out-Null
foreach ($assembly in @('Core', 'Assets', 'Data.Model', 'Data', 'Pathfinding', 'Simulation', 'Presentation', 'Art')) {
    $dll = "$repo/src/DK2.$assembly/bin/Release/netstandard2.1/DK2.$assembly.dll"
    if (!(Test-Path -LiteralPath $dll)) { throw "Build the Release assemblies first: missing $dll" }
    Copy-Item -LiteralPath $dll -Destination $plugins -Force
}
Copy-Item -LiteralPath "$PSScriptRoot/FrontEndSessionProbe.cs" -Destination "$project/Assets/DK2/Editor/FrontEndSessionProbe.cs"
if ($Menus) {
    Copy-Item -LiteralPath "$PSScriptRoot/FrontEndMenuProbe.cs" -Destination "$project/Assets/DK2/Editor/FrontEndMenuProbe.cs"
    Copy-Item -LiteralPath "$PSScriptRoot/FrontEndProbeComposite.shader" -Destination "$project/Assets/DK2/Editor/FrontEndProbeComposite.shader"
}
$assemblyPath = "$project/Assets/DK2/Editor/DK2.Unity.Editor.asmdef"
$assembly = Get-Content -LiteralPath $assemblyPath -Raw | ConvertFrom-Json
$assembly.references = @($assembly.references) + @('Unity.RenderPipelines.Universal.Runtime', 'Unity.RenderPipelines.Core.Runtime')
[IO.File]::WriteAllText($assemblyPath, ($assembly | ConvertTo-Json -Depth 10), [Text.UTF8Encoding]::new($false))
New-Item -ItemType File -Path "$project/.front-end-session-probe" | Out-Null
$log = Join-Path $run 'unity.log'
$entry = if ($Menus) { 'DK2.Unity.Editor.FrontEndMenuProbe.Run' } else { 'DK2.Unity.Editor.FrontEndSessionProbe.Run' }
$arguments = @('-batchmode', '-projectPath', $project, '-executeMethod', $entry, '-logFile', $log)
$quoted = foreach ($argument in $arguments) { '"' + $argument + '"' }
$process = Start-Process -FilePath $Unity -ArgumentList ($quoted -join ' ') -WindowStyle Hidden -PassThru
Write-Output "Probe PID=$($process.Id); output=$run"
while (!$process.WaitForExit(10000)) { Write-Output "Front-end probe running: $log" }
if ($process.ExitCode -ne 0) { throw "Probe failed ($($process.ExitCode)): $log" }
Select-String -LiteralPath $log -Pattern 'Front-end capture|verification passed'
