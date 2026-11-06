<#
.SYNOPSIS
    Records three Level6a release runs serially and compares them with three reviewed baseline captures.
#>
param(
    [Parameter(Mandatory = $true)][string]$Package,
    [Parameter(Mandatory = $true)][string]$Install,
    [Parameter(Mandatory = $true)][string[]]$Baseline,
    [Parameter(Mandatory = $true)][string]$OutputRoot
)
$ErrorActionPreference = 'Stop'
if ($Baseline.Count -lt 3) { throw 'Pass at least three baseline capture directories' }
$packagePath = (Resolve-Path -LiteralPath $Package).Path
$captureScript = Join-Path $packagePath 'capture.ps1'
if (!(Test-Path -LiteralPath $captureScript)) { throw "No capture.ps1 in $packagePath" }
$runRoot = Join-Path $OutputRoot ('level6a-regression-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $runRoot
$candidates = @()
for ($index = 1; $index -le 3; $index++) {
    $destination = Join-Path $runRoot "run-$index"
    & powershell -NoProfile -ExecutionPolicy Bypass -File $captureScript -Install $Install -Level level6a `
        -Seed 1 -Seconds 40 -Width 1920 -Height 1080 -Windowed -FrameCap -1 -VSync 0 -Output $destination
    if ($LASTEXITCODE -ne 0) { throw "Capture $index failed; evidence is in $runRoot" }
    $candidates += $destination
}
$report = & python "$PSScriptRoot/compare_captures.py" --baseline @Baseline --candidate @candidates
$comparisonExit = $LASTEXITCODE
$report | Set-Content -LiteralPath (Join-Path $runRoot 'comparison.json') -Encoding UTF8
$report | Write-Output
exit $comparisonExit
