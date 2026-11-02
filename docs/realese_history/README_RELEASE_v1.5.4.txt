Lassistant 1.5.4 — Помощник юриста
==================================

Платформа: Windows 10 (1809+) / 11, x64
Дата сборки: 10.08.2026

СОСТАВ
------
Lassistant.exe              — приложение (self-contained)
config\                     — настройки (executors.json, contract-ner.json, …)
models\contract-ner\        — локальная NER-модель
scripts\contract_ner\       — CLI инференса
templates\                  — шаблоны документов
docs\                       — документация

НОВОЕ В 1.5.4
-------------
- Форма «Создать документ» по {{плейсхолдерам}} шаблона
- Справочник исполнителей (до 3 карточек)
- Карточки заказчика: data\заказчики\*.md (сохранение и импорт через drag-and-drop)
- Пилот: Договор ИП — пожарная сигнализация

ЗАПУСК
------
Распакуйте архив и запустите Lassistant.exe

НАСТРОЙКА NER
-------------
Установите Python 3.11+ и зависимости:
  cd scripts\contract_ner
  python -m venv ..\..\python-venv-contract-ner
  ..\..\python-venv-contract-ner\Scripts\pip install -r requirements-infer.txt

Подробнее: docs\LOCAL_CONTRACT_NER.md и docs\RELEASE_v1.5.4.md
