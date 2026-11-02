#!/usr/bin/env python3
"""Generate Excel client data template without external dependencies."""

import zipfile
from pathlib import Path

HEADERS = [
    "ФИО", "Серия", "Номер", "Кем выдан", "Дата выдачи", "Код подразделения",
    "Адрес регистрации", "ИНН", "ОГРНИП", "Наименование ИП",
    "Наименование организации", "ОГРН", "КПП", "Юридический адрес",
    "Должность подписанта", "ФИО подписанта", "Основание полномочий",
    "Р/с", "БИК", "Банк", "К/с", "Телефон", "Email",
    "Номер договора", "Дата договора", "Адрес объекта", "Сумма договора",
]

def inn_individual(first10: str) -> str:
    """Контрольные цифры ИНН физлица/ИП (12 знаков)."""
    coeff11 = [7, 2, 4, 10, 3, 5, 9, 4, 6, 8]
    coeff12 = [3, 7, 2, 4, 10, 3, 5, 9, 4, 6, 8]
    digits = [int(ch) for ch in first10]
    check11 = sum(digits[i] * coeff11[i] for i in range(10)) % 11 % 10
    with11 = digits + [check11]
    check12 = sum(with11[i] * coeff12[i] for i in range(11)) % 11 % 10
    return first10 + str(check11) + str(check12)


def ogrnip(first14: str) -> str:
    """Контрольная цифра ОГРНИП (15 знаков, начинается с 3)."""
    check = int(first14) % 13 % 10
    return first14 + str(check)


# Тестовые, но валидные по контрольным суммам номера для примера в шаблоне.
def ogrn(first12: str) -> str:
    check = int(first12) % 11 % 10
    return first12 + str(check)


EXAMPLE_INN_IP = inn_individual("7707083893")
EXAMPLE_OGRNIP = ogrnip("31777490000001")
EXAMPLE_INN_LEGAL = "7707083893"  # 10-digit INN for legal entity
EXAMPLE_OGRN = ogrn("102770013219")

EXAMPLE_IP = [
    "Иванов Иван Иванович", "4510", "123456", "ОВД района", "01.01.2020", "770-001",
    "г. Москва, ул. Примерная, д. 1", EXAMPLE_INN_IP, EXAMPLE_OGRNIP, "ИП Иванов И.И.",
    "", "", "", "", "", "", "",
    "40802810000000000000", "044525225", "ПАО Банк", "30101810400000000225",
    "+79001234567", "example@mail.ru", "", "", "", "",
]

EXAMPLE_LEGAL = [
    "", "", "", "", "", "", "",
    EXAMPLE_INN_LEGAL, "", "",
    "ООО Пример", EXAMPLE_OGRN, "770701001", "г. Москва, ул. Деловая, д. 10",
    "Генеральный директор", "Петров Пётр Петрович", "Устава",
    "40702810000000000000", "044525225", "ПАО Банк", "30101810400000000225",
    "+79007654321", "legal@example.ru", "Д-100/25", "01.06.2025", "г. Москва, ул. Объектная, 5", "150000",
]

CONTENT_TYPES = """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
  <Default Extension="xml" ContentType="application/xml"/>
  <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
  <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
  <Override PartName="/xl/sharedStrings.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sharedStrings+xml"/>
</Types>"""

RELS = """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
</Relationships>"""

WORKBOOK_RELS = """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
  <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/sharedStrings" Target="sharedStrings.xml"/>
</Relationships>"""

WORKBOOK = """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
          xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
  <sheets>
    <sheet name="Клиенты" sheetId="1" r:id="rId1"/>
  </sheets>
</workbook>"""


def col_name(index: int) -> str:
    result = ""
    while index >= 0:
        result = chr(65 + index % 26) + result
        index = index // 26 - 1
    return result


def escape_xml(text: str) -> str:
    return text.replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;")


def build_shared_strings(rows: list[list[str]]) -> tuple[str, dict[tuple[int, int], int]]:
    unique: list[str] = []
    index_map: dict[str, int] = {}

    def get_index(value: str) -> int:
        if value not in index_map:
            index_map[value] = len(unique)
            unique.append(value)
        return index_map[value]

    cell_to_si: dict[tuple[int, int], int] = {}
    for r, row in enumerate(rows):
        for c, value in enumerate(row):
            cell_to_si[(r, c)] = get_index(value)

    items = "".join(f"<si><t>{escape_xml(s)}</t></si>" for s in unique)
    xml = f"""<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<sst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" count="{len(unique)}" uniqueCount="{len(unique)}">
{items}
</sst>"""
    return xml, cell_to_si


def build_sheet(rows: list[list[str]], cell_to_si: dict[tuple[int, int], int]) -> str:
    row_xml = []
    for r, row in enumerate(rows):
        cells = []
        for c, _ in enumerate(row):
            ref = f"{col_name(c)}{r + 1}"
            si = cell_to_si[(r, c)]
            cells.append(f'<c r="{ref}" t="s"><v>{si}</v></c>')
        row_xml.append(f'<row r="{r + 1}">' + "".join(cells) + "</row>")

    return f"""<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
  <sheetData>
    {"".join(row_xml)}
  </sheetData>
</worksheet>"""


def write_xlsx(path: Path, rows: list[list[str]]) -> None:
    shared_strings, cell_to_si = build_shared_strings(rows)
    sheet = build_sheet(rows, cell_to_si)
    path.parent.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(path, "w", zipfile.ZIP_DEFLATED) as zf:
        zf.writestr("[Content_Types].xml", CONTENT_TYPES)
        zf.writestr("_rels/.rels", RELS)
        zf.writestr("xl/_rels/workbook.xml.rels", WORKBOOK_RELS)
        zf.writestr("xl/workbook.xml", WORKBOOK)
        zf.writestr("xl/sharedStrings.xml", shared_strings)
        zf.writestr("xl/worksheets/sheet1.xml", sheet)


if __name__ == "__main__":
    root = Path(__file__).resolve().parent.parent
    path = root / "templates" / "client_data_template.xlsx"
    write_xlsx(path, [HEADERS, EXAMPLE_IP, EXAMPLE_LEGAL])
    print("Excel template created:", path)
