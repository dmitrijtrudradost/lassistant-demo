#!/usr/bin/env python3
"""Добавляет [legal_address_line3] в шаблон termination_legal_fire_alarm.docx."""

from __future__ import annotations

import copy
import shutil
import sys
import zipfile
from pathlib import Path
from xml.etree import ElementTree as ET

W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main"
NS = {"w": W}


def q(tag: str) -> str:
    return f"[{W]}{tag}"


def para_text(paragraph: ET.Element) -> str:
    return "".join((t.text or "") for t in paragraph.iter(q("t")))


def build_parent_map(root: ET.Element) -> dict[ET.Element, ET.Element]:
    return {child: parent for parent in root.iter() for child in parent}


def make_line3_paragraph(template_para: ET.Element) -> ET.Element:
    para = copy.deepcopy(template_para)
    for t in para.iter(q("t")):
        if not t.text:
            continue
        t.text = (
            t.text.replace("legal_address_line2", "legal_address_line3")
            .replace("юр_адрес_2", "юр_адрес_3")
        )
    return para


def patch_docx(path: Path) -> bool:
    with zipfile.ZipFile(path) as z:
        xml = z.read("word/document.xml").decode("utf-8")

    if "legal_address_line3" in xml or "юр_адрес_3" in xml:
        print("Already has line3:", path)
        return False

    temp = path.with_suffix(".line3.zip")
    shutil.copy2(path, temp)

    with zipfile.ZipFile(temp, "r") as zin:
        names = zin.namelist()
        files = {name: zin.read(name) for name in names}

    root = ET.fromstring(files["word/document.xml"])
    parents = build_parent_map(root)

    for para in root.iter(q("p")):
        text = para_text(para)
        if "legal_address_line2" not in text and "юр_адрес_2" not in text:
            continue
        if "line3" in text or "адрес_3" in text:
            continue

        parent = parents.get(para)
        if parent is None:
            raise RuntimeError(f"Parent not found for line2 paragraph in {path}")

        siblings = list(parent)
        idx = siblings.index(para)
        parent.insert(idx + 1, make_line3_paragraph(para))

        files["word/document.xml"] = ET.tostring(root, encoding="utf-8", xml_declaration=True)

        with zipfile.ZipFile(path, "w", zipfile.ZIP_DEFLATED) as zout:
            for name in names:
                zout.writestr(name, files[name])

        temp.unlink(missing_ok=True)
        print("Added legal_address_line3:", path)
        return True

    temp.unlink(missing_ok=True)
    raise RuntimeError(f"legal_address_line2 paragraph not found in {path}")


def main() -> None:
    root = Path(__file__).resolve().parent.parent
    targets = [
        root / "templates" / "termination_legal_fire_alarm.docx",
        root / "templates" / "termination_legal_fire_alarm_ru.docx",
        Path(r"templates/termination_legal_fire_alarm.docx"),
    ]
    if len(sys.argv) > 1:
        targets = [Path(p) for p in sys.argv[1:]]

    for target in targets:
        if not target.exists():
            print("Skip (not found):", target)
            continue
        patch_docx(target)


if __name__ == "__main__":
    main()
