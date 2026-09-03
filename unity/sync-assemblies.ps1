<#
.SYNOPSIS
    Builds the DK2 assemblies and copies them into the Unity project.

.DESCRIPTION
    Unity cannot reference a .csproj, so the simulation reaches it as plain DLLs
    under Assets/Plugins/DK2. This builds the netstandard2.1 assemblies and
    copies them there.

    The DLLs are build output and are gitignored. Run this after changing
    anything in src/, and after pulling.

    Why netstandard2.1 matters here and not elsewhere: Unity cannot load a
    net10.0 assembly at all. That constraint was configuration-only and silently
    broken for four months (ADR-0036); this script fails loudly rather than
    copying assemblies Unity will refuse.

.EXAMPLE
    pwsh unity/sync-assemblies.ps1
#>

[CmdletBinding()]
param(
    [string] $Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent $PSScriptRoot
$target = Join-Path $PSScriptRoot 'DK2.Unity/Assets/Plugins/DK2'

# Every assembly Unity needs, in dependency order. DK2.Harness and DK2.Tools are
# absent on purpose: they are net10.0 host applications, not game code.
$projects = @(
    'DK2.Core',
    'DK2.Data.Model',
    'DK2.Data',
    'DK2.Pathfinding',
    'DK2.Simulation',
    'DK2.Assets',
    'DK2.Presentation'
)

Write-Host "Building $Configuration..."
& dotnet build (Join-Path $repo 'DK2.slnx') --configuration $Configuration --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed with exit code $LASTEXITCODE." }

if (-not (Test-Path $target)) { New-Item -ItemType Directory -Force $target | Out-Null }

foreach ($project in $projects) {
    $dll = Join-Path $repo "src/$project/bin/$Configuration/netstandard2.1/$project.dll"

    if (-not (Test-Path $dll)) {
        throw @"
$project.dll was not found at:
  $dll

If it built to a net10.0 folder instead, $project is no longer importing
src/Shared/UnityBound.props and Unity will not be able to load it. See ADR-0036
and ArchitectureTests.UnityLoadableAssemblies_TargetNetStandard21.
"@
    }

    Copy-Item $dll $target -Force
    Write-Host "  $project.dll"
}

Write-Host ""
Write-Host "Copied $($projects.Count) assemblies to unity/DK2.Unity/Assets/Plugins/DK2."
Write-Host "Open unity/DK2.Unity in Unity 6000.6.0f1, add DungeonRunner to a GameObject, and press Play."
Write-Host "Set DK2_DIR, or fill in the component's Install Directory field."
