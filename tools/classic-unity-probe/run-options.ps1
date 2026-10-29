# Copyright (C) 2026 Franjo Misetic
# SPDX-License-Identifier: GPL-3.0-or-later
# See LICENSE in the project root for the full license text.

param([string]$Unity = 'C:/Program Files/Unity 6000.6.0f1/Editor/Unity.exe', [switch]$Gameplay)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path "$PSScriptRoot/../..").Path
$run = Join-Path $repo ('build/options-settings-probe-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
$project = Join-Path $run 'project'
if (Test-Path -LiteralPath $run) { throw 'Fresh probe directory required.' }
New-Item -ItemType Directory -Path $project | Out-Null
foreach ($folder in @('Assets', 'Packages', 'ProjectSettings')) {
    Copy-Item -LiteralPath "$repo/unity/DK2.Unity/$folder" -Destination $project -Recurse
}
$plugins = Join-Path $project 'Assets/Plugins/DK2'
New-Item -ItemType Directory -Path $plugins -Force | Out-Null
& "$repo/unity/copy-runtime-assemblies.ps1" -RepositoryRoot $repo -Destination $plugins -Configuration Release
Copy-Item -LiteralPath "$PSScriptRoot/OptionsSettingsProbe.cs" -Destination "$project/Assets/DK2/Editor/OptionsSettingsProbe.cs"
Copy-Item -LiteralPath "$PSScriptRoot/OptionsGameplaySettingsProbe.cs" -Destination "$project/Assets/DK2/Editor/OptionsGameplaySettingsProbe.cs"
$assemblyPath = "$project/Assets/DK2/Editor/DK2.Unity.Editor.asmdef"
$assembly = Get-Content -LiteralPath $assemblyPath -Raw | ConvertFrom-Json
$assembly.references = @($assembly.references) + @('Unity.RenderPipelines.Universal.Runtime', 'Unity.RenderPipelines.Core.Runtime')
[IO.File]::WriteAllText($assemblyPath, ($assembly | ConvertTo-Json -Depth 10), [Text.UTF8Encoding]::new($false))
New-Item -ItemType File -Path "$project/.options-settings-probe" | Out-Null
$log = Join-Path $run 'unity.log'
$entry = if ($Gameplay) { 'DK2.Unity.Editor.OptionsGameplaySettingsProbe.Run' } else { 'DK2.Unity.Editor.OptionsSettingsProbe.Run' }
$arguments = @('-batchmode' , '-projectPath', $project, '-executeMethod', $entry, '-logFile', $log)
$quoted = foreach ($argument in $arguments) { '"' + $argument + '"' }
$process = Start-Process -FilePath $Unity -ArgumentList ($quoted -join ' ') -WindowStyle Hidden -PassThru
Write-Output "Options probe PID=$($process.Id); output=$run"
while (!$process.WaitForExit(10000)) { Write-Output "Options probe running: $log" }
if ($process.ExitCode -ne 0) { throw "Options probe failed ($($process.ExitCode)): $log" }
Select-String -LiteralPath $log -Pattern 'Options settings capture|Options icon|verification passed|Gameplay settings'
