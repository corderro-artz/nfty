#!/usr/bin/env python3
"""Render docs/diagrams/*.mmd to a light and a dark SVG on nfty's own tokens.

Mermaid needs a browser, and pulling in mermaid-cli drags puppeteer and a second
Chromium behind it. This serves a render page instead and takes the SVG back over
a POST, so the only requirement is a browser pointed at the printed URL.

    python tools/diagrams/build.py          # serve, wait for a render, write, exit

The palette is read from src/Nfty.App/Themes/Tokens.axaml's own values, so the
diagram cannot drift from the app the way a hand-picked hex would.
"""
import http.server
import json
import pathlib
import socketserver
import sys
import threading
import webbrowser

ROOT = pathlib.Path(__file__).resolve().parents[2]
DIAGRAMS = ROOT / "docs" / "diagrams"
PORT = 8732

# Straight from Tokens.axaml. The border is the accent at the alpha AccentLineBrush
# carries in each dictionary, flattened over that theme's panel - an SVG loaded as an
# <img> renders in secure static mode, so nothing here may depend on a loaded font or
# an external resource.
THEMES = {
    "light": {
        "background": "transparent",
        "mainBkg": "#f8f3ed",
        "primaryColor": "#f8f3ed",
        "primaryTextColor": "#121318",
        "primaryBorderColor": "#d59ea2",
        "nodeBorder": "#d59ea2",
        "lineColor": "#6f6a63",
        "edgeLabelBackground": "#f4efe8",
        "secondaryColor": "#f1ece4",
        "tertiaryColor": "#ece5db",
        "secondaryBorderColor": "#d5cfc6",
        "tertiaryBorderColor": "#d5cfc6",
        "secondaryTextColor": "#121318",
        "tertiaryTextColor": "#121318",
    },
    "dark": {
        "background": "transparent",
        "mainBkg": "#0b0c10",
        "primaryColor": "#0b0c10",
        "primaryTextColor": "#f2ede6",
        "primaryBorderColor": "#6e1826",
        "nodeBorder": "#6e1826",
        "lineColor": "#918b83",
        "edgeLabelBackground": "#07080b",
        "secondaryColor": "#0a0b10",
        "tertiaryColor": "#12141c",
        "secondaryBorderColor": "#2b2e36",
        "tertiaryBorderColor": "#2b2e36",
        "secondaryTextColor": "#f2ede6",
        "tertiaryTextColor": "#f2ede6",
    },
}

# No webfont: see the note above. IBM Plex Sans is used when the reader has it.
FONT = '"IBM Plex Sans", ui-sans-serif, "Segoe UI", Helvetica, Arial, sans-serif'

PAGE = r"""<!doctype html><meta charset="utf-8"><title>nfty diagram build</title>
<body style="font:13px ui-sans-serif,system-ui;padding:16px">
<h3>Rendering %(name)s…</h3><div id="log"></div><div id="out" style="position:absolute;left:-99999px"></div>
<script type="module">
import mermaid from "https://cdn.jsdelivr.net/npm/mermaid@11/dist/mermaid.esm.min.mjs";
const src = %(src)s, themes = %(themes)s, font = %(font)s;
const log = (m) => document.getElementById("log").insertAdjacentHTML("beforeend", "<div>"+m+"</div>");
const done = {};
for (const [name, vars] of Object.entries(themes)) {
  mermaid.initialize({ startOnLoad:false, theme:"base", fontFamily:font,
                       themeVariables:{ ...vars, fontFamily:font, fontSize:"15px" },
                       flowchart:{ htmlLabels:true, curve:"basis", nodeSpacing:40,
                                   rankSpacing:38, useMaxWidth:false } });
  const { svg } = await mermaid.render("d_"+name, src, document.getElementById("out"));

  // mermaid hands back HTML-serialized markup, where the <br/> in a label comes out as a
  // bare <br>. A .svg file is parsed as XML, so that one unclosed tag is a FATAL parse
  // error and the whole diagram renders as a broken image. Re-serializing through
  // XMLSerializer emits well-formed XML; the HTML parser on the way in is what tolerates
  // the input. Straight DOMParser(image/svg+xml) cannot be used - it chokes on the same tag.
  const holder = document.createElement("div");
  holder.innerHTML = svg;
  const el = holder.querySelector("svg");
  el.removeAttribute("style");                      // a max-width overrides the real size
  const vb = el.getAttribute("viewBox").split(" ");
  el.setAttribute("width", Math.round(vb[2]));      // an <img> needs an intrinsic size,
  el.setAttribute("height", Math.round(vb[3]));     // and "100%%" is not one
  const out = '<?xml version="1.0" encoding="UTF-8"?>' + new XMLSerializer().serializeToString(el);
  done[name] = out; log(name + " rendered, " + out.length + " bytes");
}
const r = await fetch("/write", { method:"POST", body: JSON.stringify(done) });
log(await r.text());
</script></body>"""


class Handler(http.server.BaseHTTPRequestHandler):
    written = threading.Event()

    def log_message(self, *a):
        pass

    def do_GET(self):
        body = PAGE % {
            "name": "generation-pipeline",
            "src": json.dumps((DIAGRAMS / "generation-pipeline.mmd").read_text(encoding="utf-8")),
            "themes": json.dumps(THEMES),
            "font": json.dumps(FONT),
        }
        raw = body.encode("utf-8")
        self.send_response(200)
        self.send_header("Content-Type", "text/html; charset=utf-8")
        self.send_header("Content-Length", str(len(raw)))
        self.end_headers()
        self.wfile.write(raw)

    def do_POST(self):
        payload = json.loads(self.rfile.read(int(self.headers["Content-Length"])))
        for name, svg in payload.items():
            out = DIAGRAMS / f"generation-pipeline-{name}.svg"
            out.write_text(svg, encoding="utf-8", newline="\n")
            print(f"wrote {out.relative_to(ROOT)} ({len(svg)} bytes)")
        msg = b"written"
        self.send_response(200)
        self.send_header("Content-Length", str(len(msg)))
        self.end_headers()
        self.wfile.write(msg)
        Handler.written.set()


def main():
    with socketserver.TCPServer(("127.0.0.1", PORT), Handler) as srv:
        url = f"http://127.0.0.1:{PORT}/"
        print(f"open {url} in a browser to render")
        # Not a daemon: the POST handler is still writing the reply when `written` is
        # set, and tearing the interpreter down under it prints a fatal-looking stack
        # trace on an otherwise successful run.
        server = threading.Thread(target=srv.serve_forever)
        server.start()
        try:
            if "--no-open" not in sys.argv:
                webbrowser.open(url)
            ok = Handler.written.wait(timeout=180)
        finally:
            srv.shutdown()
            server.join()
        if not ok:
            print("timed out waiting for a render", file=sys.stderr)
            return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
