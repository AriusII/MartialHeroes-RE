<#
.SYNOPSIS
    Boot the Martial Heroes Godot client WINDOWED with the temporary _shot.gd autoload and
    wait for it to write one PNG of the rendered scene.

.DESCRIPTION
    Sets the env vars _shot.gd reads (MH_SHOT_PNG, MH_SHOT_FRAMES), launches the Godot
    4.6.3-mono console exe WINDOWED (NOT --headless — a headless run has no GPU surface and the
    viewport texture is blank), and blocks until the PNG appears or a wall-clock timeout trips.

    PREREQUISITE: res://Dev/_shot.gd must be staged AND registered as an autoload in project.godot
    (ShotCapture="*res://Dev/_shot.gd"). The godot-screenshot skill does this before calling here,
    and REMOVES both again afterwards (the autoload self-quits on every launch).

.PARAMETER Out
    Output PNG path. Default: a timestamped file under $env:TEMP.

.PARAMETER Frames
    Warmup frames before capture (passed to _shot.gd via MH_SHOT_FRAMES). Default 180. Increase
    if terrain/NPCs stream in late and the shot comes out empty.

.PARAMETER Project
    Godot project dir (contains project.godot).

.PARAMETER Godot
    Godot 4.6.3-mono console exe.

.PARAMETER TimeoutSec
    Wall-clock kill timeout. Default 90.

.EXAMPLE
    pwsh -File screenshot.ps1 -Frames 240
#>
[CmdletBinding()]
param(
    [string] $Out        = "",
    [int]    $Frames     = 180,
    [string] $Project    = "C:/Users/Arius/RiderProjects/MartialHeroes/05.Presentation/MartialHeroes.Client.Godot",
    [string] $Godot      = "C:/Users/Arius/Desktop/Godot_v4.6.3-stable_mono_win64/Godot_v4.6.3-stable_mono_win64_console.exe",
    [int]    $TimeoutSec = 90
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $Godot)) { Write-Error "Godot console exe not found: $Godot"; exit 2 }
if (-not (Test-Path (Join-Path $Project "project.godot"))) {
    Write-Error "Not a Godot project (no project.godot): $Project"; exit 2
}
$shot = Join-Path $Project "Dev/_shot.gd"
if (-not (Test-Path $shot)) {
    Write-Warning "res://Dev/_shot.gd is not staged — the skill must copy it AND register the"
    Write-Warning "ShotCapture autoload in project.godot before running this helper."
}

if ([string]::IsNullOrWhiteSpace($Out)) {
    $stamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $Out = Join-Path $env:TEMP "mh-godot-shot-$stamp.png"
}
# Start clean so we can detect a fresh write rather than an old file.
if (Test-Path $Out) { Remove-Item $Out -Force }

$env:MH_SHOT_PNG    = $Out
$env:MH_SHOT_FRAMES = "$Frames"

Write-Host "=== godot-screenshot ===" -ForegroundColor Cyan
Write-Host "exe    : $Godot"
Write-Host "project: $Project"
Write-Host "frames : $Frames    timeout: ${TimeoutSec}s"
Write-Host "out    : $Out"
Write-Host "------------------------" -ForegroundColor Cyan

# WINDOWED (no --headless). The autoload quits the tree once the PNG is saved.
$gargs = @("--path", $Project)
$proc  = Start-Process -FilePath $Godot -ArgumentList $gargs -PassThru -NoNewWindow

# Poll for the PNG (the autoload writes it then quits) up to the timeout.
$deadline = (Get-Date).AddSeconds($TimeoutSec)
while ((Get-Date) -lt $deadline) {
    if (Test-Path $Out) {
        # Give the write a beat to flush, then stop.
        Start-Sleep -Milliseconds 250
        break
    }
    if ($proc.HasExited) { break }
    Start-Sleep -Milliseconds 250
}

# Ensure the process is gone.
if (-not $proc.HasExited) {
    try { $proc.Kill($true) } catch { try { $proc.Kill() } catch {} }
}

if (Test-Path $Out) {
    $len = (Get-Item $Out).Length
    Write-Host "screenshot written: $Out ($len bytes)" -ForegroundColor Green
    Write-Host ""
    Write-Host "NEXT: Read the PNG to inspect the frame." -ForegroundColor Cyan
    Write-Host "CLEANUP: remove the ShotCapture autoload line from project.godot AND delete" -ForegroundColor Yellow
    Write-Host "         res://Dev/_shot.gd (+ .uid). The autoload self-quits every launch." -ForegroundColor Yellow
    exit 0
} else {
    Write-Host "NO screenshot produced within ${TimeoutSec}s." -ForegroundColor Red
    Write-Host "Checklist: is _shot.gd registered as an autoload? did you run WINDOWED (not headless)?" -ForegroundColor Red
    Write-Host "           is there a desktop session a window can open in? try a larger -Frames." -ForegroundColor Red
    Write-Host "REMEMBER to still clean up the temporary autoload + script." -ForegroundColor Yellow
    exit 1
}
