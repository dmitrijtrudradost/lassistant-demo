"""EXP-001: compare training-compatible and current application DOCX extraction."""

from __future__ import annotations

import argparse
import json
import sys
from collections import Counter
from pathlib import Path

from docx import Document
from transformers import pipeline

SCRIPT_DIR = Path(__file__).resolve().parent
if str(SCRIPT_DIR) not in sys.path:
    sys.path.insert(0, str(SCRIPT_DIR))

from inference import extract_document  # noqa: E402


WEAK_ROLES = {
    "заказчик_инн",
    "исполнитель_инн",
    "исполнитель_банк",
    "исполнитель_огрн",
    "исполнитель_корр_счет",
    "исполнитель_город_банка",
    "заказчик_банк",
    "объект_тип",
    "заказчик_адрес",
    "исполнитель_расчетный_счет",
}
PARTY_PREFIXES = ("исполнитель_", "заказчик_")
ADDRESS_ROLES = {"исполнитель_адрес", "заказчик_адрес", "объект_адрес"}


def iter_training_paragraphs(doc: Document):
    """Mirror align_prototype.iter_paragraphs_with_tables."""

    def walk_cell(cell, seen_tc):
        tc = cell._tc
        if tc in seen_tc:
            return
        seen_tc.add(tc)
        for paragraph in cell.paragraphs:
            yield paragraph.text
        for nested in cell.tables:
            yield from walk_table(nested, seen_tc)

    def walk_table(table, seen_tc):
        for row in table.rows:
            for cell in row.cells:
                yield from walk_cell(cell, seen_tc)

    for paragraph in doc.paragraphs:
        yield paragraph.text
    seen_tc = set()
    for table in doc.tables:
        yield from walk_table(table, seen_tc)


def exact_metrics(gold: list[dict], predicted: list[dict]) -> dict:
    gold_items = Counter(
        (e["role"], e["text"]) for e in gold if e["role"] in WEAK_ROLES
    )
    predicted_items = Counter(
        (e["role"], e["text"]) for e in predicted if e["role"] in WEAK_ROLES
    )
    true_positive = sum((gold_items & predicted_items).values())
    predicted_total = sum(predicted_items.values())
    gold_total = sum(gold_items.values())
    precision = true_positive / predicted_total if predicted_total else 0.0
    recall = true_positive / gold_total if gold_total else 0.0
    f1 = 2 * precision * recall / (precision + recall) if precision + recall else 0.0
    return {
        "true_positive": true_positive,
        "predicted": predicted_total,
        "gold": gold_total,
        "precision": precision,
        "recall": recall,
        "f1": f1,
    }


def split_party(role: str) -> tuple[str | None, str]:
    for prefix in PARTY_PREFIXES:
        if role.startswith(prefix):
            return prefix[:-1], role[len(prefix) :]
    return None, role


def party_confusion(gold: list[dict], predicted: list[dict]) -> dict:
    gold_lookup: dict[tuple[str, str], set[str]] = {}
    for entity in gold:
        party, suffix = split_party(entity["role"])
        if party:
            gold_lookup.setdefault((suffix, entity["text"]), set()).add(party)

    comparable = 0
    confused = 0
    examples = []
    for entity in predicted:
        party, suffix = split_party(entity["role"])
        expected = gold_lookup.get((suffix, entity["text"]))
        if party and expected:
            comparable += 1
            if party not in expected:
                confused += 1
                examples.append(
                    {
                        "text": entity["text"],
                        "predicted_role": entity["role"],
                        "expected_party": sorted(expected),
                    }
                )
    return {
        "confused": confused,
        "comparable": comparable,
        "rate": confused / comparable if comparable else 0.0,
        "examples": examples,
    }


def address_overcapture(gold: list[dict], predicted: list[dict]) -> dict:
    gold_addresses = {
        (e["role"], e["text"]) for e in gold if e["role"] in ADDRESS_ROLES
    }
    count = 0
    examples = []
    for entity in predicted:
        if entity["role"] not in ADDRESS_ROLES:
            continue
        value = entity["text"].strip()
        exact = (entity["role"], value) in gold_addresses
        contains_gold = any(
            role == entity["role"] and expected and expected in value and expected != value
            for role, expected in gold_addresses
        )
        suspicious = (
            "|" in value
            or "@" in value
            or "телефон" in value.lower()
            or "e-mail" in value.lower()
        )
        if not exact and (contains_gold or suspicious):
            count += 1
            examples.append({"role": entity["role"], "text": value})
    address_total = sum(1 for e in predicted if e["role"] in ADDRESS_ROLES)
    return {
        "overcaptured": count,
        "predicted_addresses": address_total,
        "rate": count / address_total if address_total else 0.0,
        "examples": examples,
    }


def summarize(gold: list[dict], predicted: list[dict], raw_text: str) -> dict:
    span_text_mismatches = [
        {
            "role": entity.get("role"),
            "text": entity.get("text"),
            "slice": raw_text[int(entity["start"]) : int(entity["end"])],
            "start": int(entity["start"]),
            "end": int(entity["end"]),
        }
        for entity in predicted
        if str(entity.get("text") or "")
        != raw_text[int(entity["start"]) : int(entity["end"])]
    ]
    return {
        "entity_count": len(predicted),
        "span_text_mismatch_count": len(span_text_mismatches),
        "span_text_mismatch_examples": span_text_mismatches[:10],
        "weak_role_text_multiset": exact_metrics(gold, predicted),
        "party_confusion": party_confusion(gold, predicted),
        "address_overcapture": address_overcapture(gold, predicted),
        "mean_score": (
            sum(float(e.get("score", 0.0)) for e in predicted) / len(predicted)
            if predicted
            else 0.0
        ),
    }


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--docx", type=Path, required=True)
    parser.add_argument("--gold", type=Path, required=True)
    parser.add_argument("--app-text", type=Path, required=True)
    parser.add_argument("--structured-text", type=Path, default=None)
    parser.add_argument("--fragments", type=Path, default=None)
    parser.add_argument("--model", type=Path, required=True)
    parser.add_argument("--output-dir", type=Path, required=True)
    args = parser.parse_args()

    output_dir = args.output_dir.resolve()
    output_dir.mkdir(parents=True, exist_ok=True)

    training_text = "\n".join(iter_training_paragraphs(Document(args.docx)))
    app_text = args.app_text.read_text(encoding="utf-8-sig").replace("\r\n", "\n")
    gold_payload = json.loads(args.gold.read_text(encoding="utf-8"))
    gold = gold_payload["entities"]

    (output_dir / "training-compatible.txt").write_text(training_text, encoding="utf-8")
    (output_dir / "current-application.txt").write_text(app_text, encoding="utf-8")

    ner = pipeline(
        "token-classification",
        model=str(args.model.resolve()),
        tokenizer=str(args.model.resolve()),
        aggregation_strategy="simple",
    )
    variants = {
        "training_compatible": extract_document(ner, training_text, apply_postprocess=True),
        "current_application": extract_document(ner, app_text, apply_postprocess=True),
    }
    variant_texts = {
        "training_compatible": training_text,
        "current_application": app_text,
    }
    if args.structured_text and args.fragments:
        structured_text = args.structured_text.read_text(encoding="utf-8-sig")
        fragments = json.loads(args.fragments.read_text(encoding="utf-8-sig"))
        variants["csharp_structured"] = extract_document(
            ner,
            structured_text,
            apply_postprocess=True,
            fragments=fragments,
        )
        variant_texts["csharp_structured"] = structured_text
    report = {
        "experiment": "EXP-001",
        "docx": str(args.docx.resolve()),
        "gold": str(args.gold.resolve()),
        "model": str(args.model.resolve()),
        "weak_roles": sorted(WEAK_ROLES),
        "extraction_parity": (
            training_text == args.structured_text.read_text(encoding="utf-8-sig")
            if args.structured_text
            else None
        ),
        "variants": {
            name: summarize(gold, entities, variant_texts[name])
            for name, entities in variants.items()
        },
    }
    for name, entities in variants.items():
        (output_dir / f"{name}.entities.json").write_text(
            json.dumps(entities, ensure_ascii=False, indent=2), encoding="utf-8"
        )
    (output_dir / "report.json").write_text(
        json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8"
    )
    print(json.dumps(report, ensure_ascii=False, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
