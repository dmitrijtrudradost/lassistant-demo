# Локальный модуль Contract NER для Lassistant

## Назначение

Извлекает ролевые слоты из текста договора охраны (заказчик = ИП)
и возвращает JSON для подстановки `{{роль}}` в шаблон.

Обучение выполнялось на Linux; инференс на Windows 11 — штатный сценарий.

## Состав

| Путь | Назначение |
|------|------------|
| `inference_cli.py` | CLI: файл/stdin → JSON в stdout |
| `inference.py` / `postprocess.py` | Инференс + правила |
| `../../models/contract-ner/ner_model_final/` | Веса + tokenizer + config + labels |
| `requirements-infer.txt` | Зависимости только для инференса |

## Установка (Windows 11)

```bat
cd scripts\contract_ner
python -m venv ..\..\python-venv-contract-ner
..\..\python-venv-contract-ner\Scripts\pip install -r requirements-infer.txt
```

Проверка:

```bat
..\..\python-venv-contract-ner\Scripts\python inference_cli.py --file sample.txt
```

## Формат ответа

```json
[
  {
    "role": "заказчик_инн",
    "text": "489396198954",
    "score": 0.95,
    "start": 12,
    "end": 24,
    "paragraph_index": 182,
    "party_hint": "заказчик"
  }
]
```

## Ограничения

- Тип договора: охрана; заказчик — ИП (ООО/ФЛ — после дообучения).
- Нужны заголовки секций ИСПОЛНИТЕЛЬ / ЗАКАЗЧИК.
- Результат всегда требует ревью юристом.
