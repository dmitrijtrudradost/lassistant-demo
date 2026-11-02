#!/usr/bin/env python3
"""HTML-справочник плейсхолдеров для юриста (редактирование шаблонов Word)."""

from __future__ import annotations

import html
import json
import shutil
from pathlib import Path

# Соответствие английский ключ → русский ключ (как в ClientData.ToPlaceholderMap)
RU_ALIASES: dict[str, str] = {
    "client_full_name": "фио",
    "passport_series": "серия_документа",
    "passport_number": "номер_документа",
    "passport_issued_by": "кем_выдан",
    "passport_issue_date": "дата_выдачи",
    "passport_dept_code": "код_документа",
    "registration_address": "адрес_регистрации",
    "inn": "инн",
    "ogrnip": "огрнип",
    "ip_name": "наименование_ип",
    "company_name": "наименование_орг",
    "ogrn": "огрн",
    "kpp": "кпп",
    "legal_address": "юр_адрес",
    "legal_address_line2": "юр_адрес_2",
    "legal_address_line3": "юр_адрес_3",
    "legal_address_line4": "юр_адрес_4",
    "director_title": "должность",
    "director_full_name": "фио_подписанта",
    "director_representation": "должность_и_фио",
    "authority_basis": "основание",
    "bank_account": "расчетный_счет",
    "bank_bik": "бик",
    "bank_name": "банк",
    "correspondent_account": "корр_счет",
    "phone": "телефон",
    "email": "эл_почта",
    "contract_number": "номер_договора",
    "contract_date": "дата_договора",
    "contract_reference": "номер_и_дата_договора",
    "agreement_date": "дата_соглашения",
    "termination_date": "дата_расторжения",
    "contract_last_day": "последний_день",
    "contract_clause": "пункт_договора",
    "debt_amount": "сумма_задолженности",
    "object_address": "адрес_объекта",
    "contract_amount": "сумма_договора",
    "client_type": "тип_клиента",
}

# Дополнительные строки (нет отдельного поля в field-definitions)
EXTRA_ROWS: list[tuple[str, str, str, str]] = [
    ("Данные юрлица", "Юридический адрес (строка 2)", "legal_address_line2", "юр_адрес_2"),
    ("Данные юрлица", "Юридический адрес (строка 3)", "legal_address_line3", "юр_адрес_3"),
    ("Данные юрлица", "Юридический адрес (строка 4)", "legal_address_line4", "юр_адрес_4"),
    ("Данные юрлица", "Должность и ФИО в преамбуле (в лице …)", "director_representation", "должность_и_фио"),
    ("Прочее", "Тип клиента (физлицо / ИП / юрлицо)", "client_type", "тип_клиента"),
]

GROUP_ORDER = [
    "passport",
    "ip",
    "legal",
    "bank",
    "contacts",
    "contract_meta",
    "termination",
]


def ph(key: str) -> str:
    return "{{" + key + "}}"


def row_html(label: str, en: str, ru: str) -> str:
    el = html.escape(label)
    en_ph = html.escape(ph(en))
    ru_ph = html.escape(ph(ru))
    return f"""    <tr>
      <td>{el}</td>
      <td><code class="copy" title="Нажмите, чтобы выделить">{en_ph}</code></td>
      <td><code class="copy" title="Нажмите, чтобы выделить">{ru_ph}</code></td>
    </tr>
"""


def build_table_rows(config: dict) -> dict[str, str]:
    fields = config["fields"]
    groups = config["groups"]
    sections: dict[str, list[str]] = {g: [] for g in GROUP_ORDER}

    for gid in GROUP_ORDER:
        g = groups[gid]
        for fk in g["fields"]:
            f = fields[fk]
            en = f["placeholder"]
            ru = RU_ALIASES.get(en, "—")
            sections[gid].append(row_html(f["label"], en, ru))

    # extras by group label
    extra_by_group: dict[str, list[str]] = {}
    for group_label, label, en, ru in EXTRA_ROWS:
        extra_by_group.setdefault(group_label, []).append(row_html(label, en, ru))

    result = {}
    for gid in GROUP_ORDER:
        g = groups[gid]
        rows = sections[gid]
        for extra in extra_by_group.get(g["label"], []):
            rows.append(extra)
        result[g["label"]] = "".join(rows)

    for group_label, rows in extra_by_group.items():
        if group_label not in result:
            result[group_label] = "".join(rows)

    return result


def build_html(config: dict) -> str:
    sections = build_table_rows(config)
    section_html = []
    for gid in GROUP_ORDER:
        title = config["groups"][gid]["label"]
        if title not in sections or not sections[title].strip():
            continue
        section_html.append(f"""
  <h2>{html.escape(title)}</h2>
  <table>
    <thead>
      <tr>
        <th>Поле в программе</th>
        <th>Ключ в шаблоне (англ.)</th>
        <th>Ключ в шаблоне (рус.)</th>
      </tr>
    </thead>
    <tbody>
{sections[title]}    </tbody>
  </table>
""")

    return f"""<!DOCTYPE html>
<html lang="ru">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>Помощник юриста — словарь полей шаблона</title>
  <style>
    :root {{
      --bg: #f4f6f9;
      --card: #fff;
      --text: #1a1a1a;
      --muted: #555;
      --accent: #00468c;
      --border: #d8dee8;
      --code-bg: #eef3fa;
    }}
    * {{ box-sizing: border-box; }}
    body {{
      font-family: "Segoe UI", Arial, sans-serif;
      line-height: 1.5;
      color: var(--text);
      background: var(--bg);
      margin: 0;
      padding: 24px;
      max-width: 960px;
      margin-inline: auto;
    }}
    header {{
      background: var(--card);
      border: 1px solid var(--border);
      border-radius: 10px;
      padding: 20px 24px;
      margin-bottom: 24px;
    }}
    h1 {{
      margin: 0 0 8px;
      font-size: 1.5rem;
      color: var(--accent);
    }}
    .subtitle {{ color: var(--muted); margin: 0; }}
    .notice {{
      background: #fff8e6;
      border: 1px solid #f0d78c;
      border-radius: 8px;
      padding: 14px 18px;
      margin-bottom: 24px;
      font-size: 0.95rem;
    }}
    .notice strong {{ color: #8a6116; }}
    .notice ul {{ margin: 8px 0 0; padding-left: 1.2rem; }}
    h2 {{
      font-size: 1.1rem;
      color: var(--accent);
      margin: 28px 0 10px;
      padding-bottom: 4px;
      border-bottom: 2px solid var(--accent);
    }}
    table {{
      width: 100%;
      border-collapse: collapse;
      background: var(--card);
      border: 1px solid var(--border);
      border-radius: 8px;
      overflow: hidden;
      margin-bottom: 8px;
      font-size: 0.92rem;
    }}
    th, td {{
      padding: 10px 12px;
      text-align: left;
      border-bottom: 1px solid var(--border);
      vertical-align: top;
    }}
    th {{
      background: #e8eef6;
      font-weight: 600;
    }}
    tr:last-child td {{ border-bottom: none; }}
    tr:nth-child(even) td {{ background: #fafbfc; }}
    code.copy {{
      display: inline-block;
      background: var(--code-bg);
      padding: 2px 8px;
      border-radius: 4px;
      font-family: Consolas, "Courier New", monospace;
      font-size: 0.88rem;
      cursor: pointer;
      user-select: all;
    }}
    code.copy:hover {{ background: #dce8f8; }}
    footer {{
      margin-top: 32px;
      color: var(--muted);
      font-size: 0.85rem;
      text-align: center;
    }}
    @media print {{
      body {{ background: #fff; padding: 0; }}
      .notice {{ break-inside: avoid; }}
      table {{ break-inside: avoid; }}
    }}
  </style>
</head>
<body>
  <header>
    <h1>Помощник юриста — словарь полей шаблона Word</h1>
    <p class="subtitle">Версия 1.0 · для ручного редактирования шаблонов в папке <code>templates\\</code></p>
  </header>

  <div class="notice">
    <strong>Правила для юриста при правке шаблона</strong>
    <ul>
      <li>В Word вставляйте ключ <strong>целиком</strong>, например <code>{{{{номер_и_дата_договора}}}}</code> — скопируйте из таблицы ниже.</li>
      <li>Можно использовать <strong>английский или русский</strong> ключ — программа понимает оба.</li>
      <li>Не перепечатывайте ключ по буквам и не ставьте пробелы внутри: <code>{{{{ contract_reference }}}}</code> — <em>неправильно</em>.</li>
      <li>Новая строка адреса — новый абзац (Enter) с отдельным ключом <code>{{{{юр_адрес_2}}}}</code>, <code>{{{{юр_адрес_3}}}}</code>.</li>
      <li>После правки сохраните файл и создайте тестовый документ — в нём не должно остаться серых <code>{{{{…}}}}</code>.</li>
    </ul>
  </div>

{"".join(section_html)}
  <footer>
    Помощник юриста · Локальная генерация документов · Подробнее — <em>Руководство_пользователя.pdf</em>
  </footer>
  <script>
    document.querySelectorAll('code.copy').forEach(el => {{
      el.addEventListener('click', () => {{
        const r = document.createRange();
        r.selectNodeContents(el);
        const s = window.getSelection();
        s.removeAllRanges();
        s.addRange(r);
      }});
    }});
  </script>
</body>
</html>
"""


def main() -> None:
    root = Path(__file__).resolve().parent.parent
    config_path = root / "config" / "field-definitions.json"
    config = json.loads(config_path.read_text(encoding="utf-8"))

    out = root / "docs" / "slovar_poley_shablona.html"
    out.write_text(build_html(config), encoding="utf-8")
    print("Создан:", out)

    pub = root / "publish" / "win-x64" / "docs"
    pub.mkdir(parents=True, exist_ok=True)
    shutil.copy2(out, pub / out.name)
    print("Скопирован в:", pub / out.name)


if __name__ == "__main__":
    main()
