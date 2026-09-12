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
- kostenloser Freundescode zum Verbinden zweier Katzen
- Ende-zu-Ende verschlüsselte Nachrichten als dynamische Gedankenblasen

## Download

Im Bereich **Releases** liegt immer die aktuelle `TaskbarKatze.exe`. Weitere Installationen oder Laufzeitpakete sind nicht erforderlich.

## Private Katzenpost

Im Katzenmenü unter **Freunde** kann der eigene Freundescode kopiert und der Code einer anderen Person eingefügt werden. Danach lassen sich Nachrichten mit bis zu 500 Zeichen senden. Die Gedankenblase passt Breite, Höhe und Zeilenumbrüche automatisch an den verfügbaren Bildschirm an.

Nachrichten werden bereits auf dem PC mit ECDH P-256 und AES-256-GCM verschlüsselt. Der private Schlüssel und das Geräte-Token sind per Windows DPAPI an das jeweilige Windows-Benutzerkonto gebunden. Der Vermittlungsdienst sieht nur verschlüsselte Nachrichten, löscht zugestellte Inhalte und verwirft nicht zugestellte Inhalte spätestens nach sieben Tagen. Unbekannte Absender werden nicht angezeigt.

Der Dienst nutzt ausschließlich die kostenlosen Kontingente von Cloudflare Workers und D1. Es gibt keine kostenpflichtige API und kein Abo innerhalb der App.

## Admin-Dashboard

Unter der geschützten `/admin`-Adresse kann der Betreiber eine registrierte Katze auswählen und ihr eine Gedankenblasen-Nachricht schicken. Der Zugang besitzt ein separates, zufälliges Admin-Passwort, ein `Secure`/`HttpOnly`/`SameSite=Strict`-Sitzungscookie, CSRF-Schutz und eine Begrenzung fehlgeschlagener Anmeldungen. Admin-Nachrichten werden für das Zielgerät verschlüsselt und mit einem fest in der App verankerten P-256-Administratorschlüssel signiert.

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

Das optionale Backend liegt unter `backend/`; Schema, Worker und Konfiguration sind versioniert. `npm test` prüft den TypeScript-Code.

Jeder Push auf `main` erzeugt automatisch eine eigenständige Windows-x86-EXE und ein eindeutig versioniertes Release. Die x86-Ausgabe läuft auf Windows 11 x64 sowie über die integrierte Emulation auf Windows 11 ARM64. Die dabei gesetzte Dateiversion verwendet die GitHub-Actions-Laufnummer.

## Lizenz

Quellcode und die projektspezifischen Sneaker-Sprites stehen unter der MIT-Lizenz.
