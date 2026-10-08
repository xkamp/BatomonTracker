# Modello dati

## Campi principali di `run_save.json`

| Campo | Uso |
|---|---|
| `run_id` | Identifica la run corrente |
| `save_seq` | Ordina gli snapshot e rileva aggiornamenti |
| `save_version` | Compatibilita dello schema |
| `round` | Round corrente |
| `wins` | Vittorie |
| `lives` | Vite residue |
| `gold` | Oro corrente |
| `trainer_id` | Trainer selezionato |
| `shop_rank` | Livello dello shop |
| `team` | Batomon attuali |
| `bench` | Unita in panchina, se presente |
| `shop_content` | Slot dello shop |
| `trinket_ids` | Trinket correnti |
| `item_state` | Stato degli oggetti |
| `blackboard` | Contatori e stato interno |

## Unita

Un'unita puo contenere:

```json
{
  "species_id": "boomagon",
  "level": 1,
  "is_shiny": false,
  "extra_types": [],
  "blackboard": {},
  "perm_buffs": {}
}
```

`species_id` e il campo minimo per considerare valida l'unita. Campi mancanti devono assumere uno stato sconosciuto o un default esplicito, non essere interpretati automaticamente come dati confermati.

## Slot dello shop

Gli slot possono essere `null`, soprattutto dopo un acquisto:

```json
[
  { "species_id": "venopuff", "level": 1 },
  null
]
```

Il modello interno deve mantenere la posizione dello slot e distinguere tra slot vuoto e dato non disponibile.

## Modello interno consigliato

```text
RunState
  RunId
  SaveVersion
  SaveSequence
  SavedAt
  Round
  Wins
  Lives
  Gold
  TrainerId
  ShopRank
  IsRanked
  Team[]
  Bench[]
  Shop[]
  Trinkets[]
  Items
  Blackboard
```

## Eventi

Un evento dedotto dovrebbe contenere almeno:

```text
TrackerEvent
  Type
  DetectedAt
  RunId
  SaveSequence
  Confidence
  Payload
```

Tipi iniziali:

```text
NewRun
RoundStarted
BattleWon
BattleLost
ShopRerolled
BatomonBought
BatomonSold
StateUpdated
```

`StateUpdated` e preferibile a un evento inventato quando il confronto non consente una deduzione certa.

## Storico profilo

`profile_snapshot.json` puo alimentare una vista separata con:

- run giocate e vinte;
- winrate;
- trainer usati;
- composizioni;
- risultati ranked;
- MMR e tier;
- storico delle partite.

Non usare questo file come sorgente live primaria: il suo scopo e lo storico persistente.
