# Batomon Showdown - Analisi del dataset `opponent_cache`

**Data:** 23 settembre 2026  
**Scopo:** documentare ciò che è stato verificato riguardo ai dataset locali scaricati da Batomon Showdown e il loro possibile utilizzo per analytics, meta, team composition e recommendation engine.

---

# 1. Contesto

Oltre al file live:

```text
run_save.json
```

e allo storico personale:

```text
profile_snapshot.json
```

Batomon Showdown mantiene anche una cache locale di avversari:

```text
%APPDATA%\Godot\app_userdata\Batomon Showdown\opponent_cache
```

La cartella contiene diversi file JSON:

```text
casual_oshima.json
casual_oshima_endless.json
casual_starter.json
casual_starter_endless.json
ranked_oshima_0.json
ranked_oshima_1.json
ranked_oshima_endless.json
ranked_starter_0.json
ranked_starter_1.json
ranked_starter_endless.json
meta.json
```

---

# 2. Struttura di uno snapshot avversario

È stato analizzato `ranked_starter_0.json`, che contiene **2000 record**.

Ogni record contiene almeno:

```text
user_id
display_name
mmr
rank_tier
round
set_id
version
run_id
won
team_data
```

Esempio:

```json
{
  "user_id": "76561198332025252",
  "display_name": "critang",
  "mmr": 844,
  "rank_tier": "bronze",
  "round": 1,
  "set_id": "starter",
  "version": 23,
  "run_id": "run_3385948711_1790157690",
  "won": false,
  "team_data": {}
}
```

---

# 3. Struttura di `team_data`

Dentro `team_data` sono stati trovati:

```text
board
lives_lost
mask_trainer_choices
run_blackboard
shop_rank
trainer_id
trinket_ids
used_trinket_ids
wins
```

La board contiene Batomon con campi come:

```text
species_id
level
is_shiny
extra_types
blackboard
perm_buffs
```

Gli slot vuoti possono essere rappresentati come `{}`.

---

# 4. Dimensione dei dataset verificati

File letti correttamente:

```text
casual_starter.json         2000 record
casual_starter_endless.json 2903 record
ranked_starter_0.json       2000 record
ranked_starter_1.json       2000 record
ranked_starter_endless.json 3000 record
```

Totale verificato:

```text
11903 snapshot
```

I file `oshima` esistono e hanno dimensioni reali, ma Windows PowerShell 5.1 ha restituito errori durante `ConvertFrom-Json`.

Questo non dimostra che siano corrotti. Nel progetto C# sarà opportuno testarli con:

```text
System.Text.Json
```

---

# 5. `meta.json`

`meta.json` contiene per ogni dataset almeno:

```text
etag
fetched_at
```

Questo suggerisce fortemente che i file di `opponent_cache` siano scaricati da un backend, salvati localmente e aggiornati usando ETag.

Schema concettuale:

```text
Server Batomon
     |
     v
Opponent dataset
     |
     v
ETag / fetch
     |
     v
opponent_cache
```

---

# 6. Distribuzione per round

In `ranked_starter_0.json` è stata osservata:

```text
Round 1-16  -> 120 record ciascuno
Round 17-20 -> 20 record ciascuno
```

Totale:

```text
16 × 120 = 1920
4 × 20   =   80
Totale   = 2000
```

Conclusione:

```text
il dataset NON è un campione casuale semplice
```

ma sembra essere intenzionalmente stratificato per round.

---

# 7. Run e utenti unici

Su 2000 snapshot:

```text
Records     = 2000
UniqueRuns  = 967
UniqueUsers = 1248
```

Quindi la stessa run può comparire in più snapshot e lo stesso utente può comparire in più run.

---

# 8. Evoluzione della stessa run

È stato verificato che lo stesso `run_id` può comparire in più round.

Questo rende possibile ricostruire l'evoluzione di una composition:

```text
RUN X

Round 1
Team A

↓
Round 2
Team A + B

↓
Round 5
Team A + B + C
```

---

# 9. Significato di `won`

È stato eseguito un test su una run presente in almeno tre round:

```text
Round 1
won        = True
WinsBefore = 0

Round 2
won        = False
WinsBefore = 1

Round 3
won        = True
WinsBefore = 1
```

Team osservato:

```text
Round 1:
bumblebolt, velocect

Round 2:
bumblebolt, velocect, velocect

Round 3:
bumblebolt, velocect, brawlmantis, velocect
```

Conclusione fortemente supportata dai dati:

```text
won
=
esito del combattimento relativo a quello snapshot / round
```

e:

```text
team_data.wins
=
numero di vittorie accumulate prima del combattimento corrente
```

---

# 10. Implicazione statistica

Ogni snapshot può essere trattato come:

```text
INPUT
----------------
round
mmr
rank_tier
trainer
board
trinkets
shop_rank
wins_before

↓

RESULT
----------------
won = true / false
```

Questo è adatto a un motore analytics.

---

# 11. Winrate per round osservata

Su `ranked_starter_0.json`:

```text
Round 1   40.83%
Round 2   51.67%
Round 3   50.00%
Round 4   48.33%
Round 5   44.17%
Round 6   50.83%
Round 7   56.67%
Round 8   55.00%
Round 9   50.83%
Round 10  57.50%
Round 11  62.50%
Round 12  61.67%
Round 13  60.83%
Round 14  66.67%
Round 15  60.83%
Round 16  60.83%

Round 17   0%
Round 18   0%
Round 19   0%
Round 20   0%
```

I round 17-20 sono anomali e non dovrebbero essere usati per statistiche affidabili finché non se ne comprende il significato.

---

# 12. Bucket MMR

`ranked_starter_0.json`:

```text
MMR minimo: 0
MMR massimo: 1000
MMR medio: 727.5775
```

`ranked_starter_1.json`:

```text
MMR minimo: 1000
MMR massimo: 1394
MMR medio: 1077.635
```

Conclusione molto probabile:

```text
ranked_starter_0 = fascia MMR 0-1000
ranked_starter_1 = fascia MMR >= 1000
```

Questo consente statistiche separate per livello competitivo.

---

# 13. Statistiche possibili

Con questi dati è possibile calcolare:

```text
pick rate Batomon
battle winrate Batomon
trainer pick rate
trainer battle winrate
trinket pick rate
trinket battle winrate
performance per round
performance per MMR
performance per rank
performance per shop rank
```

---

# 14. Sinergie

Si possono calcolare statistiche per coppie o triple di Batomon:

```text
Boomagon + Sunsage
```

oppure:

```text
Boomagon + Sunsage + Panbud
```

Esempio futuro:

```text
Games:   1834
Wins:    1131
WinRate: 61.7%
```

---

# 15. Analisi evoluzione composition

Poiché la stessa run può comparire a più round, è possibile analizzare transizioni:

```text
Round 2
A + B

↓

Round 3
A + B + C

↓

Result
WIN
```

Questo permette in futuro di stimare:

```text
P(vittoria | aggiunta di C)
```

o:

```text
P(vittoria | evoluzione A+B -> A+B+C)
```

---

# 16. Trainer analytics

Il dataset permette aggregazioni per `trainer_id`.

Possibili metriche:

```text
trainer pick rate
trainer battle winrate
trainer winrate per round
trainer winrate per MMR
trainer + Batomon synergy
trainer + trinket synergy
```

Le winrate grezze senza stratificazione per round e MMR possono essere fuorvianti.

---

# 17. Modello database consigliato

## `battle_snapshots`

```text
id
run_id
user_id
display_name
round
mmr
rank_tier
set_id
version
trainer_id
shop_rank
wins_before
lives_lost
won
```

## `snapshot_units`

```text
snapshot_id
position
species_id
level
is_shiny
extra_types
perm_buffs
blackboard
```

## `snapshot_trinkets`

```text
snapshot_id
trinket_id
used
```

---

# 18. Deduplicazione

`run_id` non è sufficiente come chiave unica perché una run può comparire più volte.

Possibili chiavi logiche:

```text
run_id + round
```

oppure:

```text
run_id + round + user_id
```

In alternativa può essere usato un hash dello snapshot JSON.

---

# 19. Dataset refresh

Dato che `meta.json` espone `etag` e `fetched_at`, il software può rilevare aggiornamenti:

```text
leggi meta.json
↓
confronta etag
↓
etag nuovo?
↓
importa dataset aggiornato
```

---

# 20. Collegamento con il tracker live

Architettura completa:

```text
                 BATOMON SHOWDOWN
                       |
          +------------+-------------+
          |            |             |
          v            v             v
   run_save.json  profile_snapshot  opponent_cache
          |            |             |
          v            v             v
     LIVE STATE     HISTORY       META DATA
          |            |             |
          +------------+-------------+
                       |
                       v
                  DATA ENGINE
                       |
          +------------+-------------+
          |            |             |
          v            v             v
        Overlay      Analytics    Recommendations
```

---

# 21. Ruolo dei file

## `run_save.json`

```text
stato live
team corrente
shop corrente
gold
round
wins
lives
trainer
trinket
```

## `profile_snapshot.json`

```text
storico personale
match history
rank
MMR
trainer stats
run completate
```

## `opponent_cache/*.json`

```text
dataset avversari
battle outcome
meta
team compositions
trainer
MMR
rank
round
trinket
evoluzione delle run
```

---

# 22. Dataset statico del gioco

Separatamente, i dati statici dei Batomon possono essere ottenuti dai file del gioco / PCK.

Possibili dati:

```text
nome Batomon
tipi
rarità
abilità
stat base
livelli
trainer
trinket
item
eventi
```

Questi dati servono ad arricchire gli ID tecnici come `boomagon`.

---

# 23. Feature possibili

Con i dati confermati è possibile costruire:

```text
Live Tracker
Personal History
Team Builder
Team Composition Explorer
Trainer Stats
Batomon Stats
Trinket Stats
Meta by MMR
Meta by Rank
Meta by Round
Early/Mid/Late Tier Lists
Pair Synergy
Triple Synergy
Composition Evolution
Battle Prediction
Shop Recommendation
Run Recommendation
```

---

# 24. Recommendation engine futuro

Input:

```text
round
MMR
trainer
current team
current shop
gold
trinkets
shop rank
wins
lives
```

Dataset:

```text
opponent_cache
```

Output possibile:

```text
BUY
SELL
REROLL
KEEP
SKIP
PRIORITY
```

---

# 25. Limiti attuali

Non è ancora verificato:

```text
significato preciso round 17-20
schema Oshima tramite System.Text.Json
frequenza di aggiornamento backend
origine esatta degli endpoint
retention del dataset
dimensione massima della cache
criteri completi di campionamento
deduplicazione lato server
```

---

# 26. Cose da NON assumere

Non assumere che:

```text
2000 record = 2000 partite
```

perché sono stati trovati 967 `run_id` unici su 2000 snapshot.

Non assumere che:

```text
won = run vinta
```

perché i test mostrano che rappresenta il risultato del combattimento relativo allo snapshot.

Non assumere che:

```text
winrate grezzo = meta reale
```

perché il dataset è stratificato almeno per round e MMR.

---

# 27. Stato delle conoscenze

```text
Opponent cache reale                  CONFERMATA
2000 snapshot nel bucket base         CONFERMATO
Stratificazione per round             CONFERMATA
Stessa run in più snapshot            CONFERMATA
Run evolution                         CONFERMATA
won = battle result                   CONFERMATO DAI TEST
wins = wins before battle             CONFERMATO DAI TEST
MMR bucket 0                          CONFERMATO
MMR bucket 1                          CONFERMATO
ETag / fetched_at                     CONFERMATI
Backend/cache behavior                FORTEMENTE INDICATO
Trainer analytics                     FATTIBILE
Batomon analytics                     FATTIBILE
Pair/triple synergy                   FATTIBILE
Round-based analytics                 FATTIBILE
MMR-based analytics                   FATTIBILE
Composition evolution                 FATTIBILE

Round 17-20 semantics                 DA STUDIARE
Oshima JSON parsing                   DA TESTARE IN C#
Endpoint backend                      NON ANCORA ANALIZZATO
```

---

# 28. Conclusione

Batomon Showdown espone localmente una quantità di dati molto superiore a quanto previsto inizialmente.

La combinazione:

```text
run_save.json
+
profile_snapshot.json
+
opponent_cache
+
game static data
```

fornisce una base sufficiente per costruire non solo un tracker live, ma un vero sistema analytics simile concettualmente a Critcap / Untapped.

La scoperta più importante è che `opponent_cache` contiene snapshot reali delle battaglie con:

```text
board
trainer
trinket
MMR
rank
round
run_id
battle result
```

e che più snapshot della stessa run permettono di studiare anche l'evoluzione delle composition.

Il progetto può quindi evolvere da:

```text
Live Tracker
```

a:

```text
Live Tracker
+
Meta Analytics
+
Team Composition Explorer
+
Recommendation Engine
```
