param(
    [string]$Unity = 'C:/Program Files/Unity 6000.6.0f1/Editor/Unity.exe',
    [string]$OutputRoot = "$PSScriptRoot/../../build/tag-overlay-probe",
    [switch]$PortalEntrance,
    [switch]$CreatureLevel,
    [switch]$PortalMovement,
    [switch]$TutorialHighlight,
    [switch]$CameraReference,
    [switch]$SpellSlotStates,
    [switch]$PortalPerformance,
    [ValidateSet('Default', 'Direct3D11', 'Direct3D12', 'Vulkan')]
    [string]$GraphicsApi = 'Default'
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path "$PSScriptRoot/../..").Path
$graphicsLease = & "$PSScriptRoot/acquire-graphics-probe.ps1" -RepositoryRoot $repo
try {
$project = Join-Path $OutputRoot 'project'
if (Test-Path -LiteralPath $project) {
    if (!(Test-Path -LiteralPath "$project/.tag-overlay-probe")) { throw 'Not a marked probe project' }
} else {
    New-Item -ItemType Directory -Path $project -Force | Out-Null
    New-Item -ItemType File -Path "$project/.tag-overlay-probe" | Out-Null
}
foreach ($folder in @('Assets', 'Packages', 'ProjectSettings')) {
    Copy-Item -LiteralPath "$repo/unity/DK2.Unity/$folder" -Destination $project -Recurse -Force
}
$manifestPath = "$project/Packages/manifest.json"
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
foreach ($module in @('com.unity.modules.screencapture', 'com.unity.modules.imageconversion')) {
    $manifest.dependencies | Add-Member -NotePropertyName $module -NotePropertyValue '1.0.0' -Force
}
[IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Depth 20), [Text.UTF8Encoding]::new($false))
$entries = @('TagOverlayOrderProbe', 'TransparentTerrainReuseProbe')
if ($SpellSlotStates) { $entries = @('SpellSlotStateProbe') }
if ($PortalPerformance) { $entries = @('PortalPerformanceProbe') }
if ($PortalEntrance) { $entries += 'PortalEntranceProbe' }
if ($CreatureLevel) { $entries += 'CreatureLevelEffectProbe' }
if ($PortalMovement) { $entries += 'PortalCrystalMovementProbe' }
if ($TutorialHighlight) { $entries += 'TutorialHighlightProbe' }
if ($CameraReference) { $entries += 'ClassicCameraReferenceProbe' }
foreach ($probe in $entries) {
    Copy-Item -LiteralPath "$PSScriptRoot/$probe.cs" -Destination "$project/Assets/DK2/Editor/$probe.cs" -Force
}
$assemblyPath = "$project/Assets/DK2/Editor/DK2.Unity.Editor.asmdef"
$assembly = Get-Content -LiteralPath $assemblyPath -Raw | ConvertFrom-Json
$assembly.references = @($assembly.references) + @('Unity.RenderPipelines.Universal.Runtime', 'Unity.RenderPipelines.Core.Runtime')
[IO.File]::WriteAllText($assemblyPath, ($assembly | ConvertTo-Json -Depth 10), [Text.UTF8Encoding]::new($false))
New-Item -ItemType File -Path "$project/.classic-window-frame-probe" -Force | Out-Null
foreach ($entry in $entries) {
    $log = [IO.Path]::GetFullPath((Join-Path $OutputRoot "$entry.log"))
    $arguments = @('-batchmode', '-projectPath', [IO.Path]::GetFullPath($project),
        '-executeMethod', "DK2.Unity.Editor.$entry.Run", '-logFile', $log)
    if ($GraphicsApi -eq 'Direct3D11') { $arguments += '-force-d3d11' }
    if ($GraphicsApi -eq 'Direct3D12') { $arguments += '-force-d3d12' }
    if ($GraphicsApi -eq 'Vulkan') { $arguments += '-force-vulkan' }
    $quoted = foreach ($argument in $arguments) { '"' + $argument + '"' }
    $process = Start-Process -FilePath $Unity -ArgumentList ($quoted -join ' ') -WindowStyle Hidden -PassThru
    if (!$process.WaitForExit(600000)) { $process.Kill(); throw "Probe timed out: $log" }
    if ($process.ExitCode -ne 0) { throw "Probe failed ($($process.ExitCode)): $log" }
    Select-String -LiteralPath $log -Pattern 'verification passed|research complete'
}
} finally {
    $graphicsLease.Dispose()
}
