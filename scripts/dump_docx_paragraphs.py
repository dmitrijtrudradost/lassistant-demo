#!/usr/bin/env python3
import sys
import zipfile
from pathlib import Path
from xml.etree import ElementTree as ET

W = "{http://schemas.openxmlformats.org/wordprocessingml/2006/main}"
path = Path(sys.argv[1]) if len(sys.argv) > 1 else Path(__file__).resolve().parent.parent / "templates" / "termination_legal_fire_alarm.docx"
with zipfile.ZipFile(path) as z:
    root = ET.fromstring(z.read("word/document.xml"))
for i, para in enumerate(root.iter(f"{W}p"), 1):
    text = "".join((t.text or "") for t in para.iter(f"{W}t"))
    if text.strip():
        print(f"P{i}: {text}")
