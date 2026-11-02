#!/usr/bin/env python3
"""
NER-помощник для Lassistant (вкладка «Шаблоны»).

Протокол:
  stdin  → JSON-массив объектов: [{text, context, color}, ...]
  stdout → JSON-массив объектов: [{key}, ...]
            key = ключ плейсхолдера из field-definitions.json
                  или "custom_value" если тип не распознан

Зависимости: pip install natasha
"""
from __future__ import annotations

import json
import sys


# ─── Инициализация natasha ────────────────────────────────────────────────────

_segmenter   = None
_ner_tagger  = None
_natasha_ok  = False

try:
    from natasha import Segmenter, NewsEmbedding, NewsNERTagger, Doc

    _segmenter  = Segmenter()
    _emb        = NewsEmbedding()
    _ner_tagger = NewsNERTagger(_emb)
    _natasha_ok = True
except ImportError:
    pass  # возвращаем custom_value для всех строк


# ─── Основная классификация ───────────────────────────────────────────────────

def _classify(text: str, context: str, color: str) -> str:
    """Возвращает ключ плейсхолдера по тексту и контексту через natasha NER."""
    from natasha import Doc  # type: ignore

    combined = f"{context} {text}".strip()
    doc = Doc(combined)
    doc.segment(_segmenter)
    doc.tag_ner(_ner_tagger)

    per_found = any(s.type == "PER" for s in doc.spans)
    org_found = any(s.type == "ORG" for s in doc.spans)
    loc_found = any(s.type == "LOC" for s in doc.spans)

    if color == "cyan":
        # Приоритеты для бирюзового: физлицо > организация > адрес
        if per_found and not org_found:
            return "фио_подписанта"
        if org_found:
            return "наименование_орг"
        if loc_found:
            return "юр_адрес"

    elif color == "green":
        # Зелёный — объект/адрес объекта
        if loc_found:
            return "адрес_объекта"
        if per_found:
            return "фио_подписанта"

    elif color == "yellow":
        # Жёлтый — реквизиты договора, NER менее применим
        if loc_found:
            return "адрес_объекта"

    return "custom_value"


# ─── Точка входа ──────────────────────────────────────────────────────────────

def main() -> None:
    raw = sys.stdin.buffer.read().decode("utf-8")
    items: list[dict] = json.loads(raw)

    results: list[dict] = []
    for item in items:
        text    = item.get("text", "")
        context = item.get("context", "")
        color   = item.get("color", "cyan")

        if not _natasha_ok:
            results.append({"key": "custom_value", "error": "natasha not installed"})
            continue

        try:
            key = _classify(text, context, color)
            results.append({"key": key})
        except Exception as exc:  # noqa: BLE001
            results.append({"key": "custom_value", "error": str(exc)})

    sys.stdout.write(json.dumps(results, ensure_ascii=False))
    sys.stdout.flush()


if __name__ == "__main__":
    main()
