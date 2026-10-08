# Architettura

## Flusso generale

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
FileWatcher + polling di sicurezza
        |
        v
SafeJsonReader
        |
        v
RawRunSnapshot
        |
        v
RunStateMapper
        |
        v
RunState
        |
        v
StateDiff
    /       |        \
   v        v         v
 UI       Storage   Diagnostics
```

## Responsabilita dei moduli

### `RunSaveLocator`

Scopre il percorso Godot, enumera gli account e segnala il save attivo. Non interpreta il JSON.

### `RunFileMonitor`

Combina `FileSystemWatcher`, debounce, coda seriale e polling di sicurezza. Non produce eventi di gameplay.

### `SafeJsonReader`

Legge il file, gestisce retry e parsing. Restituisce errori classificati e non blocca il processo principale.

### `RunStateMapper`

Trasforma il documento raw in un modello interno stabile e tollerante.

### `StateDiff`

Confronta due `RunState` e produce eventi con timestamp, `run_id`, `save_seq` e confidenza.

### `Storage`

Salva snapshot, run concluse ed eventi. Deve essere opzionale nel primo prototipo.

### `UI`

Visualizza lo stato corrente e lo stato del monitoraggio. Non deve contenere logica di parsing.

## Regole di progettazione

- Il core deve essere testabile senza avviare il gioco.
- La UI riceve aggiornamenti tramite un modello osservabile o messaggi, non legge file direttamente.
- Il passaggio da un account a un altro resetta il contesto precedente.
- Un nuovo `run_id` chiude logicamente il contesto precedente e apre una nuova run.
- `save_seq` ordina gli snapshot ma non rappresenta un evento singolo.
- `save_version` deve essere validato e mostrato nei diagnostici.

## Stato dell'applicazione

La UI dovrebbe distinguere almeno questi stati:

```text
GameNotDetected
SaveNotFound
Monitoring
TemporarilyUnreadable
UnsupportedSchema
```

Questo evita di mostrare dati vecchi come se fossero ancora live.
