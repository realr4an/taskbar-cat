# Taskbar Cat

Taskbar Cat bringt **Sneaker**, eine kleine animierte schwarze Pixelkatze, auf die untere Bildschirmkante von Windows 11.

## Funktionen

- schläft am Rand und hebt beim Hover müde den Kopf
- putzt sich nach einem Klick und läuft anschließend los
- natürliche Laufanimation in beide Richtungen
- gelegentliche Sprünge, Putzpausen und Sprechblasen
- frei verschiebbar per Drag-and-drop
- auswählbarer Bildschirm und Bewegungsbereich
- Name der Katze konfigurierbar, Standard: Sneaker
- Tray-Menü für Einstellungen, Pause und Beenden
- automatische, geprüfte Updates aus GitHub Releases

## Download

Im Bereich **Releases** liegt immer die aktuelle `TaskbarKatze.exe`. Weitere Installationen oder Laufzeitpakete sind nicht erforderlich.

## Bedienung

- **Hover:** Sneaker hebt im Schlaf müde den Kopf.
- **Linksklick:** Sneaker putzt sich und läuft los. Während des Laufens reagiert sie mit einem kleinen Sprung.
- **Ziehen:** Sneaker kann an eine andere Stelle gesetzt werden und läuft dort weiter.
- **Rechtsklick / Tray-Symbol:** Einstellungen, Pause oder Beenden.

## Entwicklung

Voraussetzung ist das .NET 9 SDK unter Windows.

```powershell
dotnet build src/TaskbarCat/TaskbarCat.csproj
```

Jeder Push auf `main` erzeugt automatisch eine eigenständige Windows-x86-EXE und ein eindeutig versioniertes Release. Die x86-Ausgabe läuft auf Windows 11 x64 sowie über die integrierte Emulation auf Windows 11 ARM64. Die dabei gesetzte Dateiversion verwendet die GitHub-Actions-Laufnummer.

## Lizenz

Quellcode und die projektspezifischen Sneaker-Sprites stehen unter der MIT-Lizenz.
