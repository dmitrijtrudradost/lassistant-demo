# Релиз Lassistant 1.5.0

| Параметр | Значение |
|----------|----------|
| Версия | **1.5.0** |
| Сборка | `1.5.0.0` |
| Дата | 09.08.2026 |
| Платформа | Windows 10 (1809+) / 11, x64 |
| Runtime | .NET 8 (self-contained) |

## Ключевые изменения

Основной путь создания шаблона — **локальный Contract NER** (ПДн не уходят в интернет):

1. Загрузить договор `.docx`
2. **Разобрать локально (NER)** → таблица ролей
3. Ревью → **Сохранить шаблон** с `{{роль}}`

LLM остаётся запасным режимом (цветная разметка + анонимизация `_for_llm`).

## Состав поставки

```
Lassistant_v1.5.0_win-x64/
├── Lassistant.exe
├── config/                 — contract-ner.json, llm-settings.json, …
├── models/contract-ner/    — ner_model_final (веса + tokenizer + config)
├── scripts/contract_ner/   — inference_cli.py, postprocess.py
├── templates/
├── docs/                   — CHANGELOG, LOCAL_CONTRACT_NER.md, …
└── README.txt
```

## Установка Python для NER

```bat
cd scripts\contract_ner
python -m venv ..\..\python-venv-contract-ner
..\..\python-venv-contract-ner\Scripts\pip install -r requirements-infer.txt
```

## Критерии приёмки

- На контрольном договоре охраны (ИП) без интернета: сущности в UI, шаблон с `{{заказчик_фио}}`, `{{дата_договора}}`
- Без модели: сообщение «модуль NER не установлен», приложение не падает
- В «О программе» отображается версия **1.5.0**

## Документы

- [CHANGELOG](CHANGELOG.md)
- [Локальный Contract NER](LOCAL_CONTRACT_NER.md)
