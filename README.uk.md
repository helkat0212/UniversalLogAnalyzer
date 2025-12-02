# Universal Log Analyzer — Аналізатор журналів мережевого обладнання

Це інструмент для парсингу конфігурацій та журналів мережевого обладнання від декількох вендорів (Huawei, Cisco, Juniper, Mikrotik, GenericTextLog) та генерації уніфікованого звіту й Excel-експортів.

Коротко:
- Зчитує лог/конфіг файл
- Витягує інформацію про інтерфейси, BGP, VLAN, ACL, NTP, VPN та ін.
- Виконує базову перевірку на аномалії (дублікати IP, інтерфейси в shutdown, високі надходження CPU/пам'яті)
- Генерує багатошарову Excel-рапорту (Anomalies, Performance, InterfaceClusters) — аркуші створюються тільки коли є дані
- UI: WPF-додаток з візуалізацією топології та Raw Data вкладкою

Швидкий старт (для презентації):
1. Відкрити рішення в Visual Studio або використати CLI `.NET`
2. Збірка:

```powershell
dotnet build HEK.sln
```

3. Опублікувати промо-версію (самодостатній exe для демонстрації):

```powershell
dotnet publish HEK.sln -c Release -r win-x64 /p:PublishSingleFile=true /p:PublishTrimmed=false --self-contained true
```

4. Запустити `UniversalLogAnalyzer.exe` з директорії `publish/` або згенерованого артефакту.

Поради для демонстрації:
- Підготуйте 2–3 зразкових файли конфігів різних вендорів (Huawei, Cisco, Juniper). Покажіть як програма:
  - відображає Raw Data
  - виявляє аномалії
  - генерує Excel з розумними аркушами (без порожніх листів)
- Поясніть що парсери поступово доробляються (поточний стан: VRRP, VLAN ranges, subinterfaces та агрегації краще розпізнаються).

Файли документації: `PUBLISH_SETUP.uk.md`, `PRESENTATION_NOTES.uk.md` — див. поруч для деталей.

Автор: команда розробки
