# Roadmap

## v0.1: Live tracker locale

Obiettivo: mostrare lo stato corrente in modo affidabile.

- [ ] soluzione .NET e separazione Core/App;
- [ ] localizzazione automatica del save;
- [ ] monitoraggio con watcher e polling di sicurezza;
- [ ] parser tollerante;
- [ ] modello `RunState`;
- [ ] UI con statistiche base;
- [x] team, panchina, shop e trinket;
- [x] rilevamento nuova run;
- [x] gestione gioco chiuso e JSON invalido;
- [x] test automatici principali.

## v0.2: Storico personale

Obiettivo: consultare le run concluse.

- [x] lettura di `profile_snapshot.json`;
- [x] elenco ultime run;
- [ ] winrate personale;
- [ ] statistiche per trainer;
- [ ] statistiche per Batomon;
- [ ] filtri ranked/non-ranked;
- [ ] vista MMR e tier.

## v0.3: Database locale

Obiettivo: conservare dati indipendentemente dai file del gioco.

- [x] SQLite;
- [ ] salvataggio snapshot selezionati;
- [ ] eventi dedotti;
- [x] run e composizioni;
- [ ] migrazioni dello schema;
- [ ] esportazione e backup dei dati.

## v0.4: Overlay

Obiettivo: rendere il tracker utilizzabile durante la partita.

- [x] finestra always-on-top;
- [x] trasparenza;
- [x] spostamento manuale con offset relativo alla finestra del gioco;
- [x] modalita click-through bloccata;
- [x] attivazione/disattivazione da hotkey;
- [x] aggancio automatico alla finestra del gioco;
- [x] apertura automatica quando il gioco e visibile;
- [x] ridimensionamento;
- [x] attivazione/disattivazione da tray;
- [x] impostazioni persistenti.

L'overlay va sviluppato dopo aver stabilizzato la finestra normale e il flusso dati.

## v0.5: Analytics e suggerimenti

Obiettivo: fornire analisi basate sui dati raccolti.

- [ ] statistiche aggregate locali;
- [x] prima valutazione dello shop;
- [x] raccomandazione iniziale Buy/Hold;
- [x] spiegazioni di acquisto e priorita basate su copie/livelli;
- [ ] analisi di sinergie;
- [ ] verifica del lifecycle di item e trinket;
- [x] backend opt-in tramite login Steam.

Le raccomandazioni non devono essere introdotte prima di avere dati sufficienti e una strategia per distinguere correlazione da causalita.

## Funzionalita future non prioritarie

- analisi di `opponent_cache`;
- statistiche globali;
- dashboard web;
- sincronizzazione cloud;
- backend PostgreSQL.

Queste funzionalita aumentano costi, responsabilita privacy e complessita operativa. Non fanno parte del primo prodotto utile.
