# Copyright (C) 2026 Franjo Misetic
# SPDX-License-Identifier: GPL-3.0-or-later
# See LICENSE in the project root for the full license text.

# Build Unity-loadable DLLs, including the runtime MPEG decoder dependency.
[CmdletBinding()]
param([string] $Configuration = 'Release')
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$target = Join-Path $PSScriptRoot 'DK2.Unity/Assets/Plugins/DK2'
& dotnet build (Join-Path $repo 'DK2.slnx') --configuration $Configuration --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed with exit code $LASTEXITCODE." }
& (Join-Path $PSScriptRoot 'copy-runtime-assemblies.ps1') -RepositoryRoot $repo -Destination $target -Configuration $Configuration
Write-Host 'Open unity/DK2.Unity in Unity6000.6.0f1 and press Play.'
Write-Host 'Set DK2_DIR or the DungeonRunner Install Directory field.'
