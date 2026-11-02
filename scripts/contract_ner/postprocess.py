"""
Гибридный постпроцессинг сущностей NER из контрактов (Задача 2 + 2.5).

Исправления (только для целевых слабых ролей):
  1) Сужение границ числовых интервалов (расширение цифр влево/вправо)
  2) Юридическая форма (длина ИНН/ОГРН) ОТДЕЛЬНО от стороны (подсказка из секции)
  3) Обрезка текстовых/банковских интервалов; адреса расширяются до конца абзаца
  4) Определение стороны банка/ИД через подсказку из секции (ИСПОЛНИТЕЛЬ / ЗАКАЗЧИК)

НЕ затрагивает сильные роли (ФИО, даты, лицензии, email, телефоны и др.).
"""

from __future__ import annotations

import copy
import re
from typing import Any, Literal

Party = Literal["исполнитель", "заказчик"]

NUMERIC_ROLES = {
    "заказчик_инн",
    "исполнитель_инн",
    "заказчик_огрнип",
    "исполнитель_огрн",
    "заказчик_огрн",
    "заказчик_бик",
    "исполнитель_бик",
    "заказчик_корр_счет",
    "исполнитель_корр_счет",
    "заказчик_расчетный_счет",
    "исполнитель_расчетный_счет",
    "заказчик_кпп",
    "исполнитель_кпп",
    "заказчик_окпо",
    "исполнитель_окпо",
    "заказчик_окато",
    "исполнитель_окато",
    "исполнитель_октмо",
    "исполнитель_окопф",
    "идентификационный_код_закупки",
}

TEXTUAL_ROLES = {
    "заказчик_банк",
    "исполнитель_банк",
    "заказчик_город_банка",
    "исполнитель_город_банка",
    "заказчик_адрес",
    "заказчик_адрес_юридический",
    "заказчик_адрес_почтовый",
    "заказчик_почтовый_адрес",
    "исполнитель_юридический_адрес",
    "исполнитель_почтовый_адрес",
    "исполнитель_адрес_юридический",
    "исполнитель_адрес_почтовый",
    "объект_адрес",
    "объект_тип",
    "объект_наименование",
    "заказчик_наименование",
    "исполнитель_наименование",
    "заказчик_наименование_кратко",
    "исполнитель_наименование_кратко",
}

ADDRESS_ROLES = {
    "заказчик_адрес",
    "заказчик_адрес_юридический",
    "заказчик_адрес_почтовый",
    "заказчик_почтовый_адрес",
    "исполнитель_юридический_адрес",
    "исполнитель_почтовый_адрес",
    "исполнитель_адрес_юридический",
    "исполнитель_адрес_почтовый",
    "объект_адрес",
}

PARTY_ID_ROLES = {
    "заказчик_инн",
    "исполнитель_инн",
    "заказчик_огрнип",
    "исполнитель_огрн",
    "заказчик_огрн",
}

BANK_ROLES = {"заказчик_банк", "исполнитель_банк"}
ORG_FORMS = ("ПАО", "АО", "ООО", "ОАО", "ЗАО")

# Track 1 / v1.1.1 — канонизация схемы (без дообучения). Применяется первой в postprocess_entity.
# Устаревшие идентификаторы ролей (объект_тип, исполнитель_директор_фио, заказчик_адрес, …) остаются в хедере до v1.2.
ROLE_CANONICAL_REMAP: dict[str, str] = {
    "объект_тип": "объект_наименование",
    "исполнитель_директор_фио": "исполнитель_директор_фио_падеж",
    "заказчик_адрес": "заказчик_адрес_юридический",
    "заказчик_почтовый_адрес": "заказчик_адрес_почтовый",
    "исполнитель_почтовый_адрес": "исполнитель_адрес_почтовый",
    "исполнитель_юридический_адрес": "исполнитель_адрес_юридический",
}

# Заголовки секций (ячейки таблицы) и явные подсказки.
# Допустим двоеточие в конце: «ИСПОЛНИТЕЛЬ:» / «ЗАКАЗЧИК:» в таблицах реквизитов (helper 1.5.4.3+).
# Множественное: «Заказчики» / «Исполнители» — начало блока из нескольких сторон.
_SECTION_HEADER_RE = re.compile(
    r"^\s*(исполнител[ьи]|заказчики?)\s*:?\s*$",
    re.IGNORECASE,
)
_PARTY_CUE_RE = re.compile(
    r"реквизиты\s+исполнителя|реквизиты\s+заказчика|"
    r"именуем\w*.{0,60}?исполнител|именуем\w*.{0,60}?заказчик",
    re.IGNORECASE | re.DOTALL,
)


def detect_party_marker(paragraph: str) -> Party | None:
    """Вернуть сторону, если абзац является/содержит секционную подсказку; иначе None."""
    text = paragraph.strip()
    if not text:
        return None
    m = _SECTION_HEADER_RE.match(text)
    if m:
        return "исполнитель" if m.group(1).lower().startswith("исполнител") else "заказчик"
    # Заголовки только из заглавных букв: ИСПОЛНИТЕЛЬ / ЗАКАЗЧИК
    upper = text.upper()
    if upper == "ИСПОЛНИТЕЛЬ":
        return "исполнитель"
    if upper == "ЗАКАЗЧИК":
        return "заказчик"
    m2 = _PARTY_CUE_RE.search(text)
    if m2:
        frag = m2.group(0).lower()
        return "исполнитель" if "исполнител" in frag else "заказчик"
    return None


def classify_legal_form(number_str: str) -> str | None:
    """
    Официальная длина → тип идентификатора (НЕ сторона договора).
      10 → inn_ul (юрлицо)
      12 → inn_fl (физлицо/ИП)
      13 → ogrn (юрлицо)
      15 → ogrnip (ИП)
    """
    digits = re.sub(r"\D", "", number_str)
    n = len(digits)
    if n == 10:
        return "inn_ul"
    if n == 12:
        return "inn_fl"
    if n == 13:
        return "ogrn"
    if n == 15:
        return "ogrnip"
    return None


_FORM_TO_SUFFIX = {
    "inn_ul": "инн",
    "inn_fl": "инн",
    "ogrn": "огрн",
    "ogrnip": "огрнип",
}


def resolve_party_and_form(
    number_str: str,
    *,
    party_hint: Party | None = None,
    current_role: str | None = None,
) -> str | None:
    """
    Собрать роль из стороны + формы (по длине числа).

    Отдавать предпочтение стороне, уже присутствующей в модели роли. Подсказка из секции party_hint —
    только запасной вариант, если в модели нет префикса стороны; в таблицах с двумя колонками
    часто остаётся устаревшая подсказка, которая приведёт к ошибке Exact→Confusion.
    Не делать вывод о стороне только по длине (Задача 2.5).
    """
    form = classify_legal_form(number_str)
    if form is None:
        return None

    party: Party | None = None
    if current_role:
        if current_role.startswith("исполнитель_"):
            party = "исполнитель"
        elif current_role.startswith("заказчик_"):
            party = "заказчик"
    if party is None:
        party = party_hint
    if party is None:
        return None

    suffix = _FORM_TO_SUFFIX[form]
    return f"{party}_{suffix}"


# Обратная совместимость имени в старых документах/импортах.
def resolve_party_by_digit_length(number_str: str, party_hint: Party | None = None, current_role: str | None = None) -> str | None:
    return resolve_party_and_form(number_str, party_hint=party_hint, current_role=current_role)


def expand_numeric_span(text: str, pred_start: int, pred_end: int) -> tuple[int, int]:
    start = pred_start
    while start > 0 and text[start - 1].isdigit():
        start -= 1
    end = pred_end
    while end < len(text) and text[end].isdigit():
        end += 1
    return start, end


_ADDR_LABEL_PREFIX_RE = re.compile(
    r"(?is)^(?:юридический\s+адрес|почтовый\s+адрес|адрес)\s*:?\s*"
)
_ADDR_NEXT_LABEL_RE = re.compile(
    r"(?i)\s*(?:юридический\s+адрес|почтовый\s+адрес)\b"
)
_ADDR_HOUSE_RE = re.compile(r"(?i)\bд\.\s*\d")
_ADDR_PLACE_RE = re.compile(r"(?i)(?:^\d{6}\b|\b(?:г\.|ул\.|пр\.|пер\.|наб\.|шоссе)\b)")


def _address_looks_complete(frag: str) -> bool:
    """True если в интервале уже есть номер дома + место/индекс — не расширять вправо."""
    f = frag.strip().rstrip(",")
    return bool(_ADDR_HOUSE_RE.search(f) and _ADDR_PLACE_RE.search(f))



def _clamp_span(start: int, end: int, hard_end: int) -> tuple[int, int]:
    """Ограничить расширенные интервалы пределами NER-чанкa/фрагмента (ячейка или абзац)."""
    hard_end = max(0, hard_end)
    start = max(0, min(start, hard_end))
    end = max(start, min(end, hard_end))
    return start, end


def expand_address_span(
    text: str,
    pred_start: int,
    pred_end: int,
    *,
    hard_end: int | None = None,
) -> tuple[int, int]:
    """
    Расширить адресные интервалы, не считая '.' за конец (д. / ул. / оф.).

    Расширять влево до перевода строки или ':'. Убрать ведущие метки адреса (с
    двоеточием или без). Расширять вправо только если интервал явно обрезан;
    остановиться перед следующей меткой адреса, '(далее…' или переводом строки.
    Сохранять запятую, если она уже была в спане модели (gold object_address_comma).
    """
    bound = len(text) if hard_end is None else min(len(text), hard_end)
    orig_start, orig_end = pred_start, pred_end
    start = pred_start
    while start > 0 and text[start - 1] not in "\n:":
        start -= 1
    if start < bound and text[start] == ":":
        start += 1
    while start < bound and text[start].isspace():
        start += 1
    m_lab = _ADDR_LABEL_PREFIX_RE.match(text[start:])
    if m_lab and start + m_lab.end() <= orig_end + 1:
        # Убирать метку только если она до/перекрывает изначальный интервал
        start += m_lab.end()
        while start < len(text) and text[start].isspace():
            start += 1

    end = orig_end
    if not _address_looks_complete(text[orig_start:orig_end]):
        while end < bound and text[end] not in "\n":
            if _ADDR_NEXT_LABEL_RE.match(text[end:]):
                break
            if re.match(r"\s*\(\s*далее\b", text[end:], flags=re.IGNORECASE):
                break
            end += 1
        # Убрать лишние запятые/пробелы при расширении вправо за изначальный конец
        while end > max(start, orig_end) and text[end - 1] in " \t,;":
            end -= 1
    while end > start and text[end - 1] in " \t":
        end -= 1
    return _clamp_span(start, end, bound)


ORG_NAME_ROLES = {
    "заказчик_наименование",
    "исполнитель_наименование",
    "заказчик_наименование_кратко",
    "исполнитель_наименование_кратко",
    "заказчик_наименование_падеж",
    "заказчик_наименование_кратко_падеж",
    "объект_наименование",
}


def _org_name_looks_complete(frag: str) -> bool:
    """True если наименование организации уже закрыто кавычками/баланс скобок — не расширять вправо."""
    f = frag.strip()
    if not f:
        return False
    if "«" in f and f.endswith("»"):
        return True
    # Краткая форма без кавычек и с ОПФ, например ООО «Щит» — уже обработано выше;
    # Обычный ООО «X без закрытия → неполное
    return False


def expand_textual_span(
    text: str,
    pred_start: int,
    pred_end: int,
    *,
    max_lookahead_chars: int = 80,
    role: str | None = None,
    hard_end: int | None = None,
) -> tuple[int, int]:
    bound = len(text) if hard_end is None else min(len(text), hard_end)
    start, end = pred_start, pred_end
    role = role or ""

    while start > 0 and text[start - 1].isalpha():
        start -= 1

    if role in ADDRESS_ROLES or role.endswith("_адрес"):
        return expand_address_span(text, start, end, hard_end=bound)

    is_bank = role.endswith("_банк") or role in BANK_ROLES
    is_short_word = (
        role.endswith("город_банка")
        or role in {"объект_тип", "объект_наименование"}
    )
    is_org_name = role in ORG_NAME_ROLES or role.endswith("_наименование") or "_наименование_" in role

    if is_short_word:
        while end < bound and text[end].isalpha():
            end += 1
        return _clamp_span(start, end, bound)

    if is_bank:
        s, e = _expand_bank_span(text, start, end, max_lookahead_chars)
        return _clamp_span(s, e, bound)

    # Наименования организаций/сторон: если уже завершено («…»), не расширять в "(лицензия…"
    if is_org_name and _org_name_looks_complete(text[pred_start:pred_end]):
        # опционально: подвинуть ОПФ влево, если модель выделила внутри названия
        left = text[max(0, start - 8) : start]
        for form in ORG_FORMS:
            idx = left.rfind(form)
            if idx != -1:
                start = max(0, start - (len(left) - idx))
                break
        return _clamp_span(start, pred_end, bound)

    # Обычный текст (не адрес): избегать ловушки с точкой в аббревиатурах для коротких токенов
    terminators = {",", ";", "»", "\n", "("}
    limit = min(bound, end + max_lookahead_chars)
    while end < limit and text[end] not in terminators:
        if text[end] == ".":
            prev = text[max(0, end - 3) : end].strip()
            if len(prev) <= 3 and prev.replace(".", "").isalpha():
                end += 1
                continue
            break
        end += 1
    if end < bound and text[end] == "»":
        end += 1
    # Не идти далее '»' до следующих слов для наименований организаций (ранее: while isalpha).
    if not is_org_name:
        while end < bound and text[end].isalpha():
            end += 1
    return _clamp_span(start, end, bound)


def _expand_bank_span(text: str, start: int, end: int, max_lookahead: int) -> tuple[int, int]:
    left = text[max(0, start - 8) : start]
    for form in ORG_FORMS:
        idx = left.rfind(form)
        if idx != -1:
            start = max(0, start - (len(left) - idx))
            break
    while start > 0 and text[start - 1] in "«\"'":
        start -= 1
    m = re.search(r"(?:ПАО|АО|ООО|ОАО|ЗАО)\s*$", text[max(0, start - 6) : start])
    if m:
        start = start - len(m.group(0))

    limit = min(len(text), start + max_lookahead)
    close = text.find("»", end, limit)
    if close != -1:
        return start, close + 1
    terminators = {",", ";", "\n"}
    pos = end
    while pos < limit and text[pos] not in terminators:
        pos += 1
    return start, pos


def resolve_bank_party(
    text: str,
    ent_start: int,
    *,
    party_hint: Party | None = None,
) -> str | None:
    """Предпочесть явную party_hint секции; иначе поиск по тексту."""
    if party_hint is not None:
        return f"{party_hint}_банк"

    window_start = max(0, ent_start - 800)
    window = text[window_start:ent_start]
    # Предпочесть последний заголовок секции в окне.
    last: tuple[int, Party] | None = None
    for m in re.finditer(r"(?im)^\s*(исполнитель|заказчик)\s*:?\s*$", window):
        side: Party = "исполнитель" if m.group(1).lower().startswith("исполнител") else "заказчик"
        last = (m.start(), side)
    if last:
        return f"{last[1]}_банк"
    return None


def _refresh_entity(ent: dict[str, Any], text: str, start: int, end: int, role: str | None = None) -> dict[str, Any]:
    out = copy.deepcopy(ent)
    out["start"] = start
    out["end"] = end
    out["text"] = text[start:end]
    if role is not None:
        out["role"] = role
    return out


def canonicalize_role(role: str) -> str:
    """Преобразовать устаревшие/дублирующиеся роли к каноническим v1.1.1."""
    return ROLE_CANONICAL_REMAP.get(role, role)


def postprocess_entity(
    text: str,
    ent: dict[str, Any],
    *,
    party_hint: Party | None = None,
    hard_end: int | None = None,
) -> dict[str, Any]:
    role = canonicalize_role(ent.get("role") or "")
    start, end = int(ent["start"]), int(ent["end"])
    bound = len(text) if hard_end is None else min(len(text), hard_end)
    if start < 0 or end > bound or start > end:
        return copy.deepcopy(ent)
    if role != (ent.get("role") or ""):
        ent = _refresh_entity(ent, text, start, end, role)

    # 1) Расширение числовых границ
    if role in NUMERIC_ROLES:
        start, end = expand_numeric_span(text, start, end)
        start, end = _clamp_span(start, end, bound)
        ent = _refresh_entity(ent, text, start, end, role)

    # 2) Сторона (hint) + форма (длина) — не только длина→сторона
    role = ent["role"]
    if role in PARTY_ID_ROLES or (
        role in NUMERIC_ROLES and any(role.endswith(suf) for suf in ("_инн", "_огрн", "_огрнип"))
    ):
        resolved = resolve_party_and_form(
            ent["text"],
            party_hint=party_hint,
            current_role=role,
        )
        if resolved is not None:
            ent = _refresh_entity(ent, text, ent["start"], ent["end"], resolved)

    # 3) Расширение текстовых/адресных/банковских спанов
    role = ent["role"]
    if role in TEXTUAL_ROLES:
        start, end = expand_textual_span(
            text, ent["start"], ent["end"], role=role, hard_end=bound
        )
        ent = _refresh_entity(ent, text, start, end, role)

    # 4) Определение стороны банка по секционной подсказке — только если роль модели не содержит уже стороны
    # (устаревшая party_hint в таблицах с двумя колонками приводит к ошибкам Exact→Confusion).
    role = ent["role"]
    if role in BANK_ROLES:
        model_has_party = role.startswith("исполнитель_") or role.startswith("заказчик_")
        if not model_has_party:
            side_role = resolve_bank_party(text, ent["start"], party_hint=party_hint)
            if side_role is not None:
                ent = _refresh_entity(ent, text, ent["start"], ent["end"], side_role)

    return ent


def postprocess_entities(
    text: str,
    entities: list[dict[str, Any]],
    *,
    party_hint: Party | None = None,
    hard_end: int | None = None,
) -> list[dict[str, Any]]:
    if not entities:
        return []
    bound = len(text) if hard_end is None else hard_end
    return [
        postprocess_entity(text, e, party_hint=party_hint, hard_end=bound)
        for e in entities
    ]


def _self_check() -> None:
    # Расширение числового спана + разделение по форме/стороне
    t = "ИНН   489396198954"
    s, e = expand_numeric_span(t, t.index("396198954"), t.index("396198954") + 9)
    assert t[s:e] == "489396198954"
    assert classify_legal_form(t[s:e]) == "inn_fl"
    assert resolve_party_and_form(t[s:e], party_hint="заказчик") == "заказчик_инн"
    # 10 цифр + заказчик (будущий ООО) НЕ должен стать исполнителем
    assert resolve_party_and_form("7227220728", party_hint="заказчик") == "заказчик_инн"
    assert resolve_party_and_form("7227220728", party_hint="исполнитель") == "исполнитель_инн"
    # Только длина без стороны → None
    assert resolve_party_and_form("7227220728") is None

    t2 = "ОГРН   3271377352324"
    s2, e2 = expand_numeric_span(t2, t2.index("1377352324"), t2.index("1377352324") + 10)
    assert t2[s2:e2] == "3271377352324"
    assert resolve_party_and_form(t2[s2:e2], party_hint="исполнитель") == "исполнитель_огрн"

    # Адрес: точка-аббревиатура не должна останавливать расширение; слева до ':'
    addr = "Московский пр., д. 100, оф. 5"
    a0, a1 = expand_textual_span(addr, addr.index("д"), addr.index("д") + 1, role="заказчик_адрес")
    assert addr[a0:a1] == addr, addr[a0:a1]
    labeled = "Юридический адрес: 138144, Екатеринбург, ул. Кораблестроителей, д. 12"
    val = "138144, Екатеринбург, ул. Кораблестроителей, д. 12"
    # корректный спан не должен заходить в метку
    b0, b1 = expand_address_span(labeled, labeled.index(val), labeled.index(val) + len(val))
    assert labeled[b0:b1] == val, labeled[b0:b1]
    # метка без двоеточия + следующая почтовая метка не должны быть захвачены
    multi = (
        "Юридический адрес 134251, г. Екатеринбург, ул. Оптиков, д. 31 "
        "Почтовый адрес : 134251, г. Екатеринбург, ул. Оптиков, д. 31"
    )
    frag = "134251, г. Екатеринбург, ул. Оптиков, д. 31"
    c0, c1 = expand_address_span(multi, multi.index(frag), multi.index(frag) + len(frag))
    assert multi[c0:c1] == frag, multi[c0:c1]
    # запятая внутри исходного спана должна сохраняться
    comma = frag + ","
    d0, d1 = expand_address_span(comma, 0, len(comma))
    assert comma[d0:d1] == comma, comma[d0:d1]
    # Полный адрес по середине предложения не должен захватывать "(далее…"
    mid = "по адресу: г. Екатеринбург, пр. Славы, д. 58, литер Г (далее «Объект»)."
    g = "г. Екатеринбург, пр. Славы, д. 58, литер Г"
    e0, e1 = expand_address_span(mid, mid.index(g), mid.index(g) + len(g))
    assert mid[e0:e1] == g, mid[e0:e1]
    # роль из модели важнее устаревшей party_hint
    assert resolve_party_and_form("3202025500", party_hint="исполнитель", current_role="заказчик_инн") == "заказчик_инн"
    assert resolve_party_and_form("3202025500", party_hint="исполнитель") == "исполнитель_инн"

    # Маркеры секции
    assert detect_party_marker("ИСПОЛНИТЕЛЬ") == "исполнитель"
    assert detect_party_marker("ЗАКАЗЧИК") == "заказчик"
    assert detect_party_marker("ПАО «Совкомбанк»") is None
    assert detect_party_marker("Заказчики") == "заказчик"
    assert detect_party_marker("ИСПОЛНИТЕЛИ:") == "исполнитель"
    assert detect_party_marker("Исполнитель  Заказчик") is None

    # Банк с подсказкой
    assert resolve_bank_party("ПАО «Совкомбанк»", 0, party_hint="исполнитель") == "исполнитель_банк"

    t3 = "АО «Альфа-Банк», далее"
    s3, e3 = expand_textual_span(t3, t3.index("АО"), t3.index("АО") + 2, role="исполнитель_банк")
    assert "Альфа-Банк" in t3[s3:e3] and t3[s3:e3].endswith("»"), t3[s3:e3]

    # Уже завершённое название организации — не должно поглощать "(лицензия…"
    org = (
        "Общество с ограниченной ответственностью «Охранная организация «Щит Северо-Запад» "
        "(лицензия на осуществление деятельности по монтажу"
    )
    g = "Общество с ограниченной ответственностью «Охранная организация «Щит Северо-Запад»"
    o0, o1 = expand_textual_span(org, 0, len(g), role="исполнитель_наименование")
    assert org[o0:o1] == g, org[o0:o1]
    assert _org_name_looks_complete(g)

    # Канонизация v1.1.1
    assert canonicalize_role("объект_тип") == "объект_наименование"
    assert canonicalize_role("исполнитель_директор_фио") == "исполнитель_директор_фио_падеж"
    assert canonicalize_role("заказчик_адрес") == "заказчик_адрес_юридический"
    assert canonicalize_role("заказчик_почтовый_адрес") == "заказчик_адрес_почтовый"
    assert canonicalize_role("исполнитель_почтовый_адрес") == "исполнитель_адрес_почтовый"
    remapped = postprocess_entity("аптека", {"role": "объект_тип", "start": 0, "end": 6, "text": "аптека", "score": 1.0})
    assert remapped["role"] == "объект_наименование"

    print("postprocess self-check OK (task 2.5 + v1.1.1 remap)")


if __name__ == "__main__":
    _self_check()
