#!/usr/bin/env python3
import zipfile
from pathlib import Path
from xml.etree import ElementTree as ET

W = "{http://schemas.openxmlformats.org/wordprocessingml/2006/main}"
path = Path(__file__).resolve().parent.parent / "templates" / "source_termination_legal_fire_alarm.docx"
out = Path(__file__).resolve().parent.parent / "templates" / "_template_analysis.txt"

with zipfile.ZipFile(path) as zf:
    xml = zf.read("word/document.xml")

root = ET.fromstring(xml)
lines = []
for para in root.iter(f"{W}p"):
    parts = []
    for run in para.iter(f"{W}r"):
        rpr = run.find(f"{W}rPr")
        kind = "def"
        if rpr is not None:
            if rpr.find(f"{W}highlight", {"w": W[1:-1]}) is not None or rpr.find(f"{W}highlight") is not None:
                hl = rpr.find(f"{W}highlight")
                if hl is not None and (hl.get(f"{W}val") or "").lower() == "cyan":
                    kind = "CYAN"
            color_el = rpr.find(f"{W}color")
            if color_el is not None and (color_el.get(f"{W}val") or "").upper() == "FF0000":
                kind = "RED"
        text = "".join((t.text or "") for t in run.iter(f"{W}t"))
        if text:
            parts.append((kind, text))
    if parts:
        lines.append(parts)

with out.open("w", encoding="utf-8") as f:
    for i, parts in enumerate(lines, 1):
        f.write(f"--- P{i} ---\n")
        for kind, text in parts:
            f.write(f"  [{kind}] {text}\n")
        f.write("\n")
print(out)
