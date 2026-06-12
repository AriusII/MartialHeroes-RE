<#
.SYNOPSIS
    Build the Martial Heroes Godot client C# assembly. Optional clean-rebuild for stale Godot glue.

.DESCRIPTION
    Runs `dotnet build` on MartialHeroes.Client.Godot.csproj — the assembly the engine loads at
    runtime. Run this before /godot-run-headless or /godot-screenshot so you verify CURRENT code.

    -Clean first deletes Godot's generated mono glue (.godot/mono) plus bin/ and obj/, then rebuilds.
    Use it when `dotnet build` reports phantom errors on Godot types that clearly exist (stale glue
    after a Godot SDK bump or a project.godot change). .godot/ is editor cache and gitignored, so
    removing it is safe — the editor / next build regenerate it. If the glue is missing entirely,
    open the project once in the Godot editor to regenerate it, then rebuild.

.PARAMETER Clean
    Delete generated mono glue + bin/obj before building.

.PARAMETER Csproj
    Path to the Godot client csproj.

.PARAMETER Dotnet
    Path to the .NET 10 dotnet executable.

.EXAMPLE
    pwsh -File build_godot.ps1
.EXAMPLE
    pwsh -File build_godot.ps1 -Clean
#>
[CmdletBinding()]
param(
    [switch] $Clean,
    [string] $Csproj = "C:/Users/Arius/RiderProjects/MartialHeroes/05.Presentation/MartialHeroes.Client.Godot/MartialHeroes.Client.Godot.csproj",
    [string] $Dotnet = "C:/Program Files/dotnet/dotnet.EXE"
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $Csproj)) { Write-Error "csproj not found: $Csproj"; exit 2 }
if (-not (Test-Path $Dotnet)) { Write-Error "dotnet not found: $Dotnet"; exit 2 }

$projDir = Split-Path -Parent $Csproj

if ($Clean) {
    Write-Host "=== clean: removing generated glue + bin/obj ===" -ForegroundColor Yellow
    # ONLY ever remove these three generated dirs — never committed sources.
    foreach ($sub in @(".godot/mono", "bin", "obj")) {
        $target = Join-Path $projDir $sub
        if (Test-Path $target) {
            Write-Host "  removing $target"
            Remove-Item -Recurse -Force $target
        }
    }
}

Write-Host "=== dotnet build: MartialHeroes.Client.Godot ===" -ForegroundColor Cyan
Write-Host "csproj : $Csproj"
Write-Host "dotnet : $Dotnet"
Write-Host "clean  : $($Clean.IsPresent)"
Write-Host "-----------------------------------------------" -ForegroundColor Cyan

& $Dotnet build $Csproj --nologo
$code = $LASTEXITCODE

Write-Host "-----------------------------------------------" -ForegroundColor Cyan
if ($code -eq 0) {
    Write-Host "BUILD SUCCEEDED. Next: /godot-run-headless (loads cleanly) and /godot-screenshot (looks right)." -ForegroundColor Green
} else {
    Write-Host "BUILD FAILED (exit $code)." -ForegroundColor Red
    Write-Host "If errors are on Godot types that clearly exist, the generated glue may be stale —" -ForegroundColor Yellow
    Write-Host "re-run with -Clean. For bare Input./Environment./Time. CS0234, fully-qualify as" -ForegroundColor Yellow
    Write-Host "global::Godot.<Type> (namespace collision with the sibling project)." -ForegroundColor Yellow
}

exit $code
