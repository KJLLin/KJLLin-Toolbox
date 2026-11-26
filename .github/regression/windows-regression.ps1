# Drives the shipped client on a Windows runner like a real user would and
# captures every step, so the store can be regression-tested without a Windows box.
#
# Window layout assumption: the client opens at 908x608 and every coordinate below is
# relative to the window's top-left corner, so the runner's screen size does not matter.

param(
    [Parameter(Mandatory = $true)][string]$Exe,
    [Parameter(Mandatory = $true)][string]$OutDir
)

$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type -Language CSharp -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

public struct RECT { public int Left, Top, Right, Bottom; }

public class Win {
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(int flags, int dx, int dy, int data, IntPtr extra);

    public static void Click(int x, int y) {
        SetCursorPos(x, y);
        System.Threading.Thread.Sleep(200);
        mouse_event(0x0002, 0, 0, 0, IntPtr.Zero);
        System.Threading.Thread.Sleep(80);
        mouse_event(0x0004, 0, 0, 0, IntPtr.Zero);
    }

    public static void DoubleClick(int x, int y) {
        SetCursorPos(x, y);
        System.Threading.Thread.Sleep(200);
        for (int i = 0; i < 2; i++) {
            mouse_event(0x0002, 0, 0, 0, IntPtr.Zero);
            System.Threading.Thread.Sleep(60);
            mouse_event(0x0004, 0, 0, 0, IntPtr.Zero);
            System.Threading.Thread.Sleep(60);
        }
    }
}
'@

New-Item -ItemType Directory -Path $OutDir -Force | Out-Null

# --- window helpers -------------------------------------------------------------

function Get-Client {
    Get-Process UniGetUI -ErrorAction SilentlyContinue | Select-Object -First 1
}

function Get-ClientRect {
    $live = Get-Client
    if (-not $live -or $live.MainWindowHandle -eq 0) { return $null }
    $rect = New-Object RECT
    [Win]::GetWindowRect($live.MainWindowHandle, [ref]$rect) | Out-Null
    if ($rect.Right -le $rect.Left -or $rect.Bottom -le $rect.Top) { return $null }
    return $rect
}

function Save-Shot([string]$name) {
    $rect = Get-ClientRect
    if (-not $rect) {
        Write-Output "SHOT-SKIP $name (window gone)"
        return
    }
    $w = $rect.Right - $rect.Left
    $h = $rect.Bottom - $rect.Top
    $bmp = New-Object System.Drawing.Bitmap $w, $h
    $graphics = [System.Drawing.Graphics]::FromImage($bmp)
    $graphics.CopyFromScreen($rect.Left, $rect.Top, 0, 0, (New-Object System.Drawing.Size $w, $h))
    $graphics.Dispose()
    $path = Join-Path $OutDir "$name.png"
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Output ("SHOT {0} {1}x{2} {3} bytes" -f $name, $w, $h, (Get-Item $path).Length)
}

function Move-Away {
    $screen = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
    [System.Windows.Forms.Cursor]::Position = New-Object System.Drawing.Point ($screen.Right - 4), ($screen.Bottom - 4)
}

function Click-Rel($x, $y, [switch]$Double) {
    $rect = Get-ClientRect
    if (-not $rect) { throw "window gone before clicking $x,$y" }
    if ($Double) {
        [Win]::DoubleClick(($rect.Left + $x), ($rect.Top + $y))
    } else {
        [Win]::Click(($rect.Left + $x), ($rect.Top + $y))
    }
    Start-Sleep -Milliseconds 300
}

function Clear-Search {
    Click-Rel 470 25
    Start-Sleep -Milliseconds 300
    [System.Windows.Forms.SendKeys]::SendWait("^a")
    Start-Sleep -Milliseconds 200
    [System.Windows.Forms.SendKeys]::SendWait("{DEL}")
    Start-Sleep -Milliseconds 500
}

function Search([string]$query, [int]$wait) {
    Clear-Search
    [System.Windows.Forms.SendKeys]::SendWait($query)
    Start-Sleep -Milliseconds 500
    [System.Windows.Forms.SendKeys]::SendWait("{ENTER}")
    Start-Sleep -Seconds $wait
    Move-Away
    Start-Sleep -Milliseconds 500
}

function Browse {
    Clear-Search
    [System.Windows.Forms.SendKeys]::SendWait("{ENTER}")
}

# --- log helpers ----------------------------------------------------------------

function Get-LogText {
    $text = ""
    $logs = @(Get-ChildItem $env:LOCALAPPDATA -Recurse -Filter "*.log" -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -like "*UniGetUI*" -or $_.FullName -like "*KJLLin*" })
    foreach ($log in $logs) {
        try {
            $text += (Get-Content $log.FullName -Raw -Encoding UTF8) + "`n"
        } catch {
            # the client keeps the file open while it writes
        }
    }
    return $text
}

# Polls the client's own log instead of guessing how long a winget round trip takes.
function Wait-ForLog([string]$pattern, [int]$seconds) {
    $deadline = (Get-Date).AddSeconds($seconds)
    while ((Get-Date) -lt $deadline) {
        if ((Get-LogText) -match $pattern) {
            Write-Host "LOG-HIT $pattern"
            return $true
        }
        Start-Sleep -Seconds 3
    }
    Write-Host "LOG-TIMEOUT $pattern after ${seconds}s"
    return $false
}

# --- run ------------------------------------------------------------------------

$proc = Start-Process -FilePath $Exe -PassThru
Write-Output "CLIENT-PID $($proc.Id)"

$handle = 0
for ($i = 0; $i -lt 60; $i++) {
    Start-Sleep -Seconds 2
    $live = Get-Client
    if ($live -and $live.MainWindowHandle -ne 0) {
        $handle = $live.MainWindowHandle
        break
    }
}
if ($handle -eq 0) { throw "no window after 120s" }
Write-Output "WINDOW-READY after $((($i + 1) * 2))s"

$probe = @(Get-ChildItem $env:LOCALAPPDATA -Recurse -Filter "*.log" -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -like "*UniGetUI*" -or $_.FullName -like "*KJLLin*" })
Write-Host "LOG-FILES $($probe.Count): $($probe | Select-Object -First 3 -ExpandProperty FullName)"

[Win]::ShowWindow($handle, 9) | Out-Null   # SW_RESTORE: SW_SHOWMAXIMIZED kills the capture
[Win]::SetForegroundWindow($handle) | Out-Null
Start-Sleep -Seconds 3

# 1. first paint. Browsing asks winget once per curated id, so this is the slow one.
$firstLoad = Wait-ForLog "\[TOOLBOX\] catalogue curated=" 150
Start-Sleep -Seconds 3
Move-Away
Save-Shot "01-home-first-load"
if (-not $firstLoad) { throw "the catalogue never finished loading" }

# 2. the own app, which no winget source knows about
Search "hpcl" 12
Save-Shot "02-search-hpcl"

# 3. something that exists in winget but not in the catalogue: must stay out
Search "scoop" 12
Save-Shot "03-search-not-curated"

# 4. a curated package
Search "chrome" 14
Save-Shot "04-search-chrome"

# 5. browsing again: the catalogue cache should make this instant
Browse
$cached = Wait-ForLog "\[TOOLBOX\] catalogue cache hit" 45
Start-Sleep -Seconds 3
Move-Away
Save-Shot "05-home-from-cache"

# 6. cloud sections
Click-Rel 702 136
Start-Sleep -Seconds 3
Move-Away
Save-Shot "06-section-games"

Click-Rel 482 136
Start-Sleep -Seconds 3
Move-Away
Save-Shot "07-section-essentials"

Click-Rel 400 136
Start-Sleep -Seconds 3
Move-Away
Save-Shot "08-section-all"

# 7. details of the first row, which is the own app (it sorts first)
Click-Rel 500 225 -Double
Start-Sleep -Seconds 10
Move-Away
Save-Shot "09-own-app-details"

Write-Output "SCRIPT-DONE"
