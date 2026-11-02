#!/usr/bin/env python3
"""Verify agreement date paragraph layout in template and generated docx."""

import subprocess
import sys
import tempfile
import zipfile
from pathlib import Path
from xml.etree import ElementTree as ET

W = "{http://schemas.openxmlformats.org/wordprocessingml/2006/main}"


def inspect_para(path: Path, label: str) -> bool:
    root = ET.fromstring(zipfile.ZipFile(path).read("word/document.xml"))
    paras = list(root.iter(W + "p"))
    for idx, para in enumerate(paras):
        text = "".join(t.text or "" for t in para.iter(W + "t"))
        if "agreement_date" in text or "Санкт" in text and "2025" in text or "июля" in text:
            if "Санкт" not in text and "agreement_date" not in text:
                continue
            print(f"--- {label} para[{idx}] ---")
            print("  text:", text[:100])
            runs = para.findall(W + "r")
            city_idx = tab_idx = date_idx = -1
            for ri, run in enumerate(runs):
                rt = "".join(t.text or "" for t in run.iter(W + "t"))
                has_tab = run.find(W + "tab") is not None
                bold = run.find(".//" + W + "b") is not None
                if rt.strip() or has_tab:
                    print(f"  run{ri}: tab={has_tab} bold={bold} text={rt!r}")
                if "Санкт" in rt or rt.strip().startswith("г."):
                    city_idx = ri
                if has_tab:
                    tab_idx = ri
                if "agreement_date" in rt or "июля" in rt or "2025" in rt:
                    if date_idx < 0 or "июля" in rt:
                        date_idx = ri
            ok = city_idx >= 0 and tab_idx > city_idx and date_idx > tab_idx
            print("  layout ok:", ok, f"(city={city_idx}, tab={tab_idx}, date={date_idx})")
            return ok
    print(f"--- {label}: agreement paragraph NOT FOUND ---")
    return False


def main() -> int:
    root = Path(__file__).resolve().parent.parent
    template = root / "templates" / "termination_legal_fire_alarm.docx"
    desktop_template = Path(r"templates/termination_legal_fire_alarm.docx")

    print("TEMPLATE CHECK")
    t_ok = inspect_para(template, "project template")
    if desktop_template.exists():
        inspect_para(desktop_template, "desktop template")

    out = Path(tempfile.gettempdir()) / "lassistant_verify_agreement.docx"
    smoke = root / "src" / "Lassistant.SmokeTest"
    subprocess.run(
        ["dotnet", "run", "-c", "Release", "--project", str(smoke), "--", "--gen-termination", str(out)],
        cwd=root / "src",
        check=True,
    )

    print("\nGENERATED CHECK")
    g_ok = inspect_para(out, "generated")
    return 0 if t_ok and g_ok else 1


if __name__ == "__main__":
    sys.exit(main())
