# Drives and captures the WPF app without a human (UI Automation + PrintWindow). Dot-source it:
#   . .\scripts\ui-automation.ps1
#   Start-App; Invoke-ByName "Eingangsordner verarbeiten"; Start-Sleep 40; Select-ListItem 3; Save-Shot "detail"; Stop-App
# Screenshots go to artifacts\screenshots (gitignored). Build the app first (dotnet build src\InvoiceProcessing.App).

$RepoRoot = Split-Path $PSScriptRoot -Parent
$ShotDir = Join-Path $RepoRoot "artifacts\screenshots"
$AppExe = Join-Path $RepoRoot "src\InvoiceProcessing.App\bin\Debug\net10.0-windows\Rechnungsverarbeitung.exe"
New-Item -ItemType Directory -Force $ShotDir | Out-Null

$native = @"
using System; using System.Runtime.InteropServices;
public static class NativeWin {
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
  [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr h);
  public struct RECT { public int L, T, R, B; }
}
"@
if (-not ("NativeWin" -as [type])) { Add-Type -TypeDefinition $native }
Add-Type -AssemblyName System.Drawing, UIAutomationClient, UIAutomationTypes
[NativeWin]::SetProcessDPIAware() | Out-Null
$AE = [System.Windows.Automation.AutomationElement]
$Scope = [System.Windows.Automation.TreeScope]::Descendants

function Start-App {
  $env:DOTNET_ENVIRONMENT = 'Development'   # folders under data\, local Docker database
  $p = Start-Process $AppExe -PassThru
  Start-Sleep -Seconds 8
  $p.Refresh()
  $p.Id | Out-File (Join-Path $ShotDir "pid.txt")
  [NativeWin]::ShowWindow($p.MainWindowHandle, 3) | Out-Null   # maximized, as for the recording
  return $p
}

function Get-AppWindow { (Get-Process -Id (Get-Content (Join-Path $ShotDir "pid.txt"))).MainWindowHandle }

# Fixed window size in logical pixels (independent of the screen's DPI), e.g. for documentation screenshots.
function Set-AppSize([int]$width = 1400, [int]$height = 880) {
  $h = Get-AppWindow
  $scale = [NativeWin]::GetDpiForWindow($h) / 96.0
  [NativeWin]::ShowWindow($h, 9) | Out-Null   # restore from maximized
  [NativeWin]::SetWindowPos($h, [IntPtr]::Zero, 40, 40, [int]($width * $scale), [int]($height * $scale), 0x0004) | Out-Null
  Start-Sleep -Milliseconds 700
  return $scale
}

# PrintWindow captures the app even when other windows cover it (CopyFromScreen does not).
function Save-Shot([string]$name) {
  $h = Get-AppWindow
  $r = New-Object NativeWin+RECT
  [NativeWin]::GetWindowRect($h, [ref]$r) | Out-Null
  $bmp = New-Object System.Drawing.Bitmap ($r.R - $r.L), ($r.B - $r.T)
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $hdc = $g.GetHdc()
  [NativeWin]::PrintWindow($h, $hdc, 2) | Out-Null
  $g.ReleaseHdc($hdc); $g.Dispose()
  $path = Join-Path $ShotDir "$name.png"
  $bmp.Save($path); $bmp.Dispose()
  return $path
}

function Get-Root { $AE::FromHandle((Get-AppWindow)) }

function Find-ByName([string]$name) {
  (Get-Root).FindFirst($Scope, (New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, $name)))
}

# Tabs ("Import", "Rechnungen", "Protokoll")
function Select-ByName([string]$name) {
  (Find-ByName $name).GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
}

# Buttons by AutomationProperties.Name ("Eingangsordner verarbeiten", "Aktualisieren", "Originaldatei öffnen", ...)
function Invoke-ByName([string]$name) {
  (Find-ByName $name).GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
}

# Entry in the "Verarbeitete Dateien" list (0-based, top to bottom)
function Select-ListItem([int]$index) {
  $cond = New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem)
  $items = (Get-Root).FindAll($Scope, $cond)
  $items[$index].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
}

function Stop-App { Stop-Process -Id (Get-Content (Join-Path $ShotDir "pid.txt")) -ErrorAction SilentlyContinue }
