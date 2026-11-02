# Отчёт для дальнейшего ИИ-анализа Contract NER

Дата фиксации: 2026-08-09  
Система: Lassistant / ПЮ, версия поставки 1.5.0  
Назначение: передача следующему ИИ проверенного контекста, проблем, гипотез и плана работ по повышению качества поиска именованных ролевых сущностей в договорах.

## 1. Главный вывод

Низкое качество на реальном договоре объясняется сочетанием двух независимых факторов:

1. Модель обучена на узком синтетическом корпусе одного шаблонного семейства.
2. Приложение подаёт таблицы DOCX модели в ином формате, чем использовался при обучении.

Порог score, C#-агрегация и размер базовой модели влияют на результат, но не являются первопричинами. До исправления входного представления и создания реального gold-набора смена `rubert-tiny2` на более крупную модель не имеет доказанной целесообразности.

## 2. Область анализа

Проанализированы:

- обучающий проект `E:\ИИ_обучение\project_11_local_AI\ner_training`;
- модель `cointegrated/rubert-tiny2`, fine-tuned под 43 ролевых слота;
- отчёты обучения от 2026-07-28;
- train/val JSONL;
- инференс Python и гибридная постобработка;
- C#-интеграция Lassistant;
- реальный тестовый договор:
  `sample-contract.docx`;
- ранее зафиксированный реальный инференс:
  `ner_training\inference_results\lavrienko_anon_20260728`.

## 3. Текущее назначение модели

Это не универсальная NER-модель общего назначения.

Модель выполняет token classification по BIO-схеме и выбирает только одну из 43 заранее известных договорных ролей:

- номер и дата договора;
- реквизиты исполнителя;
- реквизиты заказчика;
- лицензия;
- объект охраны;
- цена;
- банковские и идентификационные реквизиты.

Она не умеет открывать новые типы сущностей и не строит полноценное семантическое представление договора. На незнакомом контексте модель вынуждена выбирать ближайшую известную роль либо `O`.

## 4. Зафиксированные метрики

### 4.1. Данные обучения

```yaml
base_model: cointegrated/rubert-tiny2
roles: 43
bio_labels: 87
synthetic_documents: 20
entities_total: 1800
entities_per_document: 90
train_documents: 16
validation_documents: 4
train_paragraphs: 3776
validation_paragraphs: 944
customer_type: IP_only
contract_family: security_contract_single_template_family
```

### 4.2. Raw NER на synthetic validation

```yaml
raw_macro_f1: 0.812729
raw_micro_f1: 0.874667
raw_weighted_f1: 0.882070
```

Провальные или слабые raw-роли:

- `заказчик_инн`: F1 0.00, support 4;
- `исполнитель_инн`: F1 0.00, support 4;
- `исполнитель_банк`: F1 0.00, support 4;
- `исполнитель_огрн`: F1 0.00, support 4;
- `исполнитель_корр_счет`: F1 0.25, support 4;
- `исполнитель_город_банка`: F1 0.25, support 4;
- `заказчик_банк`: F1 0.43, support 4;
- `объект_тип`: F1 0.57, support 8;
- `заказчик_адрес`: F1 0.67, support 4;
- `исполнитель_расчетный_счет`: F1 0.73, support 4.

### 4.3. NER + postprocess на том же validation

```yaml
postprocess_macro_f1: 0.972457
postprocess_micro_f1: 0.972376
postprocess_weighted_f1: 0.970665
weak_gold_exact_before: 18
weak_gold_exact_after: 44
weak_gold_total: 44
```

Важно: правила постобработки разрабатывались и проверялись на тех же четырёх validation-документах. Значение 0.97 нельзя считать подтверждённой production-метрикой.

### 4.4. Реальный договор Лавриенко

```yaml
entities: 104
roles_found: 40
roles_total: 43
mean_score: 0.6476
entities_below_0_5: 29
share_below_0_5_percent: 27.9
```

Системные ошибки:

- смешение исполнителя и заказчика;
- фрагменты банков и счетов;
- обрезанные ФИО;
- WordPiece-артефакты;
- ошибочные границы адресов.

### 4.5. Текущий тестовый договор в Lassistant

Наблюдения, не являющиеся gold-метрикой:

```yaml
python_postprocess_rows_at_score_0_05: 105
raw_rows_without_postprocess: 106
production_rows_observed_in_ui: 26
```

Большое число строк не означает высокое качество: значительная часть кандидатов имеет низкий score либо неверную роль/границы.

## 5. Архитектура текущего инференса

```text
DOCX
  -> DocxPlainTextExtractor.cs
  -> UTF-8 stdin
  -> inference_cli.py
  -> HuggingFace token-classification pipeline
  -> inference.extract_document (по строкам)
  -> optional postprocess.py
  -> JSON entities
  -> optional NerEntityAggregator.cs
  -> UI review
  -> NerSpanToTemplateConverter
```

Поддерживаются три режима:

- `production`: Python postprocess + C# aggregator;
- `python_postprocess`: Python postprocess без C# aggregator;
- `raw`: без Python postprocess и C# aggregator.

Минимальный score выбирается в UI до запуска. Значение 0.00 отключает фильтрацию.

## 6. Реестр подтверждённых проблем

### NER-001 — несовпадение сериализации таблиц

Приоритет: P0  
Статус: подтверждено кодом  

Обучение:

- `ner_training\align_prototype.py`;
- каждый paragraph внутри каждой table cell выдаётся отдельно;
- модель обучается на отдельных абзацах/ячейках.

Приложение:

- `src\Lassistant.Templates\DocxPlainTextExtractor.cs`;
- все абзацы ячейки схлопываются в одну строку;
- все ячейки table row объединяются через ` | `.

Последствия:

- исполнитель и заказчик могут оказаться в одном NER-абзаце;
- метки и значения разных колонок смешиваются;
- теряются внутренние переносы;
- возникают контексты, отсутствующие в train;
- длинные строки таблиц могут обрезаться tokenizer/model limit.

Рекомендуемое исправление:

- сделать отдельный extraction mode для NER;
- выдавать одну строку на paragraph каждой cell;
- повторить порядок и правила `iter_paragraphs_with_tables` из обучения;
- не использовать ` | ` для NER-входа.

### NER-002 — party tracker не соответствует табличной структуре

Приоритет: P0  
Статус: подтверждено кодом и наблюдениями  

`postprocess.detect_party_marker` ожидает отдельную строку `ИСПОЛНИТЕЛЬ` или `ЗАКАЗЧИК`. Строка вида `ИСПОЛНИТЕЛЬ | ЗАКАЗЧИК` не задаёт корректную сторону. Состояние `current_party` может «залипать».

Последствия:

- ИНН/ОГРН/банк получают неправильный префикс стороны;
- отображение стороны в UI может выглядеть исправленным по имени роли, но исходная классификация остаётся неверной.

Рекомендуемое исправление:

- передавать cell/column context отдельно;
- для двухколоночных реквизитов определять сторону по колонке;
- не использовать один глобальный party state для строки с обеими сторонами.

### NER-003 — address postprocess захватывает соседние поля

Приоритет: P0  
Статус: подтверждено кодом и скриншотами  

`expand_address_span` расширяет адрес до конца строки. После объединения table row концом строки становится конец всей строки таблицы, включая email, телефон и соседние реквизиты.

Рекомендуемое исправление:

- сначала исправить NER-экстрактор таблиц;
- дополнительно ограничивать адрес границей cell/field delimiter;
- добавить реальные тесты на адрес рядом с email/телефоном.

### NER-004 — synthetic template leakage

Приоритет: P0  
Статус: подтверждено аудитом данных  

Зафиксировано:

- 233 идентичных текста абзацев встречаются и в train, и в validation;
- 70 из них содержат сущности в обоих split;
- 127 групп имеют одинаковый текст и одинаковую разметку;
- документы построены из одного шаблонного семейства;
- меняются главным образом значения слотов.

Следствие: validation измеряет запоминание шаблона значительно сильнее, чем перенос на новый договор.

Рекомендуемое исправление:

- split по шаблону/источнику, а не только по document id;
- запретить одинаковые normalized paragraphs в train и test;
- использовать leave-one-template-out evaluation.

### NER-005 — недостаточное разнообразие данных

Приоритет: P0  
Статус: подтверждено  

Корпус содержит:

- только ИП-заказчиков;
- один тип договора;
- четыре банка;
- два города банка;
- пять типов объектов;
- десять ФИО заказчиков;
- в 20 из 20 документов одинаковый банк у исполнителя и заказчика.

Рекомендуемое исправление:

- добавить ООО, ИП и физических лиц;
- добавить разные банки сторон;
- добавить разные структуры реквизитов и таблиц;
- добавить документы разных организаций и редакций;
- включить реальные обезличенные документы.

### NER-006 — weighted F1 скрывает слабые роли

Приоритет: P1  
Статус: подтверждено  

64.4% validation-сущностей относятся к лёгким ролям с почти идеальным raw F1. Для 25 из 43 ролей support равен только четырём.

Рекомендуемые основные метрики:

- macro entity-level exact F1;
- per-role precision/recall/F1;
- confusion matrix по стороне;
- boundary error rate;
- false positives per document;
- recall обязательных ролей;
- leave-one-template-out score.

### NER-007 — postprocess переоценён

Приоритет: P1  
Статус: подтверждено  

Постобработка повышает val weighted F1 до 0.97, но правила откалиброваны на том же validation. Она исправляет формат и сторону только при выполнении предположений о структуре.

Рекомендуемое исправление:

- отдельный untouched test set;
- ablation каждого правила;
- метрики raw и postprocess публиковать раздельно;
- не называть postprocess F1 качеством самой модели.

### NER-008 — C# aggregator скрывает альтернативы

Приоритет: P1  
Статус: подтверждено кодом  

В production для большинства ролей остаётся только сущность с максимальным score. Правильный повтор с меньшим score может быть удалён.

Рекомендуемое исправление:

- оставить диагностический режим;
- для production группировать по normalized value, section и party;
- разрешить несколько значений для повторяемых ролей;
- логировать причины удаления кандидата.

### NER-009 — артефакты text extraction и tokenizer

Приоритет: P1  
Статус: частично подтверждено  

Риски:

- `\r` остаётся после split только по `\n`;
- `TabChar`, `Break` и некоторые OpenXML-элементы не извлекаются;
- `aggregation_strategy="simple"` создаёт пробелы и `##`;
- длинные строки обрабатываются без явного stride;
- exact replacement в DOCX может не найти нормализованный NER-текст.

Рекомендуемое исправление:

- нормализовать `\r\n` в `\n`;
- сохранять tabs/breaks либо единообразно нормализовать train/inference;
- реализовать sliding window/stride;
- использовать исходные offsets и текст документа для replacement, а не восстановленную строку HF.

## 7. Приоритетный план улучшений

### Этап A — исправить вход модели

Цель: train и inference должны получать одинаковое представление DOCX.

Задачи:

1. Реализовать NER-specific extractor с одной строкой на cell paragraph.
2. Добавить сохранение диагностического `input.txt`.
3. Нормализовать переносы, tabs и breaks.
4. Добавить идентификаторы paragraph/table/row/cell/column.
5. Передавать party context из структуры таблицы.
6. Добавить тесты на вложенные и объединённые ячейки.

Критерий завершения:

- для одного synthetic DOCX текст приложения совпадает с текстом training extractor по строкам и порядку;
- нет строк, одновременно содержащих реквизиты обеих сторон;
- address postprocess не захватывает соседнюю cell.

### Этап B — создать честный real gold set

Минимум:

- 10 реальных обезличенных договоров для первого baseline;
- 3–5 разных шаблонных семейств;
- ИП, ООО и физические лица;
- ручная разметка всех обязательных ролей;
- двойная проверка разметки человеком.

Рекомендуемый устойчивый объём:

- 50–100 документов для первого production-цикла;
- отдельный immutable test set;
- не менее 20–30 примеров каждой критичной роли;
- больше примеров для банка, адреса, ФИО, счетов и side confusion.

### Этап C — пересчитать baseline

Для каждого документа запускать:

1. raw, score 0.00;
2. Python postprocess, score 0.00;
3. production, score 0.00;
4. production при рабочих thresholds.

Сохранять:

- входной текст;
- raw JSON;
- postprocess JSON;
- production JSON;
- удалённые aggregator candidates;
- latency;
- модель/config hash.

### Этап D — исправить правила до переобучения

Приоритет правил:

1. party по cell/column;
2. границы адресов;
3. числовые идентификаторы по regex + контрольным длинам;
4. банк по label/cell;
5. телефон/email по детерминированным правилам;
6. дедупликация с сохранением альтернатив.

### Этап E — расширить и переобучить

Требования к split:

- group split по template family;
- дедупликация boilerplate;
- запрет normalized-text overlap между train и test;
- untouched real test;
- фиксированные seeds и dataset manifest.

Сначала повторить `rubert-tiny2`. Переход на более крупную модель рассматривать только после подтверждения, что качество ограничено capacity, а не данными/экстрактором.

## 8. Рекомендуемая гибридная архитектура

Для структурированных реквизитов чистая token NER не является оптимальным единственным инструментом.

Рекомендуемый pipeline:

```text
DOCX structural extraction
  -> document blocks with table/cell metadata
  -> section and party resolver
  -> deterministic candidate extractors
       INN / OGRN / KPP / BIK / accounts / email / phone / dates
  -> NER for contextual roles
       names / organizations / addresses / object type / license context
  -> candidate reconciliation
  -> confidence calibration
  -> human review
```

Преимущества:

- regex обеспечивает границы числовых полей;
- структура таблицы обеспечивает сторону;
- NER решает неоднозначные текстовые роли;
- human review остаётся последним защитным слоем.

## 9. Экспериментальная матрица

### EXP-001 — extraction parity

Гипотеза: cell-level extraction повысит точность банковских и идентификационных ролей.

Сравнить:

- текущий row-joined input;
- training-compatible cell paragraph input.

Основные метрики:

- exact F1 по weak roles;
- party confusion;
- address overcapture rate.

### EXP-002 — raw vs postprocess

Гипотеза: часть ошибок создаётся или расширяется postprocess.

Сравнить raw и Python postprocess на одной ручной разметке.

Классифицировать ошибки:

- exact;
- boundary;
- confusion;
- miss;
- false positive.

### EXP-003 — aggregator impact

Гипотеза: production скрывает правильные альтернативы.

Сравнить Python postprocess и production:

- сколько gold-сущностей удалено;
- сколько ложных дублей удалено;
- score правильного и оставленного кандидата.

### EXP-004 — template holdout

Гипотеза: текущая val-метрика завышена template leakage.

Обучить на нескольких шаблонах, один шаблон полностью исключить в test.

### EXP-005 — party resolver

Гипотеза: column-aware party context устранит большую часть confusion.

Сравнить:

- sticky text marker;
- explicit table column/cell party.

## 10. Формат будущего evaluation report

Каждый новый отчёт должен содержать:

```yaml
run_id: unique
date: ISO-8601
model_name: string
model_hash: string
dataset_manifest_hash: string
extractor_version: string
postprocess_version: string
aggregator_version: string
processing_mode: raw|python_postprocess|production
min_score: float
documents:
  total: integer
  template_families: integer
  customer_types:
    ip: integer
    ooo: integer
    person: integer
metrics:
  entity_exact_macro_f1: float
  entity_exact_micro_f1: float
  boundary_error_rate: float
  party_confusion_rate: float
  false_positives_per_document: float
  mandatory_role_recall: float
per_role: required
error_samples: required
```

## 11. Definition of Done для production NER

Предварительные целевые значения необходимо подтвердить бизнесом:

- entity-level exact macro F1 не ниже 0.85 на untouched real test;
- recall обязательных ролей не ниже 0.95;
- party confusion не выше 2%;
- false positives не более 3 на документ после production aggregation;
- address overcapture отсутствует на тестовых таблицах;
- шаблон сохраняется без ручного исправления границ для не менее 90% high-confidence сущностей;
- все результаты воспроизводимы по model/dataset/config hash.

## 12. Артефакты для следующего ИИ

Обучение:

- `E:\ИИ_обучение\project_11_local_AI\ner_training\train_ner.py`
- `E:\ИИ_обучение\project_11_local_AI\ner_training\dataset_builder.py`
- `E:\ИИ_обучение\project_11_local_AI\ner_training\align_prototype.py`
- `E:\ИИ_обучение\project_11_local_AI\ner_training\train_dataset.jsonl`
- `E:\ИИ_обучение\project_11_local_AI\ner_training\val_dataset.jsonl`
- `E:\ИИ_обучение\project_11_local_AI\ner_training\reports\2026-07-28\`
- `E:\ИИ_обучение\project_11_local_AI\ner_training\inference_results\lavrienko_anon_20260728\`

Приложение:

- `src\Lassistant.Templates\DocxPlainTextExtractor.cs`
- `src\Lassistant.Templates\ContractNerService.cs`
- `src\Lassistant.Templates\NerEntityAggregator.cs`
- `src\Lassistant.Templates\NerSpanToTemplateConverter.cs`
- `scripts\contract_ner\inference_cli.py`
- `scripts\contract_ner\inference.py`
- `scripts\contract_ner\postprocess.py`
- `src\Lassistant.App\ViewModels\TemplateEditorViewModel.cs`
- `config\contract-ner.json`
- `docs\LOCAL_CONTRACT_NER.md`

Аудиты Cursor:

- аудит обучения: agent `179d1a87-bd2c-499c-9f2d-2bfb7875071f`;
- аудит инференса: agent `86de060b-2bb9-4bee-bd37-5d188a8b3aed`.

## 13. Нерешённые вопросы

1. Является ли тестовый договор заказчиком-ИП, ООО или физическим лицом?
2. Сколько реально используемых шаблонных семейств должно поддерживаться?
3. Какие роли обязательны для автоматического создания шаблона?
4. Допустимы ли несколько значений одной роли?
5. Как бизнес оценивает критичность false positive относительно miss?
6. Должна ли модель анализировать колонтитулы, textbox и structured document tags?
7. Есть ли возможность собрать обезличенный real gold corpus?

## 14. Рекомендуемый промпт следующему ИИ

```text
Изучи docs/NER_SYSTEM_AI_ANALYSIS_REPORT_2026-08-09.md и перечисленные в нём
артефакты. Не начинай с замены модели. Сначала:

1. воспроизведи baseline на одном synthetic и одном real DOCX;
2. сравни training extractor и DocxPlainTextExtractor построчно;
3. спроектируй cell-level NER extraction с table/cell/column metadata;
4. подготовь тесты extraction parity и party resolution;
5. предложи схему real gold dataset и entity-level evaluation;
6. оцени raw, Python postprocess и C# aggregator отдельно.

Каждый вывод подтверждай путём к файлу, метрикой или минимальным
воспроизводимым примером. Не используй synthetic val F1=0.97 как production
оценку без untouched real test.
```

