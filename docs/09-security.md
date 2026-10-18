# Sicurezza e deployment

## Configurazione locale

Il file `.env` non deve essere committato. Crearlo a partire da `.env.example` e sostituire tutti i valori di esempio:

```powershell
Copy-Item .env.example .env
docker compose up -d --build
```

Il database e l'API vengono pubblicati solo su `127.0.0.1` dal file Compose. Per un deployment remoto e necessario aggiungere un reverse proxy HTTPS e non esporre direttamente PostgreSQL.

## Autenticazione Steam

- Il callback locale e vincolato a `127.0.0.1:48721`.
- Lo stato di autenticazione viene verificato e consumato una sola volta dal server.
- Il browser riceve un codice monouso, non il JWT.
- Il codice viene scambiato via POST dal client.
- Gli access token durano 30 minuti.
- I refresh token sono ruotati e conservati nel database solo come hash.
- Il token locale del client e protetto con DPAPI Windows.

## Dati analytics

Il server associa gli snapshot analytics allo SteamID del token autenticato e non accetta lo `UserId` fornito dal client. Le richieste hanno limiti su snapshot, unità, trinket e JSON grezzo.

## Verifiche

```powershell
dotnet test .\img\BatomonTracker.sln
dotnet list .\img\BatomonTracker.sln package --vulnerable --include-transitive
```

Il pacchetto SQLite nativo transitivo e ancora segnalato dall'advisory `GHSA-2m69-gcr7-jv3q`; la dipendenza arriva da `Microsoft.Data.Sqlite` e va rivalutata quando il maintainer pubblichera una versione non affetta o sostituita.
