"""Builds the Chest Demo CookBook that ships inside nfty, and commits it.

    python tools/demo/build-demo.py                 # -> src/Nfty.Core/Demo/ChestDemo.cbk
    python tools/demo/build-demo.py --workspace M:/nfty-demo   # ...and a folder to screenshot

It draws the art, writes the manifests, and drives the CLI's own authoring commands to assemble the
archives - the same commands a user has - so the shipped demo cannot be built by a path no one else
can take. The result is committed as a binary, the way `Icons.axaml` is committed next to the SVGs
it is generated from: Nfty.Core embeds it, so the demo is present in a single-file .exe copied to a
machine with nothing else on it. `DemoCookBookTests` reads, validates and cooks the committed bytes,
which is what keeps the file and this script from drifting apart.

WHY THIS BOOK LOOKS THE WAY IT DOES. It is the first CookBook most people will open, so it has to
show what nfty is for in one screen and stay small enough to read:

  * all three layer kinds - Dynamic bodies and bands, a Static lock, Custom trim;
  * a WEIGHTED colorization, so "dynamic" reads as "rolls within bands you set" rather than
    "random" - a body rolls warm timber 60% of the time and cold metal 40%;
  * two optional layers, so the absent-percent feature is visible without being hunted for;
  * one incompatibility rule, stated in a sentence a reader can check against the art;
  * two Recipes at uneven weights, which is the thing a CookBook is FOR;
  * a DNA space in the hundreds of thousands from sixteen small sprites, which is the whole
    argument for value-map colorization.

--workspace additionally lays out a Kitchen with loose parts and a cooked Set beside the book, which
is what tools/docs-capture screenshots. That used to be a second demo (a pet collection) maintained
separately from anything that shipped, so the manual showed a book no user ever had.
"""
import argparse
import json
import os
import shutil
import subprocess
import sys

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


# The quantize steps are chosen so the WHOLE book counts EXACTLY rather than saturating
# UniqueSpace's million-bucket cap: a demo whose headline figure is "more than 1000000" teaches the
# reader that nfty cannot count its own space. Buckets are absolute indices from zero, not a range
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
    ("chestbody", "Body", "dynamic", rolled(15, 20, (TIMBER, 60), (COLD, 40)),
     [("planked", "Planked", 45), ("plated", "Plated", 35), ("stone", "Stone", 20)]),
    ("boxbody", "Body", "dynamic", rolled(15, 20, (TIMBER, 60), (COLD, 40)),
     [("planked", "Planked", 55), ("plated", "Plated", 45)]),
    ("bands", "Bands", "dynamic", rolled(15, 20, (LEATHER, 50), (IRON, 50)),
     [("hoops", "Hoops", 45), ("corners", "Corners", 35), ("straps", "Straps", 20)]),
    # Static: ONE color for the whole collection, so every chest wears the same brass. Only the H and
    # S of the spec are used - the value comes from the art, which is why the lock still has form.
    ("lock", "Lock", "static", fixed("hex:c9a227"),
     [("keyhole", "Keyhole", 40), ("latch", "Latch", 30),
      ("padlock", "Padlock", 20), ("keypad", "Keypad", 10)]),
    # Custom: composited exactly as drawn, never colorized, so `colorization` MUST be null.
    ("trim", "Trim", "custom", None,
     [("gilt", "Gilt", 60), ("gems", "Gems", 40)]),
    ("glow", "Glow", "dynamic", rolled(60, 40, (ARCANE, 100)),
     [("sparks", "Sparks", 65), ("runes", "Runes", 35)]),
]

# Bottom to top. Glow sits behind the chest, trim over the bands (a gilded hoop, not a hoop over
# gilt), and the lock last because a lock plate is the thing bolted on top of everything else.
CHEST_STACK = ["glow", "chestbody", "bands", "trim", "lock"]
BOX_STACK = ["glow", "boxbody", "bands", "trim", "lock"]

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
    "targetSupply": 500,
    # The book carries its own colors, so a collection handed on brings its palette with it.
    "palette": ["hex:8a5a2b", "hex:c9a227", "hex:5c6a78",
                "hex:ce3648", "hex:4884d6", "hex:1a1a24"],
    "schemaVersion": 1,
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
                       "schemaVersion": 1}, f, indent=2)
        run(["new", "ingredient", os.path.join(igt, iid + ".igt"),
             "--manifest", p, "--images", os.path.join(art, iid)])

    for rid, name, order, rules, absent in RECIPES:
        p = os.path.join(man, rid + ".rcp.json")
        with open(p, "w", encoding="utf-8") as f:
            json.dump({"id": rid, "name": name, "layerOrder": order, "rules": rules,
                       "absentPercent": absent, "schemaVersion": 1}, f, indent=2)
        run(["new", "recipe", os.path.join(rcp, rid + ".rcp"), "--manifest", p, "--ingredients", igt])

    p = os.path.join(man, "book.json")
    with open(p, "w", encoding="utf-8") as f:
        json.dump(BOOK, f, indent=2)
    cbk = os.path.join(work, "ChestDemo.cbk")
    if os.path.exists(cbk):
        os.remove(cbk)          # `new cookbook` refuses to overwrite, and this script is re-run
    run(["new", "cookbook", cbk, "--manifest", p, "--recipes", rcp])
    return cbk, igt, rcp


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
    a = ap.parse_args()

    os.makedirs(a.work, exist_ok=True)
    cbk, igt, rcp = build(a.work)

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
