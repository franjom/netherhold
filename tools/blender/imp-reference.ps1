# Rebuilds the original Imp's Blender reference set (docs/ASSET_PIPELINE.md section 10, step 1):
# converts the catalogue, stages the Imp's clips, imports and inspects them in Blender, renders.
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools/blender/imp-reference.ps1 -Blender <blender.exe> -Dk2Dir <DK2 install>
#
# -Blender defaults to $env:BLENDER, -Dk2Dir to $env:DK2_DIR. Everything written lands under
# build/ (gitignored): the whole catalogue in build/converted-assets, the reference in
# build/reference/imp. -NoVideo skips the previews, -NoRender everything but the inspection.
param(
    [string]$Blender = $env:BLENDER,
    [string]$Dk2Dir = $env:DK2_DIR,
    [switch]$NoVideo,
    [switch]$NoRender
)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
if (-not $Blender -or -not (Test-Path $Blender)) { throw "Blender not found: pass -Blender or set BLENDER (got '$Blender')" }
if (-not $Dk2Dir -or -not (Test-Path (Join-Path $Dk2Dir 'Data\Meshes.WAD'))) { throw "DK2 install not found: pass -Dk2Dir or set DK2_DIR (got '$Dk2Dir')" }

function Invoke-Checked([string]$what, [scriptblock]$command) {
    & $command
    if ($LASTEXITCODE -ne 0) { throw "$what failed with exit code $LASTEXITCODE" }
}

$build = Join-Path $root 'build'
$reference = Join-Path $build 'reference\imp'
New-Item -ItemType Directory -Force $reference | Out-Null

Invoke-Checked 'dotnet build' { dotnet build (Join-Path $root 'src\DK2.Tools') -c Release -v q -nologo }
$tools = Join-Path $root 'src\DK2.Tools\bin\Release\net10.0\DK2.Tools.dll'

# dk2tools convert has no subject filter and only writes below ./converted-assets/.
$env:DK2_DIR = $Dk2Dir
Push-Location $build
try {
    Invoke-Checked 'dk2tools convert' { dotnet $tools convert --out converted-assets/ | Out-File -Encoding utf8 convert.log }
} finally { Pop-Location }

$keys = 'idle', 'moving_to_job', 'working', 'dying' | ForEach-Object { '--key'; "creature.imp.animation.$_" }
Invoke-Checked 'stage_reference.py' {
    python (Join-Path $PSScriptRoot 'stage_reference.py') --converted (Join-Path $build 'converted-assets') `
        --log (Join-Path $build 'convert.log') --subject creature.imp --out (Join-Path $reference 'source') `
        --install $Dk2Dir @keys --key creature.imp.mesh
}

$sceneArgs = @('--sources', (Join-Path $reference 'source\sources.json'), '--out', $reference, '--blend', 'imp_reference.blend')
if ($NoVideo) { $sceneArgs += '--no-video' }
if ($NoRender) { $sceneArgs += '--no-render' }
$log = Join-Path $reference 'blender.log'
# Windows PowerShell turns a native command's stderr into an error record; under 'Stop' the first
# Python warning would end the run, so Blender's exit code is the only verdict here.
$ErrorActionPreference = 'Continue'
& $Blender --background --factory-startup --python-exit-code 1 `
    --python (Join-Path $PSScriptRoot 'reference_scene.py') -- @sceneArgs *> $log
$code = $LASTEXITCODE
$ErrorActionPreference = 'Stop'
Select-String -Path $log -Pattern '^\[(FAIL|WARN)\]|^-- ' | ForEach-Object { $_.Line }
if ($code -ne 0) { throw "reference_scene.py failed with exit code $code; see $log" }
Write-Output "report: $(Join-Path $reference 'report.md')"
