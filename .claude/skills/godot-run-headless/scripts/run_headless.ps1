<#
.SYNOPSIS
    Run the Martial Heroes Godot client HEADLESS and capture all stdout/stderr
    (GD.Print, GD.PrintErr, engine ERROR/SCRIPT ERROR lines) to a log + the console.

.DESCRIPTION
    Boots the Godot 4.6.3-mono CONSOLE executable against the client project with
    --headless (no window / no GPU surface), lets it render N frames, then quits via
    --quit-after. The console exe is required so the engine writes to stdout; the plain
    .exe detaches and surfaces nothing.

    This is the headless half of the verify loop: it confirms scenes parse, scripts
    instantiate, autoloads run, and assets resolve — WITHOUT a human opening the editor.
    It cannot capture pixels (no rendering surface); use the godot-screenshot skill for that.

    A hard wall-clock timeout guards against a hung asset load blocking the run forever:
    if Godot does not exit within -TimeoutSec, it is force-killed and exit code 124 is returned.

.PARAMETER Frames
    Number of rendered frames before --quit-after fires. ~150 ≈ a few seconds at 60 fps,
    enough for async terrain/asset loads to print. Default 150.

.PARAMETER Project
    Path to the Godot project dir (the one containing project.godot).

.PARAMETER Godot
    Path to the Godot 4.6.3-mono CONSOLE executable.

.PARAMETER Scene
    Optional res:// scene to run instead of the project's main scene (focused test).

.PARAMETER TimeoutSec
    Hard wall-clock kill timeout in seconds. Default 90.

.PARAMETER Log
    Output log file. Default: a timestamped file under $env:TEMP.

.EXAMPLE
    pwsh -File run_headless.ps1 -Frames 150
.EXAMPLE
    pwsh -File run_headless.ps1 -Scene res://Scenes/World.tscn -TimeoutSec 60
#>
[CmdletBinding()]
param(
    [int]    $Frames     = 150,
    [string] $Project    = "C:/Users/Arius/RiderProjects/MartialHeroes/05.Presentation/MartialHeroes.Client.Godot",
    [string] $Godot      = "C:/Users/Arius/Desktop/Godot_v4.6.3-stable_mono_win64/Godot_v4.6.3-stable_mono_win64_console.exe",
    [string] $Scene      = "",
    [int]    $TimeoutSec = 90,
    [string] $Log        = ""
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $Godot))   { Write-Error "Godot console exe not found: $Godot"; exit 2 }
if (-not (Test-Path (Join-Path $Project "project.godot"))) {
    Write-Error "Not a Godot project (no project.godot): $Project"; exit 2
}
if ([string]::IsNullOrWhiteSpace($Log)) {
    $stamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $Log = Join-Path $env:TEMP "mh-godot-headless-$stamp.log"
}

# Build argument list. --headless: no window. --quit-after N: quit after N frames.
$gargs = @("--headless", "--path", $Project, "--quit-after", "$Frames")
if (-not [string]::IsNullOrWhiteSpace($Scene)) { $gargs += $Scene }

Write-Host "=== godot-run-headless ===" -ForegroundColor Cyan
Write-Host "exe    : $Godot"
Write-Host "project: $Project"
Write-Host "frames : $Frames    timeout: ${TimeoutSec}s"
if ($Scene) { Write-Host "scene  : $Scene" }
Write-Host "log    : $Log"
Write-Host "--------------------------" -ForegroundColor Cyan

# Run with merged stderr->stdout so SCRIPT ERROR/ERROR lines are interleaved in capture order.
# Start-Process can't easily merge streams to one ordered file, so we use the call operator with
# a redirect and a watchdog job for the wall-clock kill.
$exitCode = 0
$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName               = $Godot
foreach ($a in $gargs) { [void]$psi.ArgumentList.Add($a) }
$psi.RedirectStandardOutput = $true
$psi.RedirectStandardError  = $true
$psi.UseShellExecute        = $false
$psi.CreateNoWindow         = $true

$proc = [System.Diagnostics.Process]::Start($psi)
# Read both streams asynchronously to avoid pipe-buffer deadlock on chatty output.
$stdoutTask = $proc.StandardOutput.ReadToEndAsync()
$stderrTask = $proc.StandardError.ReadToEndAsync()

if (-not $proc.WaitForExit($TimeoutSec * 1000)) {
    Write-Host "TIMEOUT after ${TimeoutSec}s — killing Godot (possible hung load)." -ForegroundColor Yellow
    try { $proc.Kill($true) } catch { try { $proc.Kill() } catch {} }
    $exitCode = 124
} else {
    $exitCode = $proc.ExitCode
}

$out = $stdoutTask.GetAwaiter().GetResult()
$err = $stderrTask.GetAwaiter().GetResult()
$combined = ($out + "`n" + $err).TrimEnd()

# Persist + echo.
$combined | Out-File -FilePath $Log -Encoding UTF8
Write-Host $combined
Write-Host "--------------------------" -ForegroundColor Cyan
Write-Host "exit code: $exitCode   (log: $Log)" -ForegroundColor Cyan

# Surface a quick triage summary so the caller sees failures even in a long log.
$errLines = $combined -split "`r?`n" | Where-Object {
    $_ -match 'SCRIPT ERROR|Unhandled exception|^ERROR:|Failed to load|Cannot open file|Parse Error|does not exist'
}
if ($errLines) {
    Write-Host "=== suspected problems ===" -ForegroundColor Red
    $errLines | Select-Object -First 25 | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
} else {
    Write-Host "no obvious error lines found (verify expected GD.Print breadcrumbs above)." -ForegroundColor Green
}

exit $exitCode
