<#
.SYNOPSIS
    Records one timed run of the game on this machine and zips the result. Read README.txt first.
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File capture.ps1 -Install "C:\GOG Games\Dungeon Keeper 2"
#>
param(
    [Parameter(Mandatory = $true)][string]$Install,
    [string]$Level = 'level6a',
    [int]$Seconds = 90,
    [int]$Seed = 1,
    [int]$Width = 1920,
    [int]$Height = 1080,
    [switch]$Windowed,
    [int]$FrameCap = -1,
    [int]$VSync = 0,
    [double]$ProfileFrom = 0,
    [double]$ProfileTo = 0,
    [string[]]$ExtraArgs = @(),
    [switch]$NoPresentMon,
    [string]$Output = ''
)
$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$player = Join-Path $here 'player/DK2.exe'
$presentMon = Join-Path $here 'tools/PresentMon-2.6.0-x64.exe'
if (!(Test-Path -LiteralPath $player)) { throw "No player at $player; unzip the whole package" }
if (!(Test-Path -LiteralPath (Join-Path $Install 'Data'))) { throw "No Dungeon Keeper 2 install at $Install (expected its Data folder)" }

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$capture = if ($Output) { $Output } else { Join-Path $here "captures/$stamp" }
New-Item -ItemType Directory -Path $capture -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $here 'manifest.txt') -Destination "$capture/manifest.txt" -Force

# The machine, as Windows reports it; the player writes what Unity sees beside it.
$cpu = Get-CimInstance Win32_Processor | Select-Object -First 1
$os = Get-CimInstance Win32_OperatingSystem
$lines = @("CPU=$($cpu.Name.Trim())", "CPUCores=$($cpu.NumberOfCores)", "CPUThreads=$($cpu.NumberOfLogicalProcessors)",
    "OS=$($os.Caption) $($os.Version)", "RAMGB=$([math]::Round($os.TotalVisibleMemorySize / 1MB, 1))",
    "Captured=$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss zzz')", "Level=$Level", "Seconds=$Seconds", "Seed=$Seed")
foreach ($gpu in Get-CimInstance Win32_VideoController) {
    $lines += "GPU=$($gpu.Name); driver $($gpu.DriverVersion) ($($gpu.DriverDate.ToString('yyyy-MM-dd'))); $($gpu.CurrentHorizontalResolution)x$($gpu.CurrentVerticalResolution) @ $($gpu.CurrentRefreshRate) Hz"
}
Set-Content -LiteralPath "$capture/machine.txt" -Value $lines

function Quote([string[]]$Arguments) {
    foreach ($argument in $Arguments) {
        if ($argument.Contains('"')) { throw 'Embedded quotes in process argument' }
        '"' + $argument + '"'
    }
}

# PresentMon traces the player's presents (frame-to-frame intervals the display saw), for the
# whole run plus a margin; it needs an elevated PowerShell on most machines.
$monitor = $null
if (!$NoPresentMon) {
    if (@(Get-Process -Name 'DK2' -ErrorAction SilentlyContinue).Count -ne 0) { throw 'Another DK2.exe is running; close it first' }
    $session = 'DK2Tester-' + [guid]::NewGuid().ToString('N')
    $monitorArguments = Quote @('--process_name', 'DK2.exe', '--output_file', "$capture/presentmon.csv", '--qpc_time',
        '--no_console_stats', '--no_track_input', '--no_track_gpu', '--timed', "$($Seconds + 30)", '--terminate_after_timed', '--session_name', $session)
    $monitor = Start-Process -FilePath $presentMon -ArgumentList ($monitorArguments -join ' ') -WindowStyle Hidden -PassThru `
        -RedirectStandardOutput "$capture/presentmon.log" -RedirectStandardError "$capture/presentmon-errors.log"
    $ready = [Diagnostics.Stopwatch]::StartNew()
    while ($true) {
        & logman query $session -ets *> $null
        if ($LASTEXITCODE -eq 0) { break }
        if ($monitor.HasExited -or $ready.Elapsed.TotalSeconds -gt 10) {
            if (!$monitor.HasExited) { $monitor.Kill() }
            Write-Warning "PresentMon could not start a trace (run this script from an elevated PowerShell, or pass -NoPresentMon); see $capture/presentmon-errors.log"
            $monitor = $null
            break
        }
        Start-Sleep -Milliseconds 100
    }
}

# The game: a campaign level with a fixed seed; the recorder writes frames.csv when the game closes.
$playerArguments = @('-screen-fullscreen', $(if ($Windowed) { '0' } else { '1' }), '-screen-width', "$Width", '-screen-height', "$Height",
    '-level', $Level, '-seed', "$Seed", '-dk2Install', $Install, '-frameCap', "$FrameCap", '-vsync', "$VSync",
    '-recordFrames', "$capture/recorder", '-profileFrom', "$ProfileFrom", '-profileTo', "$ProfileTo", '-logFile', "$capture/Player.log")
$playerArguments += $ExtraArgs
Set-Content -LiteralPath "$capture/player-arguments.txt" -Value ($playerArguments -join ' ')
ConvertTo-Json -InputObject $playerArguments | Set-Content -LiteralPath "$capture/player-arguments.json" -Encoding UTF8
Write-Host "Playing $Level for $Seconds s. Leave the game alone until it closes by itself."
$process = Start-Process -FilePath $player -ArgumentList ((Quote $playerArguments) -join ' ') -PassThru
$timer = [Diagnostics.Stopwatch]::StartNew()
while (!$process.WaitForExit(500)) {
    if ($timer.Elapsed.TotalSeconds -ge $Seconds) { break }
}
if ($process.HasExited) { Write-Warning "The game closed on its own after $([int]$timer.Elapsed.TotalSeconds) s (exit code $($process.ExitCode)); see $capture/Player.log" }

# A screenshot after the timed interval, never during it.
try {
    Add-Type -AssemblyName System.Windows.Forms, System.Drawing
    $bounds = [Windows.Forms.Screen]::PrimaryScreen.Bounds
    $bitmap = New-Object Drawing.Bitmap $bounds.Width, $bounds.Height
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $graphics.CopyFromScreen($bounds.Location, [Drawing.Point]::Empty, $bounds.Size)
    $bitmap.Save("$capture/screenshot.png", [Drawing.Imaging.ImageFormat]::Png)
    $graphics.Dispose(); $bitmap.Dispose()
} catch { Write-Warning "No screenshot: $_" }

# Closing the window is how the recorder gets to write; a kill loses the rows.
if (!$process.HasExited) {
    $null = $process.CloseMainWindow()
    if (!$process.WaitForExit(30000)) { $process.Kill(); Write-Warning 'The game did not close in 30 s and was killed; the frame recording may be missing' }
}
if ($monitor) {
    if (!$monitor.WaitForExit(60000)) { & logman stop $session -ets *> $null; $monitor.Kill() }
    Add-Content -LiteralPath "$capture/machine.txt" -Value "PresentMonProcessId=$($process.Id)`nPresentMonExitCode=$($monitor.ExitCode)"
}
if (!(Test-Path -LiteralPath "$capture/recorder/frames.csv")) { Write-Warning "No frames.csv was recorded; see $capture/Player.log" }

$zip = Join-Path (Split-Path $capture) "dk2-capture-$stamp.zip"
Compress-Archive -Path "$capture/*" -DestinationPath $zip -CompressionLevel Optimal
Write-Host "Done. Send this file: $zip"
