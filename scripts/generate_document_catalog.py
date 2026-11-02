#!/usr/bin/env python3
"""Generate config/document-catalog.json with 24 document entries."""

import json
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
OUTPUT = ROOT / "config" / "document-catalog.json"

KIND_RU = {
    "Contract": "Договор",
    "Addendum": "Допсоглашение",
    "Termination": "Соглашение о расторжении",
}

CLIENT_RU = {
    "Individual": "физлицо",
    "IndividualEntrepreneur": "ИП",
    "LegalEntity": "юрлицо",
}

CLIENT_CODE = {
    "Individual": "individual",
    "IndividualEntrepreneur": "ip",
    "LegalEntity": "legal",
}

SERVICE_RU = {
    "Montage": "монтаж",
    "FireAlarm": "пожарная сигнализация",
    "Gbr": "ГБР",
    "SecurityPost": "пост охраны",
}

SERVICE_CODE = {
    "Montage": "montage",
    "FireAlarm": "fire_alarm",
    "Gbr": "gbr",
    "SecurityPost": "security_post",
}

KIND_CODE = {
    "Contract": "contract",
    "Addendum": "addendum",
    "Termination": "termination",
}

MATRIX = {
    "Contract": {
        "Individual": ["Montage", "FireAlarm", "Gbr"],
        "IndividualEntrepreneur": ["Montage", "FireAlarm", "Gbr"],
        "LegalEntity": ["Montage", "FireAlarm", "Gbr", "SecurityPost"],
    },
    "Addendum": {
        "Individual": ["FireAlarm", "Gbr"],
        "IndividualEntrepreneur": ["FireAlarm", "Gbr"],
        "LegalEntity": ["FireAlarm", "Gbr", "SecurityPost"],
    },
    "Termination": {
        "Individual": ["FireAlarm", "Gbr"],
        "IndividualEntrepreneur": ["FireAlarm", "Gbr"],
        "LegalEntity": ["FireAlarm", "Gbr", "SecurityPost"],
    },
}

PROFILE = {
    ("Contract", "Individual"): "individual_contract",
    ("Contract", "IndividualEntrepreneur"): "ip_contract",
    ("Contract", "LegalEntity"): "legal_contract",
    ("Addendum", "Individual"): "individual_addendum",
    ("Addendum", "IndividualEntrepreneur"): "ip_addendum",
    ("Addendum", "LegalEntity"): "legal_addendum",
    ("Termination", "Individual"): "individual_termination",
    ("Termination", "IndividualEntrepreneur"): "ip_termination",
    ("Termination", "LegalEntity"): "legal_termination",
}

OUTPUT_NAME = {
    "Contract": "Договор_{name}_{service}.docx",
    "Addendum": "Допсоглашение_{name}_{service}.docx",
    "Termination": "Расторжение_{name}_{service}.docx",
}

NAME_TOKEN = {
    "Individual": "client_full_name",
    "IndividualEntrepreneur": "client_full_name",
    "LegalEntity": "company_name",
}


def build_entries() -> list[dict]:
    entries = []
    for kind, clients in MATRIX.items():
        for client, services in clients.items():
            for service in services:
                kind_code = KIND_CODE[kind]
                client_code = CLIENT_CODE[client]
                service_code = SERVICE_CODE[service]
                doc_id = f"{kind_code}_{client_code}_{service_code}"
                display = (
                    f"{KIND_RU[kind]} — {CLIENT_RU[client]} — {SERVICE_RU[service]}"
                )
                entries.append({
                    "id": doc_id,
                    "displayName": display,
                    "documentKind": kind,
                    "clientType": client,
                    "serviceKind": service,
                    "templateFile": f"{doc_id}.docx",
                    "fieldProfile": PROFILE[(kind, client)],
                    "outputNamePattern": OUTPUT_NAME[kind].format(
                        name="{" + NAME_TOKEN[client] + "}",
                        service=service_code,
                    ),
                    "isStub": True,
                })
    return entries


def main() -> None:
    entries = build_entries()
    assert len(entries) == 24, f"Expected 24 entries, got {len(entries)}"
    OUTPUT.parent.mkdir(parents=True, exist_ok=True)
    OUTPUT.write_text(
        json.dumps({"documents": entries}, ensure_ascii=False, indent=2),
        encoding="utf-8",
    )
    print(f"Created {len(entries)} entries: {OUTPUT}")


if __name__ == "__main__":
    main()
