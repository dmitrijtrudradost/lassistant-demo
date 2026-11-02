"""Calculate true entity precision/recall/F1 on one val_dataset document."""

from __future__ import annotations

import argparse
import importlib.util
import json
import sys
from collections import Counter
from pathlib import Path

import torch
from transformers import AutoModelForTokenClassification, AutoTokenizer

SCRIPT_DIR = Path(__file__).resolve().parent
if str(SCRIPT_DIR) not in sys.path:
    sys.path.insert(0, str(SCRIPT_DIR))

from exp001_extraction_parity import WEAK_ROLES  # noqa: E402
from score_weak_entities import score_entities  # noqa: E402


def load_postprocess(path: Path):
    spec = importlib.util.spec_from_file_location("task25_postprocess", path)
    if spec is None or spec.loader is None:
        raise RuntimeError(f"Cannot load postprocess: {path}")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def predict_raw_entities(text, tokenizer, model, id2label):
    """Exact prediction algorithm used by Task 2.5 evaluate_task2.py."""
    encoded = tokenizer(
        text,
        return_offsets_mapping=True,
        truncation=True,
        max_length=512,
        return_tensors="pt",
    )
    offsets = encoded.pop("offset_mapping")[0].tolist()
    with torch.no_grad():
        prediction_ids = model(**encoded).logits[0].argmax(-1).tolist()

    entities, current = [], None
    for prediction, (start, end) in zip(prediction_ids, offsets):
        if start == end == 0:
            if current:
                entities.append(current)
                current = None
            continue
        label = id2label[int(prediction)]
        if label == "O":
            if current:
                entities.append(current)
                current = None
            continue
        prefix, role = label[:2], label[2:]
        if prefix == "B-" or current is None or current["role"] != role:
            if current:
                entities.append(current)
            current = {
                "role": role,
                "start": start,
                "end": end,
                "text": text[start:end],
                "score": 1.0,
            }
        else:
            current["end"] = end
            current["text"] = text[current["start"] : end]
    if current:
        entities.append(current)
    return entities


def exact_metrics(gold, predicted):
    key = lambda entity: (
        int(entity["paragraph_index"]),
        str(entity["role"]),
        int(entity["local_start"]),
        int(entity["local_end"]),
    )
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


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--val", type=Path, required=True)
    parser.add_argument("--doc-id", required=True)
    parser.add_argument("--model", type=Path, required=True)
    parser.add_argument("--postprocess", type=Path, required=True)
    parser.add_argument("--output-dir", type=Path, required=True)
    args = parser.parse_args()

    rows = [
        json.loads(line)
        for line in args.val.read_text(encoding="utf-8").splitlines()
        if line.strip()
    ]
    rows = [row for row in rows if row["doc_id"] == args.doc_id]
    postprocess = load_postprocess(args.postprocess)

    tokenizer = AutoTokenizer.from_pretrained(args.model)
    model = AutoModelForTokenClassification.from_pretrained(args.model).eval()
    id2label = {int(key): value for key, value in model.config.id2label.items()}

    current_party = None
    absolute_offset = 0
    all_text = []
    gold_entities = []
    predicted_entities = []
    for paragraph_index, row in enumerate(rows):
        text = row["text"]
        all_text.append(text)
        marker = postprocess.detect_party_marker(text)
        if marker is not None:
            current_party = marker

        raw = predict_raw_entities(text, tokenizer, model, id2label)
        predicted = postprocess.postprocess_entities(
            text, raw, party_hint=current_party
        )
        for entity in predicted:
            if entity["role"] not in WEAK_ROLES:
                continue
            predicted_entities.append(
                {
                    **entity,
                    "paragraph_index": paragraph_index,
                    "local_start": int(entity["start"]),
                    "local_end": int(entity["end"]),
                    "start": absolute_offset + int(entity["start"]),
                    "end": absolute_offset + int(entity["end"]),
                    "party_hint": current_party,
                }
            )
        for entity in row["entities"]:
            if entity["role"] not in WEAK_ROLES or entity["end"] <= entity["start"]:
                continue
            gold_entities.append(
                {
                    **entity,
                    "paragraph_index": paragraph_index,
                    "local_start": int(entity["start"]),
                    "local_end": int(entity["end"]),
                    "start": absolute_offset + int(entity["start"]),
                    "end": absolute_offset + int(entity["end"]),
                }
            )
        absolute_offset += len(text) + 1

    key = lambda entity: (
        entity["paragraph_index"],
        entity["role"],
        entity["local_start"],
        entity["local_end"],
    )
    remaining_gold = Counter(key(entity) for entity in gold_entities)
    false_positives = []
    for entity in predicted_entities:
        entity_key = key(entity)
        if remaining_gold[entity_key] > 0:
            remaining_gold[entity_key] -= 1
        else:
            false_positives.append(entity)

    remaining_predictions = Counter(key(entity) for entity in predicted_entities)
    misses = []
    for entity in gold_entities:
        entity_key = key(entity)
        if remaining_predictions[entity_key] > 0:
            remaining_predictions[entity_key] -= 1
        else:
            misses.append(entity)

    output_dir = args.output_dir
    output_dir.mkdir(parents=True, exist_ok=True)
    (output_dir / "task25_document.txt").write_text(
        "\n".join(all_text), encoding="utf-8"
    )
    (output_dir / "task25_gold.json").write_text(
        json.dumps(gold_entities, ensure_ascii=False, indent=2), encoding="utf-8"
    )
    (output_dir / "task25_predictions.json").write_text(
        json.dumps(predicted_entities, ensure_ascii=False, indent=2),
        encoding="utf-8",
    )
    report = {
        "doc_id": args.doc_id,
        "paragraphs": len(rows),
        "method": "Task 2.5 predictor + specified postprocess; exact entity micro metrics",
        "postprocess": str(args.postprocess.resolve()),
        "metrics": exact_metrics(gold_entities, predicted_entities),
        "scoring_policies": score_entities(gold_entities, predicted_entities),
        "false_positives": false_positives,
        "misses": misses,
    }
    (output_dir / "task25_entity_metrics.json").write_text(
        json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8"
    )
    print(json.dumps(report, ensure_ascii=False, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
