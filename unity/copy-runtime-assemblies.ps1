# Copyright (C) 2026 Franjo Misetic
# SPDX-License-Identifier: GPL-3.0-or-later
# See LICENSE in the project root for the full license text.

[CmdletBinding()]
param(
    [string] $RepositoryRoot = (Split-Path -Parent $PSScriptRoot),
    [Parameter(Mandatory = $true)] [string] $Destination,
    [string] $Configuration = 'Release'
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path -LiteralPath $RepositoryRoot).Path
$target = [IO.Path]::GetFullPath($Destination)
$projects = @('DK2.Core', 'DK2.Data.Model', 'DK2.Data', 'DK2.Art', 'DK2.Pathfinding',
    'DK2.Simulation', 'DK2.Assets', 'DK2.Presentation')
New-Item -ItemType Directory -Path $target -Force | Out-Null
foreach ($project in $projects) {
    $dll = Join-Path $repo "src/$project/bin/$Configuration/netstandard2.1/$project.dll"
    if (!(Test-Path -LiteralPath $dll)) {
        throw "Missing Unity-loadable assembly: $dll. Build $Configuration; see ADR-0036."
    }
    Copy-Item -LiteralPath $dll -Destination $target -Force
}
$assets = Get-Content -LiteralPath (Join-Path $repo 'src/DK2.Data/obj/project.assets.json') -Raw | ConvertFrom-Json
$package = $assets.libraries.PSObject.Properties | Where-Object { $_.Name -like 'NLayer/*' } | Select-Object -First 1
if (!$package) { throw 'NLayer is missing from DK2.Data restore assets.' }
$nlayer = $null
foreach ($folder in $assets.packageFolders.PSObject.Properties.Name) {
    $candidate = Join-Path $folder "$($package.Value.path)/lib/netstandard2.0/NLayer.dll"
    if (Test-Path -LiteralPath $candidate) { $nlayer = $candidate; break }
}
if (!$nlayer) { throw 'NLayer netstandard2.0 assembly was not found in the NuGet cache.' }
Copy-Item -LiteralPath $nlayer -Destination $target -Force
Write-Host "Copied $($projects.Count + 1) runtime assemblies to $target."
