# Sicherheit und Datenschutz

## Schutzmodell

- Freundescodes enthalten ausschließlich Geräte-ID, Katzenname und öffentlichen P-256-Schlüssel.
- Private Schlüssel und Geräte-Token verlassen den PC nicht. Sie werden mit Windows DPAPI für den aktuellen Windows-Benutzer geschützt.
- Nachrichten werden lokal mit ECDH P-256 und AES-256-GCM verschlüsselt und authentifiziert.
- Nur zuvor über einen Freundescode gespeicherte Absender werden akzeptiert.
- Nachrichten-IDs und Zeitstempel begrenzen Replay-Angriffe; akzeptierte IDs werden lokal zwischengespeichert.
- Der Worker speichert nur verschlüsselte Umschläge. Zugestellte Nachrichten werden bestätigt und gelöscht, übrige Nachrichten laufen nach sieben Tagen ab.
- Geräte- und Versandendpunkte besitzen Größen- und Ratenbegrenzungen. Tokens werden serverseitig nur als SHA-256-Wert gespeichert.

## Grenzen

Wer Zugriff auf das entsperrte Windows-Benutzerkonto erhält, kann auch dessen lokale Taskbar-Cat-Identität verwenden. Der Freundescode sollte nur direkt mit der gewünschten Person geteilt werden. Metadaten wie Geräte-IDs, Absender, Empfänger und Zeitpunkte sind für den Vermittlungsdienst technisch erforderlich und nicht Ende-zu-Ende verschlüsselt.

## Meldung einer Schwachstelle

Bitte Sicherheitsprobleme nicht als öffentliche GitHub-Issue mit Exploitdetails veröffentlichen. Verwende stattdessen die private Security-Advisory-Funktion des GitHub-Repositories.
