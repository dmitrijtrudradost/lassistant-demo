"""Reconcile Task 2.5 gold-centric diagnostics with EXP-001 entity F1."""

from __future__ import annotations

import argparse
import json
from collections import Counter
from pathlib import Path

from exp001_extraction_parity import WEAK_ROLES


def key_value(entity: dict) -> tuple[str, str]:
    return str(entity["role"]), str(entity["text"])


def key_span(entity: dict) -> tuple[str, int, int]:
    return str(entity["role"]), int(entity["start"]), int(entity["end"])


def entity_metrics(gold: list[dict], predicted: list[dict], *, by_span: bool) -> dict:
    key = key_span if by_span else key_value
    gold_counter = Counter(key(entity) for entity in gold)
    predicted_counter = Counter(key(entity) for entity in predicted)
    true_positive = sum((gold_counter & predicted_counter).values())
    n_gold = sum(gold_counter.values())
    n_predicted = sum(predicted_counter.values())
    precision = true_positive / n_predicted if n_predicted else 0.0
    recall = true_positive / n_gold if n_gold else 0.0
    f1 = 2 * precision * recall / (precision + recall) if precision + recall else 0.0
    return {
        "true_positive": true_positive,
        "predicted": n_predicted,
        "gold": n_gold,
        "precision": precision,
        "recall": recall,
        "f1": f1,
    }


def classify_gold(gold: dict, predictions: list[dict]) -> dict:
    exact = [
        entity
        for entity in predictions
        if entity["role"] == gold["role"]
        and int(entity["start"]) == int(gold["start"])
        and int(entity["end"]) == int(gold["end"])
    ]
    if exact:
        kind, prediction = "EXACT", exact[0]
    else:
        overlaps = [
            entity
            for entity in predictions
            if max(int(gold["start"]), int(entity["start"]))
            < min(int(gold["end"]), int(entity["end"]))
        ]
        same_role = [entity for entity in overlaps if entity["role"] == gold["role"]]
        if same_role:
            kind, prediction = "BOUNDARY", same_role[0]
        elif overlaps:
            kind, prediction = "CONFUSION", overlaps[0]
        else:
            kind, prediction = "MISS", None
    return {
        "kind": kind,
        "true_role": gold["role"],
        "true_text": gold["text"],
        "true_span": [gold["start"], gold["end"]],
        "pred_role": prediction.get("role") if prediction else None,
        "pred_text": prediction.get("text") if prediction else None,
        "pred_span": (
            [prediction["start"], prediction["end"]] if prediction else None
        ),
    }


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--task25-report", type=Path, required=True)
    parser.add_argument("--gold", type=Path, required=True)
    parser.add_argument("--predictions", type=Path, required=True)
    parser.add_argument("--aggregated", type=Path, required=True)
    parser.add_argument("--input-text", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()

    task_report = json.loads(args.task25_report.read_text(encoding="utf-8"))
    gold_payload = json.loads(args.gold.read_text(encoding="utf-8"))
    predictions = json.loads(args.predictions.read_text(encoding="utf-8"))
    aggregated = json.loads(args.aggregated.read_text(encoding="utf-8"))
    input_text = args.input_text.read_text(encoding="utf-8-sig")

    task_gold = [
        {
            "role": detail["true_role"],
            "text": detail["true_text"],
            "start": detail["true_span"][0],
            "end": detail["true_span"][1],
        }
        for detail in task_report["details_after"]
        if detail["doc_id"] == "dogovor_0005"
    ]
    exp_gold = [
        entity for entity in gold_payload["entities"] if entity["role"] in WEAK_ROLES
    ]
    weak_predictions = [
        entity for entity in predictions if entity["role"] in WEAK_ROLES
    ]
    weak_aggregated = [
        entity for entity in aggregated if entity["role"] in WEAK_ROLES
    ]

    task_values = Counter(key_value(entity) for entity in task_gold)
    exp_values = Counter(key_value(entity) for entity in exp_gold)
    additions = list((exp_values - task_values).elements())
    missing = list((task_values - exp_values).elements())

    offsets_comparable = input_text == str(gold_payload.get("text") or "")
    errors = []
    false_positives = []
    if offsets_comparable:
        errors = [
            item
            for item in (classify_gold(entity, weak_predictions) for entity in exp_gold)
            if item["kind"] != "EXACT"
        ]
        exact_prediction_keys = Counter(key_span(entity) for entity in exp_gold)
        false_positives = [
            entity
            for entity in weak_predictions
            if exact_prediction_keys[key_span(entity)] == 0
        ]

    report = {
        "task25_method": {
            "description": "gold-centric exact recall; false positives are not counted",
            "all_documents_gold": int(task_report["n_gold_weak"]),
            "all_documents_exact_after": int(
                task_report["after"]["totals"].get("EXACT", 0)
            ),
            "dogovor_0005_gold": len(task_gold),
            "dogovor_0005_exact_after": sum(
                detail["kind"] == "EXACT"
                for detail in task_report["details_after"]
                if detail["doc_id"] == "dogovor_0005"
            ),
        },
        "gold_reconciliation": {
            "same_list": task_values == exp_values,
            "task25_is_value_subset_of_exp001": not missing,
            "task25_count": len(task_gold),
            "exp001_count": len(exp_gold),
            "only_in_exp001_role_text": [
                {"role": role, "text": text} for role, text in additions
            ],
            "only_in_task25_role_text": [
                {"role": role, "text": text} for role, text in missing
            ],
            "task25_gold": task_gold,
            "exp001_gold": exp_gold,
        },
        "offset_compatibility": {
            "compatible": offsets_comparable,
            "exp001_input_length": len(input_text),
            "json_gold_text_length": len(str(gold_payload.get("text") or "")),
            "conclusion": (
                "absolute span metrics are valid"
                if offsets_comparable
                else "absolute offsets belong to different text serializations"
            ),
        },
        "without_aggregator": {
            "role_text_multiset_metrics": entity_metrics(
                exp_gold, weak_predictions, by_span=False
            ),
            "span_exact_metrics": (
                entity_metrics(exp_gold, weak_predictions, by_span=True)
                if offsets_comparable
                else None
            ),
            "errors": errors,
            "false_positives": false_positives,
        },
        "with_csharp_aggregator": {
            "role_text_multiset_metrics": entity_metrics(
                exp_gold, weak_aggregated, by_span=False
            ),
            "span_exact_metrics": (
                entity_metrics(exp_gold, weak_aggregated, by_span=True)
                if offsets_comparable
                else None
            ),
            "weak_predictions": weak_aggregated,
        },
    }
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(
        json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8"
    )
    print(json.dumps(report, ensure_ascii=False, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
