Lassistant 1.5.4.2 — Помощник юриста
====================================

Платформа: Windows 10 (1809+) / 11, x64
Дата сборки: 13.08.2026

НАЗНАЧЕНИЕ
----------
Зафиксированная стабильная сборка до эксперимента NER (стороны/границы).
Точка отката. Следующая экспериментальная версия: 1.5.4.3.

СОСТАВ
------
Lassistant.exe              — приложение (self-contained)
config\                     — настройки (executors.json, contract-ner.json, …)
models\contract-ner\        — локальная NER-модель
scripts\contract_ner\       — CLI инференса
templates\                  — шаблоны документов (в т.ч. договор_юрлицо_гбр.docx)
docs\                       — документация

НОВОЕ ОТНОСИТЕЛЬНО 1.5.4
-----------------------
- Шаблон и форма: договор юрлицо / ГБР
- Подгруппы полей на вкладке «Создать документ»
- Дополнен справочник исполнителей
- Убраны лишние поля исполнителя с формы

ЗАПУСК
------
Распакуйте архив и запустите Lassistant.exe

НАСТРОЙКА NER
-------------
Установите Python 3.11+ и зависимости:
  cd scripts\contract_ner
  python -m venv ..\..\python-venv-contract-ner
  ..\..\python-venv-contract-ner\Scripts\pip install -r requirements-infer.txt

Подробнее: docs\LOCAL_CONTRACT_NER.md и docs\RELEASE_v1.5.4.2.md
