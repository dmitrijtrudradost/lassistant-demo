"""
Инференс для обученной модели NER по договорам.

Разбивает длинный ввод на абзацы (такая же гранулярность, как при обучении) и
запускает pipeline token-classification HuggingFace с aggregation_strategy=simple.
Гибридный постпроцессинг (Задача 2 / 2.5) — можно отключить с помощью --no-postprocess.

В структурированных запросах роли ИСПОЛНИТЕЛЬ/ЗАКАЗЧИК определяются по метаданным
DOCX-таблиц: заголовки столбцов, строки-секции во всю ширину («Заказчики» … «ИСПОЛНИТЕЛЬ:»)
и родительской ячейке вложенной таблицы. В этих случаях роль из таблицы
переопределяет предсказание модели (для модели абзац — это одна ячейка без заголовка).
Вне таблиц объявление стороны относится только к своему абзацу; одиночные заголовки «прилипают»
(plain-text запросы используют те же правила для тела).
"""

from __future__ import annotations

import argparse
import json
import re
from pathlib import Path

from transformers import pipeline

from postprocess import canonicalize_role, detect_party_marker, postprocess_entities

ROOT = Path(__file__).resolve().parent
DEFAULT_MODEL = ROOT / "ner_model_final"
_REQUISITE_CUES = (
    "инн",
    "огрн",
    "бик",
    "р/с",
    "расчетн",
    "корр",
    "к/с",
    "e-mail",
    "email",
    "телефон",
    "адрес",
    "банк",
)

# Безопасное разделение сторон в двусторонней преамбуле (только для тела).
_PREAMBLE_SPLIT_RE = re.compile(r"с одной стороны,\s*и\s+", re.IGNORECASE)
_HAS_EXECUTOR_RE = re.compile(
    r"именуем\w*.{0,80}?исполнител|в дальнейшем\s+Исполнитель|«\s*Исполнитель\s*»",
    re.IGNORECASE | re.DOTALL,
)
_HAS_CUSTOMER_RE = re.compile(
    r"именуем\w*.{0,80}?заказчик|в дальнейшем\s+Заказчик|«\s*Заказчик\s*»",
    re.IGNORECASE | re.DOTALL,
)
# Объявление стороны («…, именуемое в дальнейшем «Заказчик»») — определяет сторону только для
# своего абзаца; не должно распространяться на весь документ.
_INTRO_RE = re.compile(r"именуем\w*.{0,80}?(исполнител|заказчик)", re.IGNORECASE | re.DOTALL)
# Секция подписей: «Исполнитель      Заказчик» над строкой «____Белов М.О.   ____ А.И. Соловьёва».
_SIGNATURE_HEADER_RE = re.compile(
    r"^\s*(исполнител\w*|заказчик\w*)(?:\s{3,}|\t)\s*(исполнител\w*|заказчик\w*)\s*$",
    re.IGNORECASE,
)
_SIGNATURE_RUN_RE = re.compile(r"_{3,}")
_SIGNATURE_LOOKAHEAD = 3  # ненулевые абзацы между заголовком и строкой подписи


_PARTIES = ("заказчик", "исполнитель")
_MODEL_ROLES_CACHE: dict[int, frozenset[str]] = {}


def _model_roles(ner) -> frozenset[str]:
    """Канонические названия ролей, которые может выдавать модель (по id2label, без B-/I-)."""
    key = id(ner)
    if key not in _MODEL_ROLES_CACHE:
        config = getattr(getattr(ner, "model", None), "config", None)
        labels = getattr(config, "id2label", None) or {}
        _MODEL_ROLES_CACHE[key] = frozenset(
            canonicalize_role(str(label).split("-", 1)[-1])
            for label in labels.values()
            if str(label) != "O"
        )
    return _MODEL_ROLES_CACHE[key]


def align_role_party(role: str, party: str, known_roles: frozenset[str]) -> str:
    """Заменяет префикс стороны у ``role`` на ``party``, если у роли есть «зеркальный» вариант.

    Роли без префикса стороны и роли без зеркала (например, лицензия только для исполнителя)
    остаются как предсказала модель.
    """
    role = canonicalize_role(role)
    for side in _PARTIES:
        prefix = f"{side}_"
        if role.startswith(prefix):
            if side == party:
                return role
            mirrored = canonicalize_role(f"{party}_{role[len(prefix):]}")
            return mirrored if mirrored in known_roles else role
    return role


def extract_from_text(
    ner,
    text: str,
    *,
    apply_postprocess: bool = True,
    party_hint: str | None = None,
    party_authoritative: bool = False,
) -> list[dict]:
    """Запускает NER на одном текстовом фрагменте и нормализует результат.

    ``party_authoritative`` — party_hint получен из таблицы (заголовок столбца) и
    перевешивает сторону, предсказанную моделью (применяется до постпроцессинга,
    чтобы правила по длине формы и банку увидели скорректированную сторону).
    """
    if not text.strip():
        return []
    results = ner(text)
    known_roles = _model_roles(ner) if party_authoritative and party_hint else frozenset()
    out = []
    for ent in results:
        start = int(ent["start"])
        end = int(ent["end"])
        role = ent.get("entity_group") or ent.get("entity")
        if known_roles and role:
            role = align_role_party(str(role), party_hint, known_roles)
        out.append(
            {
                "role": role,
                # HuggingFace восстанавливает ``word`` из subwords и вставляет
                # пробелы вокруг пунктуации. Смещения соответствуют исходному
                # абзацу и являются единственно достоверным источником для замены.
                "text": text[start:end],
                "score": float(ent["score"]),
                "start": start,
                "end": end,
            }
        )
    if apply_postprocess:
        # hard_end = длина чанка: expand_* не должен выходить за NER-юнит
        # (абзац-ячейка таблицы или кусок преамбулы по сторонам).
        out = postprocess_entities(
            text, out, party_hint=party_hint, hard_end=len(text)
        )
    return out


def _party_word(word: str) -> str:
    """
    Определяет, к какой стороне договора (исполнитель/заказчик) относится переданное слово.
    - Если слово начинается с «исполнител» (без учета регистра), возвращает «исполнитель».
    - Во всех остальных случаях — «заказчик».
    Используется для сопоставления формальных и неформальных ссылок на стороны.
    """
    return "исполнитель" if word.lower().startswith("исполнител") else "заказчик"


def _other_party(party: str) -> str:
    """
    Возвращает противоположную сторону договора:
    - Если передан «исполнитель», вернет «заказчик».
    - Если передан «заказчик» (или что угодно другое) — вернет «исполнитель».
    Удобно для логического вывода информации о сторонах, когда известна только одна из них.
    """
    return "заказчик" if party == "исполнитель" else "исполнитель"


def _named_party(text: str) -> str | None:
    """
    Анализирует произвольный текст (например, преамбулу контракта) и возвращает явно названную сторону,
    если присутствует ровно одна из них («исполнитель» или «заказчик»).
    Использует регулярные выражения для поиска упоминаний сторон:
      - Если найдены оба (или ни один) индикатора — возвращает None.
      - Если нашелся только «исполнитель», возвращает «исполнитель».
      - Если только «заказчик» — возвращает «заказчик».
    Позволяет понять, какая сторона явно указана, если в абзаце встречается ровно одна из них.
    """
    executor = _HAS_EXECUTOR_RE.search(text) is not None
    customer = _HAS_CUSTOMER_RE.search(text) is not None
    if executor == customer:
        # Оба значения равны: или оба True, или оба False, то есть либо обе стороны найдены, либо ни одной.
        return None
    return "исполнитель" if executor else "заказчик"


def _intro_party(para: str) -> str | None:
    """
    Извлекает сторону, представившуюся в абзаце (например, в конструкции «именуемое в дальнейшем «X»»).
    Использует _INTRO_RE для поиска всех таких вхождений.
    Для каждого найденного слова определяет сторону через _party_word.
    Если единственная сторона упомянута в абзаце — возвращает ее («исполнитель» или «заказчик»).
    Если ни одной или обе — возвращает None.
    Это критично для правильного связывания информации с конкретной стороной по месту объявления в контракте.
    """
    parties = {_party_word(m.group(1)) for m in _INTRO_RE.finditer(para)}
    return next(iter(parties)) if len(parties) == 1 else None

def _split_dual_party_preamble(para: str) -> list[tuple[str, int, str]] | None:
    """
    Если в абзаце тела указываются обе стороны, разделить по «с одной стороны, и …».

    Возвращает список (chunk_text, offset_in_para, party_hint), либо None, если деление не требуется.
    Каждая часть получает свою сторону (указанную явно, заказчик может идти первым);
    если ни с одной стороны не указано явно, стороны идут в порядке: исполнитель — заказчик.
    Не затрагивает номера/предмет договора (нет двух якорей).
    Лицензия/директор всегда — в части исполнителя.
    """
    if not _HAS_EXECUTOR_RE.search(para) or not _HAS_CUSTOMER_RE.search(para):
        return None
    m = _PREAMBLE_SPLIT_RE.search(para)
    if m is None:
        return None
    split_at = m.start()
    left = para[:split_at]
    right = para[split_at:]
    if not left.strip() or not right.strip():
        return None
    left_party, right_party = _named_party(left), _named_party(right)
    if left_party is None and right_party is not None:
        left_party = _other_party(right_party)
    if right_party is None and left_party is not None:
        right_party = _other_party(left_party)
    if left_party is None or left_party == right_party:
        left_party, right_party = "исполнитель", "заказчик"
    return [
        (left, 0, left_party),
        (right, split_at, right_party),
    ]


def _signature_cut(para: str) -> int | None:
    """Смещение второй подписи в строке «____Белов М.О.   ____ А.И. Соловьёва», иначе None.

    Абзац все равно идет в модель целиком (обрезанная «____Зайцев Н.А.»
    делится моделью на «Зайцев» / «Н.» / «А.»); только party_hint использует разрез.
    """
    runs = list(_SIGNATURE_RUN_RE.finditer(para))
    if len(runs) != 2:
        return None
    cut = runs[1].start()
    if not para[:cut].strip("_ \t") or not para[cut:].strip("_ \t"):
        return None
    return cut


CellKey = tuple[str, int, int]  # (путь до таблицы, строка, столбец)
CellParty = tuple[str, bool]  # (сторона, authoritative: важнее, чем мнение модели)


def _table_cell_parties(cells: dict[tuple[int, int], list[str]]) -> dict[tuple[int, int], CellParty]:
    """Определяет стороны для всех ячеек одной таблицы по ключу (строка, столбец).

    1. Строки-секции: строка во всю ширину (только столбец 0, если в таблице больше столбцов)
       с заголовком стороны («Заказчики», «ИСПОЛНИТЕЛЬ:») определяет сторону всех ячеек ниже до
       следующей секции.
    2. Заголовки столбцов: если маркеры дают однозначную сторону на весь столбец.
    3. Fallback 0/1 для таблиц из двух колонок без явных заголовков (НЕ authoritative).
    """
    markers = {
        key: {m for text in texts if (m := detect_party_marker(text)) is not None}
        for key, texts in cells.items()
    }
    max_column = max(column for _, column in cells)
    rows = sorted({row for row, _ in cells})
    section_rows: dict[int, str] = {}
    if max_column >= 1:
        for row in rows:
            row_columns = {column for r, column in cells if r == row}
            parties = markers.get((row, 0), set())
            if row_columns == {0} and len(parties) == 1:
                section_rows[row] = next(iter(parties))
    if section_rows:
        result: dict[tuple[int, int], CellParty] = {}
        current = None
        for row, column in sorted(cells):
            current = section_rows.get(row, current)
            if current is not None:
                result[(row, column)] = (current, True)
        return result

    by_column: dict[int, set[str]] = {}
    for (_, column), parties in markers.items():
        by_column.setdefault(column, set()).update(parties)
    result = {}
    for column, parties in by_column.items():
        if len(parties) == 1:
            party = next(iter(parties))
            result.update({key: (party, True) for key in cells if key[1] == column})
    columns = sorted({column for _, column in cells})
    if result or columns != [0, 1]:
        return result

    cue_counts = {}
    for column in columns:
        combined = "\n".join(
            text.lower() for (_, c), texts in cells.items() if c == column for text in texts
        )
        cue_counts[column] = sum(cue in combined for cue in _REQUISITE_CUES)
    if all(cue_counts[column] >= 2 for column in columns):
        for row, column in cells:
            result[(row, column)] = ("исполнитель" if column == 0 else "заказчик", False)
    return result


def _table_party_map(fragments: list[dict]) -> dict[CellKey, CellParty]:
    """Создаёт отображение ячеек таблицы на (сторону, authoritative) без передачи состояния между таблицами.

    Вложенная таблица без собственного заголовка наследует сторону родительской ячейки
    (путь ``P/r:c:n`` → ячейка ``(P, r, c)``), например, реквизиты исполнителя во вложенной
    таблице в колонке «ИСПОЛНИТЕЛЬ:».
    """
    tables: dict[str, dict[tuple[int, int], list[str]]] = {}
    for fragment in fragments:
        table = fragment.get("table")
        column = fragment.get("column")
        if table is None or column is None:
            continue
        row = fragment.get("row")
        key = (int(row) if row is not None else -1, int(column))
        tables.setdefault(str(table), {}).setdefault(key, []).append(str(fragment.get("text") or ""))

    result: dict[CellKey, CellParty] = {}
    for table in sorted(tables, key=lambda path: path.count("/")):  # сначала родители
        cells = tables[table]
        parties = _table_cell_parties(cells)
        if not parties and "/" in table:
            parent, tail = table.rsplit("/", 1)
            parent_row, parent_column, _ = tail.split(":")
            inherited = result.get((parent, int(parent_row), int(parent_column)))
            if inherited is not None:
                parties = {key: inherited for key in cells}
        for (row, column), value in parties.items():
            result[(table, row, column)] = value
    return result


def _fragment_at_offset(
    fragments: list[dict], offset: int, start_index: int
) -> tuple[dict | None, int]:
    index = start_index
    while index < len(fragments) and int(fragments[index].get("end", 0)) < offset:
        index += 1
    if index >= len(fragments):
        return None, index
    fragment = fragments[index]
    if int(fragment.get("start", 0)) <= offset <= int(fragment.get("end", 0)):
        return fragment, index
    return None, index


def extract_document(
    ner,
    text: str,
    *,
    apply_postprocess: bool = True,
    fragments: list[dict] | None = None,
) -> list[dict]:
    """Пошаговое извлечение по абзацам с абсолютными смещениями и (опционально) структурой DOCX."""
    all_entities = []
    offset = 0
    current_party = None
    signature_order: tuple[str, str] | None = None
    signature_left = 0
    structured = bool(fragments)
    ordered_fragments = sorted(fragments or [], key=lambda item: int(item.get("start", 0)))
    table_parties = _table_party_map(ordered_fragments) if structured else {}
    fragment_index = 0
    paragraphs = text.split("\n")
    for i, para in enumerate(paragraphs):
        marker = detect_party_marker(para)
        fragment = None
        in_table = False
        party_authoritative = False
        if structured:
            fragment, fragment_index = _fragment_at_offset(
                ordered_fragments, offset, fragment_index
            )
            if fragment and fragment.get("table") is not None:
                in_table = True
                table = str(fragment["table"])
                column = fragment.get("column")
                row = fragment.get("row")
                # Карта ячеек (секционные строки/заголовки столбцов/родительская ячейка) — приоритетно
                # для реквизитных таблиц; если в самой ячейке есть маркер (вкл. «ИСПОЛНИТЕЛЬ:») — переопределяет.
                cell = (
                    table_parties.get((table, int(row) if row is not None else -1, int(column)))
                    if column is not None
                    else None
                )
                party_hint, party_authoritative = cell if cell is not None else (None, False)
                if marker is not None:
                    party_hint = marker
                    party_authoritative = True
                # Никогда не наследуем «липкую» сторону из тела в ячейку таблицы.

        chunks: list[tuple[str, int, str | None]]
        # (cut, left party, right party): подсказка для сущности по стороне раздела.
        hint_cut: tuple[int, str, str] | None = None
        if in_table:
            chunks = [(para, 0, party_hint)]
        else:
            # Тело: двусторонняя преамбула → два чанка; объявление одной стороны привязывает к одному абзацу;
            # одиночный заголовок "прилипает"; подписи делятся по заголовку «Исполнитель   Заказчик».
            split = _split_dual_party_preamble(para)
            intro = _intro_party(para) if split is None else None
            cut = (
                _signature_cut(para)
                if split is None and intro is None and signature_order is not None
                else None
            )
            if split is not None or intro is not None:
                current_party = None
            if split is not None:
                chunks = list(split)
            elif intro is not None:
                chunks = [(para, 0, intro)]
            elif cut is not None:
                chunks = [(para, 0, None)]
                hint_cut = (cut, *signature_order)
                signature_order = None
            else:
                if marker is not None:
                    current_party = marker
                chunks = [(para, 0, current_party)]
            header = _SIGNATURE_HEADER_RE.match(para)
            if header is not None and _party_word(header.group(1)) != _party_word(header.group(2)):
                signature_order = (_party_word(header.group(1)), _party_word(header.group(2)))
                signature_left = _SIGNATURE_LOOKAHEAD
            elif signature_order is not None and para.strip() and cut is None:
                signature_left -= 1
                if signature_left < 0:
                    signature_order = None

        for chunk_text, local_off, chunk_hint in chunks:
            ents = extract_from_text(
                ner,
                chunk_text,
                apply_postprocess=apply_postprocess,
                party_hint=chunk_hint if apply_postprocess else None,
                party_authoritative=party_authoritative and apply_postprocess,
            )
            for e in ents:
                hint = chunk_hint
                if hint_cut is not None:
                    hint = hint_cut[1] if e["start"] + local_off < hint_cut[0] else hint_cut[2]
                all_entities.append(
                    {
                        **e,
                        "start": e["start"] + offset + local_off,
                        "end": e["end"] + offset + local_off,
                        "paragraph_index": i,
                        "party_hint": hint,
                        "table": fragment.get("table") if fragment else None,
                        "row": fragment.get("row") if fragment else None,
                        "cell": fragment.get("cell") if fragment else None,
                        "column": fragment.get("column") if fragment else None,
                    }
                )
        offset += len(para) + (1 if i < len(paragraphs) - 1 else 0)
    return all_entities


def _self_check() -> None:
    """Проверка: сторона по колонке против стороны по модели (без весов модели, фейковый pipeline)."""

    class _Config:
        id2label = {
            0: "O",
            1: "B-заказчик_инн",
            2: "B-исполнитель_инн",
            3: "B-заказчик_огрн",
            4: "B-исполнитель_огрн",
            5: "B-исполнитель_номер_лицензии",
            6: "B-заказчик_адрес",
            7: "B-исполнитель_адрес_юридический",
        }

    class _Model:
        config = _Config()

    class _FakeNer:
        """Предсказывает фиксированную роль для каждого известного паттерна в чанке."""

        model = _Model()

        def __init__(self, role_by_text: dict[str, str]):
            self.role_by_text = role_by_text

        def __call__(self, text: str) -> list[dict]:
            found = [
                {"entity_group": role, "score": 0.9,
                 "start": text.index(needle), "end": text.index(needle) + len(needle)}
                for needle, role in self.role_by_text.items()
                if needle in text
            ]
            return sorted(found, key=lambda ent: ent["start"])

    known = _model_roles(_FakeNer({}))
    assert align_role_party("исполнитель_огрн", "заказчик", known) == "заказчик_огрн"
    assert align_role_party("заказчик_огрн", "заказчик", known) == "заказчик_огрн"
    # нет зеркала для лицензии → оставляем роль как есть
    assert align_role_party("исполнитель_номер_лицензии", "заказчик", known) == "исполнитель_номер_лицензии"
    assert align_role_party("объект_адрес", "заказчик", known) == "объект_адрес"
    # мёртвый label приводит к канонизации перед зеркалированием
    assert align_role_party("заказчик_адрес", "исполнитель", known) == "исполнитель_адрес_юридический"

    def fragments(text: str, cells: list[tuple]) -> list[dict]:
        """cells: (таблица, столбец) либо (таблица, строка, столбец) на каждый абзац."""
        out, offset = [], 0
        for para, cell in zip(text.split("\n"), cells):
            table, row, column = cell if len(cell) == 3 else (cell[0], None, cell[1])
            out.append({"start": offset, "end": offset + len(para), "text": para,
                        "source": "table" if table is not None else "body",
                        "table": table, "row": row, "column": column})
            offset += len(para) + 1
        return out

    # заголовочная таблица: модель считает ИНН из колонки ЗАКАЗЧИК исполнителем → побеждает колонка
    text = "ИСПОЛНИТЕЛЬ\nИНН 8558415436\nЗАКАЗЧИК\nИНН 7309655804"
    cells = [("0", 0), ("0", 0), ("0", 1), ("0", 1)]
    ner = _FakeNer({"7309655804": "исполнитель_инн", "8558415436": "исполнитель_инн"})
    ents = extract_document(ner, text, fragments=fragments(text, cells))
    assert [e["role"] for e in ents] == ["исполнитель_инн", "заказчик_инн"], ents
    # ОГРНИП (15 цифр) в колонке ЗАКАЗЧИК: зеркалирование, после чего — тип по длине
    text = "ИСПОЛНИТЕЛЬ\nОГРН 2771420150432\nЗАКАЗЧИК\nОГРНИП 662172024381889"
    ner = _FakeNer({"662172024381889": "исполнитель_огрн", "2771420150432": "исполнитель_огрн"})
    ents = extract_document(ner, text, fragments=fragments(text, cells))
    assert [e["role"] for e in ents] == ["исполнитель_огрн", "заказчик_огрнип"], ents
    # без заголовков (fallback 0/1) → сторона по модели
    text = "ИНН 8558415436, р/с, БИК, адрес\nИНН 7309655804, р/с, БИК, адрес"
    ner = _FakeNer({"7309655804": "исполнитель_инн", "8558415436": "исполнитель_инн"})
    ents = extract_document(ner, text, fragments=fragments(text, [("1", 0), ("1", 1)]))
    assert [e["role"] for e in ents] == ["исполнитель_инн", "исполнитель_инн"], ents
    # --no-postprocess сохраняет сырое предсказание модели
    text = "ИСПОЛНИТЕЛЬ\nИНН 8558415436\nЗАКАЗЧИК\nИНН 7309655804"
    ner = _FakeNer({"7309655804": "исполнитель_инн"})
    ents = extract_document(ner, text, apply_postprocess=False, fragments=fragments(text, cells))
    assert [e["role"] for e in ents] == ["исполнитель_инн"], ents

    # строки-секции: «Заказчики» во второй таблице (2-колонки), «ИСПОЛНИТЕЛЬ:» ниже
    text = "Заказчики\nИНН 7309655804\nИНН 3202025500\nИСПОЛНИТЕЛЬ:\nИНН 8558415436"
    cells = [("0", 0, 0), ("0", 1, 0), ("0", 1, 1), ("0", 2, 0), ("0", 3, 0)]
    ner = _FakeNer({"7309655804": "исполнитель_инн", "3202025500": "заказчик_инн",
                    "8558415436": "заказчик_инн"})
    ents = extract_document(ner, text, fragments=fragments(text, cells))
    assert [e["role"] for e in ents] == ["заказчик_инн", "заказчик_инн", "исполнитель_инн"], ents
    # вложенная таблица без собственного заголовка наследует родительскую ячейку (колонка «ИСПОЛНИТЕЛЬ:»)
    text = "ЗАКАЗЧИК:\nИНН 7309655804\nИСПОЛНИТЕЛЬ:\nИНН 8558415436"
    cells = [("0", 0, 0), ("0", 0, 0), ("0", 0, 1), ("0/0:1:0", 0, 0)]
    ner = _FakeNer({"7309655804": "заказчик_инн", "8558415436": "заказчик_инн"})
    ents = extract_document(ner, text, fragments=fragments(text, cells))
    assert [e["role"] for e in ents] == ["заказчик_инн", "исполнитель_инн"], ents

    # преамбула с первым заказчиком; сторона не переносится на следующий абзац
    text = (
        "ПАО «Выборг Техно», именуемое в дальнейшем «Заказчик», в лице Фёдоровой Ю.В., "
        "с одной стороны, и ООО «Рубеж», именуемое в дальнейшем «Исполнитель», в лице Орлова А.Д.\n"
        "Телефон оперативного дежурного: 8 (995) 759-26-65"
    )
    ner = _FakeNer({"Фёдоровой Ю.В.": "заказчик_фио_падеж", "Орлова А.Д.": "исполнитель_директор_фио_падеж",
                    "8 (995) 759-26-65": "исполнитель_телефон"})
    ents = extract_document(ner, text)
    assert [e["party_hint"] for e in ents] == ["заказчик", "исполнитель", None], ents
    # объявление одной стороны относит только к своему абзацу
    text = "ООО «Рубеж», именуемое в дальнейшем «Исполнитель», в лице Орлова А.Д.\nИНН 8558415436"
    ner = _FakeNer({"Орлова А.Д.": "исполнитель_директор_фио_падеж", "8558415436": "исполнитель_инн"})
    ents = extract_document(ner, text)
    assert [e["party_hint"] for e in ents] == ["исполнитель", None], ents
    # одиночный заголовок «прилипает» к строкам ниже
    text = "ЗАКАЗЧИК\nИНН 7309655804"
    ents = extract_document(_FakeNer({"7309655804": "заказчик_инн"}), text)
    assert [e["party_hint"] for e in ents] == ["заказчик"], ents
    # строка подписи в теле делит по порядку из заголовка
    text = (
        "Исполнитель                    Заказчик\nГенеральный директор\n"
        "_______________Белов М.О.            ______________ А.И. Соловьёва"
    )
    ner = _FakeNer({"Белов М.О.": "исполнитель_директор_фио_кратко", "А.И. Соловьёва": "заказчик_фио_кратко"})
    ents = extract_document(ner, text)
    assert [(e["text"], e["party_hint"]) for e in ents] == [
        ("Белов М.О.", "исполнитель"), ("А.И. Соловьёва", "заказчик")], ents

    print("inference self-check OK (table party over model party; body party rules)")


def main() -> None:
    parser = argparse.ArgumentParser(description="Run contract NER inference")
    parser.add_argument("--model", type=Path, default=DEFAULT_MODEL)
    parser.add_argument("--text", type=str, default=None, help="Inline text to analyze")
    parser.add_argument("--file", type=Path, default=None, help="UTF-8 text file to analyze")
    parser.add_argument("--json-out", type=Path, default=None, help="Optional path to save JSON results")
    parser.add_argument(
        "--no-postprocess",
        action="store_true",
        help="Disable Task-2 hybrid post-processing (A/B comparison)",
    )
    parser.add_argument(
        "--self-check",
        action="store_true",
        help="Run rule self-checks without loading the model",
    )
    args = parser.parse_args()

    if args.self_check:
        _self_check()
        return

    if args.text is None and args.file is None:
        args.text = (
            "ДОГОВОР № 123-ГБР\n"
            "г. Санкт-Петербург\t\t\t«15» марта 2025 г.\n"
            "индивидуальный предприниматель Иванов Иван Иванович, "
            "действующая на основании ОГРНИП 123456789012345 от 01.01.2020"
        )

    if args.file is not None:
        text = args.file.read_text(encoding="utf-8")
    else:
        text = args.text

    model_path = str(args.model)
    ner = pipeline(
        "token-classification",
        model=model_path,
        tokenizer=model_path,
        aggregation_strategy="simple",
    )

    apply_pp = not args.no_postprocess
    entities = extract_document(ner, text, apply_postprocess=apply_pp)
    tag = "raw" if args.no_postprocess else "postprocess"
    print(f"[mode={tag}] entities={len(entities)}")
    for ent in entities:
        print(
            f"{ent['role']}: '{ent['text']}'  "
            f"score={ent['score']:.3f}  pos={ent['start']}-{ent['end']}"
        )

    if args.json_out is not None:
        args.json_out.write_text(json.dumps(entities, ensure_ascii=False, indent=2), encoding="utf-8")
        print("Saved:", args.json_out)


if __name__ == "__main__":
    main()
