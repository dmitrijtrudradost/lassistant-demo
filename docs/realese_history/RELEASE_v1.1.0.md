# Релиз Lassistant 1.1.0

| Параметр | Значение |
|----------|----------|
| Версия | **1.1.0** |
| Сборка | `1.1.0.0` |
| Дата | 17.06.2026 |
| Платформа | Windows 10 (1809+) / 11, x64 |
| Runtime | .NET 8 (self-contained в `publish/win-x64`) |

## Состав поставки

```
publish/win-x64/
├── Lassistant.exe
├── config/
│   ├── anonymization-rules.json
│   ├── anonymization-policy.json
│   ├── anonymization-providers.json
│   ├── own-organizations.json
│   └── field-mapping.json
├── templates/
└── docs/
```

## Ключевые изменения относительно 1.0

1. Модуль **анонимизации** (отдельная вкладка и проект `Lassistant.Anonymization`)
2. Обратимая замена ПДн с mapping
3. 19 категорий детекции, валидаторы ИНН/ОГРН/СНИЛС
4. Стабильная замена в Word (все вхождения, нормализация текста)

## Проверенные сценарии

- Договор КС 280-ГБР (частично анонимизированный docx)
- Договор КГ Сергеева Е.В. (полный docx, агрессивный режим)
- Жёлтая разметка юриста → сверка с правилами

## Сборка

```bat
scripts\publish.bat
```

## Документы релиза

- [Руководство пользователя](rukovodstvo_polzovatelya.md)
- [Архитектура v1.1](arkhitektura_v1.1.md)
- [История версий](CHANGELOG.md)
- [ТЗ анонимизации](ТЗ_анонимизация_v1.1.md)
