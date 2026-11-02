# -*- coding: utf-8 -*-
"""Convert docs/INSTALL_v1.5.3.md -> HTML + PDF (Edge headless)."""
from __future__ import annotations

import html
import re
import subprocess
import time
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
MD = ROOT / "docs" / "INSTALL_v1.5.3.md"
HTML_PATH = ROOT / "docs" / "INSTALL_v1.5.3.html"
PDF_PATH = ROOT / "docs" / "INSTALL_v1.5.3.pdf"
EDGE = Path(r"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe")


def md_inline(text: str) -> str:
    text = html.escape(text)
    text = re.sub(r"\*\*(.+?)\*\*", r"<strong>\1</strong>", text)
    text = re.sub(r"`([^`]+)`", r"<code>\1</code>", text)
    text = re.sub(r"\[([^\]]+)\]\(([^)]+)\)", r'<a href="\2">\1</a>', text)
    return text


def convert(md: str) -> str:
    out: list[str] = [
        "<!DOCTYPE html><html><head><meta charset='utf-8'>",
        "<title>Установка Lassistant 1.5.3</title>",
        "<style>",
        "@page { margin: 16mm; }",
        "body{font-family:'Segoe UI',Arial,sans-serif;font-size:11pt;"
        "line-height:1.45;color:#222;}",
        "h1{font-size:20pt;margin:0 0 12px;}",
        "h2{font-size:14pt;margin:20px 0 8px;border-bottom:1px solid #ccc;"
        "padding-bottom:4px;}",
        "h3{font-size:12pt;margin:14px 0 6px;}",
        "table{border-collapse:collapse;width:100%;margin:8px 0 14px;}",
        "th,td{border:1px solid #bbb;padding:6px 8px;text-align:left;"
        "vertical-align:top;}",
        "th{background:#f3f3f3;}",
        "code,pre{font-family:Consolas,monospace;font-size:9.5pt;}",
        "pre{background:#f6f6f6;border:1px solid #ddd;padding:10px;}",
        "ul{margin:6px 0 10px 22px;} li{margin:3px 0;}",
        "hr{border:none;border-top:1px solid #ccc;margin:16px 0;}",
        "p{margin:6px 0;}",
        "</style></head><body>",
    ]

    in_code = False
    table_rows: list[list[str]] = []

    def flush_table() -> None:
        nonlocal table_rows
        if not table_rows:
            return
        out.append("<table>")
        for i, row in enumerate(table_rows):
            # skip markdown separator row
            if i == 1 and all(set(c.strip()) <= set("-: ") for c in row):
                continue
            tag = "th" if i == 0 else "td"
            cells = "".join(f"<{tag}>{md_inline(c.strip())}</{tag}>" for c in row)
            out.append(f"<tr>{cells}</tr>")
        out.append("</table>")
        table_rows = []

    for line in md.splitlines():
        if line.strip().startswith("```"):
            if in_code:
                out.append("</pre>")
                in_code = False
            else:
                flush_table()
                out.append("<pre>")
                in_code = True
            continue

        if in_code:
            out.append(html.escape(line))
            continue

        if line.startswith("|"):
            cells = [c for c in line.strip().strip("|").split("|")]
            table_rows.append(cells)
            continue

        flush_table()

        if line.strip() == "---":
            out.append("<hr>")
            continue

        m = re.match(r"^(#{1,3})\s+(.*)$", line)
        if m:
            level = len(m.group(1))
            out.append(f"<h{level}>{md_inline(m.group(2))}</h{level}>")
            continue

        if re.match(r"^- \[[ xX]\] ", line):
            checked = line[3].lower() == "x"
            box = "☑" if checked else "☐"
            out.append(f"<div>{box} {md_inline(line[6:].strip())}</div>")
            continue

        if line.startswith("- "):
            out.append(f"<ul><li>{md_inline(line[2:])}</li></ul>")
            continue

        if not line.strip():
            continue

        out.append(f"<p>{md_inline(line)}</p>")

    flush_table()
    out.append("</body></html>")
    return "\n".join(out)


def main() -> None:
    md = MD.read_text(encoding="utf-8")
    HTML_PATH.write_text(convert(md), encoding="utf-8")
    print(f"HTML: {HTML_PATH}")

    if not EDGE.exists():
        raise SystemExit(f"Edge not found: {EDGE}")

    uri = HTML_PATH.resolve().as_uri()
    if PDF_PATH.exists():
        PDF_PATH.unlink()

    subprocess.run(
        [
            str(EDGE),
            "--headless",
            "--disable-gpu",
            "--no-pdf-header-footer",
            f"--print-to-pdf={PDF_PATH}",
            uri,
        ],
        check=False,
    )

    # Edge may finish asynchronously
    for _ in range(20):
        if PDF_PATH.exists() and PDF_PATH.stat().st_size > 1000:
            break
        time.sleep(0.25)

    if not PDF_PATH.exists() or PDF_PATH.stat().st_size < 1000:
        raise SystemExit("PDF was not created")

    print(f"PDF: {PDF_PATH} ({PDF_PATH.stat().st_size} bytes)")


if __name__ == "__main__":
    main()
