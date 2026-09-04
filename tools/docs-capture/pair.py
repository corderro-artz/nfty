"""Capture the current screen in both themes, verifying the toggle actually landed.

SendKeys is fire-and-forget: if the window loses focus for a moment the Ctrl+T is swallowed and you
get the same theme twice, named as if it were a pair. So: capture, toggle, capture, and CHECK -- the
titlebar pixel has to have moved. Retry the toggle if it did not, and name the files by which one is
actually darker.
"""
import subprocess, sys, os, time
from PIL import Image

SCRATCH = os.path.dirname(os.path.abspath(__file__))
NAME, OUT = sys.argv[1], sys.argv[2]
HEIGHT = sys.argv[3] if len(sys.argv) > 3 else "950"

def ps(*args):
    return subprocess.run(["powershell", "-NoProfile", "-ExecutionPolicy", "Bypass", *args],
                          capture_output=True, text=True)

def shot(path):
    ps("-File", os.path.join(SCRATCH, "shot.ps1"), "-Out", path, "-Height", HEIGHT)

def toggle():
    ps("-Command",
       "Add-Type @'\nusing System;using System.Runtime.InteropServices;\n"
       "public class FG{[DllImport(\"user32.dll\")]public static extern bool SetForegroundWindow(IntPtr h);}\n'@\n"
       "$p=Get-Process Nfty.Desktop|Where-Object{$_.MainWindowHandle -ne 0}|Select-Object -First 1\n"
       "[FG]::SetForegroundWindow($p.MainWindowHandle)|Out-Null; Start-Sleep -Milliseconds 350\n"
       "(New-Object -ComObject WScript.Shell).SendKeys('^t')")
    time.sleep(1.0)

def lum(path):
    im = Image.open(path).convert('RGB')
    r, g, b = im.getpixel((im.width // 2, 12))
    return 0.2126 * r + 0.7152 * g + 0.0722 * b

a = os.path.join(OUT, NAME + "-a.png")
b = os.path.join(OUT, NAME + "-b.png")
shot(a)
for attempt in range(4):
    toggle()
    shot(b)
    if abs(lum(a) - lum(b)) > 40:
        break
    print(f"  toggle did not land (try {attempt + 1})")
else:
    sys.exit(f"{NAME}: theme never toggled")

dark, light = (a, b) if lum(a) < lum(b) else (b, a)
os.replace(dark, os.path.join(OUT, NAME + "-dark.png"))
os.replace(light, os.path.join(OUT, NAME + "-light.png"))
toggle()                                   # leave the app the way it was found
print(f"{NAME}: dark + light")
