"""Builds the downloadable artifacts for a release.

    python tools/release/build.py 0.1.0 [win-x64 | linux-x64 | linux-arm64]

Four shapes, because they answer different questions a person downloading this actually has:

  portable   "I just want to run it."          Self-contained folder. Unpack, double-click. No
                                               runtime to install, nothing written outside the
                                               folder.
  single     "I want one file."                The same thing as a single executable. Larger,
                                               because the runtime is inside it, and slower on first
                                               launch because it unpacks to a temp folder.
  framework  "I already have .NET 10."         A fifth the size. Needs the .NET 10 runtime
                                               installed.
  compact    "One file, and I have .NET."      One executable with the natives embedded, but the
                                               runtime left out - a third of the self-contained
                                               single file.

WINDOWS AND LINUX DIFFER IN THE WRAPPER, NOT THE SHAPES. A Windows RID makes `.zip`s exactly as
before. A Linux RID makes `.tar.gz`s, because a zip written on Windows stores every file as mode
0666: unzipped on Linux, `Nfty.Desktop` and `cli/Nfty.Cli` are not executable and the download looks
broken. tarfile writes the mode EXPLICITLY (0755 for the binaries and every folder, 0644 for the
rest, owner root/0), so the exec bit survives whichever OS built the archive. The Linux artifact also
names its binaries without `.exe`, writes READ-ME-FIRST with LF and Linux wording (the runtime is
`dotnet-runtime-10.0`, there is no "desktop runtime"; the GUI needs libICE/libSM), and carries
`dev.vaporsoft.nfty.desktop` + `nfty.png` + `install-desktop-entry.sh`, which puts a launcher entry and icon
under ~/.local/share for this copy, wherever it was unpacked.

Native AOT is deliberately NOT here. It compiles and links, but the app it produces cannot resolve
its own views (ViewLocator uses Type.GetType on a name built at runtime) and cannot read a .cbk
(System.Text.Json reflection). A release must not contain a binary that starts and then does
nothing.

Each artifact carries BOTH front-ends, the README, this project's MIT licence, and the SIL Open Font
License — the app embeds IBM Plex, and shipping the faces without their licence is not optional.

It also carries `demo/ChestDemo.cbk`, unpacked by RUNNING the just-published CLI rather than copied
out of the source tree. The demo is an embedded resource, and the two single-file shapes rewrite how
resources are laid out — so extracting it from the artifact is the only check that proves it
survived the publish. A copy taken from `src/` would prove the file exists in git, which nobody
doubted. The CLI to run is chosen by the TARGET RID, not the host: a linux-x64 build made on Windows
runs its Linux CLI through WSL when `wsl` is there; when nothing can run it (no WSL, or arm64 on an
x64 host) the source copy is used and the line says so.
"""
import argparse
import io
import os
import platform
import re
import shutil
import subprocess
import sys
import tarfile
import zipfile

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
OUT = os.path.join(ROOT, '.release')
DEMO_SOURCE = os.path.join(ROOT, 'src', 'Nfty.Core', 'Demo', 'ChestDemo.cbk')
ICON_PNG = os.path.join(ROOT, 'src', 'Nfty.Desktop', 'Assets', 'nfty.png')
DESKTOP_FILE = 'dev.vaporsoft.nfty.desktop'

VERSION = sys.argv[1] if len(sys.argv) > 1 else '0.1.0'
RID = sys.argv[2] if len(sys.argv) > 2 else 'win-x64'
LINUX = RID.startswith('linux-')
EXE = '' if LINUX else '.exe'
NEWLINE = '\n' if LINUX else '\r\n'

# The files a Linux tarball marks executable. Everything else is data to the loader (a .so is
# dlopen'ed, which needs read, not execute), so 0644 is right for it.
EXECUTABLES = {'Nfty.Desktop', 'Nfty.Cli', 'createdump', 'install-desktop-entry.sh'}


def run(args):
    r = subprocess.run(args, cwd=ROOT, capture_output=True, text=True)
    # A successful exit can still carry SDK, linker or native-tool warnings. Preserve the complete
    # diagnostics on both success and failure so a release review can actually enforce its bar.
    sys.stdout.write(r.stdout)
    sys.stderr.write(r.stderr)
    if r.returncode != 0:
        raise SystemExit('FAILED: ' + ' '.join(args))


def publish(project, dest, extra):
    run(['dotnet', 'publish', project, '-c', 'Release', '-r', RID,
         '-o', dest, '--nologo'] + extra)


def host_arch():
    m = platform.machine().lower()
    return 'arm64' if m in ('arm64', 'aarch64') else 'x64' if m in ('amd64', 'x86_64') else m


def cli_command(folder):
    """How to run the artifact's own CLI on this host, or None when this host cannot.

    Chosen by the RID the artifact was built for. Same OS + same arch: run it. A Linux artifact on a
    Windows host: through WSL, if WSL is installed and the arch matches (the folder is on a Windows
    drive, which WSL mounts with every file executable, so the missing exec bit does not matter)."""
    cli = os.path.join(folder, 'cli', 'Nfty.Cli' + EXE)
    if not os.path.exists(cli) or RID.split('-')[-1] != host_arch():
        return None
    if (LINUX and sys.platform.startswith('linux')) or (not LINUX and os.name == 'nt'):
        return [cli]
    if LINUX and os.name == 'nt' and shutil.which('wsl'):
        r = subprocess.run(['wsl', '-e', 'wslpath', '-a', cli.replace('\\', '/')],
                           capture_output=True, text=True)
        if r.returncode == 0 and r.stdout.strip():
            return ['wsl', '-e', r.stdout.strip()]
    return None


def unpack_demo(folder):
    """Puts the built-in demo CookBook in the artifact, by asking the artifact for it."""
    dest = os.path.join(folder, 'demo')
    cli = cli_command(folder)
    if cli is not None:
        # Relative destination: the same string means the same folder to a Windows and a WSL process
        # started in this cwd.
        r = subprocess.run(cli + ['demo', 'demo'], cwd=folder, capture_output=True, text=True)
        if r.returncode == 0 and os.path.exists(os.path.join(dest, os.path.basename(DEMO_SOURCE))):
            if cli[0] == 'wsl':
                print('    (demo extracted by the Linux CLI, run through WSL)')
            return
        sys.stdout.write(r.stdout)
        sys.stderr.write(r.stderr)
        raise SystemExit('FAILED: the published CLI could not unpack its embedded demo')
    os.makedirs(dest, exist_ok=True)
    shutil.copy(DEMO_SOURCE, dest)
    print('    (demo copied from source, not extracted from the build)')


def write_text(path, text):
    io.open(path, 'w', encoding='utf-8', newline=NEWLINE).write(text.strip() + '\n')


def linux_desktop_files(folder):
    """The launcher entry, its icon, and the script that installs both for this copy."""
    shutil.copy(ICON_PNG, os.path.join(folder, 'nfty.png'))
    # A template: Exec and Icon are relative here and rewritten to this folder's absolute path by
    # install-desktop-entry.sh, because a .desktop file cannot name a path relative to itself.
    # NOT `nfty.desktop`: on a case-insensitive file system (the Windows build host) that name IS
    # `Nfty.Desktop`, and writing it replaced the app binary with a 237-byte text file. The
    # reverse-DNS name is also what the freedesktop spec recommends for a desktop entry.
    write_text(os.path.join(folder, DESKTOP_FILE), DESKTOP_ENTRY % ('Nfty.Desktop', 'nfty.png'))
    write_text(os.path.join(folder, 'install-desktop-entry.sh'), INSTALL_SCRIPT)


def tar_mode(info):
    info.uid = info.gid = 0
    info.uname = info.gname = ''
    if info.isdir() or os.path.basename(info.name) in EXECUTABLES:
        info.mode = 0o755
    else:
        info.mode = 0o644
    return info


def stage(name, desktop_extra, cli_extra, blurb):
    """Publishes both front-ends into one folder and archives it."""
    folder = os.path.join(OUT, 'nfty-%s-%s-%s' % (VERSION, RID, name))
    require_output_path(folder)
    if os.path.isdir(folder):
        shutil.rmtree(folder)
    os.makedirs(folder)

    publish(os.path.join('src', 'Nfty.Desktop'), folder, desktop_extra)
    # The CLI goes in a subfolder: both heads produce a file called Nfty.*(.exe) and their shared
    # dependencies are identical, but publishing them over each other means whichever ran last wins
    # any file they disagree about. Keeping them apart costs a little duplication and no doubt.
    publish(os.path.join('src', 'Nfty.Cli'), os.path.join(folder, 'cli'), cli_extra)

    unpack_demo(folder)
    shutil.copy(os.path.join(ROOT, 'README.md'), folder)
    shutil.copy(os.path.join(ROOT, 'LICENSE'), folder)
    shutil.copy(os.path.join(ROOT, 'src', 'Nfty.App', 'Assets', 'Fonts', 'OFL.txt'),
                os.path.join(folder, 'OFL-IBM-Plex.txt'))
    if LINUX:
        linux_desktop_files(folder)
    write_text(os.path.join(folder, 'READ-ME-FIRST.txt'), blurb)

    if LINUX:
        # Both heads must still be the ELF binaries publish wrote: a name that collides on the
        # Windows build host (case-insensitive) once replaced Nfty.Desktop with a text file, and the
        # archive looked fine until someone ran it.
        for exe in ('Nfty.Desktop', os.path.join('cli', 'Nfty.Cli')):
            with open(os.path.join(folder, exe), 'rb') as f:
                if f.read(4) != b'\x7fELF':
                    raise SystemExit('FAILED: %s in %s is not an ELF executable' % (exe, folder))
        archive = folder + '.tar.gz'
        if os.path.exists(archive):
            os.remove(archive)
        with tarfile.open(archive, 'w:gz') as t:
            t.add(folder, arcname=os.path.basename(folder), filter=tar_mode)
        verb = 'tarred'
    else:
        archive = folder + '.zip'
        if os.path.exists(archive):
            os.remove(archive)
        with zipfile.ZipFile(archive, 'w', zipfile.ZIP_DEFLATED) as z:
            for root, _dirs, files in os.walk(folder):
                for f in files:
                    full = os.path.join(root, f)
                    z.write(full, os.path.relpath(full, os.path.dirname(folder)))
        verb = 'zipped'

    size = os.path.getsize(archive) / (1024 * 1024)
    print('  %-50s %6.1f MB %s' % (os.path.basename(archive), size, verb))
    return archive


COMMON = """
nfty %s (%s)

The desktop app is Nfty.Desktop.exe in this folder.
The command line is cli/Nfty.Cli.exe - run it with --help.

nfty normally keeps settings, recent files and palettes in .nfty beside the
executable; Settings > DATA shows the active location if a fallback is needed.
Optional file registrations and OS-saved publishing credentials live separately.
Remove those in Settings before moving or deleting this copy if you used them.

New here? Open Nfty.Desktop.exe and click "Open the demo CookBook" - a sample
collection of layered chests, yours to take apart. It is built into the program,
so it is always there; there is also a loose copy in the demo folder beside this
file, and `cli/Nfty.Cli.exe demo <folder>` writes a fresh one anywhere.

nfty is MIT licensed - see LICENSE. The interface is set in IBM Plex, which
is bundled inside the application under the SIL Open Font License - see
OFL-IBM-Plex.txt.

%s
"""

COMMON_LINUX = """
nfty %s (%s)

The desktop app is ./Nfty.Desktop in this folder.
The command line is ./cli/Nfty.Cli - run it with --help.

The desktop app needs the X11 and font libraries, which every desktop install
(GNOME, KDE, Xfce) already has but a minimal or server install may not:
  Debian/Ubuntu:  sudo apt install libice6 libsm6 libx11-6 libfontconfig1 libxrandr2 libxi6 libxcursor1
  Fedora:         sudo dnf install libICE libSM libX11 fontconfig libXrandr libXi libXcursor
  Arch:           sudo pacman -S libice libsm libx11 fontconfig libxrandr libxi libxcursor
It runs under X11 and under Wayland through XWayland. The command line needs
none of these.

nfty keeps its own settings in a .nfty folder beside the executable when this
folder is writable, so an unpacked copy carries its own recent-files list and
saved palette and leaves the rest of the machine alone. Unpacked somewhere
read-only (/opt, /usr), it uses ~/.config/nfty ($XDG_CONFIG_HOME/nfty) instead.

To get a launcher entry and icon for this copy, run ./install-desktop-entry.sh
(it writes to ~/.local/share only; run it again if you move the folder).

Saving a Pinata key in Settings uses your keyring through secret-tool:
  sudo apt install libsecret-tools      (Debian/Ubuntu)

New here? Run ./Nfty.Desktop and click "Open the demo CookBook" - a sample
collection of layered chests, yours to take apart. It is built into the program,
so it is always there; there is also a loose copy in the demo folder beside this
file, and `./cli/Nfty.Cli demo <folder>` writes a fresh one anywhere.

nfty is MIT licensed - see LICENSE. The interface is set in IBM Plex, which
is bundled inside the application under the SIL Open Font License - see
OFL-IBM-Plex.txt.

%s
"""

DESKTOP_ENTRY = """
[Desktop Entry]
Type=Application
Name=nfty
GenericName=Layered collection generator
Comment=Cook layered pixel-art collections
Exec=%s %%f
Icon=%s
Terminal=false
Categories=Graphics;2DGraphics;
StartupWMClass=Nfty.Desktop
"""

INSTALL_SCRIPT = r"""
#!/bin/sh
# Installs a launcher entry and icon for THIS copy of nfty, for the current user only.
# Nothing outside ~/.local/share is touched; run it again after moving the folder.
set -e
here=$(cd "$(dirname "$0")" && pwd)
data=${XDG_DATA_HOME:-$HOME/.local/share}
mkdir -p "$data/applications" "$data/icons/hicolor/256x256/apps"
cp "$here/nfty.png" "$data/icons/hicolor/256x256/apps/nfty.png"
# Exec's quoted argument is decoded twice (desktop string, then command argument), so a literal
# backslash needs four and quotes/backticks/dollars need two. Percent is a field-code introducer.
# Never interpolate the path into sed's replacement: an ampersand or delimiter would corrupt it.
executable=$(printf '%s' "$here/Nfty.Desktop" | sed -e 's/\\/\\\\\\\\/g' -e 's/["`$]/\\\\&/g' -e 's/%/%%/g')
while IFS= read -r line; do
    case "$line" in
        Exec=*) printf 'Exec="%s" %%f\n' "$executable" ;;
        Icon=*) printf 'Icon=nfty\n' ;;
        *) printf '%s\n' "$line" ;;
    esac
done < "$here/dev.vaporsoft.nfty.desktop" > "$data/applications/dev.vaporsoft.nfty.desktop"
command -v update-desktop-database >/dev/null 2>&1 && update-desktop-database "$data/applications" || true
echo "Installed $data/applications/dev.vaporsoft.nfty.desktop -> $here/Nfty.Desktop"
"""


def blurb(kind):
    """The shape paragraph, worded for the platform the artifact is for."""
    if LINUX:
        runtime = ('the .NET 10 runtime (dotnet-runtime-10.0):\n'
                   '  https://learn.microsoft.com/dotnet/core/install/linux')
        text = {
            'portable': 'PORTABLE BUILD. Everything needed is in this folder - you do not\n'
                        'need .NET installed. Unpack it anywhere and run ./Nfty.Desktop.',
            'single-file': 'SINGLE-FILE BUILD. One executable, nothing to install. It unpacks\n'
                           'itself to a cache folder on first run, so the first launch is\n'
                           'slower than the portable build; every launch after that is not.',
            'framework-dependent': 'FRAMEWORK-DEPENDENT BUILD - the small one. It needs\n' + runtime +
                                   '\nIf you are not sure, take the portable build instead.',
            'single-file-net10': 'SINGLE FILE, .NET REQUIRED. One executable with nothing beside\n'
                                 'it - the native libraries are embedded too - but the runtime is\n'
                                 'left out, so it is a third the size of the self-contained single\n'
                                 'file. Needs ' + runtime,
        }[kind]
        return COMMON_LINUX % (VERSION, RID, text)
    text = {
        'portable': 'PORTABLE BUILD. Everything needed is in this folder - you do not\n'
                    'need .NET installed. Unzip it anywhere and run Nfty.Desktop.exe.',
        'single-file': 'SINGLE-FILE BUILD. One executable, nothing to install. It unpacks\n'
                       'itself to a temporary folder on first run, so the first launch is\n'
                       'slower than the portable build; every launch after that is not.',
        'framework-dependent': 'FRAMEWORK-DEPENDENT BUILD - the small one. It needs the .NET 10\n'
                               'runtime already installed:\n'
                               '  https://dotnet.microsoft.com/download/dotnet/10.0\n'
                               'If you are not sure, take the portable build instead.',
        'single-file-net10': 'SINGLE FILE, .NET REQUIRED. One executable with nothing beside\n'
                             'it - the native libraries are embedded too - but the runtime is\n'
                             'left out, so it is a third the size of the self-contained single\n'
                             'file. Needs the .NET 10 runtime:\n'
                             '  https://dotnet.microsoft.com/download/dotnet/10.0',
    }[kind]
    return COMMON % (VERSION, RID, text)


SHAPES = [
    ('portable', ['--self-contained', 'true']),
    ('single-file', ['--self-contained', 'true', '-p:PublishSingleFile=true',
                     '-p:IncludeNativeLibrariesForSelfExtract=true']),
    ('framework-dependent', ['--self-contained', 'false']),
    ('single-file-net10', ['--self-contained', 'false', '-p:PublishSingleFile=true',
                           '-p:IncludeNativeLibrariesForSelfExtract=true']),
]


def require_output_path(path):
    """Refuse cleanup outside this checkout's real .release folder, including symlink escapes."""
    expected = os.path.join(os.path.realpath(ROOT), '.release')
    resolved = os.path.realpath(path)
    if os.path.realpath(OUT) != expected or os.path.commonpath([expected, resolved]) != expected:
        raise SystemExit('FAILED: release output must stay inside ' + expected)


def main():
    ap = argparse.ArgumentParser(description=__doc__.split('\n')[0])
    ap.add_argument('version', nargs='?', default=VERSION)
    ap.add_argument('rid', nargs='?', default=RID, choices=('win-x64', 'linux-x64', 'linux-arm64'))
    args = ap.parse_args()
    if not re.fullmatch(r'[0-9]+\.[0-9]+\.[0-9]+(?:[-+][A-Za-z0-9.-]+)?', args.version):
        ap.error('version must be a version number, without path separators')
    require_output_path(OUT)
    if os.path.isdir(OUT):
        shutil.rmtree(OUT)
    os.makedirs(OUT)

    print('building nfty %s for %s\n' % (VERSION, RID))
    made = [stage(name, extra, extra, blurb(name)) for name, extra in SHAPES]
    print('\nwrote %d archives to %s' % (len(made), OUT))
    return made


if __name__ == '__main__':
    main()
