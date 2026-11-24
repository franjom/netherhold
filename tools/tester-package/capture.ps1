<#
.SYNOPSIS
    Records one timed run of the game on this machine and zips the result. Read README.txt first.
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File capture.ps1 -Install "C:\GOG Games\Dungeon Keeper 2"
#>
param(
    [Parameter(Mandatory = $true)][string]$Install,
    [string]$Level = 'Level6a',
    [int]$Seconds = 90,
    [int]$Seed = 1,
    [int]$Width = 1920,
    [int]$Height = 1080,
    [switch]$Windowed,
    [int]$FrameCap = -1,
    [int]$VSync = 0,
    # The harness's camera (--perf-camera): tiles east and south of the level's start focus, keyed by frame. The opening
    # fly-by draws over it while it plays; the loop starts at frame 6000 (50 s at 120 FPS).
    [string]$Camera = '0:0,0;6000:0,0;6450:8,0;6900:8,8;7350:0,8;7800:0,0',
    # Godot's rendering driver: empty for the project's (Vulkan, falling back to Direct3D 12), or vulkan or d3d12.
    [string]$RenderingDriver = '',
    [string[]]$ExtraArgs = @(),
    [switch]$NoPresentMon,
    [string]$Output = ''
)
$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$game = Join-Path $here 'game/DK2.exe'
$presentMon = Join-Path $here 'tools/PresentMon-2.6.0-x64.exe'
if (!(Test-Path -LiteralPath $game)) { throw "No game at $game; unzip the whole package" }
if (!(Test-Path -LiteralPath (Join-Path $Install 'Data'))) { throw "No Dungeon Keeper 2 install at $Install (expected its Data folder)" }

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$capture = if ($Output) { $Output } else { Join-Path $here "captures/$stamp" }
# The run's own profile, settings, saves, hi-scores and launcher file, so the player's own are never read or written.
$files = Join-Path $capture 'player-files'
New-Item -ItemType Directory -Path "$files/saves" -Force | Out-Null
$capture = (Resolve-Path -LiteralPath $capture).Path
Copy-Item -LiteralPath (Join-Path $here 'manifest.txt') -Destination "$capture/manifest.txt" -Force

# The machine, as Windows reports it; the game writes what Godot sees beside it.
$cpu = Get-CimInstance Win32_Processor | Select-Object -First 1
$os = Get-CimInstance Win32_OperatingSystem
$lines = @("CPU=$($cpu.Name.Trim())", "CPUCores=$($cpu.NumberOfCores)", "CPUThreads=$($cpu.NumberOfLogicalProcessors)",
    "OS=$($os.Caption) $($os.Version)", "RAMGB=$([math]::Round($os.TotalVisibleMemorySize / 1MB, 1))",
    "Captured=$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss zzz')", "Level=$Level", "Seconds=$Seconds", "Seed=$Seed")
foreach ($gpu in Get-CimInstance Win32_VideoController) {
    $lines += "GPU=$($gpu.Name); driver $($gpu.DriverVersion) ($($gpu.DriverDate.ToString('yyyy-MM-dd'))); $($gpu.CurrentHorizontalResolution)x$($gpu.CurrentVerticalResolution) @ $($gpu.CurrentRefreshRate) Hz"
}
Set-Content -LiteralPath "$capture/machine.txt" -Value $lines

Add-Type -AssemblyName System.Windows.Forms, System.Drawing
$screen = [Windows.Forms.Screen]::PrimaryScreen.Bounds
# Full screen takes the desktop's size (the game does not change the display mode); a window renders at Width x Height.
if (!$Windowed -and ($screen.Width -ne $Width -or $screen.Height -ne $Height)) {
    Write-Warning "Full screen runs at the desktop's $($screen.Width)x$($screen.Height), not ${Width}x${Height}; pass -Windowed for that size"
}

function Quote([string[]]$Arguments) {
    foreach ($argument in $Arguments) {
        if ($argument.Contains('"')) { throw 'Embedded quotes in process argument' }
        '"' + $argument + '"'
    }
}

# PresentMon traces the game's presents (frame-to-frame intervals the display saw), for the
# whole run plus a margin; it needs an elevated PowerShell on most machines.
$monitor = $null
if (!$NoPresentMon) {
    if (@(Get-Process -Name 'DK2' -ErrorAction SilentlyContinue).Count -ne 0) { throw 'Another DK2.exe is running; close it first' }
    $session = 'DK2Tester-' + [guid]::NewGuid().ToString('N')
    $monitorArguments = Quote @('--process_name', 'DK2.exe', '--output_file', "$capture/presentmon.csv", '--qpc_time',
        '--no_console_stats', '--no_track_input', '--no_track_gpu', '--timed', "$($Seconds + 30)", '--terminate_after_timed', '--session_name', $session)
    $monitor = Start-Process -FilePath $presentMon -ArgumentList ($monitorArguments -join ' ') -WindowStyle Hidden -PassThru `
        -RedirectStandardOutput "$capture/presentmon.log" -RedirectStandardError "$capture/presentmon-errors.log"
    $null = $monitor.Handle
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

# The game: a campaign level with a fixed seed, hands-off. Godot's own arguments come before '--', the game's after it.
# The pointer is held at the centre by the game (--pointer-path), so the mouse cannot steer the camera. The recorder and
# the harness write frames.csv and perf-report.json when the game closes.
$engineArguments = @('--log-file', "$capture/godot.log")
if ($FrameCap -gt 0) { $engineArguments += @('--max-fps', "$FrameCap") }
if ($RenderingDriver) { $engineArguments += @('--rendering-driver', $RenderingDriver) }
$centre = if ($Windowed) { "0:$([int]($Width / 2)),$([int]($Height / 2))" } else { "0:$([int]($screen.Width / 2)),$([int]($screen.Height / 2))" }
$gameArguments = @('--level', $Level, '--seed', "$Seed", '--dk2Install', $Install, '--record-frames', "$capture/recorder",
    '--perf-report', "$capture/perf-report.json", '--perf-camera', $Camera, '--pointer-path', $centre,
    '--profile', "$files/profile.dk2p", '--settings', "$files/settings.ini", '--saves', "$files/saves",
    '--hiscores', "$files/HiScores.dat", '--launcher-config', "$files/launcher.cfg")
if ($Windowed) { $gameArguments += @('--perf-size', "${Width}x${Height}") }
if ($VSync -ne 0) { $gameArguments += '--perf-vsync' }
$playerArguments = $engineArguments + @('--') + $gameArguments + $ExtraArgs
Set-Content -LiteralPath "$capture/player-arguments.txt" -Value ($playerArguments -join ' ')
ConvertTo-Json -InputObject $playerArguments | Set-Content -LiteralPath "$capture/player-arguments.json" -Encoding UTF8
Write-Host "Playing $Level for $Seconds s. Leave the game alone until it closes by itself."
# DK2_DIR would win over -Install; this process's copy only.
$env:DK2_DIR = $Install
$process = Start-Process -FilePath $game -ArgumentList ((Quote $playerArguments) -join ' ') -PassThru
$null = $process.Handle
$timer = [Diagnostics.Stopwatch]::StartNew()
while (!$process.WaitForExit(500)) {
    if ($timer.Elapsed.TotalSeconds -ge $Seconds) { break }
}
if ($process.HasExited) { Write-Warning "The game closed on its own after $([int]$timer.Elapsed.TotalSeconds) s (exit code $($process.ExitCode)); see $capture/godot.log" }

# A screenshot after the timed interval, never during it: the screen, or only the game's window when windowed.
try {
    $bounds = $screen
    if ($Windowed -and !$process.HasExited) {
        Add-Type -Namespace DK2Capture -Name Window -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr window, out RECT rect);
[StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
'@
        $process.Refresh()
        $rect = New-Object DK2Capture.Window+RECT
        if ($process.MainWindowHandle -ne [IntPtr]::Zero -and [DK2Capture.Window]::GetWindowRect($process.MainWindowHandle, [ref]$rect)) {
            $bounds = [Drawing.Rectangle]::FromLTRB($rect.Left, $rect.Top, $rect.Right, $rect.Bottom)
        }
    }
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
if (!(Test-Path -LiteralPath "$capture/recorder/frames.csv")) { Write-Warning "No frames.csv was recorded; see $capture/godot.log" }

$zip = Join-Path (Split-Path $capture) "dk2-capture-$stamp.zip"
Compress-Archive -Path "$capture/*" -DestinationPath $zip -CompressionLevel Optimal
Write-Host "Done. Send this file: $zip"
