# Guida allo sviluppo

Questa guida descrive l'ordine consigliato per costruire il progetto senza anticipare funzionalita non ancora validate.

## Stack consigliato

- C#;
- .NET 8 o superiore;
- WPF per il primo client Windows;
- `System.Text.Json` per il parsing;
- `FileSystemWatcher` per la notifica dei cambiamenti;
- SQLite dalla versione che introduce lo storico locale.

WPF e una scelta pragmatica per il primo MVP grazie all'integrazione con Windows, al supporto per finestre always-on-top e alla semplicita di packaging. La UI deve comunque restare separata dal core, cosi in futuro sara possibile cambiare tecnologia.

## Fase 1: creare la soluzione

Struttura suggerita:

```text
src/
  BatomonTracker.App/          # WPF, tray e composizione dei servizi
  BatomonTracker.Core/         # modelli, parser e state diff
  BatomonTracker.Infrastructure/ # filesystem e persistenza
tests/
  BatomonTracker.Core.Tests/
docs/
```

Il core non deve conoscere WPF e non deve leggere direttamente il filesystem. Questo rende possibili test con JSON statici.

## Fase 2: localizzare il save attivo

Implementare `RunSaveLocator` con queste regole:

1. costruire il percorso dalla variabile `APPDATA`;
2. verificare che la cartella Godot esista;
3. enumerare `accounts/*/run_save.json`;
4. ignorare file non leggibili o temporaneamente assenti;
5. selezionare il file con `LastWriteTimeUtc` piu recente;
6. esporre l'account selezionato e il percorso corrente;
7. notificare se il percorso attivo cambia.

Il locator non deve lanciare eccezioni non gestite se il gioco non e avviato.

## Fase 3: leggere il file in sicurezza

Implementare `SafeJsonReader` come servizio indipendente:

1. ricevere il percorso del file;
2. attendere un breve debounce dopo la notifica;
3. leggere il contenuto;
4. deserializzare lo snapshot;
5. se il JSON non e valido, ritentare con un piccolo backoff;
6. usare `run_save.bak.json` solo come fallback documentato;
7. scartare lo snapshot se non e possibile validarlo.

Il reader deve restituire un risultato esplicito, ad esempio `Success`, `FileMissing`, `InvalidJson`, `SchemaUnsupported` o `AccessDenied`. Non deve nascondere tutti gli errori dietro un semplice `null`.

## Fase 4: osservare gli aggiornamenti

Usare `FileSystemWatcher` sulla directory dell'account attivo, filtrando `run_save.json`. Gli eventi devono passare attraverso un debounce e una coda seriale.

Il watcher e una notifica, non una garanzia che ogni stato sia stato osservato. Il sistema deve quindi caricare sempre lo snapshot piu recente disponibile.

In aggiunta, eseguire una scansione periodica leggera come fallback. Questo copre eventi persi e cambi account.

## Fase 5: normalizzare lo stato

Convertire il JSON in un modello interno `RunState`. Il modello deve:

- usare nomi stabili indipendenti dallo schema raw;
- tollerare campi sconosciuti;
- rappresentare esplicitamente slot vuoti;
- conservare `run_id`, `save_seq` e `save_version`;
- distinguere dati mancanti da valori pari a zero;
- non fallire per un singolo Batomon incompleto.

## Fase 6: implementare lo StateDiff

Confrontare snapshot consecutivi appartenenti allo stesso `run_id`. Prima di dedurre un evento verificare che entrambi gli stati siano validi.

Regole MVP:

```text
run_id cambia                         -> NEW_RUN
round aumenta                         -> ROUND_STARTED
wins aumenta                          -> BATTLE_WON
lives diminuisce                      -> BATTLE_LOST
shop_roll_seq aumenta                 -> SHOP_REROLLED
team acquisisce un'unita              -> BUY, se coerente con oro/shop
team perde un'unita e oro aumenta     -> SELL, se coerente con lo stato
```

Gli eventi dedotti devono contenere anche il livello di confidenza. Se due interpretazioni sono possibili, aggiornare lo stato UI ma non inventare un evento preciso.

## Fase 7: costruire la UI dell'MVP

La prima schermata deve mostrare soltanto informazioni affidabili:

- trainer, round e stato della connessione;
- lives, wins, gold e shop rank;
- team con livello e shiny;
- shop con slot vuoti espliciti;
- trinket correnti;
- messaggio chiaro quando il gioco e chiuso o il JSON e temporaneamente invalido.

L'overlay puo essere aggiunto dopo una finestra normale funzionante. Prima bisogna validare il flusso dati e la leggibilita.

## Fase 8: persistenza locale

Non introdurre SQLite prima che il modello `RunState` e lo `StateDiff` siano stabili. In una prima iterazione e sufficiente salvare snapshot raw e log degli errori in una directory applicativa del tracker.

Quando si aggiunge SQLite, usare almeno:

```text
runs
run_units
run_trinkets
run_events
snapshots
```

## Definition of Done per MVP v0.1

- il tracker parte anche senza gioco aperto;
- trova automaticamente il save attivo;
- aggiorna la UI dopo una modifica reale del file;
- gestisce JSON incompleto senza chiudersi;
- riconosce una nuova run;
- visualizza team, shop, trinket e statistiche base;
- registra errori diagnostici senza esporre dati sensibili;
- ha test per parser, locator e transizioni principali.
