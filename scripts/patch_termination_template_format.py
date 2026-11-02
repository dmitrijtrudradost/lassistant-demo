#!/usr/bin/env python3
"""Ensure date/signature formatting in termination_legal_fire_alarm.docx."""

from __future__ import annotations

import re
import shutil
import sys
import zipfile
from pathlib import Path
from xml.etree import ElementTree as ET

W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main"
NS = {"w": W}

PLACEHOLDER_RE = re.compile(r"\{\{([^}]+)\}\}")


def q(tag: str) -> str:
    return f"[{W]}{tag}"


def para_text(paragraph: ET.Element) -> str:
    return "".join((t.text or "") for t in paragraph.iter(q("t")))


def ensure_bold(run: ET.Element) -> None:
    rpr = run.find("w:rPr", NS)
    if rpr is None:
        rpr = ET.SubElement(run, q("rPr"))
    if rpr.find("w:b", NS) is None:
        ET.SubElement(rpr, q("b"))
    if rpr.find("w:bCs", NS) is None:
        ET.SubElement(rpr, q("bCs"))


def ensure_right_tab(paragraph: ET.Element) -> None:
    ppr = paragraph.find("w:pPr", NS)
    if ppr is None:
        ppr = ET.SubElement(paragraph, q("pPr"))
    tabs = ppr.find("w:tabs", NS)
    if tabs is None:
        tabs = ET.SubElement(ppr, q("tabs"))
    if not tabs.findall("w:tab", NS):
        tab = ET.SubElement(tabs, q("tab"))
        tab.set(q("val"), "right")
        tab.set(q("pos"), "9350")


def _merge_one_split_placeholder(paragraph: ET.Element) -> bool:
    """Склеивает один [key], разбитый на несколько run-ов. Возвращает True, если был слит."""
    runs = list(paragraph.findall(q("r")))
    if not runs:
        return False

    run_texts: list[tuple[ET.Element, str]] = []
    for run in runs:
        text = "".join((t.text or "") for t in run.iter(q("t")))
        run_texts.append((run, text))

    full = "".join(t for _, t in run_texts)

    for match in PLACEHOLDER_RE.finditer(full):
        start, end = match.span()
        pos = 0
        first_run: ET.Element | None = None
        last_run: ET.Element | None = None
        prefix = ""
        suffix = ""

        for run, text in run_texts:
            run_start = pos
            run_end = pos + len(text)
            pos = run_end

            if run_end <= start or run_start >= end:
                continue

            if first_run is None:
                first_run = run
                prefix = text[: max(0, start - run_start)]

            if run_end >= end:
                last_run = run
                suffix = text[end - run_start :]
                break

            last_run = run

        if first_run is None or last_run is None or first_run is last_run:
            continue

        placeholder = match.group(0)
        break
    else:
        return False

    set_run_text(first_run, prefix + placeholder + (suffix if first_run is last_run else ""))
    ensure_bold(first_run)

    clearing = False
    for run, _text in run_texts:
        if run is first_run:
            clearing = True
            if run is last_run:
                break
            continue
        if clearing:
            if run is last_run:
                set_run_text(run, suffix)
                ensure_bold(run)
                break
            set_run_text(run, "")

    return True


def merge_split_placeholders(paragraph: ET.Element) -> None:
    """Склеивает все [key], разбитые Word-ом на несколько run-ов."""
    while _merge_one_split_placeholder(paragraph):
        pass


def set_run_text(run: ET.Element, text: str) -> None:
    texts = run.findall(q("t"))
    if not texts:
        t = ET.SubElement(run, q("t"))
        t.text = text
        if text.startswith(" ") or text.endswith(" "):
            t.set("{http://www.w3.org/XML/1998/namespace}space", "preserve")
        return

    texts[0].text = text
    if text.startswith(" ") or text.endswith(" "):
        texts[0].set("{http://www.w3.org/XML/1998/namespace}space", "preserve")
    for extra in texts[1:]:
        extra.text = ""


def ensure_agreement_date_layout(paragraph: ET.Element) -> None:
    """г. Город [TAB] [agreement_date] — город слева, дата справа."""
    ensure_right_tab(paragraph)
    merge_split_placeholders(paragraph)

    text = para_text(paragraph)
    if "agreement_date" not in text and "дата_соглашения" not in text:
        return

    runs = list(paragraph.findall(q("r")))
    if not runs:
        return

    # Найти run с плейсхолдером даты
    placeholder_run: ET.Element | None = None
    city_run: ET.Element | None = None
    for run in runs:
        rt = "".join((t.text or "") for t in run.iter(q("t")))
        if "agreement_date" in rt or "дата_соглашения" in rt:
            placeholder_run = run
            ensure_bold(run)
        elif "Санкт" in rt or rt.strip().startswith("г."):
            city_run = run

    if placeholder_run is None:
        return

    # Проверить, есть ли tab run между городом и плейсхолдером
    if city_run is not None:
        city_idx = runs.index(city_run)
        ph_idx = runs.index(placeholder_run)
        has_tab_between = any(
            runs[i].find(q("tab")) is not None for i in range(city_idx + 1, ph_idx)
        )
        if not has_tab_between and ph_idx == city_idx + 1:
            tab_run = ET.Element(q("r"))
            ET.SubElement(tab_run, q("tab"))
            paragraph.insert(ph_idx, tab_run)
            runs = list(paragraph.findall(q("r")))
            placeholder_run = runs[ph_idx + 1]

    ensure_bold(placeholder_run)


def patch_paragraph(paragraph: ET.Element, placeholders: set[str]) -> None:
    for run in paragraph.iter(q("r")):
        text = "".join((t.text or "") for t in run.iter(q("t")))
        if any(ph in text for ph in placeholders):
            ensure_bold(run)


def find_paragraph(root: ET.Element, needle: str) -> ET.Element | None:
    for para in root.iter(q("p")):
        if needle in para_text(para):
            return para
    return None


def patch_docx(path: Path) -> None:
    temp = path.with_suffix(".patch.zip")
    shutil.copy2(path, temp)

    with zipfile.ZipFile(temp, "r") as zin:
        names = zin.namelist()
        files = {name: zin.read(name) for name in names}

    root = ET.fromstring(files["word/document.xml"])

    for para in root.iter(q("p")):
        merge_split_placeholders(para)
        text = para_text(para)
        if "(да (далее" in text:
            for t in para.iter(q("t")):
                if t.text:
                    t.text = t.text.replace("(да (далее", "(далее")

    agreement_para = find_paragraph(root, "[agreement_date]")
    if agreement_para is None:
        agreement_para = find_paragraph(root, "[дата_соглашения]")
    if agreement_para is not None:
        ensure_agreement_date_layout(agreement_para)

    termination_para = find_paragraph(root, "[termination_date]")
    if termination_para is None:
        termination_para = find_paragraph(root, "[дата_расторжения]")
    if termination_para is not None:
        merge_split_placeholders(termination_para)
        patch_paragraph(termination_para, {"[termination_date]", "[дата_расторжения]"})

    last_day_para = find_paragraph(root, "[contract_last_day]")
    if last_day_para is None:
        last_day_para = find_paragraph(root, "[последний_день]")
    if last_day_para is not None:
        merge_split_placeholders(last_day_para)
        patch_paragraph(last_day_para, {"[contract_last_day]", "[последний_день]"})

    files["word/document.xml"] = ET.tostring(root, encoding="utf-8", xml_declaration=True)

    with zipfile.ZipFile(path, "w", zipfile.ZIP_DEFLATED) as zout:
        for name in names:
            zout.writestr(name, files[name])

    temp.unlink(missing_ok=True)


def main() -> None:
    root = Path(__file__).resolve().parent.parent
    targets = [root / "templates" / "termination_legal_fire_alarm.docx"]
    if len(sys.argv) > 1:
        targets = [Path(p) for p in sys.argv[1:]]

    for target in targets:
        if not target.exists():
            print("Skip (not found):", target)
            continue
        patch_docx(target)
        print("Patched:", target)


if __name__ == "__main__":
    main()
