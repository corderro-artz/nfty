"""Builds the Chest Demo CookBook that ships inside nfty, and commits it.

    python tools/demo/build-demo.py                 # -> src/Nfty.Core/Demo/ChestDemo.cbk
    python tools/demo/build-demo.py --workspace M:/nfty-demo   # ...and a folder to screenshot
    python tools/demo/build-demo.py --check         # rebuild, compare with the embedded copy, write nothing

It draws the art, writes the manifests, and drives the CLI's own authoring commands to assemble the
archives - the same commands a user has - so the shipped demo cannot be built by a path no one else
can take. The result is committed as a binary, the way `Icons.axaml` is committed next to the SVGs
it is generated from: Nfty.Core embeds it, so the demo is present in a single-file .exe copied to a
machine with nothing else on it. `DemoCookBookTests` reads, validates and cooks the committed bytes,
which is what keeps the file and this script from drifting apart. `--check` is the cheap form of
that guard for a human: it builds into the scratch folder and compares the result with the embedded
archive entry by entry (recursively - a .cbk nests .rcp nests .igt - ignoring zip timestamps, which
change on every write), so a generator edit never needs a re-embed just to find out it matters.

THE BOOK CARRIES DEPTH. Every variant but Glow's has a hand-designed depth map (drawn by
draw-chest-art.py next to the art) so the Chest Demo rises into a convincing 3D object. This script
stores each map by running `nfty set depth` - the command a user has - and never writes a `.dep`
itself. Glow stays flat on purpose.

WHY THIS BOOK LOOKS THE WAY IT DOES. It is the first CookBook most people will open, so it has to
show what nfty is for in one screen and stay small enough to read:

  * all three layer kinds - Dynamic bodies and bands, a Static lock, Custom trim;
  * a WEIGHTED colorization, so "dynamic" reads as "rolls within bands you set" rather than
    "random" - a body rolls warm timber 60% of the time and cold metal 40%;
  * two optional layers, so the absent-percent feature is visible without being hunted for;
  * one incompatibility rule, stated in a sentence a reader can check against the art;
  * two Recipes at uneven weights, which is the thing a CookBook is FOR;
  * a DNA space in the millions from twenty-nine small sprites, which is the whole argument for
    value-map colorization.

--workspace additionally lays out a Kitchen with loose parts and a cooked Set beside the book, which
is what tools/docs-capture screenshots. That used to be a second demo (a pet collection) maintained
separately from anything that shipped, so the manual showed a book no user ever had.
"""
import argparse
import io
import json
import os
import shutil
import subprocess
import sys
import zipfile

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
EMBEDDED = os.path.join(ROOT, "src", "Nfty.Core", "Demo", "ChestDemo.cbk")
CLI = ["dotnet", "run", "--project", os.path.join("src", "Nfty.Cli"), "--no-build", "--"]

# ---------------------------------------------------------------------------- colorization
# Hue in degrees, saturation in percent, both HALF-OPEN: ColorRoller samples Min + r*(Max-Min) with
# r in [0,1), so Max is never reached. Quantize is the DNA bucket width - it is what turns a
# continuous color space into a countable one, so a coarse step means fewer, more distinct colors.
TIMBER = {"hueMin": 15, "hueMax": 45, "satMin": 35, "satMax": 80}
COLD = {"hueMin": 190, "hueMax": 250, "satMin": 5, "satMax": 30}
LEATHER = {"hueMin": 18, "hueMax": 40, "satMin": 40, "satMax": 70}
IRON = {"hueMin": 200, "hueMax": 230, "satMin": 4, "satMax": 18}
ARCANE = {"hueMin": 0, "hueMax": 360, "satMin": 60, "satMax": 100}


# The quantize steps are chosen so the WHOLE book counts EXACTLY: totals are BigInteger and nothing
# caps the arithmetic, but the ruled Chest recipe is WALKED selection by selection and every color
# set is enumerated, both under UniqueSpace's budget - a demo whose headline figure is a floor
# ("more than N") teaches the reader that nfty cannot count its own space. Buckets are absolute indices from zero, not a range
# divided by a step, so a band of 30 degrees at a step of 15 spans three buckets when it straddles a
# boundary and two when it does not - count it with `nfty stats`, do not derive it.
def rolled(hq, sq, *entries):
    return {"model": "hsv", "hueQuantize": hq, "satQuantize": sq,
            "entries": [{"weight": w, "range": r, "fixed": None} for r, w in entries]}


def fixed(spec):
    return {"model": "hsv", "hueQuantize": 30, "satQuantize": 20,
            "entries": [{"weight": 100, "range": None, "fixed": spec}]}


# ---------------------------------------------------------------------------- the book
INGREDIENTS = [
    # id, display name, kind, colorization, [(variantId, name, weight)]
    # Five bodies under the barrel lid, four under the slab (stone is a chest-only material - see
    # the rule below for why it matters which). Weights fall off so a plain plank chest is common
    # and a stone one is the find.
    ("chestbody", "Body", "dynamic", rolled(15, 20, (TIMBER, 60), (COLD, 40)),
     [("planked", "Planked", 30), ("plated", "Plated", 22), ("slatted", "Slatted", 20),
      ("paneled", "Paneled", 16), ("stone", "Stone", 12)]),
    ("boxbody", "Body", "dynamic", rolled(15, 20, (TIMBER, 60), (COLD, 40)),
     [("plated", "Plated", 35), ("planked", "Planked", 30), ("paneled", "Paneled", 20),
      ("slatted", "Slatted", 15)]),
    # 30/40 rather than 15/20: coarser steps mean fewer, more distinct band colors - which at 32px
    # is no loss, and it keeps the space a number a reader can still take in.
    ("bands", "Bands", "dynamic", rolled(30, 40, (LEATHER, 50), (IRON, 50)),
     [("hoops", "Hoops", 30), ("straps", "Straps", 25), ("corners", "Corners", 20),
      ("frame", "Frame", 15), ("studs", "Studs", 10)]),
    # Static: ONE color for the whole collection, so every chest wears the same brass. Only the H and
    # S of the spec are used - the value comes from the art, which is why the lock still has form.
    ("lock", "Lock", "static", fixed("hex:c9a227"),
     [("keyhole", "Keyhole", 30), ("latch", "Latch", 22), ("padlock", "Padlock", 18),
      ("dial", "Dial", 14), ("crest", "Crest", 10), ("keypad", "Keypad", 6)]),
    # Custom: composited exactly as drawn, never colorized, so `colorization` MUST be null.
    ("trim", "Trim", "custom", None,
     [("gilt", "Gilt", 40), ("gems", "Gems", 25), ("pearls", "Pearls", 20),
      ("filigree", "Filigree", 15)]),
    ("glow", "Glow", "dynamic", rolled(60, 40, (ARCANE, 100)),
     [("sparks", "Sparks", 65), ("runes", "Runes", 35)]),
    # Feet sit below the plinth, so they are the one layer that cannot interact with either lid.
    # CUSTOM for an arithmetic reason, not a stylistic one: a dynamic layer multiplies the space by
    # its variants AND its color buckets, which makes it the dearest way to buy the least visible
    # variety. Custom multiplies by variants alone - and it gives the demo a second Custom example
    # beside the trim.
    ("feet", "Feet", "custom", None,
     [("blocks", "Blocks", 50), ("skids", "Skids", 30), ("claws", "Claws", 20)]),
]

# Bottom to top. Glow sits behind the chest, trim over the bands (a gilded hoop, not a hoop over
# gilt), and the lock last because a lock plate is the thing bolted on top of everything else.
CHEST_STACK = ["glow", "feet", "chestbody", "bands", "trim", "lock"]
BOX_STACK = ["glow", "feet", "boxbody", "bands", "trim", "lock"]

# A rule you can check by looking: a stone chest has no keypad on it.
STONE_HAS_NO_KEYPAD = {
    "type": "exclude",
    "when": {"ingredientId": "chestbody", "variantId": "stone"},
    "targets": [{"ingredientId": "lock", "variantId": "keypad"}],
}

# Percent, not probability, and it lives on the RECIPE - the same trim .igt is a chase item here and
# could be guaranteed in someone else's project.
ABSENT = {"trim": 55, "glow": 72}

RECIPES = [
    ("chest", "Chest", CHEST_STACK, [STONE_HAS_NO_KEYPAD], ABSENT),
    ("strongbox", "Strongbox", BOX_STACK, [], ABSENT),
]

BOOK = {
    "id": "chestdemo",
    "name": "Chest Demo",
    "canvas": {"width": 32, "height": 32},
    "collection": {
        "name": "Chest Demo",
        "description": "A demo collection that ships with nfty: layered chests to open, "
                       "edit and cook. Nothing here is precious - break it and rebuild it.",
        "symbol": "CHST",
    },
    "recipeWeights": {"chest": 65, "strongbox": 35},
    "targetSupply": 1200,
    # The book carries its own colors, so a collection handed on brings its palette with it.
    "palette": ["hex:8a5a2b", "hex:c9a227", "hex:5c6a78",
                "hex:ce3648", "hex:4884d6", "hex:1a1a24"],
    "schemaVersion": 2,
}


def run(args, cwd=ROOT):
    r = subprocess.run(CLI + args, cwd=cwd, capture_output=True, text=True)
    if r.returncode != 0:
        sys.exit("FAILED: %s\n%s%s" % (" ".join(args), r.stdout, r.stderr))
    return r.stdout


def build(work):
    art = os.path.join(work, "art")
    man = os.path.join(work, "manifests")
    igt = os.path.join(work, "igt")
    rcp = os.path.join(work, "rcp")
    for d in (man, igt, rcp):
        shutil.rmtree(d, ignore_errors=True)
        os.makedirs(d)

    subprocess.check_call([sys.executable, os.path.join(ROOT, "tools", "demo", "draw-chest-art.py"), art])

    for iid, name, kind, col, variants in INGREDIENTS:
        p = os.path.join(man, iid + ".json")
        with open(p, "w", encoding="utf-8") as f:
            json.dump({"id": iid, "name": name, "kind": kind, "colorization": col,
                       "variants": [{"id": v, "name": n, "weight": w} for v, n, w in variants],
                       "schemaVersion": 2}, f, indent=2)
        igt_path = os.path.join(igt, iid + ".igt")
        run(["new", "ingredient", igt_path, "--manifest", p, "--images", os.path.join(art, iid)])
        depth_dir = os.path.join(art, "depth", iid)
        if os.path.isdir(depth_dir):
            for f in sorted(os.listdir(depth_dir)):
                if f.endswith(".png"):
                    run(["set", "depth", igt_path, "--variant", f[:-4], "--map",
                         os.path.join(depth_dir, f)])

    for rid, name, order, rules, absent in RECIPES:
        p = os.path.join(man, rid + ".rcp.json")
        with open(p, "w", encoding="utf-8") as f:
            json.dump({"id": rid, "name": name, "layerOrder": order, "rules": rules,
                       "absentPercent": absent, "schemaVersion": 2}, f, indent=2)
        run(["new", "recipe", os.path.join(rcp, rid + ".rcp"), "--manifest", p, "--ingredients", igt])

    p = os.path.join(man, "book.json")
    with open(p, "w", encoding="utf-8") as f:
        json.dump(BOOK, f, indent=2)
    cbk = os.path.join(work, "ChestDemo.cbk")
    if os.path.exists(cbk):
        os.remove(cbk)          # `new cookbook` refuses to overwrite, and this script is re-run
    run(["new", "cookbook", cbk, "--manifest", p, "--recipes", rcp])
    return cbk, igt, rcp


def entries(data, prefix=""):
    """Every entry of a zip as {path: bytes}, descending into nested archives so a difference deep in
    an .igt is named by its full path. Zip timestamps are not part of the content."""
    found = {}
    with zipfile.ZipFile(io.BytesIO(data)) as z:
        for info in sorted(z.infolist(), key=lambda i: i.filename):
            body = z.read(info)
            name = prefix + info.filename
            if info.filename.lower().endswith((".rcp", ".igt")):
                found.update(entries(body, name + "!/"))
            else:
                found[name] = body
    return found


def check(cbk):
    """Compare a fresh build with the embedded copy. Returns the process exit code."""
    with open(cbk, "rb") as f:
        fresh = entries(f.read())
    with open(EMBEDDED, "rb") as f:
        shipped = entries(f.read())
    for name in sorted(set(fresh) | set(shipped)):
        if name not in shipped:
            print("differs: %s is not in the embedded demo" % name)
            return 1
        if name not in fresh:
            print("differs: %s is only in the embedded demo" % name)
            return 1
        if fresh[name] != shipped[name]:
            print("differs: %s (%d bytes built, %d embedded)" % (name, len(fresh[name]), len(shipped[name])))
            return 1
    print("demo is up to date")
    return 0


def workspace(out, cbk, igt, rcp):
    """The screenshot workspace: the book, a Kitchen, loose parts on its shelf, and a cooked Set."""
    os.makedirs(out, exist_ok=True)
    shutil.copy(cbk, out)
    shutil.copy(os.path.join(rcp, "chest.rcp"), out)
    shutil.copy(os.path.join(igt, "trim.igt"), out)
    shutil.copy(os.path.join(igt, "glow.igt"), out)
    run(["new", "kitchen", os.path.join(out, "Workshop.ktn"), "--name", "Workshop"])
    run(["generate", os.path.join(out, "ChestDemo.cbk"), "--count", "500", "--seed", "launch",
         "--out", os.path.join(out, "ChestDemo-launch"), "--pack"])


def main():
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--work", default=os.path.join(ROOT, ".demo"), help="scratch build folder")
    ap.add_argument("--workspace", help="also lay out a Kitchen + cooked Set here, for docs capture")
    ap.add_argument("--no-embed", action="store_true", help="build but do not update the shipped copy")
    ap.add_argument("--check", action="store_true",
                    help="build, compare with the embedded copy, write nothing (exit 1 if they differ)")
    a = ap.parse_args()
    if a.check and (a.workspace or a.no_embed):
        ap.error("--check only compares; it cannot be combined with --workspace or --no-embed")

    os.makedirs(a.work, exist_ok=True)
    cbk, igt, rcp = build(a.work)

    if a.check:
        sys.exit(check(cbk))
    if not a.no_embed:
        os.makedirs(os.path.dirname(EMBEDDED), exist_ok=True)
        shutil.copy(cbk, EMBEDDED)
        print("embedded  %s  (%.1f KB)" % (os.path.relpath(EMBEDDED, ROOT),
                                           os.path.getsize(EMBEDDED) / 1024.0))
    if a.workspace:
        workspace(a.workspace, cbk, igt, rcp)
        print("workspace %s" % a.workspace)

    sys.stdout.write(run(["inspect", cbk]))


if __name__ == "__main__":
    main()
