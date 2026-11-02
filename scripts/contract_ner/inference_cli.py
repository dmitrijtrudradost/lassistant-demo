"""
CLI for Lassistant: document text → JSON entities on stdout.

Usage:
  python inference_cli.py --file contract.txt
  python inference_cli.py --file contract.txt --model ../../models/contract-ner/ner_model_final
  echo "..." | python inference_cli.py --stdin

Stdout: JSON array of entities (UTF-8, no extra logs).
Stderr: diagnostics / errors.
"""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

# Allow importing sibling modules when launched by absolute path from C#
_SCRIPT_DIR = Path(__file__).resolve().parent
if str(_SCRIPT_DIR) not in sys.path:
    sys.path.insert(0, str(_SCRIPT_DIR))

from inference import extract_document  # noqa: E402
from transformers import pipeline  # noqa: E402

ROOT = _SCRIPT_DIR
# Default: models/contract-ner/ner_model_final relative to repo / publish layout
_DEFAULT_CANDIDATES = [
    ROOT.parent.parent / "models" / "contract-ner" / "ner_model_1.1",
    ROOT.parent.parent / "models" / "contract-ner" / "ner_model_1.0",
    ROOT.parent.parent / "models" / "contract-ner" / "ner_model_final",
    ROOT / "ner_model_1.1",
    ROOT / "ner_model_final",
    ROOT.parent / "models" / "contract-ner" / "ner_model_1.1",
]


def resolve_model(explicit: Path | None) -> Path:
    if explicit is not None:
        p = explicit.resolve()
        if not p.is_dir():
            raise FileNotFoundError(f"Model directory not found: {p}")
        return p
    for c in _DEFAULT_CANDIDATES:
        if c.is_dir() and (c / "config.json").is_file():
            return c.resolve()
    raise FileNotFoundError(
        "NER model not found. Expected models/contract-ner/ner_model_final "
        "or pass --model PATH"
    )


def read_stdin_utf8() -> str:
    """Read stdin as UTF-8 bytes.

    On Windows the default stdin encoding is often cp1251 with surrogateescape.
    Lassistant writes UTF-8; cp1251 then turns continuation bytes (e.g. 0x98)
    into lone surrogates that break json.dumps / transformers.
    """
    raw = sys.stdin.buffer.read()
    return raw.decode("utf-8", errors="replace")


def sanitize_text(text: str) -> str:
    """Drop lone UTF-16 surrogates and other non-encodable chars."""
    return text.encode("utf-8", errors="replace").decode("utf-8")


def main() -> int:
    parser = argparse.ArgumentParser(description="Contract NER CLI for Lassistant")
    parser.add_argument("--model", type=Path, default=None)
    parser.add_argument("--file", type=Path, default=None, help="UTF-8 text file")
    parser.add_argument("--json-out", type=Path, default=None, help="Also save JSON output")
    parser.add_argument("--stdin", action="store_true", help="Read UTF-8 text from stdin")
    parser.add_argument(
        "--stdin-json",
        action="store_true",
        help="Read JSON object with text and DOCX fragments from UTF-8 stdin",
    )
    parser.add_argument(
        "--no-postprocess",
        action="store_true",
        help="Disable hybrid post-processing",
    )
    parser.add_argument(
        "--min-score",
        type=float,
        default=0.0,
        help="Drop entities with score below threshold (default 0)",
    )
    args = parser.parse_args()

    if not args.file and not args.stdin and not args.stdin_json:
        print("Provide --file, --stdin or --stdin-json", file=sys.stderr)
        return 2

    try:
        model_path = resolve_model(args.model)
    except FileNotFoundError as ex:
        print(str(ex), file=sys.stderr)
        return 3

    fragments = None
    if args.stdin_json:
        request = json.loads(read_stdin_utf8())
        text = str(request.get("text") or "")
        fragments = request.get("fragments")
        if not isinstance(fragments, list):
            fragments = []
    elif args.stdin:
        text = read_stdin_utf8()
    else:
        text = args.file.read_text(encoding="utf-8", errors="replace")

    text = sanitize_text(text)

    if not text.strip():
        print("[]")
        return 0

    try:
        ner = pipeline(
            "token-classification",
            model=str(model_path),
            tokenizer=str(model_path),
            aggregation_strategy="simple",
        )
        entities = extract_document(
            ner,
            text,
            apply_postprocess=not args.no_postprocess,
            fragments=fragments,
        )
        if args.min_score > 0:
            entities = [e for e in entities if float(e.get("score", 0)) >= args.min_score]
        # Ensure JSON-serializable plain dicts
        out = []
        for e in entities:
            out.append(
                {
                    "role": e.get("role"),
                    "text": sanitize_text(str(e.get("text") or "")),
                    "score": float(e.get("score", 0)),
                    "start": int(e.get("start", 0)),
                    "end": int(e.get("end", 0)),
                    "paragraph_index": int(e.get("paragraph_index", 0)),
                    "party_hint": e.get("party_hint"),
                    "table": e.get("table"),
                    "row": e.get("row"),
                    "cell": e.get("cell"),
                    "column": e.get("column"),
                }
            )
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
        serialized = json.dumps(out, ensure_ascii=False)
        if args.json_out is not None:
            args.json_out.parent.mkdir(parents=True, exist_ok=True)
            args.json_out.write_text(
                json.dumps(out, ensure_ascii=False, indent=2),
                encoding="utf-8",
            )
        print(serialized)
        return 0
    except Exception as ex:
        print(f"NER inference failed: {ex}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
