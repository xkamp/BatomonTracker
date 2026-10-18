# Batomon Showdown Live Tracker

Documentazione tecnica e guida allo sviluppo del tracker locale per Batomon Showdown.

## Da dove iniziare

1. Seguire [Avvio applicazione](00-avvio.md) per avviare client, API e database.
2. Leggere [Analisi e fattibilita](01-analisi-e-fattibilita.md) per capire la sorgente dati e i limiti del progetto.
3. Seguire [Guida allo sviluppo](02-guida-allo-sviluppo.md) per costruire nuovi moduli.
4. Consultare [Architettura](03-architettura.md) prima di aggiungere nuove funzionalita.
5. Usare [Modello dati](04-modello-dati.md) quando si implementano parser e stato interno.
6. Consultare [Testing e robustezza](05-testing-e-robustezza.md) prima di considerare stabile una funzionalita.
7. Seguire [Roadmap](06-roadmap.md) per pianificare le versioni successive.
8. Consultare [Stato implementazione](07-stato-implementazione.md) per vedere cosa e gia stato costruito.
9. Consultare [Sicurezza e deployment](09-security.md) prima di avviare API e database online.
10. Consultare [Audit dati e statistiche](10-audit-dati-statistiche.md) per interpretare correttamente metriche e aggregazioni.

## Documenti di riferimento

- [Analisi originale completa](../Batomon_Showdown_Live_Tracker_Specifiche.md): raccolta dettagliata delle osservazioni fatte sui file locali del gioco.

## Stato del progetto

La fattibilita tecnica della lettura live e stata verificata. Il progetto puo iniziare dal MVP v0.1, completamente locale e read-only.

### Dati confermati

- lettura live di `run_save.json`;
- rilevamento di round, vittorie, vite, oro e trainer;
- lettura di team, shop, trinket e shop rank;
- rilevamento della nuova run tramite `run_id`;
- storico run tramite `profile_snapshot.json`;
- rilevamento di acquisti, vendite, reroll e sconfitte tramite confronto tra snapshot.

### Dati ancora da verificare

- ciclo completo della battaglia;
- merge e level-up;
- lifecycle di item e trinket;
- semantica di `run_save.bak.json`;
- struttura di `opponent_cache`.

## Principi tecnici

- Il tracker legge soltanto file locali.
- Non modifica i file del gioco.
- Non legge la memoria del processo e non usa DLL injection.
- Ogni snapshot deve essere trattato come potenzialmente incompleto o incompatibile.
- Le funzionalita non ancora osservate non devono essere presentate come certe.
