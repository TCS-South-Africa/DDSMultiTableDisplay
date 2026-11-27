# Install a completed Windows Unity build. Run this on the display PC.
[CmdletBinding()]
param(
    [string] $PlayerPath = (Join-Path $PSScriptRoot '..\Builds\Windows\MTD.exe')
)

$ErrorActionPreference = 'Stop'
$root = 'C:\tcs\mtd'
$application = Join-Path $root 'application'
$logs = Join-Path $root 'logs'
$player = Get-Item -LiteralPath $PlayerPath
$source = $player.DirectoryName
$target = Join-Path $application $player.Name

if ($player.Extension -ine '.exe' -or
    !(Test-Path -LiteralPath (Join-Path $source 'UnityPlayer.dll') -PathType Leaf) -or
    !(Test-Path -LiteralPath (Join-Path $source ($player.BaseName + '_Data\globalgamemanagers')) -PathType Leaf)) {
    throw "Expected a built Unity Windows player with its data folder and UnityPlayer.dll beside it: $PlayerPath"
}

if ($application.StartsWith($source.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Choose a player build outside the parent directories of the installation.'
}

[IO.Directory]::CreateDirectory($application) | Out-Null
[IO.Directory]::CreateDirectory($logs) | Out-Null
if ($source -ine $application) {
    $running = Get-Process -Name $player.BaseName -ErrorAction SilentlyContinue | Where-Object { $_.Path -ieq $target }
    if ($running) { throw "Close the running player before updating $target." }

    # Copy the runtime payload. Unity can also emit folders for developer symbols and backups.
    robocopy $source $application /E /XJ /R:2 /W:1 /NFL /NDL /NJH /NJS /NP /XD '*_BackUpThisFolder_ButDontShipItWithYourGame' '*_BurstDebugInformation_DoNotShip' | Out-Host
    if ($LASTEXITCODE -ge 8) { throw "Player copy failed (robocopy exit code $LASTEXITCODE). Rerun the installer after correcting the reported error." }
}

$shortcutPath = Join-Path $application 'Start MTD.lnk'
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $target
$shortcut.WorkingDirectory = $application
$shortcut.Arguments = ''
$shortcut.IconLocation = "$target,0"
$shortcut.Save()
Write-Output "Installed $shortcutPath"
