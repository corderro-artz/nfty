#!/usr/bin/env python3
"""Build a single tabbed gallery page over the nfty design mockups.

Every mockup in this directory is a self-contained fragment (no <!doctype>/<html>/
<body> — see README). This tool wraps each in its own sandboxed <iframe> and stacks
them behind a left-rail tab switcher, so the whole design set is reviewable at once —
handy right after the design companion drafts a new mockup.

Run from anywhere:  python3 docs/design/mockups/build-gallery.py
Writes:  docs/design/mockups/gallery.html   (open it directly in a browser)

Add a mockup: drop its file in this directory and add one row to SCREENS below.

How it works, and why:
- Each mockup is base64-encoded and decoded to UTF-8 at load. This preserves every
  byte — including each mockup's own <script>…</script> blocks — with no escaping.
  (A <script type="text/html"> template does NOT work: its extracted textContent keeps
  the backslash from any </script> escape, which breaks the mockup's JS — fatal for the
  Explorer, whose entire tree/detail DOM is built in script.)
- The 8 mockups share one LOCKED token block (identical hex across files). The gallery
  injects one !important token override per frame, gated by data-theme, forcing the
  chosen theme regardless of whether a mockup keys off prefers-color-scheme (brainstorm
  set) or data-theme (committed set). The same injection normalizes layout: it hides the
  demo scaffold (title/subtitle/note) and flex-fills the app window to a uniform frame.
- Frames re-render on any theme change — the gallery's button, the host's theme toggle
  (both stamp data-theme on <html>), or an OS change.
"""
import base64, html, json, pathlib

HERE = pathlib.Path(__file__).resolve().parent

# Uniform render box = the app's minimum window (1180 confirmed width; 760 min height).
W, H = 1180, 760

# (file, tab name, one-line descriptor) — order is the review order.
SCREENS = [
    ("landing.html",             "Landing", "Create · Open · Import · Recent (empty on first run)"),
    ("wizard-cookbook.html",     "New CookBook", "Single pane · 5 grounded fields"),
    ("wizard-recipe.html",       "New Recipe", "Name + mandatory weight · live mix"),
    ("wizard-ingredient.html",   "New Ingredient", "Kind radios · colour-range sliders"),
    ("explorer.html",            "Explorer", "The primary screen · tree + detail"),
    ("ingredient-editor.html",   "Ingredient Editor", "Paint a value-map · Colorize rail"),
    ("help.html",                "Help", "Quick-reference sheet"),
]

DATA = [base64.b64encode((HERE / f).read_text(encoding="utf-8").encode("utf-8")).decode("ascii")
        for f, _, _ in SCREENS]
DATA_JS = json.dumps(DATA)
META = json.dumps([{"t": n, "d": d} for _, n, d in SCREENS])

rail = []
for i, (f, name, desc) in enumerate(SCREENS):
    rail.append(
        f'''      <button class="tab" data-i="{i}" role="tab" aria-selected="{'true' if i==0 else 'false'}">
        <span class="num">{i+1}</span>
        <span class="lbl"><span class="nm">{html.escape(name)}</span><span class="ds">{html.escape(desc)}</span></span>
      </button>'''
    )
RAIL = "\n".join(rail)

def _vars(d):
    return "".join(f"{k}:{v} !important;" for k, v in d.items())

LIGHT = {
    "--bg": "#f4efe8", "--bg-alt": "#f1ece4", "--bg-alt2": "#ede7df", "--panel": "#f8f3ed",
    "--fg": "#121318", "--fg-muted": "#121418b8", "--line": "#1214181f", "--line-strong": "#12141833",
    "--accent": "#a11f31", "--accent-text": "#97192a", "--on-accent": "#f7f2ec",
    "--accent-wash": "#a11f3114", "--accent-line": "#a11f3140", "--tile": "#ece5db", "--info": "#3b5b6f",
}
DARK = {
    "--bg": "#07080b", "--bg-alt": "#0a0b10", "--bg-alt2": "#0f1118", "--panel": "#0b0c10",
    "--fg": "#f2ede6", "--fg-muted": "#f2ede6c7", "--line": "#f2ede624", "--line-strong": "#f2ede633",
    "--accent": "#a11f31", "--accent-text": "#e0788a", "--on-accent": "#f7f2ec",
    "--accent-wash": "#a11f3126", "--accent-line": "#a11f3166", "--tile": "#12141c", "--info": "#7fb0c4",
}
FORCE = (
    "<style>"
    ':root[data-theme="light"],:root[data-theme="light"] .nfty-scope{' + _vars(LIGHT) + "}"
    ':root[data-theme="dark"],:root[data-theme="dark"] .nfty-scope{' + _vars(DARK) + "}"
    "html,body{margin:0;height:100%}"
    "body{padding:0;background:var(--bg);color:var(--fg);overflow:hidden;display:flex;flex-direction:column}"
    "h2,p.subtitle,.note,.pitch{display:none !important}"
    ".stage,.nfty-scope{margin:0 !important;padding:0 !important;max-width:none !important;width:100% !important;"
    "flex:1 1 auto !important;display:flex !important;flex-direction:column !important;min-height:0 !important}"
    ".frame{margin:0 !important;padding:0 !important;max-width:none !important;width:100% !important;"
    "overflow:visible !important;flex:1 1 auto !important;display:flex !important;min-height:0 !important}"
    ".window,.nw{width:100% !important;max-width:none !important;margin:0 !important;"
    "border-radius:0 !important;border:0 !important;flex:1 1 auto !important;min-height:0 !important;"
    "display:flex !important;flex-direction:column !important}"
    ".panes-scroll,.winbody,.land,.body{flex:1 1 auto !important;min-height:0 !important}"
    "</style>"
)
FORCE_JS = json.dumps(FORCE)

PAGE = f'''<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>nfty — mockup gallery</title>
<style>
  :root {{
    --bg:#efe9e0; --panel:#f6f1ea; --rail:#e9e2d7; --fg:#121318; --fg-muted:#12141899;
    --line:#12141822; --line-strong:#1214183a; --accent:#a11f31; --accent-text:#97192a;
    --accent-wash:#a11f3112; --accent-line:#a11f3140; --on-accent:#f7f2ec;
    --mono:"SF Mono","JetBrains Mono",ui-monospace,Menlo,Consolas,monospace;
  }}
  @media (prefers-color-scheme: dark) {{
    :root {{ --bg:#050609; --panel:#0b0c11; --rail:#0a0b0f; --fg:#f2ede6; --fg-muted:#f2ede699;
      --line:#f2ede61f; --line-strong:#f2ede636; --accent:#a11f31; --accent-text:#e0788a;
      --accent-wash:#a11f3126; --accent-line:#a11f3166; --on-accent:#f7f2ec; }}
  }}
  :root[data-theme="light"] {{ --bg:#efe9e0; --panel:#f6f1ea; --rail:#e9e2d7; --fg:#121318; --fg-muted:#12141899;
    --line:#12141822; --line-strong:#1214183a; --accent-text:#97192a; --accent-wash:#a11f3112; --accent-line:#a11f3140; }}
  :root[data-theme="dark"] {{ --bg:#050609; --panel:#0b0c11; --rail:#0a0b0f; --fg:#f2ede6; --fg-muted:#f2ede699;
    --line:#f2ede61f; --line-strong:#f2ede636; --accent-text:#e0788a; --accent-wash:#a11f3126; --accent-line:#a11f3166; }}

  * {{ box-sizing:border-box; }}
  html,body {{ margin:0; }}
  .gwrap {{ font-family:var(--mono); color:var(--fg); background:var(--bg); min-height:100vh;
           display:grid; grid-template-columns:288px 1fr; }}

  .grail {{ background:var(--rail); border-right:1px solid var(--line); display:flex; flex-direction:column;
           padding:16px 14px; gap:3px; position:sticky; top:0; height:100vh; overflow-y:auto; }}
  .ghead {{ display:flex; align-items:center; gap:9px; padding:4px 6px 12px; }}
  .gtile {{ width:22px; height:22px; border-radius:6px; display:grid; place-items:center;
           background:var(--accent-wash); border:1px solid var(--accent-line); flex:0 0 auto; }}
  .gtile i {{ width:9px; height:9px; background:var(--accent); border-radius:2px; transform:rotate(45deg); display:block; }}
  .gwm {{ font-weight:700; font-size:14px; letter-spacing:-.01em; }} .gwm b {{ color:var(--accent-text); }}
  .gsub {{ font-size:10px; letter-spacing:.14em; text-transform:uppercase; color:var(--fg-muted); padding:0 6px 6px; }}

  .tab {{ display:flex; align-items:center; gap:11px; width:100%; text-align:left; cursor:pointer;
         font:inherit; padding:8px 9px; border:1px solid transparent; border-radius:8px; background:transparent;
         color:var(--fg); transition:background .12s, border-color .12s; }}
  .tab:hover {{ background:var(--accent-wash); }}
  .tab[aria-selected="true"] {{ background:var(--panel); border-color:var(--line-strong); box-shadow:inset 2px 0 0 var(--accent); }}
  .tab .num {{ flex:0 0 auto; width:22px; height:22px; display:grid; place-items:center; border-radius:6px;
         font-size:11px; font-variant-numeric:tabular-nums; color:var(--fg-muted);
         border:1px solid var(--line-strong); background:var(--bg); }}
  .tab[aria-selected="true"] .num {{ color:var(--on-accent); background:var(--accent); border-color:var(--accent); }}
  .tab .lbl {{ display:flex; flex-direction:column; gap:2px; min-width:0; }}
  .tab .nm {{ font-size:12.5px; font-weight:600; white-space:nowrap; overflow:hidden; text-overflow:ellipsis; }}
  .tab .ds {{ font-size:10px; color:var(--fg-muted); white-space:nowrap; overflow:hidden; text-overflow:ellipsis; }}

  .gfoot {{ margin-top:auto; padding:12px 6px 4px; font-size:10px; color:var(--fg-muted); line-height:1.6; border-top:1px solid var(--line); }}
  .gfoot kbd {{ font-family:var(--mono); font-size:9.5px; border:1px solid var(--line-strong); border-radius:4px; padding:0 4px; background:var(--panel); }}
  .themebtn {{ margin-top:8px; font:inherit; font-size:11px; cursor:pointer; width:100%; text-align:left;
           padding:6px 8px; border:1px solid var(--line-strong); border-radius:6px; background:var(--panel);
           color:var(--fg); display:flex; align-items:center; gap:7px; }}
  .themebtn svg {{ width:13px; height:13px; }}

  .gstage {{ overflow:auto; padding:26px; }}
  .gframe {{ width:{W}px; margin:0 auto; }}
  .gcap {{ display:flex; align-items:baseline; gap:10px; margin:0 2px 14px; }}
  .gcap .t {{ font-size:15px; font-weight:700; }}
  .gcap .d {{ font-size:11.5px; color:var(--fg-muted); }}
  .gcap .n {{ margin-left:auto; font-size:11px; color:var(--fg-muted); font-variant-numeric:tabular-nums; }}
  .gcap .dim {{ font-size:10.5px; color:var(--fg-muted); border:1px solid var(--line-strong); border-radius:5px;
           padding:1px 7px; font-variant-numeric:tabular-nums; }}
  iframe#stage {{ width:{W}px; height:{H}px; border:1px solid var(--line-strong); border-radius:12px;
           background:var(--panel); box-shadow:0 1px 2px #12141810, 0 24px 60px -34px #12141840; display:block; }}

  :where(button):focus-visible {{ outline:2px solid var(--accent); outline-offset:2px; }}
</style>
</head>
<body>
<div class="gwrap">
  <nav class="grail" role="tablist" aria-label="nfty mockups">
    <div class="ghead"><span class="gtile"><i></i></span><span class="gwm">nft<b>y</b></span></div>
    <div class="gsub">Mockups · design set</div>
{RAIL}
    <button class="themebtn" id="theme"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.6" aria-hidden="true"><circle cx="12" cy="12" r="8"/><path d="M12 4a8 8 0 0 1 0 16z" fill="currentColor" stroke="none"/></svg> Toggle theme</button>
    <div class="gfoot">Keys — <kbd>1</kbd>…<kbd>{len(SCREENS)}</kbd> jump · <kbd>&uarr;</kbd><kbd>&darr;</kbd> step. Every screen renders at the
      app's minimum window — {W}&times;{H} — in its own live frame.</div>
  </nav>

  <main class="gstage">
    <div class="gframe">
      <div class="gcap"><span class="t" id="capT"></span><span class="d" id="capD"></span>
        <span class="dim">{W} &times; {H}</span><span class="n" id="capN"></span></div>
      <iframe id="stage" title="mockup" sandbox="allow-scripts allow-same-origin"></iframe>
    </div>
  </main>
</div>

<script>
(function () {{
  var DATA = {DATA_JS};
  var meta = {META};
  var FORCE = {FORCE_JS};
  var tabs = Array.prototype.slice.call(document.querySelectorAll('.tab'));
  var stage = document.getElementById('stage');
  var capT = document.getElementById('capT'), capD = document.getElementById('capD'), capN = document.getElementById('capN');
  var cur = 0, N = tabs.length;

  function b64utf8(b64) {{
    var bin = atob(b64), bytes = new Uint8Array(bin.length);
    for (var i = 0; i < bin.length; i++) bytes[i] = bin.charCodeAt(i);
    return new TextDecoder('utf-8').decode(bytes);
  }}
  function themeAttr() {{
    var r = document.documentElement.getAttribute('data-theme');
    if (r) return r;
    return window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
  }}
  function render() {{
    var raw = b64utf8(DATA[cur]);
    stage.srcdoc = '<!doctype html><html data-theme="' + themeAttr() + '"><head><meta charset="utf-8">'
      + '<meta name="viewport" content="width=device-width,initial-scale=1">'
      + FORCE + '</head><body>' + raw + '</body></html>';
  }}
  function load(i) {{
    cur = i;
    tabs.forEach(function (t, k) {{ t.setAttribute('aria-selected', k === i ? 'true' : 'false'); }});
    tabs[i].scrollIntoView({{ block:'nearest' }});
    render();
    capT.textContent = meta[i].t; capD.textContent = meta[i].d; capN.textContent = (i + 1) + ' / ' + N;
  }}
  tabs.forEach(function (t) {{ t.addEventListener('click', function () {{ load(+t.dataset.i); }}); }});
  document.getElementById('theme').addEventListener('click', function () {{
    document.documentElement.setAttribute('data-theme', themeAttr() === 'dark' ? 'light' : 'dark');
  }});
  var lastTheme = themeAttr();
  function syncTheme() {{ var t = themeAttr(); if (t !== lastTheme) {{ lastTheme = t; render(); }} }}
  new MutationObserver(syncTheme).observe(document.documentElement, {{ attributes:true, attributeFilter:['data-theme'] }});
  if (window.matchMedia) {{
    var mq = window.matchMedia('(prefers-color-scheme: dark)');
    (mq.addEventListener ? mq.addEventListener.bind(mq, 'change') : mq.addListener.bind(mq))(syncTheme);
  }}
  document.addEventListener('keydown', function (e) {{
    if (e.target && /^(INPUT|TEXTAREA)$/.test(e.target.tagName)) return;
    if (e.key >= '1' && e.key <= String(Math.min(9, N))) {{ load(+e.key - 1); }}
    else if (e.key === 'ArrowDown') {{ e.preventDefault(); load((cur + 1) % N); }}
    else if (e.key === 'ArrowUp') {{ e.preventDefault(); load((cur - 1 + N) % N); }}
  }});
  load(0);
}})();
</script>
</body>
</html>'''

out = HERE / "gallery.html"
out.write_text(PAGE, encoding="utf-8")
print(f"wrote {out} ({len(PAGE)} bytes; {len(SCREENS)} screens)")
