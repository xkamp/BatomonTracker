# Audit dati e statistiche

## Scopo

Questo documento descrive quali dati vengono letti, quali vengono conservati e quali statistiche vengono calcolate dal progetto. Distingue sempre tra:

- **dato osservato:** presente direttamente nei file del gioco o nel database;
- **dato derivato:** calcolato dal tracker confrontando o aggregando dati osservati;
- **interpretazione:** conclusione utile, ma non necessariamente causale.

La presenza di una statistica non significa che il tracker abbia osservato ogni evento intermedio della partita: il gioco può aggiornare il save tra due letture.

## 1. Sorgenti originali

### `run_save.json`

Percorso:

```text
%APPDATA%\Godot\app_userdata\Batomon Showdown\accounts\<account>\run_save.json
```

È la sorgente live. Contiene, quando disponibili:

- `run_id` e `save_seq`;
- round, vittorie, vite e oro;
- trainer;
- team corrente;
- panchina;
- shop e item;
- trinket;
- shop reroll sequence/rank;
- stato corrente, non necessariamente tutti gli eventi avvenuti.

### `profile_snapshot.json`

Contiene il profilo corrente e `match_history`. Il parser storico conserva:

- identificativo run;
- data di fine;
- ranked/non-ranked;
- vite, round, vittorie, outcome;
- trainer;
- team e panchina;
- trinket.

Dal profilo corrente vengono inoltre letti:

- partite giocate;
- run vinte;
- MMR;
- Ranked MMR;
- tier;
- sotto-rank e stelle.

### `opponent_cache/*.json`

È una sorgente di snapshot di battaglie/composizioni osservate. Ogni snapshot può contenere:

- dataset, modalità, set e segmento;
- user id e run id dell’avversario;
- round;
- MMR e tier dell’avversario;
- vittoria della battaglia;
- vittorie precedenti;
- vite perse;
- trainer;
- team, livelli, shiny, tipi e dati extra;
- trinket e trinket usati;
- JSON originale e hash SHA-256.

## 2. Conservazione dei dati

### SQLite locale

SQLite contiene la copia locale necessaria a funzionamento offline:

- run osservate;
- snapshot live;
- eventi dedotti;
- run completate;
- team, panchina, shop e trinket serializzati;
- build locali.

Il provider locale usa SQLite di Windows e non viene usato come database globale.

### PostgreSQL

Quando l’utente è autenticato, PostgreSQL è la fonte principale per i dati sincronizzati:

- utenti e sessioni Steam;
- run e loro stato più recente;
- team e panchina;
- trinket;
- eventi dedotti;
- dataset analytics;
- snapshot opponent cache;
- unità e trinket degli snapshot;
- leaderboard e dati globali.

SQLite resta una cache/fallback. Se l’API non è disponibile, i dati rimangono locali e vengono sincronizzati quando possibile.

## 3. Eventi dedotti dal live tracker

Il tracker confronta due snapshot consecutivi. Gli eventi sono prodotti solo quando il confronto è sufficientemente chiaro.

| Evento | Regola | Confidenza |
|---|---|---|
| `NewRun` | `run_id` nuovo o precedente assente | confermata |
| `RoundStarted` | round corrente maggiore del precedente | confermata |
| `BattleWon` | vittorie correnti maggiori | confermata |
| `BattleLost` | vite correnti minori | confermata |
| `ShopRerolled` | sequenza reroll maggiore | confermata |
| `BatomonBought` | una specie aggiunta al team e oro diminuito | dedotta |
| `BatomonSold` | una specie rimossa dal team e oro aumentato | dedotta |

Non vengono inventati eventi intermedi se il salto tra snapshot è ambiguo. Un acquisto, una vendita o un merge possono quindi non essere classificati correttamente quando più cambiamenti avvengono tra due letture.

## 4. Statistiche personali

Le statistiche personali usano le run completate. Il tracker considera una run vinta se:

```text
wins >= 10 OR outcome == "won"
```

### Statistiche generali

- **Run completate:** numero di run concluse.
- **Vittorie:** numero di run che soddisfano la regola sopra.
- **Winrate:**

```text
vittorie / run completate
```

- **Media vittorie per run:** media del campo `wins`, usando zero quando il valore manca.

### Filtro ranked/non-ranked

- `Tutte le run`: nessun filtro;
- `Solo ranked`: `is_ranked == true`;
- `Solo non ranked`: `is_ranked != true`, quindi include anche valori mancanti.

L’inclusione dei valori mancanti nel filtro non-ranked è intenzionale per non perdere dati storici incompleti, ma significa che “non ranked” non equivale sempre a “confermato non ranked”.

### Statistiche per trainer

Per ogni trainer vengono calcolati:

- numero di run in cui compare;
- numero di vittorie di quelle run;
- winrate del trainer.

Formula:

```text
vittorie delle run con trainer / run con trainer
```

### Statistiche per Batomon

Per ogni specie presente nel team:

- una presenza conta al massimo una volta per run;
- `Runs` è il numero di run in cui la specie compare nel team;
- `Wins` è il numero di quelle run vinte;
- `WinRate = Wins / Runs`.

La panchina non viene inclusa nelle statistiche personali dei Batomon. La specie non viene quindi premiata per essere stata soltanto acquistata o tenuta in panchina.

## 5. Statistiche PostgreSQL personali

L’endpoint autenticato:

```text
GET /api/me/stats?filter=all|ranked|unranked
```

calcola le stesse statistiche direttamente dalle tabelle PostgreSQL:

- `runs` per run, trainer e vittorie;
- `run_units` con `location = 'team'` per i Batomon;
- filtro sullo SteamID del token JWT;
- filtro ranked applicato sul campo `runs.is_ranked`.

Questa vista è preferibile quando l’utente è online perché include i dati già sincronizzati sul server. In caso di errore API, la UI torna alla copia SQLite.

## 6. MMR e tier

MMR e tier mostrati nel profilo sono valori osservati nel `profile_snapshot.json` corrente:

- MMR generale;
- Ranked MMR;
- tier;
- stelle.

Non sono ricostruiti dal numero di vittorie. Non rappresentano quindi una media storica né il MMR di ogni singola run. Per una storia MMR completa servirebbe salvare questi valori a ogni snapshot del profilo.

Gli analytics opponent conservano invece MMR e tier per ogni snapshot avversario, quando presenti nel dataset.

## 7. Analytics globali opponent

### Fasi

- **All:** tutti gli snapshot;
- **Standard:** `wins_before` mancante oppure minore di 10;
- **Extended:** `wins_before >= 10`.

### Statistiche per Batomon

Per una specie selezionata:

- **Battle count:** snapshot/battaglie in cui la specie è nel board;
- **Battle wins:** quelle battaglie con `won = true`;
- **Battle winrate:** `battle wins / battle count`;
- **Run count:** run distinte che contengono la specie;
- **Completed run count:** run che raggiungono 10 vittorie o zero vite;
- **Run wins:** run completate che raggiungono 10 vittorie;
- **Run winrate:** `run wins / completed run count`.

Il battle winrate e il run winrate misurano fenomeni diversi e non devono essere confrontati come se avessero lo stesso denominatore.

### Compagni

Per una specie target, un compagno conta se compare nella stessa run almeno una volta. La statistica misura co-occorrenza:

- run condivise;
- run condivise completate;
- run condivise arrivate a 10 vittorie;
- winrate delle run condivise completate.

Non dimostra che il compagno migliori la specie target: non corregge per MMR, ordine di acquisto, trainer, patch, fase o selezione del giocatore.

### Statistiche per round

Per ogni round:

```text
battle count = snapshot del round
battle wins = snapshot con won = true
battle winrate = battle wins / battle count
```

Snapshot duplicati vengono distinti tramite `dataset_id`, `user_id`, `run_id`, round e hash.

## 8. Cosa è corretto interpretare

È ragionevole interpretare:

- winrate personale come percentuale di run vinte;
- winrate di un trainer come rendimento delle run in cui compare;
- winrate di un Batomon come rendimento delle run in cui compare nel team;
- battle winrate opponent come percentuale degli snapshot vinti;
- companion count come frequenza di co-presenza;
- MMR/tier come fotografia del profilo o dello snapshot.

## 9. Cosa non deve essere dedotto

I dati non dimostrano automaticamente:

- causalità di una specie sulla vittoria;
- superiorità di un Batomon con pochi campioni;
- correttezza di un acquisto dedotto dal diff;
- vittoria di una singola battaglia quando il save ha saltato snapshot;
- MMR storico completo dal profilo corrente;
- qualità di una build solo dalla frequenza di utilizzo;
- efficacia di un compagno rispetto a un altro senza controllo per contesto.

## 10. Controlli consigliati sui dati

Prima di usare una statistica per decisioni di bilanciamento o recommendation engine, controllare:

1. numero assoluto di run/battaglie;
2. presenza di valori mancanti;
3. filtro ranked/non-ranked;
4. dataset, patch e fase;
5. differenza tra denominatore battle e denominatore run;
6. duplicati e run con snapshot incompleti;
7. intervallo temporale dei dati;
8. eventuale selezione dei giocatori presenti nella cache.

## Conclusione

Il tracker usa correttamente i dati come osservazioni e aggregazioni descrittive. Le aree più affidabili sono conteggio run, team presenti, eventi con contatori monotoni e statistiche con denominatori espliciti. Le aree da trattare con cautela sono eventi dedotti, causalità delle build, companion analytics e statistiche basate su campioni piccoli.
