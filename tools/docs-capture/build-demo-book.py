"""Assemble the demo CookBook the user manual is screenshotted from.

Run `draw-demo-art.py <dir>` first, then this. It writes the manifests, calls the CLI's authoring
commands to build the archives, and drops the result plus a Kitchen into an output folder.

    python tools/docs-capture/draw-demo-art.py .demo/art
    python tools/docs-capture/build-demo-book.py .demo/art .demo/out

The book is deliberately small and deliberately real: five layers, all three layer kinds, one
exclusion rule, two Recipes at uneven weights, and a DNA space around 900k. Screenshots taken against
the 8x8 test fixture look empty and quote numbers like "2 unique DNA", which teaches the reader the
wrong thing about what the app is for.
"""
import json, os, subprocess, shutil, sys

ART, OUT = sys.argv[1], sys.argv[2]
CLI = ["dotnet", "run", "--project", "src/Nfty.Cli", "--no-build", "--"]

MAN = os.path.join(OUT, "manifests")
IGT = os.path.join(OUT, "igt")
RCP = os.path.join(OUT, "rcp")
for d in (MAN, IGT, RCP):
    shutil.rmtree(d, ignore_errors=True)
    os.makedirs(d)


def dyn(hmin, hmax, smin, smax, hq=30, sq=20):
    return {"model": "hsv", "hueQuantize": hq, "satQuantize": sq,
            "entries": [{"weight": 100,
                         "range": {"hueMin": hmin, "hueMax": hmax, "satMin": smin, "satMax": smax},
                         "fixed": None}]}


def fixed(spec):
    return {"model": "hsv", "hueQuantize": 30, "satQuantize": 20,
            "entries": [{"weight": 100, "range": None, "fixed": spec}]}


INGREDIENTS = [
    ("bg",    "Background", "dynamic", dyn(0, 360, 25, 70),
     [("plain", "Plain", 50), ("grid", "Grid", 30), ("rays", "Rays", 20)]),
    ("body",  "Body",       "dynamic", dyn(20, 70, 30, 80),
     [("prowl", "Prowl", 60), ("sit", "Sit", 40)]),
    ("fbody", "Body",       "dynamic", dyn(10, 45, 55, 95),
     [("stand", "Stand", 100)]),
    ("eyes",  "Eyes",       "static",  fixed("hex:1a1a24"),
     [("round", "Round", 50), ("sleepy", "Sleepy", 25), ("wink", "Wink", 25)]),
    ("aura",  "Aura",       "dynamic", dyn(160, 330, 55, 95),
     [("glow", "Glow", 70), ("spark", "Spark", 30)]),
    ("hat",   "Hat",        "custom",  None,
     [("bare", "Bare", 60), ("cap", "Cap", 25), ("crown", "Crown", 15)]),
]

RECIPES = [
    ("cat", "Cat", ["bg", "body", "eyes", "aura", "hat"],
     [{"type": "exclude",
       "when": {"ingredientId": "bg", "variantId": "rays"},
       "targets": [{"ingredientId": "aura", "variantId": "spark"}]}]),
    ("fox", "Fox", ["bg", "fbody", "eyes", "aura", "hat"], []),
]

BOOK = {"id": "vaporpets", "name": "Vapor Pets",
        "canvas": {"width": 64, "height": 64},
        "collection": {"name": "Vapor Pets",
                       "description": "Little creatures with a colorful aura.",
                       "symbol": "VP"},
        "recipeWeights": {"cat": 60, "fox": 40},
        "schemaVersion": 1}


def run(args):
    r = subprocess.run(CLI + args, capture_output=True, text=True)
    if r.returncode != 0:
        sys.exit(f"FAILED: {' '.join(args)}\n{r.stdout}{r.stderr}")
    print(" ", r.stdout.strip().splitlines()[0] if r.stdout.strip() else " ".join(args[:2]))


for iid, name, kind, col, variants in INGREDIENTS:
    p = os.path.join(MAN, iid + ".json")
    json.dump({"id": iid, "name": name, "kind": kind, "colorization": col,
               "variants": [{"id": v, "name": n, "weight": w} for v, n, w in variants],
               "schemaVersion": 1},
              open(p, "w"), indent=2)
    run(["new", "ingredient", os.path.join(IGT, iid + ".igt"),
         "--manifest", p, "--images", os.path.join(ART, iid)])

for rid, name, order, rules in RECIPES:
    p = os.path.join(MAN, rid + ".rcp.json")
    json.dump({"id": rid, "name": name, "layerOrder": order, "rules": rules, "schemaVersion": 1},
              open(p, "w"), indent=2)
    run(["new", "recipe", os.path.join(RCP, rid + ".rcp"), "--manifest", p, "--ingredients", IGT])

p = os.path.join(MAN, "book.json")
json.dump(BOOK, open(p, "w"), indent=2)
cbk = os.path.join(OUT, "VaporPets.cbk")
run(["new", "cookbook", cbk, "--manifest", p, "--recipes", RCP])

# A Kitchen, plus a loose Recipe and Ingredient beside it, so the shelf and the "loose parts" states
# have something real in them.
shutil.copy(os.path.join(RCP, "cat.rcp"), OUT)
shutil.copy(os.path.join(IGT, "aura.igt"), OUT)
shutil.copy(os.path.join(IGT, "hat.igt"), OUT)
run(["new", "kitchen", os.path.join(OUT, "Studio.ktn"), "--name", "Studio"])

# The cooked Set the browser screenshots need. Packed, because "Open a cooked .set..." takes a file.
run(["generate", cbk, "--count", "500", "--seed", "launch",
     "--out", os.path.join(OUT, "VaporPets-launch"), "--pack"])

print(f"\nDemo workspace ready in {OUT}")
