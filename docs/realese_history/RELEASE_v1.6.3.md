# Lassistant 1.6.3

Дата: 2026-09-21  
Базовая линия: **1.6.2**  
Модуль NER: **1.1.1** (без смены весов)

## Summary

- Удалена regex-анонимизация: вкладка, ViewModel, библиотека правил, конфиги и отладочный CLI.
- NER-анонимизация переименована в **«Анонимизация»** — в приложении осталась одна вкладка анонимизации.
- Проект `Lassistant.Anonymization` расформирован. Обратная подстановка (`AnonymizationMapping`, `MappingFileService`, `DocxDeAnonymizer`) перенесена в `Lassistant.Templates`.
- Из графа зависимостей ушёл `PdfSharp`; `PdfPig` остался только в `Lassistant.Extractors` для извлечения текста из PDF.
- Устранена лишняя загрузка `anonymization-rules.json` при старте приложения.
- Документация приведена к 1.6.3: INSTALL_v1.6.3, руководство пользователя, словарь полей, генератор PDF.

## Удалено

- `src/Lassistant.Anonymization/` — проект целиком (regex-детектор, распознавание документов, PDF-анонимизация, конфиги правил)
- `src/Lassistant.App/ViewModels/AnonymizationViewModel.cs`
- Вкладка «Анонимизация» (regex) в `MainWindow.xaml` и её обработчики в code-behind
- `scripts/AnonDetectCli/`
- `config/anonymization-rules.json`, `config/own-organizations.json`, `config/anonymization-policy.json`, `config/anonymization-providers.json`
- Неиспользуемые пути в `AppPaths`: `AnonymizationRulesPath`, `AnonymizationPolicyPath`, `OwnOrganizationsPath`

## Совместимость

Настройки пользователя (`user/settings/`) на удалённую вкладку не ссылались — миграция не требуется. Формат `*.mapping.json` не изменился, файлы, созданные NER-анонимизацией в 1.6.2, восстанавливаются в 1.6.3.

## Проверка

1. Настройки → Общие: версия программы **1.6.3**, версия модуля NER (например 1.1.1).
2. В окне одна вкладка анонимизации с заголовком **«Анонимизация»**.
3. «Анонимизация»: `.docx` → «Разобрать NER» → «Анонимизировать» → «Сохранить» — документ с `{{роль}}` и `*.mapping.json`.
4. «Восстановить из mapping…» возвращает исходный текст.
5. «Создать документ» и «Шаблоны» работают как прежде.
6. «О программе» открывает `INSTALL_v1.6.3.html` и руководство пользователя.
