#!/usr/bin/env python3
"""
Создаёт копию termination_legal_fire_alarm.docx с русскоязычными ключами-плейсхолдерами.

Английский ключ              → Русский ключ
--------------------------------------------------
[agreement_date]           → [дата_соглашения]
[bank_account]             → [расчетный_счет]
[bank_bik]                 → [бик]
[bank_name]                → [банк]
[company_name]             → [наименование_орг]
[contract_clause]          → [пункт_договора]
[contract_last_day]        → [последний_день]
[contract_reference]       → [номер_и_дата_договора]
[correspondent_account]    → [корр_счет]
[debt_amount]              → [сумма_задолженности]
[director_full_name]       → [фио_подписанта]
[director_representation]  → [должность_и_фио]
[director_title]           → [должность]
[email]                    → [эл_почта]
[inn]                      → [инн]
[kpp]                      → [кпп]
[legal_address_line2]      → [юр_адрес_2]
[legal_address]            → [юр_адрес]
[ogrn]                     → [огрн]
[phone]                    → [телефон]
[termination_date]         → [дата_расторжения]
"""

import re
import shutil
import zipfile
from pathlib import Path

# ── Таблица замен (сначала длинные ключи — чтобы не поймать подстроку) ────────
REPLACEMENTS: list[tuple[str, str]] = [
    ("agreement_date",        "дата_соглашения"),
    ("bank_account",          "расчетный_счет"),
    ("bank_bik",              "бик"),
    ("bank_name",             "банк"),
    ("company_name",          "наименование_орг"),
    ("contract_clause",       "пункт_договора"),
    ("contract_last_day",     "последний_день"),
    ("contract_reference",    "номер_и_дата_договора"),
    ("correspondent_account", "корр_счет"),
    ("debt_amount",           "сумма_задолженности"),
    ("director_full_name",    "фио_подписанта"),
    ("director_representation", "должность_и_фио"),
    ("director_title",        "должность"),
    ("email",                 "эл_почта"),
    ("inn",                   "инн"),
    ("kpp",                   "кпп"),
    ("legal_address_line2",   "юр_адрес_2"),
    ("legal_address",         "юр_адрес"),
    ("ogrn",                  "огрн"),
    ("phone",                 "телефон"),
    ("termination_date",      "дата_расторжения"),
]


def merge_split_placeholders(xml: str) -> str:
    """Склеивает плейсхолдеры, разбитые Word-ом между несколькими <w:t> или <w:r>.

    Word иногда разрезает [key] на несколько run-ов (например [director_full_
    …XML… name]). Регулярное выражение пропускает любые XML-теги внутри [].
    """
    # Паттерн: от [ до ] с возможными XML-тегами между символами ключа.
    pattern = re.compile(
        r'\{\{([^{}]*?(?:<[^>]+>[^{}]*?)*)\}\}',
        re.DOTALL,
    )

    def clean_match(m: re.Match) -> str:
        inner = m.group(1)
        # Убираем все XML-теги внутри плейсхолдера → получаем чистый ключ.
        key = re.sub(r'<[^>]+>', '', inner).strip()
        return '[' + key + ']'

    return pattern.sub(clean_match, xml)


def replace_placeholders(xml: str) -> str:
    """Заменяет английские ключи на русские."""
    # Сортировка: сначала длинные (legal_address_line2 раньше legal_address)
    for en_key, ru_key in sorted(REPLACEMENTS, key=lambda x: -len(x[0])):
        xml = xml.replace('[' + en_key + ']', '[' + ru_key + ']')
    return xml


def process_docx(src: Path, dst: Path) -> None:
    shutil.copy2(src, dst)

    # Читаем все файлы архива в память.
    files: dict[str, bytes] = {}
    with zipfile.ZipFile(src, 'r') as zin:
        for name in zin.namelist():
            files[name] = zin.read(name)

    # Обрабатываем document.xml.
    xml = files['word/document.xml'].decode('utf-8')
    xml = merge_split_placeholders(xml)
    xml = replace_placeholders(xml)
    files['word/document.xml'] = xml.encode('utf-8')

    # Записываем новый docx.
    with zipfile.ZipFile(dst, 'w', zipfile.ZIP_DEFLATED) as zout:
        for name, data in files.items():
            zout.writestr(name, data)

    print(f"Шаблон с русскими ключами создан: {dst}")


def verify(dst: Path) -> None:
    """Проверяет, что в результирующем файле нет нераспознанных английских ключей."""
    with zipfile.ZipFile(dst) as z:
        xml = z.read('word/document.xml').decode('utf-8')
    remaining = sorted(set(re.findall(r'\{\{[a-z][a-z_]+\}\}', xml)))
    if remaining:
        print(f"ВНИМАНИЕ: остались английские плейсхолдеры: {remaining}")
    else:
        print("OK: все английские плейсхолдеры заменены.")

    ru_keys = sorted(set(re.findall(r'\{\{[а-яё_]+\}\}', xml)))
    print(f"Русские ключи в шаблоне: {ru_keys}")


def main() -> None:
    root = Path(__file__).resolve().parent.parent
    src = root / "templates" / "termination_legal_fire_alarm.docx"
    dst = root / "templates" / "termination_legal_fire_alarm_ru.docx"

    if not src.exists():
        raise FileNotFoundError(f"Исходный шаблон не найден: {src}")

    process_docx(src, dst)
    verify(dst)

    # Публикуем рядом с остальными шаблонами.
    pub_dst = root / "publish" / "win-x64" / "templates" / dst.name
    shutil.copy2(dst, pub_dst)
    print(f"Скопирован в publish: {pub_dst}")


if __name__ == "__main__":
    main()
