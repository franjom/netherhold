<#
.SYNOPSIS
    Builds the tester package: a release export of the Godot game (godot/DK2.Godot/export_presets.cfg), the pinned
    PresentMon, the capture script and its instructions, zipped for someone on other hardware (docs/PERFORMANCE.md, the
    desktop tier). See README.md.
#>
param(
    [string]$OutputRoot = (Join-Path (Resolve-Path "$PSScriptRoot/../..").Path 'build/tester-package'),
    # A debug export (Godot's debug template): for diagnosis only; summarise_capture.py rates its captures invalid.
    [switch]$Development,
    [string]$InstallRoot = $(if ($env:DK2_GODOT_ROOT) { $env:DK2_GODOT_ROOT } else { Join-Path $env:LOCALAPPDATA 'Programs\Godot' })
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path "$PSScriptRoot/../..").Path
$project = Join-Path $repo 'godot/DK2.Godot'
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$package = Join-Path $OutputRoot $stamp
$game = Join-Path $package 'game'
New-Item -ItemType Directory -Path $game, "$package/tools" -Force | Out-Null

# The pinned editor and its .NET export templates, both verified by hash (godot/godot-version.json).
& "$repo/godot/setup.ps1" -InstallRoot $InstallRoot -ExportTemplates | Out-Host
$pin = Get-Content -Raw "$repo/godot/godot-version.json" | ConvertFrom-Json
$console = Join-Path (Join-Path $InstallRoot $pin.editor.version) $pin.editor.windowsConsoleExe

function Run-Native([string]$Executable, [string[]]$Arguments, [int]$Minutes) {
    $quoted = foreach ($argument in $Arguments) {
        if ($argument.Contains('"')) { throw 'Embedded quotes in process argument' }
        '"' + $argument + '"'
    }
    $process = Start-Process -FilePath $Executable -ArgumentList ($quoted -join ' ') -WindowStyle Hidden -PassThru
    $null = $process.Handle   # kept open, or ExitCode can come back empty
    $deadline = (Get-Date).AddMinutes($Minutes)
    while (!$process.WaitForExit(1000)) {
        if ((Get-Date) -gt $deadline) { $process.Kill(); throw "Timed out: $Executable" }
    }
    return $process.ExitCode
}

# The export, headless. Godot exits 0 even when the .NET part fails, so its log and the output are checked too.
$log = Join-Path ([IO.Path]::GetTempPath()) "dk2-tester-export-$stamp.log"
$mode = if ($Development) { '--export-debug' } else { '--export-release' }
Write-Host "Exporting the game ($mode; the C# is published as part of it)..."
$exit = Run-Native $console @('--headless', '--path', $project, '--log-file', $log, $mode, 'Windows Desktop', (Join-Path $game 'DK2.exe')) 20
$errors = @(Select-String -LiteralPath $log -Pattern '^(ERROR|SCRIPT ERROR):' -ErrorAction SilentlyContinue)
$data = Join-Path $game 'data_DK2.Godot_windows_x86_64'
if ($exit -ne 0 -or $errors.Count -or !(Test-Path -LiteralPath "$game/DK2.pck") -or !(Test-Path -LiteralPath "$data/DK2.Godot.dll")) {
    throw "The export failed (exit $exit, $($errors.Count) errors); see $log"
}
Remove-Item -LiteralPath $log

# The pack must hold only our own files: the game reads every DK2 file from the player's install.
& python -I "$PSScriptRoot/pck_contents.py" "$game/DK2.pck" | Out-Host
if ($LASTEXITCODE -ne 0) { throw "The exported pack holds files it must not (pck_contents.py exit $LASTEXITCODE)" }

# The pinned PresentMon (hash and publisher signature checked by the installer).
$presentMon = "$repo/build/profiling-tools/PresentMon-2.6.0-x64.exe"
& "$repo/tools/classic-unity-probe/install-presentmon.ps1" | Out-Null
Copy-Item -LiteralPath $presentMon -Destination "$package/tools/PresentMon-2.6.0-x64.exe" -Force

Copy-Item -LiteralPath "$PSScriptRoot/capture.ps1" -Destination "$package/capture.ps1" -Force
Copy-Item -LiteralPath "$PSScriptRoot/package-README.md" -Destination "$package/README.txt" -Force
Copy-Item -LiteralPath "$repo/LICENSE" -Destination "$package/LICENSE.txt" -Force
Copy-Item -LiteralPath "$repo/THIRD-PARTY-NOTICES.md" -Destination "$package/THIRD-PARTY-NOTICES.txt" -Force

# What the package was built from, so a capture names its build. --no-optional-locks: reading the status never writes the index.
$commit = git -C $repo rev-parse HEAD
$dirty = git -C $repo --no-optional-locks status --porcelain
function Hash([string]$Path) { (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash }
# Every file of the .NET data folder, as one hash over its sorted relative paths and their hashes.
$listing = Get-ChildItem -LiteralPath $data -Recurse -File | ForEach-Object { "$($_.FullName.Substring($data.Length + 1).Replace('\', '/')) $(Hash $_.FullName)" } |
    Sort-Object
$dataHash = [BitConverter]::ToString([Security.Cryptography.SHA256]::Create().ComputeHash([Text.Encoding]::UTF8.GetBytes(($listing -join "`n")))).Replace('-', '')
$assemblies = Get-ChildItem -LiteralPath $data -File | Where-Object { $_.Name -like 'DK2.*.dll' -or $_.Name -eq 'GodotSharp.dll' } | Sort-Object Name
$manifest = @("Package=$stamp", "Commit=$commit", "WorktreeClean=$([string]::IsNullOrWhiteSpace($dirty))",
    "Godot=$($pin.editor.version)", "Backend=Godot .NET", "Development=$($Development.IsPresent)", "PresentMon=2.6.0",
    "SHA256 DK2.exe=$(Hash "$game/DK2.exe")", "SHA256 DK2.pck=$(Hash "$game/DK2.pck")") +
    @($assemblies | ForEach-Object { "SHA256 $($_.Name)=$(Hash $_.FullName)" }) + @("SHA256 data folder=$dataHash")
Set-Content -LiteralPath "$package/manifest.txt" -Value $manifest
if ($dirty) { Set-Content -LiteralPath "$package/worktree-status.txt" -Value $dirty }

$zip = Join-Path $OutputRoot "dk2-tester-$stamp.zip"
Compress-Archive -Path "$package/*" -DestinationPath $zip -CompressionLevel Optimal
Write-Host "Package: $zip ($([math]::Round((Get-Item -LiteralPath $zip).Length / 1MB)) MB)"
Write-Output $zip
