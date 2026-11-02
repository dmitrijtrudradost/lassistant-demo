#!/usr/bin/env python3
import json
import re
import sys
import zipfile
from pathlib import Path
from xml.etree import ElementTree as ET

W = "{http://schemas.openxmlformats.org/wordprocessingml/2006/main}"


def main() -> int:
    docx = Path(sys.argv[1])
    rules_path = Path(sys.argv[2])
    out = Path(sys.argv[3]) if len(sys.argv) > 3 else None

    rules = json.loads(rules_path.read_text(encoding="utf-8"))

    with zipfile.ZipFile(docx) as zf:
        root = ET.fromstring(zf.read("word/document.xml"))

    paras: list[tuple[int, str, str]] = []
    runs: list[str] = []

    for pi, para in enumerate(root.iter(f"{W}p"), 1):
        parts: list[tuple[str | None, str]] = []
        for run in para.iter(f"{W}r"):
            rpr = run.find(f"{W}rPr")
            hl = None
            if rpr is not None:
                h = rpr.find(f"{W}highlight")
                if h is not None:
                    hl = (h.get(f"{W}val") or "").lower()
            t = "".join((x.text or "") for x in run.iter(f"{W}t"))
            if t:
                parts.append((hl, t))
        if any(h == "yellow" for h, _ in parts if h):
            y = "".join(t for h, t in parts if h == "yellow")
            full = "".join(t for _, t in parts)
            paras.append((pi, y, full))

    for run in root.iter(f"{W}r"):
        rpr = run.find(f"{W}rPr")
        is_y = (
            rpr is not None
            and rpr.find(f"{W}highlight") is not None
            and (rpr.find(f"{W}highlight").get(f"{W}val") or "").lower() == "yellow"
        )
        t = "".join((x.text or "") for x in run.iter(f"{W}t"))
        if is_y and t.strip():
            runs.append(t.strip())

    lines: list[str] = []
    lines.append("=== PARAGRAPHS WITH YELLOW ===")
    for pi, y, full in paras:
        lines.append(f"P{pi}")
        lines.append(f"YELLOW: {y}")
        lines.append(f"FULL: {full}")
        lines.append("")

    lines.append("=== YELLOW RUNS ===")
    ok = miss = 0
    for r in runs:
        if r in ("[", "м") and len(r) <= 2:
            continue
        matched = [
            c
            for c, rr in rules["categories"].items()
            if any(re.search(p, r, re.I) for p in rr["patterns"])
        ]
        tag = matched[0] if matched else "MISS"
        lines.append(f"[{tag}] {r}")
        if matched:
            ok += 1
        else:
            miss += 1

    lines.append(f"\nCovered: {ok}/{ok + miss}")
    text = "\n".join(lines)

    if out:
        out.write_text(text, encoding="utf-8")
        print(out)
    else:
        print(text)

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
