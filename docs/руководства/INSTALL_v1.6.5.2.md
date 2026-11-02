# Установка Lassistant 1.6.5.2 на удалённый компьютер (Windows 10)

**Продукт:** Lassistant — Помощник юриста  
**Версия:** **1.6.5.2**  
**Модуль NER:** **1.1.1** (`ner_model_1.1`; откат `ner_model_1.0`)  
**Платформа:** Windows 10 (1809+) x64 (также Windows 11 x64)  
**Архив поставки:** `release/Lassistant_v1.6.5.2_win-x64.zip`  
**Дата инструкции:** 24.09.2026

Инструкция для переноса готовой сборки на другой ПК (удалённый рабочий стол, флешка, сетевая папка) и настройки модулей, включая локальный NER.

Версия для чтения в браузере и печати: `docs\руководства\INSTALL_v1.6.5.2.html`.

---

## 0. Краткий порядок действий

1. Скопировать на удалённый ПК архив `Lassistant_v1.6.5.2_win-x64.zip`.
2. Распаковать целиком в постоянную папку.
3. Запустить `Lassistant.exe` → **«О программе»** или **Настройки → Общие**: версия **1.6.5.2**.
4. Для **«Шаблоны»** / **«Анонимизация»**: установить Python 3.12 (или 3.11) и создать `python-venv-contract-ner` (раздел **5**).
5. Проверить NER на тестовом `.docx`.

**Пример рабочей папки:**  
`C:\Program Files (x86)\Lassistant\Lassistant_v1.6.5.2_win-x64`

---

## 1. Что входит в поставку

`Lassistant_v1.6.5.2_win-x64.zip` → после распаковки `Lassistant_v1.6.5.2_win-x64\`

| Путь | Назначение |
|------|------------|
| `Lassistant.exe` | Приложение (self-contained, .NET 8 **встроен**) |
| `config\` | Каталог документов, канон исполнителей, `contract-ner.json`, роли NER |
| `models\contract-ner\ner_model_1.1\` | Модель по умолчанию (пакет 1.1.1) |
| `models\contract-ner\ner_model_1.0\` | Откат |
| `scripts\contract_ner\` | Python-скрипты инференса и постобработки |
| `templates\` | Шаблоны договоров `.docx` |
| `docs\руководства\` | Эта инструкция, руководство пользователя, словарь полей |
| `docs\правила\` | Канон ролей NER (`ROLE_SCHEMA_v1.1.1.md`) |

**Не входят в zip / не копируются publish:** папка **`user\`** (создаётся при первом запуске). В поставку **не** кладутся `docs\изменения`, `docs\realese_history`, CHANGELOG.

**Отдельно ставить .NET Runtime / SDK не нужно.**

В эту сборку входят:

- личные данные в `user\` (роли, карточки, overlay исполнителей) — **не затираются** повторным publish / обновлением файлов программы;
- «Создать документ»: поля исполнителя с выбранной карточки + плейсхолдеры шаблона; имя файла и красная заливка пустых `{{ключ}}`.

---

## 2. Требования к удалённому ПК

| Требование | Значение |
|------------|----------|
| ОС | Windows 10 **1809+** или Windows 11, **x64** |
| Разрядность | Только 64-bit |
| Место на диске | Ориентир **2–4 ГБ** (приложение + модель + Python venv с torch) |
| Права | Для папки в `Program Files (x86)` — **cmd от имени администратора** при создании venv |
| Сеть | Для копирования архива; для NER — **один раз** интернет (Python и `pip install`) |
| Python (для NER) | **3.11 или 3.12, 64-bit** (проверено: **Python 3.12.8**) |
| OCR (опционально) | Русский языковой пакет Windows OCR — для сканов/фото |

Без Python работает: **«Создать документ»**.  
С Python/venv: **«Шаблоны»**, **«Анонимизация»**.

---

## 3. Перенос архива на удалённый компьютер

### 3.1. Курьер / флешка / внешний диск

1. На исходном ПК: `…\agent_sosdat_dogovor\release\Lassistant_v1.6.5.2_win-x64.zip`
2. Скопируйте на носитель.
3. На удалённом ПК скопируйте zip к месту установки.

### 3.2. Сетевая папка / RDP

Скопируйте zip **локально** (не запускайте exe с сетевого диска без необходимости).

### 3.3. Проверка целостности (рекомендуется)

```powershell
Get-Item "C:\Users\Public\Downloads\Lassistant_v1.6.5.2_win-x64.zip" |
  Select-Object FullName, Length, LastWriteTime
```

---

## 4. Установка приложения (без .NET)

1. Закройте старые окна Lassistant.
2. Распакуйте архив в постоянную папку, например:
   - `C:\Program Files (x86)\Lassistant\Lassistant_v1.6.5.2_win-x64\`
   - `C:\Lassistant\`
3. **Не разделяйте** содержимое: рядом с exe должны быть `config`, `models`, `scripts`, `templates`, `docs`.
4. Запуск: `Lassistant.exe`.
5. Проверка: **«О программе»** → **Версия: 1.6.5.2** или **Настройки → Общие**.

При первом запуске создаётся `user\` (настройки, роли, карточки заказчика).

SmartScreen: «Подробнее» → «Выполнить в любом случае» / исключение антивируса.

---

## 5. Установка Contract NER (проверенный сценарий)

Рабочая папка-пример:  
`C:\Program Files (x86)\Lassistant\Lassistant_v1.6.5.2_win-x64`

### 5.1. Python: что не сработало / что сработало

**winget** часто отсутствует — для Lassistant не обязателен.

Не склеивайте `curl` и установщик в одну строку без `&&` / Enter (ошибка `Could not resolve host: InstallAllUsers=1`).

### 5.2. Скачать установщик Python 3.12 (cmd)

```bat
curl -L -o "%TEMP%\python312.exe" https://www.python.org/ftp/python/3.12.8/python-3.12.8-amd64.exe
dir "%TEMP%\python312.exe"
```

Файл должен быть **десятки мегабайт**, не ~146 байт. Иначе скачайте в браузере:  
https://www.python.org/ftp/python/3.12.8/python-3.12.8-amd64.exe

### 5.3. Тихая установка Python

```bat
"%TEMP%\python312.exe" /quiet InstallAllUsers=1 PrependPath=1 Include_test=0
```

Закройте cmd, откройте **новое**:

```bat
python --version
```

Ожидается `Python 3.12.8` (допустимы 3.11.x / другие 3.12.x).

### 5.4. Создание venv и зависимостей NER

Папка в `Program Files (x86)`: **cmd от имени администратора**.

```bat
cd /d "C:\Program Files (x86)\Lassistant\Lassistant_v1.6.5.2_win-x64\scripts\contract_ner"
python -m venv ..\..\python-venv-contract-ner
..\..\python-venv-contract-ner\Scripts\pip install -U pip
..\..\python-venv-contract-ner\Scripts\pip install -r requirements-infer.txt
```

Последняя команда может идти **10–30 минут**. Итог: рядом с exe папка `python-venv-contract-ner\Scripts\python.exe`.

Если приложение в `C:\Lassistant\`:

```bat
cd /d C:\Lassistant\scripts\contract_ner
python -m venv ..\..\python-venv-contract-ner
..\..\python-venv-contract-ner\Scripts\pip install -U pip
..\..\python-venv-contract-ner\Scripts\pip install -r requirements-infer.txt
```

### 5.5. Проверка состава NER

- [ ] `models\contract-ner\ner_model_1.1\` (веса, config, tokenizer, `labels.json`)
- [ ] `models\contract-ner\ner_model_1.0\` (откат)
- [ ] `scripts\contract_ner\`
- [ ] `config\contract-ner.json`
- [ ] `python-venv-contract-ner\Scripts\python.exe`

Каталог модели выбирается в **Настройки → NER**.

### 5.6. Проверка в приложении

1. `Lassistant.exe`
2. **«О программе»** / **Настройки → Общие** → **1.6.5.2**, версия модуля NER (например 1.1.1)
3. **«Шаблоны»** или **«Анонимизация»** → договор `.docx` → разобрать NER
4. Первый запуск: 1–2 минуты

Если «Модуль NER не установлен» — venv должен лежать **рядом с exe**.

---

## 6. Что умеет каждая вкладка после установки

| Вкладка / кнопка | Нужен Python/NER | Назначение |
|------------------|------------------|------------|
| Создать документ | Нет | Шаблоны, карточка исполнителя, заливка пустых `{{ключ}}` |
| Анонимизация | Да | NER → `{{роль}}` + mapping |
| Шаблоны | Да | NER → шаблон `{{роль}}` |
| Настройки | Нет | Версии, модель NER, пути `user\`, исполнители |

---

## 7. Обновление с 1.6.5 / 1.6.4 / 1.6.3 / 1.6.1 / 1.5.4.x

1. Закройте все окна Lassistant.
2. Распакуйте 1.6.5.2 в **новую** папку **или** замените файлы программы (exe, `config`, `models`, `scripts`, `templates`, `docs`).
3. **Не копируйте поверх** существующую папку **`user\`** с другого ПК и не удаляйте её при обновлении файлов поставки — там роли, карточки, ваши исполнители.
4. `python-venv-contract-ner` можно оставить, если Python тот же; при ошибках — удалите venv и повторите §5.4.
5. Свои правки `templates\` сохраните заранее.
6. `config\executors.json` из интерфейса больше не перезаписывается; личные карточки — в `user\settings\executors.json`.

Старые карточки из `data\заказчики` при первом запуске (начиная с 1.6.3) копируются в `user\data\заказчики`, если там ещё нет одноимённых файлов.

---

## 8. Типичные проблемы на удалённом ПК

| Проблема | Что сделать |
|----------|-------------|
| `winget` не найден | Нормально; Python через curl или браузер |
| `curl: malformed` / `host: InstallAllUsers=1` | Две команды склеены — выполните отдельно |
| `python312.exe` ~146 байт | Скачивание не удалось |
| В «О программе» не 1.6.5.2 | Старый ярлык — проверьте путь к exe |
| `python` не находится | Новый терминал; `PrependPath=1` |
| Отказ создать venv в Program Files | cmd **от имени администратора** |
| «Модуль NER не установлен» | Нет venv рядом с exe или нет `ner_model_1.1` |
| Скопировали только exe | Нужен **весь** архив |
| Запуск с `\\сервер\…` | Скопируйте на диск C:/D: |
| Пропали роли / карточки после обновления | Должна остаться папка `user\` рядом с exe |

---

## 9. Контрольный список приёмки

- [ ] Распакован полный `Lassistant_v1.6.5.2_win-x64`
- [ ] «О программе» / Настройки → Общие: версия **1.6.5.2**
- [ ] Появилась папка `user\`
- [ ] `python --version` — 3.11+ / 3.12.x
- [ ] Рядом с exe: `python-venv-contract-ner\Scripts\python.exe`
- [ ] `pip install -r requirements-infer.txt` без ошибки
- [ ] «Разобрать NER» на тестовом договоре работает
- [ ] «О программе»: открываются руководство и словарь из `docs\руководства`

---

## 10. Где лежат файлы на машине разработки

| Артефакт | Путь |
|----------|------|
| Архив для передачи | `release\Lassistant_v1.6.5.2_win-x64.zip` |
| Эта инструкция | `docs\руководства\INSTALL_v1.6.5.2.md` |
| Руководство | `docs\руководства\rukovodstvo_polzovatelya.md` |
| Словарь полей | `docs\руководства\slovar_poley_shablona.html` |
| Канон ролей | `docs\правила\ROLE_SCHEMA_v1.1.1.md` |
| Описание релиза | `docs\realese_history\RELEASE_v1.6.5.2.md` |

---

## 11. Шпаргалка команд

```bat
curl -L -o "%TEMP%\python312.exe" https://www.python.org/ftp/python/3.12.8/python-3.12.8-amd64.exe
dir "%TEMP%\python312.exe"
"%TEMP%\python312.exe" /quiet InstallAllUsers=1 PrependPath=1 Include_test=0
```

(новый cmd → `python --version`)

```bat
cd /d "C:\Program Files (x86)\Lassistant\Lassistant_v1.6.5.2_win-x64\scripts\contract_ner"
python -m venv ..\..\python-venv-contract-ner
..\..\python-venv-contract-ner\Scripts\pip install -U pip
..\..\python-venv-contract-ner\Scripts\pip install -r requirements-infer.txt
```

---

*Конец инструкции. HTML-версия собирается командой `python scripts/md_to_html.py docs/руководства/INSTALL_v1.6.5.2.md`.*
