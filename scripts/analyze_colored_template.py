#!/usr/bin/env python3
"""Analyze turquoise/red colored runs in a Word template."""

import sys
import zipfile
from pathlib import Path
from xml.etree import ElementTree as ET

W_NS = "http://schemas.openxmlformats.org/wordprocessingml/2006/main"
NS = {"w": W_NS}


def analyze(path: Path) -> None:
    with zipfile.ZipFile(path) as zf:
        xml = zf.read("word/document.xml")

    root = ET.fromstring(xml)
    colors: set[str] = set()
    entries: list[tuple[list[tuple[str | None, str]], str]] = []

    for para in root.iter(f"[{W_NS]}p"):
        parts: list[tuple[str | None, str]] = []
        for run in para.iter(f"[{W_NS]}r"):
            rpr = run.find("w:rPr", NS)
            color = None
            if rpr is not None:
                color_el = rpr.find("w:color", NS)
                if color_el is not None:
                    color = color_el.get(f"[{W_NS]}val") or color_el.get("val")

            texts = [
                (t.text or "")
                for t in run.iter(f"[{W_NS]}t")
            ]
            text = "".join(texts)
            if text:
                parts.append((color, text))
                if color:
                    colors.add(color.upper())

        if parts and any(c for c, _ in parts):
            entries.append((parts, "".join(t for _, t in parts)))

    print(f"File: {path}")
    print(f"Unique colors ({len(colors)}): {sorted(colors)}")
    print("--- Colored paragraphs ---")
    for i, (parts, line) in enumerate(entries, 1):
        detail = " | ".join(
            f"{(c or 'default').upper()}:{text!r}" for c, text in parts if text.strip()
        )
        print(f"{i:3}. {detail}")
    print(f"Total colored paragraphs: {len(entries)}")


if __name__ == "__main__":
    target = Path(sys.argv[1]) if len(sys.argv) > 1 else Path(__file__).resolve().parent.parent / "templates" / "source_termination_legal_fire_alarm.docx"
    analyze(target)
