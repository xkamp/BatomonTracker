# Testing e robustezza

## Test del parser

Preparare fixture JSON per:

- snapshot completo;
- team vuoto;
- shop con slot `null`;
- unita senza `species_id`;
- campi sconosciuti;
- campi mancanti;
- JSON vuoto o troncato;
- `save_version` non supportata;
- numeri nulli e valori ai limiti.

Il parser deve ignorare campi sconosciuti e rifiutare soltanto cio che impedisce di identificare lo snapshot.

## Test del locator

Verificare:

- directory Godot assente;
- nessun account;
- un solo account;
- piu account;
- file aggiornato piu recentemente;
- cambio dell'account attivo;
- file senza permessi di lettura.

## Test del reader

Simulare:

- file valido al primo tentativo;
- file temporaneamente bloccato;
- JSON scritto a meta;
- file cancellato durante la lettura;
- recupero dal backup;
- retry esauriti;
- file aggiornato durante il parsing.

## Test dello StateDiff

Ogni fixture deve contenere almeno `previous` e `current`. Verificare:

```text
run_id diverso                  -> NewRun
round +1                        -> RoundStarted
wins +1                         -> BattleWon
lives -1                        -> BattleLost
shop_roll_seq +1               -> ShopRerolled
team + unita, gold diminuito   -> possibile Buy
team - unita, gold aumentato   -> possibile Sell
nessuna modifica               -> nessun evento
save_seq saltato               -> stato aggiornato, non eventi intermedi inventati
```

I test devono includere casi ambigui. In questi casi lo stato va aggiornato, ma l'evento specifico deve essere omesso o marcato a bassa confidenza.

## Test manuali in gioco

Durante una sessione reale registrare snapshot prima e dopo:

1. avvio di una nuova run;
2. cambio round;
3. acquisto;
4. vendita;
5. reroll;
6. sconfitta;
7. vittoria;
8. merge e level-up;
9. ottenimento e utilizzo di trinket;
10. acquisto e utilizzo di item;
11. chiusura e riapertura del gioco.

Salvare i dati di test fuori dal repository se contengono identificativi personali o dati non necessari.

## Metriche utili

Durante lo sviluppo misurare:

- tempo tra modifica del file e aggiornamento UI;
- percentuale di letture valide;
- numero di retry per sessione;
- eventi duplicati;
- eventi ambigui;
- memoria usata dal tracker;
- comportamento quando il gioco resta aperto per molte ore.

## Error handling

Gli errori devono essere classificati, registrati e mostrati con messaggi utili. Il tracker non deve terminare per:

- gioco chiuso;
- save assente;
- JSON temporaneamente invalido;
- account cambiato;
- schema con campi aggiuntivi.

Un errore di schema incompatibile deve disattivare soltanto il parsing di quello snapshot e lasciare il monitor attivo.
