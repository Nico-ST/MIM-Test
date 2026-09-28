<!-- Autor: N.Stillhart | Datum: 2026-09-28 -->

# A–Z Verkehrsschule Ostschweiz AG – Redesign

Moderne, minimalistische Neugestaltung von [autofahren.ch](https://www.autofahren.ch) als statischer One-Pager.

## Struktur

```
index.html            Seite (alle Inhalte, semantisches HTML, JSON-LD)
assets/css/style.css  Design-Tokens, Layout, Responsive-Regeln
assets/js/main.js     Header, Mobile-Menü, Scroll-Reveal, Scrollspy, Frist-Rechner
assets/img/favicon.svg
```

## Lokal ansehen

`index.html` direkt im Browser öffnen – oder mit einem lokalen Server:

```bash
python3 -m http.server 8000
```

## Hinweise

- Buchung, Fahrlehrerverzeichnis, Login und PDFs verlinken weiterhin auf die bestehenden Systeme von autofahren.ch.
- Inhalte stammen von der bestehenden Website und dem WAB-Kursprogramm (PDF).
- Keine Fotos eingebunden: Die Grafiken sind reine SVG/CSS und können später durch eigene Bilder ergänzt werden.
