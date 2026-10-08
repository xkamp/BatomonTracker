# Analisi e fattibilita

## Obiettivo

Realizzare un companion Windows che mostri in tempo quasi reale lo stato della run di Batomon Showdown e, in seguito, lo storico e le statistiche del giocatore.

Il tracker non deve automatizzare il gioco. Il suo ruolo e osservare, normalizzare e visualizzare dati gia salvati localmente dal gioco.

## Sorgenti dati

La sorgente live principale e:

```text
%APPDATA%\Godot\app_userdata\Batomon Showdown\accounts\<account>\run_save.json
```

La sorgente per lo storico del profilo e:

```text
%APPDATA%\Godot\app_userdata\Batomon Showdown\accounts\<account>\profile_snapshot.json
```

Il tracker deve cercare dinamicamente tutte le directory sotto `accounts` e scegliere il salvataggio aggiornato piu recentemente. Non deve dipendere da un account hardcodato.

## Perche la soluzione e praticabile

`run_save.json` viene aggiornato durante la partita e contiene gia i dati necessari al primo prodotto:

- `round`, `wins`, `lives`, `gold`;
- `trainer_id`, `shop_rank`;
- `team`, `bench`, `shop_content`;
- `trinket_ids`;
- `run_id`, `save_seq`, `save_version`;
- `item_state` e `blackboard`.

Questo evita OCR, API ufficiali, lettura della memoria, packet sniffing e tecniche invasive.

## Confini dell'analisi

I campi osservati provengono da test locali e possono cambiare con una patch del gioco. La documentazione distingue sempre tra:

- **confermato**: osservato direttamente durante una modifica del gioco;
- **dedotto**: interpretato confrontando due snapshot;
- **da verificare**: ipotesi non ancora testata.

Questa distinzione deve essere mantenuta anche nel codice e nell'interfaccia.

## Rischi principali

1. Il gioco puo scrivere il JSON mentre il tracker lo legge.
2. Un evento del filesystem puo arrivare piu volte o in ordine non prevedibile.
3. Due snapshot possono saltare diversi valori di `save_seq`.
4. Un aggiornamento puo aggiungere, rinominare o rimuovere campi.
5. Le modifiche ai dati non identificano sempre in modo univoco l'azione che le ha causate.

Per questi motivi il tracker deve privilegiare lo stato corrente affidabile rispetto alla ricostruzione aggressiva di ogni evento intermedio.

## Policy e distribuzione

La prima versione deve essere locale e read-only. Prima della distribuzione pubblica vanno verificati i termini d'uso del gioco e le policy relative a companion, overlay e raccolta di statistiche.
