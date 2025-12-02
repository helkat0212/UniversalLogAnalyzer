# Інструкція з інсталяції та розгортання

## Розпочинаючи (End Users)

### Вимоги
- **OS**: Windows 10 або новіше
- **RAM**: Мінімум 2 ГБ (рекомендується 4+ ГБ)
- **Диск**: 500 MB вільного місця (для самого додатку та результатів)
- **.NET Runtime**: НЕ потрібно! Все вбудовано в EXE

### Встановлення та запуск

1. **Завантажити останній випуск**:
   - Перейти на [Releases](https://github.com/vladyslavroshchuk-si231-code/HEK/releases)
  - Завантажити `UniversalLogAnalyzer.exe` (180+ MB)

2. **Запустити**:
  - Подвійний клік на `UniversalLogAnalyzer.exe`
   - Додаток стартує за 5-10 секунд

3. **Використання**:
   - Вибрати логи через file chooser або drag-drop
   - Натиснути "Analyze"
   - Вибрати папку для результатів
   - Результати автоматично відкриються

### Попередження Windows SmartScreen

Windows може показати попередження "Невідоме видавця". Це нормально для open-source проектів.

**Вирішення**:
1. Клікнути "More info"
2. Вибрати "Run anyway"
3. Додаток буде виконуватись

*Альтернатива*: Завантажити вихідний код та біль самостійно (див. розділ для розробників).

---

## Розробники

### Передумови

- **.NET SDK 8.0 або новіше** ([завантажити](https://dotnet.microsoft.com/download))
- **Visual Studio 2022 Community** (безплатно) або **VS Code**
- **Git** для версійної контролю

### Налаштування середовища розробки

#### На Windows (рекомендується)

```powershell
# 1. Установити .NET SDK (якщо ще не встановлено)
# Завантажити з https://dotnet.microsoft.com/download
# Можна перевірити версію
dotnet --version

# 2. Клонувати репозиторій
git clone https://github.com/vladyslavroshchuk-si231-code/HEK.git
cd HEK

# 3. Відновити залежності
dotnet restore

# 4. Біль проект
dotnet build -c Debug

# 5. Запустити тести
dotnet test HuaweiLogAnalyzer.Tests -c Debug

# 6. Запустити додаток
dotnet run --project HuaweiLogAnalyzer/HuaweiLogAnalyzer.csproj
```

#### На macOS/Linux (майбутній підтримка через Avalonia)

```bash
# Та ж середовища передумови, але Avalonia UI потрібна (поки не готова)
# Поточна WPF версія – тільки для Windows
```

### Раніже для розробки

```powershell
# Відкрити рішення у Visual Studio
Start-Process 'C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\IDE\devenv.exe' '.\HEK.sln'

# Або використовувати VS Code
code .

# Встановити розширення для VS Code:
# - C# (від Microsoft)
# - NuGet Package Manager
```

### Компіляція та публікація

#### Debug Build

```powershell
# Компілювати в Debug (більший, але швидший за розробку)
dotnet build HuaweiLogAnalyzer.sln -c Debug
```

#### Release Build

```powershell
# Компілювати в Release (оптимізований)
dotnet build HuaweiLogAnalyzer.sln -c Release
```

#### Вихідний код (Portable ZIP)

```powershell
# Опублікувати як інтерпретований код (потребує .NET Runtime на цільовому ПК)
dotnet publish HuaweiLogAnalyzer/HuaweiLogAnalyzer.csproj `
  -c Release `
  -o publish/portable
```

#### Single-File EXE (Self-Contained)

```powershell
# Найпопулярніший формат для розповсюджування
# Результат: один EXE (~180 MB) з вбудованим runtime

# Windows x64
dotnet publish HuaweiLogAnalyzer/HuaweiLogAnalyzer.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  /p:PublishSingleFile=true `
  -o publish/win-x64

# Результат буде в: publish/win-x64/UniversalLogAnalyzer.exe
```

#### Оптимізований Single-File EXE (Trimmed)

```powershell
# Менший файл (~100-120 MB) але потребує тестування
dotnet publish HuaweiLogAnalyzer/HuaweiLogAnalyzer.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  /p:PublishSingleFile=true `
  /p:PublishTrimmed=true `
  -o publish/win-x64-trimmed
```

### Запуск тестів

```powershell
# Запустити всі тести
dotnet test HuaweiLogAnalyzer.Tests -c Release

# Запустити конкретний тест
dotnet test HuaweiLogAnalyzer.Tests -c Release -k "AnalyzeFile_HappyPath"

# З детальним виводом
dotnet test HuaweiLogAnalyzer.Tests -c Release --logger "console;verbosity=detailed"

# Генерація звіту про покриття
dotnet test HuaweiLogAnalyzer.Tests -c Release /p:CollectCoverage=true /p:CoverageFormat=cobertura
```

---

## Розгортання в корпоративній мережі

### Сценарій 1: Single-File Deployment

```powershell
# 1. Завантажити UniversalLogAnalyzer.exe з GitHub Releases

# 2. Помістити на файловому сервері
\\server\share\tools\UniversalLogAnalyzer.exe

# 3. Користувачі можуть запустити звідти (не потребує інсталяції)
# Або скопіювати на локальний диск
```

### Сценарій 2: Групова політика (GPO)

```powershell
# 1. Розмістити на центральному sервері
\\company.com\share\Tools\UniversalLogAnalyzer.exe

# 2. Створити Group Policy Object для розповсюджування посилання на Start Menu
# GPO > User Configuration > Windows Settings > Shortcuts
# Ім'я: Universal Log Analyzer
# Мета: \\company.com\share\Tools\UniversalLogAnalyzer.exe

# 3. Застосувати до організаційних одиниць
```

### Сценарій 3: Інтеграція з Ansible/Puppet

```yaml
# Ansible playbook приклад
---
- hosts: windows_servers
  tasks:
    - name: Download UniversalLogAnalyzer
      win_get_url:
        url: https://github.com/vladyslavroshchuk-si231-code/HEK/releases/download/v1.0.0/UniversalLogAnalyzer.exe
        dest: 'C:\Program Files\UniversalLogAnalyzer\UniversalLogAnalyzer.exe'

    - name: Create shortcut on Desktop
      win_shortcut:
        src: 'C:\Program Files\UniversalLogAnalyzer\UniversalLogAnalyzer.exe'
        dest: 'C:\Users\Public\Desktop\Universal Log Analyzer.lnk'
```

---

## Конфігурація та налаштування

### Стандартні розташування результатів

- **Windows**: `C:\Users\{Username}\Downloads\Logs\`
- **При вказанні папки**: `{SelectedFolder}\logs\`

### Структура результатів

```
C:\Users\{Username}\Downloads\Logs\
├── Device-01/
│   ├── Universal_Report_20251130_120000_000.xlsx
│   ├── Universal_Report_20251130_120000_000.json
│   ├── Universal_Report_20251130_120000_000.csv
│   ├── Topology_20251130_120000_000.dot
│   └── ...
├── Device-02/
│   └── ...
└── Topology_20251130_120000_000.dot (консолідований)
```

### Кастомізація (майбутні версії)

- [ ] Конфігураційний файл для правил аномалій
- [ ] Шаблони звітів
- [ ] Кольорові схеми
- [ ] Локалізація мови

---

## Вирішення проблем

### Додаток не стартує

**Проблема**: `dotnet: The term 'dotnet' is not recognized`

**Вирішення**:
```powershell
# Перевірити встановлено .NET
dotnet --version

# Якщо не встановлено, завантажити з https://dotnet.microsoft.com/download
```

### "Access Denied" при запису результатів

**Проблема**: Додаток не може записати до папки

**Вирішення**:
```powershell
# Вибрати іншу папку при запуску (це можна зробити в UI)
# Або надати дозволи на папку
icacls "C:\Users\{Username}\Downloads\Logs" /grant:r "${env:USERNAME}:(OI)(CI)F"
```

### Повільна робота на старих ПК

**Проблема**: Додаток виконується повільно

**Вирішення**:
- Закрити інші додатки
- Використовувати менші логи для тестування
- Збільшити RAM якщо можливо
- Використовувати SSD замість HDD

### Memory Leak / Висока утилізація пам'яті

**Проблема**: Додаток займає більш 1 ГБ пам'яті

**Вирішення**:
- Обробити логи в батчах (по кількох файлів за раз)
- Експортувати як CSV для великих файлів
- Перезапустити додаток після обробки великого файлу

---

## Оновлення на нову версію

```powershell
# 1. Завантажити нову версію z GitHub Releases

# 2. Замінити старий exe
Move-Item 'publish\win-x64\HuaweiLogAnalyzer.exe' `
  'publish\win-x64\HuaweiLogAnalyzer_backup.exe'

Copy-Item 'downloads\HuaweiLogAnalyzer.exe' `
  'publish\win-x64\HuaweiLogAnalyzer.exe'

# 3. Запустити нову версію та перевірити функціональність
```

---

## Питання та підтримка

- **GitHub Issues**: https://github.com/vladyslavroshchuk-si231-code/HEK/issues
- **Документація**: див. README.md, ScientificThesis.md
- **Contributing**: див. CONTRIBUTING.md для розробників

---

**Остання оновлення**: 30 листопада 2025
