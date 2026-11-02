#!/usr/bin/env python3
"""Generate 24 .docx template stubs with {{placeholders}} from document-catalog.json."""

import json
import zipfile
from pathlib import Path

CONTENT_TYPES = """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
  <Default Extension="xml" ContentType="application/xml"/>
  <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
</Types>"""

RELS = """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
</Relationships>"""

DOC_RELS = """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"/>"""

COMMON_PASSPORT = [
    "ФИО: {{client_full_name}}",
    "Паспорт: серия {{passport_series}} № {{passport_number}}",
    "Выдан: {{passport_issued_by}}, {{passport_issue_date}}, код {{passport_dept_code}}",
    "Адрес регистрации: {{registration_address}}",
]

COMMON_IP = [
    "Наименование ИП: {{ip_name}}",
    "ИНН: {{inn}}, ОГРНИП: {{ogrnip}}",
    *COMMON_PASSPORT,
]

COMMON_LEGAL = [
    "Организация: {{company_name}}",
    "ИНН: {{inn}}, ОГРН: {{ogrn}}, КПП: {{kpp}}",
    "Юридический адрес: {{legal_address}}",
    "Подписант: {{director_title}} {{director_full_name}}, {{authority_basis}}",
]

COMMON_BANK = [
    "Р/с: {{bank_account}}, БИК: {{bank_bik}}, банк: {{bank_name}}, к/с: {{correspondent_account}}",
]

COMMON_CONTACTS = [
    "Телефон: {{phone}}, Email: {{email}}",
]

COMMON_META = [
    "Договор № {{contract_number}} от {{contract_date}}",
    "Адрес объекта: {{object_address}}",
    "Сумма: {{contract_amount}}",
]


def escape_xml(text: str) -> str:
    return (
        text.replace("&", "&amp;")
        .replace("<", "&lt;")
        .replace(">", "&gt;")
        .replace('"', "&quot;")
    )


def paragraph(text: str) -> str:
    return f'<w:p><w:r><w:t xml:space="preserve">{escape_xml(text)}</w:t></w:r></w:p>'


def make_document(paragraphs: list[str]) -> str:
    body = "".join(paragraph(p) for p in paragraphs)
    return f"""<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
  <w:body>
    {body}
    <w:sectPr/>
  </w:body>
</w:document>"""


def write_docx(path: Path, paragraphs: list[str]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(path, "w", zipfile.ZIP_DEFLATED) as zf:
        zf.writestr("[Content_Types].xml", CONTENT_TYPES)
        zf.writestr("_rels/.rels", RELS)
        zf.writestr("word/_rels/document.xml.rels", DOC_RELS)
        zf.writestr("word/document.xml", make_document(paragraphs))


def build_paragraphs(entry: dict) -> list[str]:
    kind = entry["documentKind"]
    client = entry["clientType"]
    service = entry["serviceKind"]
    title = entry["displayName"]

    lines = [title.upper(), "", f"Тип клиента: {{client_type}}", ""]

    if client == "Individual":
        lines.extend(COMMON_PASSPORT)
        lines.extend(COMMON_CONTACTS)
    elif client == "IndividualEntrepreneur":
        lines.extend(COMMON_IP)
        lines.extend(COMMON_BANK)
        lines.extend(COMMON_CONTACTS)
    elif client == "LegalEntity":
        lines.extend(COMMON_LEGAL)
        lines.extend(COMMON_BANK)
        lines.extend(COMMON_CONTACTS)

    if kind in ("Addendum", "Termination"):
        lines.append("")
        lines.extend(COMMON_META)

    lines.extend(["", f"Услуга: {service}", "", "Текст документа — заготовка. Замените на финальный шаблон."])
    return lines


def main() -> None:
    root = Path(__file__).resolve().parent.parent
    catalog_path = root / "config" / "document-catalog.json"
    templates_dir = root / "templates"

    catalog = json.loads(catalog_path.read_text(encoding="utf-8"))
    created = 0
    skipped = 0

    for entry in catalog["documents"]:
        path = templates_dir / entry["templateFile"]
        if path.exists() and not entry.get("isStub", True):
            skipped += 1
            continue
        write_docx(path, build_paragraphs(entry))
        created += 1

    # Legacy aliases for backward compatibility
    legacy = {
        "contract_individual.docx": "contract_individual_montage.docx",
        "contract_ip.docx": "contract_ip_montage.docx",
    }
    for legacy_name, source_name in legacy.items():
        src = templates_dir / source_name
        dst = templates_dir / legacy_name
        if src.exists() and not dst.exists():
            dst.write_bytes(src.read_bytes())

    print(f"Templates: created {created}, skipped {skipped} in {templates_dir}")


if __name__ == "__main__":
    main()
