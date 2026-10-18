# Avvio di Batomon Tracker

Guida per avviare client WPF, API, PostgreSQL e dashboard web.

## Prerequisiti

- Windows 10 o Windows 11;
- .NET SDK 8;
- Docker Desktop per API, PostgreSQL e analytics globali;
- Batomon Showdown avviato almeno una volta.

Verifica:

```powershell
dotnet --version
docker --version
```

## Avvio completo

Dalla root del progetto:

```powershell
Set-Location "C:\Users\lucac\Desktop\ProgettiPython\BatomonTracker"
```

Avviare database e API:

```powershell
Copy-Item ".env.example" ".env"
# Modificare .env e sostituire le password/segreti di esempio con valori casuali.
docker compose up -d --build
docker compose ps
```

Verificare l'API:

```powershell
Invoke-RestMethod http://127.0.0.1:5000/health
```

La risposta corretta contiene `status: ok` e `database: ok`.

La dashboard web e disponibile su `http://127.0.0.1:5000/`.

Compilare e testare:

```powershell
dotnet restore "img\BatomonTracker.sln"
dotnet build "img\BatomonTracker.sln"
dotnet test "img\BatomonTracker.sln"
```

Avviare il client WPF:

```powershell
dotnet run --project "src\BatomonTracker.App\BatomonTracker.App.csproj"
```

Oppure avviare direttamente la build Debug:

```powershell
Start-Process ".\src\BatomonTracker.App\bin\Debug\net8.0-windows\BatomonTracker.App.exe"
```

## Primo utilizzo

1. Avviare Batomon Showdown.
2. Avviare Batomon Tracker.
3. Selezionare `CONTINUA OFFLINE` oppure accedere con Steam.
4. Il tracker trova automaticamente l'account Godot e `run_save.json`.
5. Usare il menu laterale:
   - `Home`: statistiche globali e podi;
   - `Live overlay`: team corrente e raccomandazioni globali;
   - `Le mie run`: storico completo delle run personali;
   - `Le mie build`: top 3 build personali e build create manualmente;
   - `Analytics`: analisi della cache locale;
   - `Impostazioni`: scala e opacita dell'overlay.

All'avvio, se una sessione Steam e gia presente, il tracker scansiona i file utili della cartella Godot e sincronizza in background i dati strutturati. Vengono inviati run, team, livelli, trinket, vittorie, round e snapshot analytics. I file JSON originali restano sul PC e non vengono caricati integralmente.

## Overlay

Hotkey:

- `Ctrl+Shift+O`: mostra o nasconde l'overlay;
- `Ctrl+Shift+L`: abilita o disabilita la modalita modifica e spostamento.

Le scorciatoie possono essere cambiate dalla pagina `Impostazioni`: cliccare il relativo campo e premere la nuova combinazione.

Quando la modalita modifica e attiva, trascinare qualsiasi bordo o angolo dell'overlay per modificarne liberamente le dimensioni. La dimensione viene salvata automaticamente.

Il menu tray permette anche di cambiare visibilita, click-through, scala e opacita.

Le impostazioni vengono salvate in:

```text
%LOCALAPPDATA%\BatomonTracker\overlay-settings.json
%LOCALAPPDATA%\BatomonTracker\overlay-position.json
```

## Modalita offline

Il client funziona anche senza Docker:

```powershell
dotnet run --project "src\BatomonTracker.App\BatomonTracker.App.csproj"
```

In questa modalita funzionano lettura live, overlay, storico locale e build personali. Analytics globali e sincronizzazione online richiedono l'API.

## Login Steam

Con Docker attivo, cliccare `ACCEDI CON STEAM` e completare il login nel browser. Il callback locale e:

```text
http://127.0.0.1:48721/auth/callback/
```

La sessione viene salvata con token protetto da DPAPI Windows in:

```text
%LOCALAPPDATA%\BatomonTracker\auth-session.json
```

## Pubblicazione Windows

```powershell
powershell -ExecutionPolicy Bypass -File ".\scripts\publish-windows.ps1"
```

Output:

```text
artifacts\BatomonTracker
```

Per ARM64:

```powershell
powershell -ExecutionPolicy Bypass -File ".\scripts\publish-windows.ps1" -Runtime win-arm64
```

## Configurazione API

Il client usa di default `http://127.0.0.1:5000`. Per usare un altro server:

```powershell
$env:BATOMON_API_URL = "http://127.0.0.1:5000"
dotnet run --project "src\BatomonTracker.App\BatomonTracker.App.csproj"
```

## Arresto

Arrestare i container mantenendo i dati:

```powershell
docker compose down
```

Eliminare anche il database e tutti i dati globali:

```powershell
docker compose down -v
```

`down -v` e distruttivo per PostgreSQL.

## Risoluzione problemi

API non raggiungibile:

```powershell
docker compose ps
docker compose logs api
docker compose logs postgres
docker compose up -d --build
```

Nessun save trovato: verificare che esista:

```text
%APPDATA%\Godot\app_userdata\Batomon Showdown\accounts\<account>\run_save.json
```

Gli asset dell'overlay vengono copiati da `img\*.png`. Dopo aver sostituito immagini, ricompilare:

```powershell
dotnet build "img\BatomonTracker.sln"
```
