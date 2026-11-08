"""Behavioral guards for release packaging and local authoring tools.

Run with ``python -m unittest discover -s tools/tests -v``. These tests never publish,
launch the app, click the desktop, or overwrite generated product assets.
"""
import contextlib
import http.client
import importlib.util
import io
import json
import os
from pathlib import Path
import runpy
import shutil
import subprocess
import tempfile
import threading
import unittest
from unittest.mock import patch


ROOT = Path(__file__).resolve().parents[2]


def module(path):
    spec = importlib.util.spec_from_file_location(path.replace("/", "_"), ROOT / path)
    loaded = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(loaded)
    return loaded


class ReleaseTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        with patch("sys.argv", ["build.py"]):
            cls.release = module("tools/release/build.py")

    def test_successful_build_preserves_diagnostics(self):
        output = io.StringIO()
        result = subprocess.CompletedProcess([], 0, "publish succeeded\n", "warning from a tool\n")
        with patch.object(self.release.subprocess, "run", return_value=result), \
                contextlib.redirect_stdout(output), contextlib.redirect_stderr(output):
            self.release.run(["dotnet", "publish"])
        self.assertIn("warning from a tool", output.getvalue())

    def test_runnable_broken_cli_cannot_pass_demo_verification(self):
        with tempfile.TemporaryDirectory() as directory, \
                patch.object(self.release, "cli_command", return_value=["published-cli"]), \
                patch.object(self.release.subprocess, "run",
                             return_value=subprocess.CompletedProcess([], 1, "", "broken resource")), \
                patch.object(self.release.shutil, "copy") as copy:
            with self.assertRaises(SystemExit):
                self.release.unpack_demo(directory)
            copy.assert_not_called()

    def test_unavailable_target_can_still_copy_demo(self):
        with tempfile.TemporaryDirectory() as directory, \
                patch.object(self.release, "cli_command", return_value=None), \
                patch.object(self.release.shutil, "copy") as copy, \
                contextlib.redirect_stdout(io.StringIO()):
            self.release.unpack_demo(directory)
            copy.assert_called_once()

    def test_cleanup_refuses_foreign_output_root(self):
        with tempfile.TemporaryDirectory() as directory, \
                patch.object(self.release, "OUT", directory):
            with self.assertRaises(SystemExit):
                self.release.require_output_path(directory)

    def test_cleanup_refuses_child_escape(self):
        with tempfile.TemporaryDirectory() as directory, patch.object(self.release, "ROOT", directory), \
                patch.object(self.release, "OUT", str(Path(directory) / ".release")):
            with self.assertRaises(SystemExit):
                self.release.require_output_path(str(Path(directory) / "outside"))

    def test_cleanup_refuses_linked_output(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            target = root / "outside"
            target.mkdir()
            link = root / ".release"
            try:
                link.symlink_to(target, target_is_directory=True)
            except OSError:
                self.skipTest("directory symlink unavailable")
            with patch.object(self.release, "ROOT", directory), patch.object(self.release, "OUT", str(link)):
                with self.assertRaises(SystemExit):
                    self.release.require_output_path(str(link))

    def test_release_arguments_reject_path_traversal_before_cleanup(self):
        result = subprocess.run([shutil.which("python"), str(ROOT / "tools/release/build.py"),
                                 "2.4.8/../../elsewhere", "win-x64"], capture_output=True, text=True)
        self.assertEqual(2, result.returncode)
        self.assertIn("without path separators", result.stderr)

    def test_release_arguments_reject_unsupported_rid_before_cleanup(self):
        result = subprocess.run([shutil.which("python"), str(ROOT / "tools/release/build.py"),
                                 "2.4.8", "osx-x64"], capture_output=True, text=True)
        self.assertEqual(2, result.returncode)
        self.assertIn("invalid choice", result.stderr)

    @unittest.skipUnless(os.name != "nt" or Path("C:/Program Files/Git/bin/bash.exe").exists(), "requires a POSIX shell")
    def test_linux_launcher_installer_preserves_reserved_path_characters(self):
        bash = "C:/Program Files/Git/bin/bash.exe" if os.name == "nt" else shutil.which("bash")
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            folder = root / "demo & 50% $assets"
            folder.mkdir()
            (folder / "nfty.png").write_bytes(b"test icon")
            (folder / self.release.DESKTOP_FILE).write_text(
                self.release.DESKTOP_ENTRY % ("Nfty.Desktop", "nfty.png"), encoding="utf-8")
            installer = folder / "install-desktop-entry.sh"
            installer.write_text(self.release.INSTALL_SCRIPT.strip() + "\n", encoding="utf-8", newline="\n")
            # A POSIX path also works under Git Bash; the only installation target is this temp tree.
            here = subprocess.run([bash, "-c", "pwd"], cwd=folder, capture_output=True, text=True, check=True).stdout.strip()
            data = here + "/../data"
            subprocess.run([bash, "./install-desktop-entry.sh"], cwd=folder,
                           env=dict(os.environ, XDG_DATA_HOME=data), capture_output=True, text=True, check=True)
            desktop = (root / "data" / "applications" / self.release.DESKTOP_FILE).read_text(encoding="utf-8")
            executable = here.replace("\\", "\\\\\\\\").replace('"', '\\\\"').replace("`", "\\\\`").replace("$", "\\\\$").replace("%", "%%")
            self.assertIn(f'Exec="{executable}/Nfty.Desktop" %f\n', desktop)
            self.assertIn("Icon=nfty\n", desktop)


class FontTests(unittest.TestCase):
    def test_missing_license_never_creates_downloadable_bundle(self):
        fonts = module("tools/mkdocs-hooks/fonts.py")
        with tempfile.TemporaryDirectory() as directory:
            source = Path(directory) / "source"
            source.mkdir()
            (source / "font.ttf").write_bytes(b"test font")
            site = Path(directory) / "site"
            with patch.object(fonts, "FONT_SOURCE", str(source)), self.assertLogs(fonts.log, "WARNING"):
                fonts.on_post_build({"site_dir": str(site)})
            self.assertFalse((site / fonts.BUNDLE).exists())


@unittest.skipUnless(os.environ.get("NFTY_TEST_API_SURFACE") == "1", "requires built Release API surface tool")
class ApiDocsTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        result = subprocess.run(["dotnet", "run", "--no-build", "--project", "tools/api-docs/surface", "-c", "Release"],
                                cwd=ROOT, capture_output=True, text=True, encoding="utf-8", check=True)
        cls.surface = json.loads(result.stdout)
        cls.types = {t["id"]: t for assembly in cls.surface for t in assembly["types"]}

    def test_record_struct_declarations_match_the_real_library(self):
        for identifier in ("T:Nfty.Core.Imaging.RgbColor", "T:Nfty.Core.Editing.Brush`1",
                           "T:Nfty.Model.StackPick"):
            with self.subTest(identifier=identifier):
                described = self.types[identifier]
                self.assertEqual("record struct", described["kind"])
                self.assertIn("readonly record struct", described["declaration"])
                self.assertNotIn("IEquatable", described["declaration"])
        api = module("tools/api-docs/build.py")
        self.assertEqual([], api.lint(self.surface))

    def test_public_generic_constraints_are_part_of_the_declaration(self):
        for name in ("AlphaSnap", "Brush", "BrushStroke", "DrawShape", "EditHistory", "EraseStroke",
                     "FloodFill", "IEditCommand", "IEditSurface", "MoveSelection", "RegionEditCommand"):
            with self.subTest(name=name):
                declaration = self.types[f"T:Nfty.Core.Editing.{name}`1"]["declaration"]
                self.assertTrue(declaration.endswith("where TPixel : struct"), declaration)
                self.assertNotIn("ValueType", declaration)
                self.assertNotIn("new()", declaration)  # struct already implies its constructor.
        keyed = self.types["T:Nfty.Core.Editing.TargetedHistory`2"]["declaration"]
        self.assertTrue(keyed.endswith("where TKey : notnull where TPixel : struct"), keyed)
        owner = self.types["T:Nfty.Core.Formats.UnsupportedSchemaVersionException"]
        require = next(m for m in owner["members"] if m["id"].startswith("M:Nfty.Core.Formats.UnsupportedSchemaVersionException.Require``1"))
        self.assertTrue(require["signature"].endswith("where T : ISchemaVersioned"), require["signature"])
        owner = self.types["T:Nfty.Core.Editing.Footprint"]
        region = next(m for m in owner["members"] if m["id"].startswith("M:Nfty.Core.Editing.Footprint.Region``1"))
        self.assertTrue(region["signature"].endswith("where TPixel : struct"), region["signature"])


class CaptureTests(unittest.TestCase):
    def test_failed_capture_cannot_reuse_old_pair(self):
        from PIL import Image

        with tempfile.TemporaryDirectory() as directory:
            for suffix, color in (("a", "black"), ("b", "white")):
                with Image.new("RGB", (32, 32), color) as frame:
                    frame.save(Path(directory) / f"test-{suffix}.png")
            with patch("sys.argv", ["pair.py", "test", directory]), \
                    patch("subprocess.run", return_value=subprocess.CompletedProcess([], 1, "", "capture failed")), \
                    patch("time.sleep"):
                with self.assertRaises(subprocess.CalledProcessError):
                    runpy.run_path(str(ROOT / "tools/docs-capture/pair.py"), run_name="__main__")
            self.assertFalse((Path(directory) / "test-light.png").exists())

    @unittest.skipUnless(os.name == "nt" and shutil.which("pwsh"), "PowerShell guard execution on Windows")
    def test_wrong_width_capture_stops_before_screen_access(self):
        # Replace native calls before loading the script; the guard must fail before capture.
        script = ROOT / "tools/docs-capture/shot.ps1"
        source = r'''
Microsoft.PowerShell.Utility\Add-Type -TypeDefinition @'
using System;
public struct NRECT { public int Left, Top, Right, Bottom; }
public static class NWin {
 public static bool SetProcessDPIAware() => true;
 public static bool SetWindowPos(IntPtr h,IntPtr a,int x,int y,int w,int t,uint f) => true;
 public static bool SetForegroundWindow(IntPtr h) => true;
 public static bool MoveWindow(IntPtr h,int x,int y,int w,int t,bool r) => true;
 public static bool GetWindowRect(IntPtr h,out NRECT r) { r = new NRECT { Right=1300, Bottom=950 }; return true; }
}
'@
function Add-Type { }
function Start-Sleep { }
function Get-Process { [pscustomobject]@{ MainWindowHandle = [IntPtr]1 } }
function New-Object {
 param([string]$TypeName)
 if ($TypeName.StartsWith('System.Drawing.')) { throw 'UNEXPECTED_SCREEN_ACCESS' }
 Microsoft.PowerShell.Utility\New-Object @PSBoundParameters
}
& $env:NFTY_TEST_SCRIPT -Out 'unused.png'
'''
        env = dict(os.environ, NFTY_TEST_SCRIPT=str(script))
        with tempfile.TemporaryDirectory() as directory:
            result = subprocess.run(["pwsh", "-NoProfile", "-Command", source], env=env, cwd=directory,
                                    capture_output=True, text=True)
        self.assertNotEqual(0, result.returncode)
        self.assertIn("window would not size to 1416x950", result.stderr)
        self.assertNotIn("CopyFromScreen", result.stderr)
        self.assertNotIn("UNEXPECTED_SCREEN_ACCESS", result.stderr)

    @unittest.skipUnless(os.name == "nt" and shutil.which("pwsh"), "PowerShell guard execution on Windows")
    def test_failed_cursor_placement_never_clicks(self):
        script = ROOT / "tools/docs-capture/click.ps1"
        source = r'''
Microsoft.PowerShell.Utility\Add-Type -TypeDefinition @'
using System;
public struct NPT { public int X, Y; }
public static class NClick {
 public static bool SetProcessDPIAware() => true;
 public static bool SetCursorPos(int x,int y) => false;
 public static bool GetCursorPos(out NPT p) { p = new NPT(); return true; }
 public static void mouse_event(uint f,uint x,uint y,uint d,IntPtr e) { throw new Exception("UNEXPECTED_CLICK"); }
}
'@
function Add-Type { }
function Start-Sleep { }
& $env:NFTY_TEST_SCRIPT -Points '100,100'
'''
        env = dict(os.environ, NFTY_TEST_SCRIPT=str(script))
        with tempfile.TemporaryDirectory() as directory:
            result = subprocess.run(["pwsh", "-NoProfile", "-Command", source], env=env, cwd=directory,
                                    capture_output=True, text=True)
        self.assertNotEqual(0, result.returncode)
        self.assertIn("cursor would not move to 100,100", result.stderr)
        self.assertNotIn("UNEXPECTED_CLICK", result.stderr)


class DiagramTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        self.diagrams = module("tools/diagrams/build.py")
        self.diagrams.ROOT = Path(self.directory.name)
        self.diagrams.DIAGRAMS = self.diagrams.ROOT / "docs" / "diagrams"
        self.diagrams.DIAGRAMS.mkdir(parents=True)
        self.diagrams.Handler.written.clear()
        self.server = self.diagrams.socketserver.TCPServer(("127.0.0.1", 0), self.diagrams.Handler)
        self.thread = threading.Thread(target=self.server.serve_forever)
        self.thread.start()
        self.addCleanup(self.stop_server)

    def stop_server(self):
        self.server.shutdown()
        self.thread.join()
        self.server.server_close()

    def post(self, payload, token=True):
        conn = http.client.HTTPConnection(*self.server.server_address)
        self.addCleanup(conn.close)
        headers = {"Content-Type": "application/json"}
        if token:
            headers["X-Nfty-Render-Token"] = getattr(self.diagrams.Handler, "token", "baseline")
        conn.request("POST", "/write", json.dumps(payload), headers)
        response = conn.getresponse()
        response.read()
        return response.status

    def test_write_requires_render_token(self):
        self.assertEqual(403, self.post({"light": "<svg/>", "dark": "<svg/>"}, token=False))
        self.assertEqual([], list(self.diagrams.DIAGRAMS.iterdir()))

    def test_unknown_theme_cannot_escape_output_directory(self):
        escaped = self.diagrams.DIAGRAMS / "generation-pipeline-x"
        escaped.mkdir()
        payload = {"x/../../../escaped": "<svg/>"}
        self.assertEqual(400, self.post(payload))
        self.assertFalse((self.diagrams.ROOT / "escaped.svg").exists())

    def test_invalid_second_svg_writes_neither_theme(self):
        self.assertEqual(400, self.post({"light": '<svg xmlns="http://www.w3.org/2000/svg"/>',
                                         "dark": "<html/>"}))
        self.assertEqual([], list(self.diagrams.DIAGRAMS.iterdir()))

    def test_valid_pair_writes_both_svgs(self):
        svg = '<svg xmlns="http://www.w3.org/2000/svg"><text>Layer</text></svg>'
        self.assertEqual(200, self.post({"light": svg, "dark": svg}))
        self.assertTrue(self.diagrams.Handler.written.is_set())
        self.assertEqual(svg, (self.diagrams.DIAGRAMS / "generation-pipeline-light.svg").read_text())
        self.assertEqual(svg, (self.diagrams.DIAGRAMS / "generation-pipeline-dark.svg").read_text())


if __name__ == "__main__":
    unittest.main()
