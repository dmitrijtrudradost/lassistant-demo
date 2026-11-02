"""Score weak-role entity files with strict and repeated-mention policies."""

from __future__ import annotations

import argparse
import json
from collections import Counter, defaultdict
from pathlib import Path

from exp001_extraction_parity import WEAK_ROLES


def span_key(entity):
    return str(entity["role"]), int(entity["start"]), int(entity["end"])


def value_key(entity):
    return str(entity["role"]), str(entity["text"])


def metric(tp: int, fp: int, fn: int) -> dict:
    precision = tp / (tp + fp) if tp + fp else 0.0
    recall = tp / (tp + fn) if tp + fn else 0.0
    f1 = 2 * precision * recall / (precision + recall) if precision + recall else 0.0
    return {
        "tp": tp,
        "fp": fp,
        "fn": fn,
        "precision": precision,
        "recall": recall,
        "f1": f1,
    }


def consume_exact(gold, predicted):
    remaining_gold = Counter(span_key(entity) for entity in gold)
    matched_predictions = set()
    for index, entity in enumerate(predicted):
        key = span_key(entity)
        if remaining_gold[key] > 0:
            remaining_gold[key] -= 1
            matched_predictions.add(index)

    matched_gold = set()
    remaining_predictions = Counter(span_key(entity) for entity in predicted)
    for index, entity in enumerate(gold):
        key = span_key(entity)
        if remaining_predictions[key] > 0:
            remaining_predictions[key] -= 1
            matched_gold.add(index)
    return matched_gold, matched_predictions


def score_entities(gold: list[dict], predicted: list[dict]) -> dict:
    """Return strict exact metrics and policy-B adjusted metrics."""
    gold = [entity for entity in gold if entity["role"] in WEAK_ROLES]
    predicted = [entity for entity in predicted if entity["role"] in WEAK_ROLES]
    matched_gold, matched_predictions = consume_exact(gold, predicted)
    strict_tp = len(matched_gold)
    strict = metric(strict_tp, len(predicted) - strict_tp, len(gold) - strict_tp)
    strict_false_positives = [
        entity for index, entity in enumerate(predicted) if index not in matched_predictions
    ]
    strict_misses = [
        entity for index, entity in enumerate(gold) if index not in matched_gold
    ]

    unmatched_gold_by_value = defaultdict(list)
    for index, entity in enumerate(gold):
        if index not in matched_gold:
            unmatched_gold_by_value[value_key(entity)].append(index)

    equivalent_pairs = []
    neutral_repeated_mentions = []
    policy_matched_predictions = set(matched_predictions)
    policy_matched_gold = set(matched_gold)
    all_gold_values = {value_key(entity) for entity in gold}
    all_gold_spans = {span_key(entity) for entity in gold}
    for index, entity in enumerate(predicted):
        if index in policy_matched_predictions:
            continue
        value = value_key(entity)
        candidates = unmatched_gold_by_value.get(value)
        if candidates:
            gold_index = candidates.pop(0)
            policy_matched_predictions.add(index)
            policy_matched_gold.add(gold_index)
            equivalent_pairs.append(
                {"gold": gold[gold_index], "prediction": entity}
            )
        elif value in all_gold_values and span_key(entity) not in all_gold_spans:
            # Same known value at a genuinely different, unlabelled occurrence.
            policy_matched_predictions.add(index)
            neutral_repeated_mentions.append(entity)

    policy_tp = len(policy_matched_gold)
    policy_fp = len(predicted) - len(policy_matched_predictions)
    policy_fn = len(gold) - policy_tp
    return {
        "strict_exact": {
            **strict,
            "false_positives": strict_false_positives,
            "misses": strict_misses,
        },
        "repeated_mentions_policy_b": {
            **metric(policy_tp, policy_fp, policy_fn),
            "equivalent_pairs": equivalent_pairs,
            "neutral_repeated_mentions": neutral_repeated_mentions,
            "false_positives": [
                entity
                for index, entity in enumerate(predicted)
                if index not in policy_matched_predictions
            ],
            "misses": [
                entity
                for index, entity in enumerate(gold)
                if index not in policy_matched_gold
            ],
        },
    }


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--gold", type=Path, required=True)
    parser.add_argument("--predictions", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()

    gold = [
        entity
        for entity in json.loads(args.gold.read_text(encoding="utf-8-sig"))
        if entity["role"] in WEAK_ROLES
    ]
    predicted = [
        entity
        for entity in json.loads(args.predictions.read_text(encoding="utf-8-sig"))
        if entity["role"] in WEAK_ROLES
    ]
    report = score_entities(gold, predicted)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(
        json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8"
    )
    print(json.dumps(report, ensure_ascii=False, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
