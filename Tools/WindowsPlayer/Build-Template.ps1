# Rebuild the x64 entry point after changing MtdLogStartup.cpp or upgrading Unity.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $EditorDirectory,
    [string] $Zig = 'zig'
)

$ErrorActionPreference = 'Stop'
$sources = Join-Path $EditorDirectory 'Data\PlaybackEngines\WindowsStandaloneSupport\Source\WindowsPlayer'
$main = Join-Path $sources 'WindowsPlayer\Main.cpp'
if (!(Test-Path -LiteralPath $main -PathType Leaf)) { throw "Missing Unity Windows player source: $main" }
$scratch = Join-Path $PSScriptRoot '..\..\Temp\MtdWindowsEntryPoint'
[IO.Directory]::CreateDirectory($scratch) | Out-Null
$compiled = Join-Path $scratch 'MtdWindowsPlayer.exe'
& $Zig c++ -target x86_64-windows-gnu -std=c++14 -O2 -s -municode '-Wl,/subsystem:windows' -nostdlib++ -fno-exceptions -fno-rtti -I $sources (Join-Path $PSScriptRoot 'MtdLogStartup.cpp') -lshell32 -o $compiled
if ($LASTEXITCODE) { throw 'Failed to build the native Windows entry point.' }
Copy-Item -LiteralPath $compiled -Destination (Join-Path $PSScriptRoot 'WindowsPlayer-x64.bin') -Force
