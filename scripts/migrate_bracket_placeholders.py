# -*- coding: utf-8 -*-
"""Migrate templates/*.docx placeholders [key] -> {{key}} for known keys."""
from __future__ import annotations

import re
import shutil
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
TEMPLATES = ROOT / "templates"
BACKUP = TEMPLATES / "_backup_brackets_pre_1.5.4"

# Keys from ClientData.ToPlaceholderMap + common EN variants in old templates
WHITELIST = {
    "client_full_name", "фио",
    "passport_series", "серия_документа",
    "passport_number", "номер_документа",
    "passport_issued_by", "кем_выдан",
    "passport_issue_date", "дата_выдачи",
    "passport_dept_code", "код_документа",
    "registration_address", "адрес_регистрации",
    "inn", "инн",
    "ogrnip", "огрнип",
    "ip_name", "наименование_ип",
    "company_name", "наименование_орг",
    "ogrn", "огрн",
    "kpp", "кпп",
    "legal_address", "юр_адрес",
    "legal_address_line2", "юр_адрес_2",
    "legal_address_line3", "юр_адрес_3",
    "legal_address_line4", "юр_адрес_4",
    "director_title", "должность",
    "director_full_name", "фио_подписанта",
    "director_representation", "должность_и_фио",
    "authority_basis", "основание",
    "bank_account", "расчетный_счет",
    "bank_bik", "бик",
    "bank_name", "банк",
    "correspondent_account", "корр_счет",
    "phone", "телефон",
    "email", "эл_почта",
    "contract_number", "номер_договора",
    "contract_date", "дата_договора",
    "contract_reference", "номер_и_дата_договора",
    "agreement_date", "дата_соглашения",
    "termination_date", "дата_расторжения",
    "contract_last_day", "последний_день",
    "contract_clause", "пункт_договора",
    "debt_amount", "сумма_задолженности",
    "object_address", "адрес_объекта",
    "contract_amount", "сумма_договора",
}

BRACKET_RE = re.compile(r"\[([A-Za-zА-Яа-яЁё0-9_]+)\]")


def migrate_xml(xml: str) -> tuple[str, int]:
    count = 0

    def repl(m: re.Match[str]) -> str:
        nonlocal count
        key = m.group(1)
        if key not in WHITELIST and key.lower() not in WHITELIST:
            # try case-insensitive match against whitelist
            for w in WHITELIST:
                if w.lower() == key.lower():
                    count += 1
                    return "{{" + w + "}}"
            return m.group(0)
        count += 1
        return "{{" + key + "}}"

    return BRACKET_RE.sub(repl, xml), count


def migrate_docx(path: Path) -> int:
    import zipfile
    from io import BytesIO

    buf = BytesIO()
    total = 0
    with zipfile.ZipFile(path, "r") as zin, zipfile.ZipFile(buf, "w", zipfile.ZIP_DEFLATED) as zout:
        for info in zin.infolist():
            data = zin.read(info.filename)
            name = info.filename
            if name == "word/document.xml" or name.startswith("word/header") or name.startswith("word/footer"):
                if name.endswith(".xml"):
                    text = data.decode("utf-8")
                    new_text, n = migrate_xml(text)
                    total += n
                    data = new_text.encode("utf-8")
            zout.writestr(info, data)
    path.write_bytes(buf.getvalue())
    return total


def main() -> None:
    BACKUP.mkdir(parents=True, exist_ok=True)
    files = sorted(TEMPLATES.glob("*.docx"))
    print(f"Found {len(files)} templates")
    for path in files:
        dest = BACKUP / path.name
        if not dest.exists():
            shutil.copy2(path, dest)
        n = migrate_docx(path)
        print(f"{path.name}: replaced {n}")


if __name__ == "__main__":
    main()
