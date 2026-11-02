# Релиз Lassistant 1.5.2

| Параметр | Значение |
|----------|----------|
| Версия | **1.5.2** |
| Сборка | `1.5.2.0` |
| Дата | 10.08.2026 |
| Платформа | Windows 10 (1809+) / 11, x64 |
| Runtime | .NET 8 (self-contained) |

## Ключевые изменения

Относительно 1.5.0 / 1.5.1:

1. **Этап A (extraction parity)** — train-compatible извлечение DOCX для NER,
   структурные fragments таблиц, точные offsets, дедупликация дублей,
   исправление C#-агрегатора для multi-value ролей.
2. **Обратная связь после ревью** — журнал `before` / `after` / `corrections`
   в `feedback/contract-ner/<сеанс>/` при сохранении шаблона; редактирование
   роли, границ («Итоговый текст»), причины и комментария.
3. **Колонка «Контекст»** — до 100 символов с каждой стороны сущности;
   текст можно выделять мышью и копировать (`Ctrl+C` / контекстное меню),
   без возможности изменить содержимое.

## Состав поставки

```
Lassistant_v1.5.2_win-x64/
├── Lassistant.exe
├── config/                 — contract-ner.json, llm-settings.json, …
├── models/contract-ner/    — ner_model_final (веса + tokenizer + config)
├── scripts/contract_ner/   — inference_cli.py, postprocess.py
├── templates/
├── docs/                   — CHANGELOG, LOCAL_CONTRACT_NER.md, RELEASE_v1.5.2.md
└── README.txt              — при наличии
```

## Установка Python для NER

```bat
cd scripts\contract_ner
python -m venv ..\..\python-venv-contract-ner
..\..\python-venv-contract-ner\Scripts\pip install -r requirements-infer.txt
```

## Критерии приёмки

- В «О программе» / свойствах EXE отображается версия **1.5.2** / `1.5.2.0`
- На вкладке «Шаблоны» после NER: контекст копируется, окно ≥ 100 символов сбоку
- Сохранение шаблона создаёт каталог обратной связи рядом с приложением
- Без модели: сообщение «модуль NER не установлен», приложение не падает

## Документы

- [Локальный Contract NER](LOCAL_CONTRACT_NER.md)
- [Сверка Этапа A](EXP001_FINAL_RECONCILIATION_2026-08-09.md)
- [Тест на реальном договоре](STAGE_A_REAL_CONTRACT_TEST_2026-08-09.md)
