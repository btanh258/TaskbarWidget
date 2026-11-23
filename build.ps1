# Builds dist\TaskbarWidget.exe with the C# compiler that ships with Windows (.NET Framework 4.x).
# No SDK or packages needed. Run from Windows PowerShell:
#   .\build.ps1        # -> dist\TaskbarWidget.exe
#   .\build.ps1 -Portable  # also copy it next to the script for a quick test run
# The PawnIO sensor modules (pawnio\*.bin, LGPL-2.1) are embedded as resources
# so the CPU-temperature path works when the PawnIO driver is already installed.
# The Bluetooth icons (icons\*.png, rasterized from the SVG files in icons\)
# are embedded the same way so the exe stays a single file.
# System.Management.dll is referenced for Win32_VideoController / Win32_Processor (WMI).
param([switch]$Portable)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) { $csc = "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\csc.exe" }
if (-not (Test-Path $csc)) { throw "csc.exe not found: this build needs Windows with .NET Framework 4.x" }
if (-not (Test-Path "$root\src\icon.ico")) { & "$root\tools\make-icon.ps1" }
New-Item -ItemType Directory -Force "$root\dist" | Out-Null
$src = Get-ChildItem "$root\src\*.cs" | ForEach-Object { $_.FullName }
& $csc /nologo /target:winexe /optimize+ /platform:anycpu `
    "/out:$root\dist\TaskbarWidget.exe" "/win32manifest:$root\src\app.manifest" "/win32icon:$root\src\icon.ico" `
    "/resource:$root\pawnio\IntelMSR.bin,IntelMSR.bin" "/resource:$root\pawnio\AMDFamily17.bin,AMDFamily17.bin" `
    "/resource:$root\icons\headset.png,icon-headset.png" "/resource:$root\icons\mouse.png,icon-mouse.png" "/resource:$root\icons\keyboard.png,icon-keyboard.png" "/resource:$root\icons\phone.png,icon-phone.png" "/resource:$root\icons\controller.png,icon-controller.png" "/resource:$root\icons\speaker.png,icon-speaker.png" `
    /r:System.Web.Extensions.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Core.dll /r:System.Management.dll $src
if ($LASTEXITCODE -ne 0) { throw "build failed" }
if ($Portable) { Copy-Item "$root\dist\TaskbarWidget.exe" "$root\TaskbarWidget.exe" -Force }
Get-Item "$root\dist\TaskbarWidget.exe" | Select-Object Name, Length
