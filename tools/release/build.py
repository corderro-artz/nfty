"""Builds the downloadable artifacts for a release.

    python tools/release/build.py 0.1.0 [win-x64]

Three shapes, because they answer three different questions a person downloading this actually has:

  portable   "I just want to run it."          Self-contained folder. Unzip, double-click. No
                                               runtime to install, nothing written outside the
                                               folder.
  single     "I want one file."                The same thing as a single .exe. Larger, because the
                                               runtime is inside it, and slower on first launch
                                               because it unpacks to a temp folder.
  framework  "I already have .NET 10."         A fifth the size. Needs the .NET 10 desktop runtime
                                               installed.
  compact    "One file, and I have .NET."      One .exe with the natives embedded, but the runtime
                                               left out - a third of the self-contained single file.

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
doubted. If the published CLI cannot be run here (a cross-RID build), the source copy is used and
the line says so.
"""
import io
import os
import shutil
import subprocess
import sys
import zipfile

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
OUT = os.path.join(ROOT, '.release')
DEMO_SOURCE = os.path.join(ROOT, 'src', 'Nfty.Core', 'Demo', 'ChestDemo.cbk')

VERSION = sys.argv[1] if len(sys.argv) > 1 else '0.1.0'
RID = sys.argv[2] if len(sys.argv) > 2 else 'win-x64'


def run(args):
    r = subprocess.run(args, cwd=ROOT, capture_output=True, text=True)
    if r.returncode != 0:
        sys.stdout.write(r.stdout[-4000:])
        sys.stdout.write(r.stderr[-2000:])
        raise SystemExit('FAILED: ' + ' '.join(args))


def publish(project, dest, extra):
    run(['dotnet', 'publish', project, '-c', 'Release', '-r', RID,
         '-o', dest, '--nologo'] + extra)


def unpack_demo(folder):
    """Puts the built-in demo CookBook in the artifact, by asking the artifact for it."""
    dest = os.path.join(folder, 'demo')
    cli = os.path.join(folder, 'cli', 'Nfty.Cli.exe' if os.name == 'nt' else 'Nfty.Cli')
    if os.path.exists(cli):
        r = subprocess.run([cli, 'demo', dest], cwd=folder, capture_output=True, text=True)
        if r.returncode == 0:
            return
        print('    (published CLI could not unpack the demo: %s)' % r.stderr.strip()[:120])
    os.makedirs(dest, exist_ok=True)
    shutil.copy(DEMO_SOURCE, dest)
    print('    (demo copied from source, not extracted from the build)')


def stage(name, desktop_extra, cli_extra, blurb):
    """Publishes both front-ends into one folder and zips it."""
    folder = os.path.join(OUT, 'nfty-%s-%s-%s' % (VERSION, RID, name))
    if os.path.isdir(folder):
        shutil.rmtree(folder)
    os.makedirs(folder)

    publish(os.path.join('src', 'Nfty.Desktop'), folder, desktop_extra)
    # The CLI goes in a subfolder: both heads produce a file called Nfty.*.exe and their shared
    # dependencies are identical, but publishing them over each other means whichever ran last wins
    # any file they disagree about. Keeping them apart costs a little duplication and no doubt.
    publish(os.path.join('src', 'Nfty.Cli'), os.path.join(folder, 'cli'), cli_extra)

    unpack_demo(folder)
    shutil.copy(os.path.join(ROOT, 'README.md'), folder)
    shutil.copy(os.path.join(ROOT, 'LICENSE'), folder)
    shutil.copy(os.path.join(ROOT, 'src', 'Nfty.App', 'Assets', 'Fonts', 'OFL.txt'),
                os.path.join(folder, 'OFL-IBM-Plex.txt'))
    io.open(os.path.join(folder, 'READ-ME-FIRST.txt'), 'w', encoding='utf-8', newline='\r\n').write(
        blurb.strip() + '\n')

    archive = folder + '.zip'
    if os.path.exists(archive):
        os.remove(archive)
    with zipfile.ZipFile(archive, 'w', zipfile.ZIP_DEFLATED) as z:
        for root, _dirs, files in os.walk(folder):
            for f in files:
                full = os.path.join(root, f)
                z.write(full, os.path.relpath(full, os.path.dirname(folder)))

    size = os.path.getsize(archive) / (1024 * 1024)
    print('  %-46s %6.1f MB zipped' % (os.path.basename(archive), size))
    return archive


COMMON = """
nfty %s (%s)

The desktop app is Nfty.Desktop.exe in this folder.
The command line is cli/Nfty.Cli.exe - run it with --help.

nfty keeps its own settings in a .nfty folder beside the executable, so this
copy carries its own recent-files list and saved palette and leaves the rest of
the machine alone. Move or delete the folder and nothing is left behind.

New here? Open Nfty.Desktop.exe and click "Open the demo CookBook" - a sample
collection of layered chests, yours to take apart. It is built into the program,
so it is always there; there is also a loose copy in the demo folder beside this
file, and `cli/Nfty.Cli.exe demo <folder>` writes a fresh one anywhere.

nfty is MIT licensed - see LICENSE. The interface is set in IBM Plex, which
is bundled inside the application under the SIL Open Font License - see
OFL-IBM-Plex.txt.

%s
"""


def main():
    if os.path.isdir(OUT):
        shutil.rmtree(OUT)
    os.makedirs(OUT)

    print('building nfty %s for %s\n' % (VERSION, RID))
    made = []

    made.append(stage(
        'portable',
        ['--self-contained', 'true'],
        ['--self-contained', 'true'],
        COMMON % (VERSION, RID,
                  'PORTABLE BUILD. Everything needed is in this folder - you do not\n'
                  'need .NET installed. Unzip it anywhere and run Nfty.Desktop.exe.')))

    made.append(stage(
        'single-file',
        ['--self-contained', 'true', '-p:PublishSingleFile=true',
         '-p:IncludeNativeLibrariesForSelfExtract=true'],
        ['--self-contained', 'true', '-p:PublishSingleFile=true',
         '-p:IncludeNativeLibrariesForSelfExtract=true'],
        COMMON % (VERSION, RID,
                  'SINGLE-FILE BUILD. One executable, nothing to install. It unpacks\n'
                  'itself to a temporary folder on first run, so the first launch is\n'
                  'slower than the portable build; every launch after that is not.')))

    made.append(stage(
        'framework-dependent',
        ['--self-contained', 'false'],
        ['--self-contained', 'false'],
        COMMON % (VERSION, RID,
                  'FRAMEWORK-DEPENDENT BUILD - the small one. It needs the .NET 10\n'
                  'DESKTOP runtime already installed:\n'
                  '  https://dotnet.microsoft.com/download/dotnet/10.0\n'
                  'If you are not sure, take the portable build instead.')))

    made.append(stage(
        'single-file-net10',
        ['--self-contained', 'false', '-p:PublishSingleFile=true',
         '-p:IncludeNativeLibrariesForSelfExtract=true'],
        ['--self-contained', 'false', '-p:PublishSingleFile=true',
         '-p:IncludeNativeLibrariesForSelfExtract=true'],
        COMMON % (VERSION, RID,
                  'SINGLE FILE, .NET REQUIRED. One executable with nothing beside\n'
                  'it - the native libraries are embedded too - but the runtime is\n'
                  'left out, so it is a third the size of the self-contained single\n'
                  'file. Needs the .NET 10 DESKTOP runtime:\n'
                  '  https://dotnet.microsoft.com/download/dotnet/10.0')))

    print('\nwrote %d archives to %s' % (len(made), OUT))
    return made


if __name__ == '__main__':
    main()
