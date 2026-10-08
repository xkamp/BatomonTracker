# Batomon Showdown Live Tracker
## Analisi tecnica e specifiche di progetto

**Data analisi:** 23 settembre 2026  
**Piattaforma analizzata:** Windows  
**Gioco:** Batomon Showdown  
**Engine rilevato:** Godot  

---

# 1. Obiettivo del progetto

Realizzare un companion/tracker live per **Batomon Showdown**, concettualmente simile a strumenti come Blitz, Untapped o altri tracker per videogiochi competitivi.

L'obiettivo iniziale è leggere automaticamente lo stato della partita mentre il giocatore gioca e visualizzare informazioni utili come:

- round corrente;
- vittorie;
- vite;
- oro;
- trainer;
- team attuale;
- Batomon presenti nello shop;
- livello dei Batomon;
- shiny;
- buff permanenti;
- trinket;
- eventuali oggetti;
- storico delle run;
- statistiche personali.

In fasi successive il tracker potrà aggiungere:

- statistiche globali;
- meta;
- winrate per Batomon;
- winrate per trainer;
- migliori composizioni;
- sinergie;
- suggerimenti live;
- valutazione dello shop corrente;
- overlay in-game.

---

# 2. Risultato principale dell'analisi

È stato verificato che **non è necessario utilizzare un'API ufficiale, OCR, lettura della memoria del processo, DLL injection o packet sniffing** per costruire il tracker base.

Batomon Showdown salva lo stato corrente della run in un file JSON locale:

```text
%APPDATA%\Godot\app_userdata\Batomon Showdown\accounts\<account>\run_save.json
```

Il file viene aggiornato continuamente durante la partita.

È quindi possibile realizzare un tracker tramite:

```text
Batomon Showdown
        |
        v
run_save.json
        |
        v
File watcher
        |
        v
JSON parser
        |
        v
State diff
        |
        v
Tracker / Overlay
```

---

# 3. Directory dati individuata

La directory applicativa utilizzata da Batomon Showdown è:

```text
C:\Users\<utente>\AppData\Roaming\Godot\app_userdata\Batomon Showdown
```

La struttura osservata è simile alla seguente:

```text
Batomon Showdown
|
+-- accounts
|   |
|   +-- <account-id>
|       |
|       +-- entitlements_cache.json
|       +-- profile_snapshot.json
|       +-- run_save.json
|       +-- run_save.bak.json
|
+-- logs
|   |
|   +-- godot.log
|   +-- godotYYYY-MM-DDTHH.MM.SS.log
|
+-- opponent_cache
|   |
|   +-- casual_oshima.json
|   +-- casual_oshima_endless.json
|   +-- casual_starter.json
|   +-- casual_starter_endless.json
|   +-- ranked_oshima_0.json
|   +-- ranked_oshima_1.json
|   +-- ranked_oshima_endless.json
|   +-- ranked_starter_0.json
|   +-- ranked_starter_1.json
|   +-- ranked_starter_endless.json
|   +-- meta.json
|
+-- shader_cache
+-- vulkan
+-- session.save
+-- settings.cfg
```

---

# 4. File principali individuati

## 4.1 `run_save.json`

È il file più importante per il tracker live.

Contiene lo stato corrente della run ed è stato verificato che cambia mentre il giocatore effettua azioni.

Campi osservati:

```text
_local_owner_user_id
bench
blackboard
damage_dealt_total
display_name
event_history
faced_opponent_mmrs
faced_run_ids
faced_user_ids
free_rerolls
gold
has_used_second_chance
is_curated_run
is_endless_mode
is_ranked
is_shop_frozen_next_round
item_state
lives
lives_lost
mask_trainer_choices
monsters_ever_owned
offline_origin
overflow
pending_battle_opponent
pending_curated_shelves
pending_event_id
pending_mask_selection
pending_reward
pending_round_start
pending_shop_ready_event_options
pending_trainer_options
prefetched_slate
queued_event_id
round
run_id
save_seq
save_version
saved_at
seed
set_id
shop_content
shop_rank
slate_prefetch_complete
team
trainer_id
trinket_ids
trinkets_ever_held
trinkets_gained_total
used_trinket_ids
wins
```

---

# 5. Struttura dello stato della run

Esempio semplificato:

```json
{
  "gold": 0,
  "lives": 8,
  "round": 4,
  "wins": 2,
  "shop_rank": 4,
  "trainer_id": "pyromaniac",
  "run_id": "run_3783007034_1790149839",
  "save_seq": 74,
  "save_version": 1,
  "team": [],
  "shop_content": [],
  "trinket_ids": []
}
```

---

# 6. Team

Ogni Batomon del team contiene almeno:

```json
{
  "blackboard": {},
  "extra_types": [],
  "is_shiny": false,
  "level": 1,
  "perm_buffs": {},
  "species_id": "boomagon"
}
```

Campi osservati:

```text
species_id
level
is_shiny
extra_types
blackboard
perm_buffs
```

Il team è quindi già leggibile direttamente senza OCR.

---

# 7. Shop

Lo shop corrente è disponibile in:

```text
shop_content
```

Esempio:

```json
[
  {
    "species_id": "venopuff",
    "level": 1,
    "is_shiny": false
  },
  {
    "species_id": "alpinine",
    "level": 1,
    "is_shiny": false
  },
  null,
  {
    "species_id": "noxalith",
    "level": 1,
    "is_shiny": false
  }
]
```

Gli slot acquistati possono diventare `null`.

Questo permette al tracker di mostrare in tempo reale:

- contenuto dello shop;
- slot vuoti;
- acquisti;
- reroll;
- eventuali shiny;
- extra type;
- modificatori applicati.

---

# 8. Trinket

I trinket correnti sono disponibili in:

```text
trinket_ids
```

Esempio:

```json
[
  "fake_diamond",
  "gold_nugget",
  "training_weights"
]
```

Sono presenti anche:

```text
trinkets_ever_held
trinkets_gained_total
used_trinket_ids
```

Questi dati potranno essere utilizzati sia live sia per statistiche post-run.

---

# 9. Blackboard e contatori interni

Nel campo `blackboard` sono stati osservati:

```text
items_bought_this_round
last_shop_item_ids
max_items_per_round
refill_frozen_shop
round_action_seq
run_rng_seq
shop_roll_seq
spawned_unique_items
```

Questi contatori sono particolarmente utili per ricostruire gli eventi.

Esempio:

```text
shop_roll_seq: 1 -> 2
```

può essere interpretato come:

```text
REROLL
```

---

# 10. Aggiornamento live verificato

È stato monitorato l'hash MD5 di `run_save.json` ogni 250 ms.

Il file ha cambiato hash ripetutamente durante la run, anche più volte nell'arco di pochi secondi.

Sono state osservate anche variazioni della dimensione del file.

Questo conferma che il file è una sorgente live adatta a un companion.

---

# 11. Azioni rilevate durante i test

## Nuovo round

Osservato:

```text
Round: 0 -> 1
Gold: 0 -> 40
ShopRoll: <vuoto> -> 1
```

Successivamente:

```text
Round: 1 -> 2
Gold: 15 -> 50
ShopRoll: 3 -> 4
```

Il cambio round è quindi facilmente rilevabile.

---

## Acquisto Batomon

Esempio osservato:

```text
Gold: 40 -> 20
Team:
  gildshell Lv.1
```

Lo slot dello shop corrispondente viene rimosso.

Possibile evento dedotto:

```text
BUY gildshell
```

---

## Reroll

Esempio:

```text
Gold: 20 -> 17
ShopRoll: 1 -> 2
```

e viene sostituito il contenuto dello shop.

Evento dedotto:

```text
REROLL
```

---

## Altro acquisto

Esempio:

```text
Gold: 17 -> 12
Team:
  gildshell Lv.1
  infernade Lv.1
```

Evento:

```text
BUY infernade
```

---

## Vendita

Esempio:

Prima:

```text
Team:
  gildshell
  infernade
  mosslug
Gold: 2
```

Dopo:

```text
Team:
  gildshell
  mosslug
Gold: 15
```

Evento deducibile:

```text
SELL infernade
```

---

## Sconfitta

È stato osservato:

```text
Lives: 10 -> 9
```

Quindi una sconfitta può essere rilevata direttamente.

---

# 12. Identificazione delle run

Ogni run possiede un identificatore:

```text
run_id
```

Esempio prima della nuova partita:

```text
run_3783007034_1790149839
```

Nuova run:

```text
run_971904529_1790150885
```

È stato verificato che, iniziando una nuova run:

```text
run_id cambia
save_seq riparte da 1
round riparte da 0
wins riparte da 0
lives riparte da 10
```

Pertanto:

```text
if previous.run_id != current.run_id
    => NEW RUN
```

Questa sarà la regola principale per identificare una nuova sessione.

---

# 13. `save_seq`

Il campo:

```text
save_seq
```

cresce durante la partita.

Esempio:

```text
1
2
4
5
6
7
...
80
81
```

Non è garantito che il tracker osservi ogni numero perché il gioco può salvare più volte tra due letture.

Quindi `save_seq` NON deve essere interpretato come un ID evento uno-a-uno.

Va invece utilizzato per:

- capire che è stato prodotto un nuovo stato;
- ordinare gli snapshot;
- ignorare duplicati;
- rilevare reset della run.

---

# 14. `save_version`

È stato osservato:

```json
"save_version": 1
```

Il tracker dovrà controllarlo.

Esempio:

```text
if save_version != supportedVersion
    mostra warning
    continua in modalità compatibilità, se possibile
```

Una futura patch di Batomon potrebbe modificare lo schema JSON.

---

# 15. `profile_snapshot.json`

È stato trovato:

```text
accounts\<account>\profile_snapshot.json
```

Il file contiene informazioni persistenti sul profilo e sullo storico.

Campi osservati:

```text
_snapshot_user_id
display_name
dex_achievements
encountered_mythicals
equipped_badge_case
games_played
match_history
mmr
ranked_mmr
rank_tier
rank_sub
rank_stars
runs_won
season_number
season_tier_history
trainer_run_stats
tutorial_flags
unlocked_badge_cases
```

---

# 16. Match history

Dentro `profile_snapshot.json` è presente:

```text
match_history
```

Ogni run completata può contenere:

```text
ended_at
is_ranked
lives
outcome
round
run_id
team
trainer_id
wins
```

Esempio concettuale:

```json
{
  "outcome": "won",
  "wins": 10,
  "round": 10,
  "trainer_id": "pyromaniac",
  "team": [
    {
      "species_id": "dracana",
      "level": 2
    }
  ]
}
```

Questo file è quindi ideale per:

- storico run;
- winrate personale;
- statistiche per trainer;
- statistiche per Batomon;
- composizioni usate;
- performance personali.

---

# 17. `opponent_cache`

Sono stati individuati file JSON molto grandi nella directory:

```text
opponent_cache
```

Esempi:

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

Alcuni file superano 20 MB.

Questi file potrebbero contenere dati relativi a:

- opponent;
- board;
- run;
- matchmaking;
- meta.

La loro struttura completa NON è stata ancora analizzata.

Questa analisi è quindi da considerare una possibile fase futura.

---

# 18. `session.save`

È presente:

```text
session.save
```

La sua intestazione binaria inizia con:

```text
47 44 45 43
```

ASCII:

```text
GDEC
```

Il contenuto non è direttamente leggibile come JSON.

Per il tracker attuale non è necessario utilizzarlo perché `run_save.json` espone già i dati necessari.

---

# 19. `run_save.bak.json`

È presente:

```text
run_save.bak.json
```

Probabilmente rappresenta una copia di backup dello stato della run.

Utilizzo previsto:

```text
leggi run_save.json
        |
        +-- parse OK -> usa stato
        |
        +-- parse error
                |
                v
           retry breve
                |
                +-- fallisce ancora
                        |
                        v
                run_save.bak.json
```

Il comportamento preciso del backup deve ancora essere verificato.

---

# 20. Gestione account

Il tracker NON deve hardcodare la cartella account.

Da evitare:

```text
accounts\76561198116069482-75aaa617
```

Strategia consigliata:

```text
1. cerca accounts\*\run_save.json
2. raccogli tutti i file trovati
3. seleziona quello modificato più recentemente
```

Questo permette di supportare:

- account Steam differenti;
- cambi account;
- più utenti Windows;
- eventuali futuri provider.

---

# 21. Architettura consigliata

```text
                   Batomon Showdown
                          |
                          v
                  run_save.json
                          |
                          v
                  RunSaveLocator
                          |
                          v
                  FileSystemWatcher
                          |
                          v
                  SafeJsonReader
                          |
                          v
                     RunState
                          |
                          v
                     StateDiff
                    /     |      \
                   /      |       \
                  v       v        v
              Overlay   Storage   Analytics
```

---

# 22. Moduli software

## 22.1 RunSaveLocator

Responsabilità:

- trovare la cartella Godot;
- trovare tutti gli account;
- individuare `run_save.json`;
- selezionare il save attivo;
- rilevare eventuali cambi account.

---

## 22.2 FileSystemWatcher

Responsabilità:

- osservare modifiche a `run_save.json`;
- evitare polling aggressivo;
- debounce degli eventi multipli;
- notificare il parser.

Su .NET:

```text
System.IO.FileSystemWatcher
```

---

## 22.3 SafeJsonReader

Il gioco può modificare il file proprio mentre il tracker tenta di leggerlo.

Strategia:

```text
File changed
    |
    v
attendi 50-100 ms
    |
    v
leggi
    |
    +-- JSON valido -> OK
    |
    +-- JSON invalido
            |
            v
         retry
            |
            +-- ancora errore
                    |
                    v
              backup JSON
```

Il tracker non deve mai bloccarsi per un errore temporaneo di parsing.

---

## 22.4 RunState

Modello interno normalizzato.

Esempio:

```text
RunState
|
+-- RunId
+-- SaveVersion
+-- SaveSequence
+-- Round
+-- Wins
+-- Lives
+-- Gold
+-- TrainerId
+-- ShopRank
+-- IsRanked
+-- Team[]
+-- Bench[]
+-- Shop[]
+-- Trinkets[]
+-- Items[]
+-- EventHistory[]
+-- Blackboard
```

---

## 22.5 StateDiff

Componente fondamentale.

Riceve:

```text
PreviousState
CurrentState
```

e produce eventi dedotti.

Esempi:

```text
gold diminuisce
team acquisisce species_id
shop perde species_id
=> BUY
```

```text
team perde species_id
gold aumenta
=> SELL
```

```text
shop_roll_seq aumenta
=> REROLL
```

```text
round aumenta
=> ROUND_STARTED
```

```text
wins aumenta
=> BATTLE_WON
```

```text
lives diminuisce
=> BATTLE_LOST
```

```text
run_id cambia
=> NEW_RUN
```

---

# 23. Event model suggerito

Possibili eventi:

```text
RunStarted
RunEnded

RoundStarted
RoundEnded

ShopUpdated
ShopRerolled

BatomonBought
BatomonSold
BatomonMoved
BatomonMerged
BatomonLeveledUp

ItemBought
ItemUsed

TrinketGained
TrinketUsed

BattleStarted
BattleWon
BattleLost

TrainerSelected
EventTriggered
EventChoiceSelected
```

Non tutti sono ancora verificati.

---

# 24. MVP v0.1

La prima versione deve essere completamente locale.

Funzioni:

```text
[ ] rilevamento automatico save
[ ] lettura live JSON
[ ] nuova run automatica
[ ] Round
[ ] Wins
[ ] Lives
[ ] Gold
[ ] Trainer
[ ] Shop rank
[ ] Team
[ ] Shop
[ ] Trinket
[ ] aggiornamento live UI
[ ] gestione JSON temporaneamente invalido
[ ] gestione chiusura gioco
```

UI indicativa:

```text
+--------------------------------------+
| Pyromaniac                Round 4    |
| Lives 8 | Wins 2 | Gold 0 | Rank 4 |
+--------------------------------------+
| TEAM                                 |
| Panbud        Lv1                    |
| Boomagon      Lv1                    |
| Sunsage       Lv3                    |
| Pebbler       Lv1                    |
| Magmite       Lv1                    |
| Stingarde     Lv1                    |
+--------------------------------------+
| SHOP                                 |
| Venopuff | Alpinine | Noxalith       |
| Dribblet                             |
+--------------------------------------+
| TRINKETS                             |
| Fake Diamond                        |
| Gold Nugget                         |
| Training Weights                    |
+--------------------------------------+
```

---

# 25. MVP v0.2 - Storico

Usare:

```text
profile_snapshot.json
```

per mostrare:

```text
- run giocate;
- run vinte;
- winrate;
- ultime partite;
- trainer più usati;
- trainer con miglior rendimento;
- Batomon più utilizzati;
- composizioni più frequenti;
- risultati ranked;
- andamento MMR.
```

---

# 26. Versione v0.3 - Database locale

Aggiungere storage locale.

Possibili tecnologie:

```text
SQLite
```

Tabelle indicative:

```text
runs
run_units
run_trinkets
run_events
snapshots
```

Esempio:

```text
runs
----
run_id
started_at
ended_at
trainer_id
is_ranked
wins
lives
outcome
round
```

---

# 27. Versione v0.4 - Backend globale

Per creare statistiche aggregate stile Untapped:

```text
Tracker client
      |
      v
REST API
      |
      v
PostgreSQL
      |
      v
Analytics
```

Possibili metriche:

```text
pick rate
win rate
10-win rate
average wins
average placement/result
trainer winrate
Batomon winrate
pair synergy
triple synergy
trinket performance
shop purchase rate
level distribution
patch trends
```

---

# 28. Versione v0.5 - Recommendation engine

Input:

```text
trainer
team
bench
shop
gold
round
trinkets
shop rank
```

Output:

```text
Buy
Skip
Sell
Reroll
Hold
Priority
```

Esempio:

```text
Current team:
Panbud
Boomagon
Sunsage

Shop:
Venopuff
Alpinine
Noxalith
Dribblet

Recommendation:
1. Venopuff
2. Noxalith
3. Reroll
```

Le raccomandazioni dovranno essere basate su dati reali e non semplicemente hardcodate.

---

# 29. Overlay

L'overlay dovrà essere:

- always-on-top;
- trasparente;
- click-through quando bloccato;
- ridimensionabile;
- spostabile;
- attivabile/disattivabile;
- leggero.

Su Windows, C#/.NET è una scelta adatta per:

```text
FileSystemWatcher
tray application
overlay window
desktop integration
```

UI possibili:

```text
WPF
WinUI 3
Avalonia
```

---

# 30. Stack consigliato

## Client Windows

```text
C#
.NET 8+
```

Possibile UI:

```text
WPF
```

Motivazioni:

- ottima integrazione Windows;
- `FileSystemWatcher`;
- JSON con `System.Text.Json`;
- tray icon;
- overlay trasparente;
- packaging semplice;
- buone prestazioni.

---

## Database locale

```text
SQLite
```

---

## Backend futuro

Possibili alternative:

```text
ASP.NET Core
```

oppure:

```text
Node.js / TypeScript
```

---

## Database server

```text
PostgreSQL
```

---

## Dashboard web futura

```text
Next.js
TypeScript
```

---

# 31. Sicurezza e compatibilità

Il tracker iniziale deve essere **read-only**.

Non deve:

```text
modificare run_save.json
modificare file del gioco
scrivere dentro opponent_cache
iniettare DLL
leggere memoria processo
automatizzare input
interagire con anti-cheat
```

Deve limitarsi a:

```text
leggere i file locali
analizzare gli snapshot
mostrare statistiche
```

Questo rende il progetto molto meno invasivo.

Prima di una distribuzione pubblica resta opportuno verificare i termini d'uso del gioco e le eventuali policy del produttore relative a companion/overlay.

---

# 32. Robustezza richiesta

Il tracker deve gestire:

```text
gioco non avviato
run_save.json non presente
file temporaneamente bloccato
JSON scritto parzialmente
nuova run
fine run
cambio account
aggiornamento schema
campi JSON mancanti
campi JSON nuovi
```

Il parser deve quindi essere tollerante.

---

# 33. Regole di parsing

Non assumere che tutti gli slot abbiano un Batomon.

Esempio:

```json
[
  {
    "species_id": "venopuff"
  },
  null,
  null
]
```

Analogamente `team` può contenere:

```json
{}
```

Quindi:

```text
if species_id exists
    unit valid
else
    empty slot
```

---

# 34. Schema forward-compatible

Evitare deserializzazione eccessivamente rigida.

Un aggiornamento del gioco potrebbe aggiungere:

```json
{
  "new_field": 123
}
```

Il tracker deve ignorare campi sconosciuti senza fallire.

---

# 35. Test ancora da completare

Prima di considerare completa la conoscenza dello schema live, restano utili questi test.

## Merge / level-up

Verificare:

```text
level: 1 -> 2
```

e se vengono generate altre modifiche.

---

## Shiny

Verificare:

```text
is_shiny: false -> true
```

o come viene rappresentato un merge shiny.

---

## Item

Analizzare:

```text
item_state
items_bought_this_round
last_shop_item_ids
```

durante acquisto e utilizzo.

---

## Trinket

Verificare variazioni in:

```text
trinket_ids
trinkets_ever_held
used_trinket_ids
```

durante ottenimento/utilizzo.

---

## Battle lifecycle

Verificare con precisione:

```text
pending_battle_opponent
pending_round_start
prefetched_slate
slate_prefetch_complete
```

per capire se è possibile distinguere:

```text
battle preparing
battle started
battle ended
```

---

## Backup

Verificare se:

```text
run_save.bak.json
```

rappresenta effettivamente lo snapshot precedente e con quale frequenza viene aggiornato.

---

# 36. Stato attuale di fattibilità

```text
Lettura live             CONFERMATA
Team                     CONFERMATO
Shop                     CONFERMATO
Gold                     CONFERMATO
Round                    CONFERMATO
Wins                     CONFERMATO
Lives                    CONFERMATO
Trainer                  CONFERMATO
Shop rank                CONFERMATO
Trinket list             CONFERMATA
Nuova run                CONFERMATA
Run ID                   CONFERMATO
Save sequence            CONFERMATO
Storico run              CONFERMATO
Profilo / rank           CONFERMATO
Reroll detection         CONFERMATO
Buy detection            CONFERMATO
Sell detection           CONFERMATO
Loss detection           CONFERMATA

Merge / level-up         DA TESTARE
Item lifecycle           DA TESTARE
Trinket lifecycle        DA TESTARE
Battle start preciso     DA TESTARE
Backup semantics         DA TESTARE
Opponent cache schema    DA ANALIZZARE
```

---

# 37. Conclusione tecnica

La fattibilità del progetto è stata dimostrata.

La sorgente principale sarà:

```text
run_save.json
```

per lo stato live.

La sorgente secondaria sarà:

```text
profile_snapshot.json
```

per storico e statistiche personali.

In futuro:

```text
opponent_cache/*.json
```

potrebbe fornire una base aggiuntiva per analisi del meta o degli avversari.

L'architettura base non richiede tecniche invasive.

```text
Batomon
   |
   v
Local JSON
   |
   v
Watcher
   |
   v
Parser
   |
   v
StateDiff
   |
   +--> Overlay
   |
   +--> History
   |
   +--> Analytics
   |
   +--> Recommendation Engine
```

Il progetto può quindi entrare nella fase di sviluppo del **MVP v0.1**.
