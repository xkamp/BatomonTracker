# Stato implementazione

## Incremento 1 completato

La prima base del progetto e stata creata con questa struttura:

```text
src/
  BatomonTracker.Core/
  BatomonTracker.Infrastructure/
  BatomonTracker.App/
tests/
  BatomonTracker.Core.Tests/
```

### Implementato

- soluzione `BatomonTracker.sln`;
- modello `AccountFiles`;
- `GodotSaveLocator` basato sulla directory reale di Windows;
- ricerca di tutti gli account sotto `accounts`;
- selezione dell'account con file piu recente;
- riconoscimento della run live quando esiste `run_save.json`;
- parser tollerante per i campi principali di `profile_snapshot.json`;
- parser tollerante per i campi principali di `run_save.json`;
- `SafeJsonReader` con retry e gestione di JSON invalido o file non accessibile;
- `StateDiff` per nuova run, round, vittorie, sconfitte, reroll, acquisti e vendite;
- `RunSaveMonitor` con `FileSystemWatcher`, debounce e polling di sicurezza;
- rilevamento esplicito di nuova run, cambio account e scomparsa del save live;
- prima finestra WPF con stato dell'account e riepilogo profilo;
- test del locator con directory temporanee;
- test del parser con fixture sintetica e campi sconosciuti;
- test end-to-end del monitor su file temporanei.
- verifica su snapshot reale: `shop_content` puo contenere anche item stringa come `voucher`;
- lettura e visualizzazione della panchina (`bench`).
- lettura e visualizzazione dei trinket (`trinket_ids`).
- persistenza locale SQLite di run, snapshot ed eventi;
- collegamento della persistenza al monitor live;
- database applicativo creato in `%LOCALAPPDATA%\BatomonTracker\tracker.db`;
- pannello Storico WPF con elenco delle run e conteggio di snapshot/eventi;
- importazione idempotente di `match_history` da `profile_snapshot.json`;
- visualizzazione separata delle run live e delle run completate;
- backend statistiche mantenuto, pannello statistiche rimosso dalla UI in attesa di nuova progettazione;
- icone dei Batomon copiate nell'output dell'app e risolte tramite `species_id`;
- overlay con icone del team, livello, shiny e tooltip identificativo;
- overlay live con build corrente, Batomon consigliati e strumenti consigliati dalla composizione globale piu vicina alle run da 10 vittorie;
- nuova shell dashboard WPF con palette `#233d4d`, `#fe7f2d`, `#fcca46`, `#a1c181`, `#619b8a`;
- Home dashboard con podio top 3 giocati, top 3 per vittorie, miglior combo e miglior squadra a 10 vittorie;
- statistiche Home alimentate dagli analytics globali PostgreSQL, con icone Batomon risolte dagli asset locali;
- teaser Patch notes nella Home con navigazione alla pagina patch;
- schermata di accesso con modalita locale/offline;
- sidebar iniziale per Home, Live Overlay, run, build, leaderboard, patch notes e impostazioni;
- icona profilo nella dashboard con menu login/logout;
- logout limitato alla sessione Steam, senza cancellare database e dati locali;
- server ASP.NET Core con flusso Steam OpenID e verifica del callback;
- callback locale `127.0.0.1:48721` verso il client desktop;
- sessione Steam locale in `%LOCALAPPDATA%\BatomonTracker\auth-session.json`;
- sync HTTP con timeout e retry per errori temporanei, 429 e 5xx;
- token Steam locale protetto con DPAPI e scrittura atomica della sessione;
- sync delle run con panchina e trinket quando presenti nello storico del profilo;
- client WPF configurato come `WinExe`, senza console associata;
- overlay live compatto always-on-top e trasparente;
- click-through predefinito e modalita modifica tramite hotkey;
- hotkey `Ctrl+Shift+O` per mostrare/nascondere l'overlay;
- hotkey `Ctrl+Shift+L` per bloccare/sbloccare l'interazione;
- rilevamento automatico della finestra `Batomon Showdown`;
- posizionamento relativo al 5% da sinistra e dall'alto;
- memorizzazione della posizione scelta dall'utente come offset relativo;
- mantenimento della posizione durante spostamento e ridimensionamento del gioco;
- persistenza della posizione relativa in `%LOCALAPPDATA%\BatomonTracker\overlay-position.json`;
- tray icon con comandi per dashboard, overlay, click-through, scala e opacita;
- persistenza delle impostazioni overlay in `%LOCALAPPDATA%\BatomonTracker\overlay-settings.json`;
- primo `BuildRecommendationEngine` con priorita Buy/Hold prudenziali;
- spiegazione della priorita con copie possedute e possibile merge;
- suggerimento acquisto quando lo shop contiene una specie gia presente nella build;
- aggiornamenti delle statistiche sincronizzati sul thread UI;
- apertura e chiusura automatica insieme alla finestra del gioco;
- statistiche personali nella Home con winrate, media vittorie, trainer e Batomon piu vincenti;
- filtri personali ranked/non-ranked e statistiche recuperate da PostgreSQL quando autenticati;
- visualizzazione di MMR, Ranked MMR, tier e stelle dal profilo locale;

### Non ancora implementato

- tray icon;
- Recommendation Engine avanzato con catalogo completo delle sinergie;
- sessione online lato server, token refresh e account persistente sul backend;

## Verifica ambiente

La soluzione e stata compilata con .NET SDK 8 senza warning. I test automatici sono verdi. Per ripetere la verifica:

```text
dotnet restore BatomonTracker.sln
dotnet build BatomonTracker.sln
dotnet test BatomonTracker.sln
```

Il prossimo incremento deve aggiungere winrate e statistiche per trainer/Batomon. L'overlay resta successivo alla stabilizzazione della UI normale.
