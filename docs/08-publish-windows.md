# Pubblicazione Windows

Per creare una build autonoma x64 eseguire dalla root del progetto:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\publish-windows.ps1
```

L'output viene creato in `artifacts\BatomonTracker`. La build include il runtime .NET, SQLite e gli asset PNG dell'overlay.

Per una build ARM64:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\publish-windows.ps1 -Runtime win-arm64
```

La configurazione API puo essere sovrascritta con la variabile `BATOMON_API_URL`. I dati locali restano in `%LOCALAPPDATA%\BatomonTracker`.

Il token Steam della sessione desktop viene protetto con DPAPI usando l'utente Windows corrente. Se il profilo Windows cambia, sara necessario effettuare nuovamente il login.

Il server valida issuer e audience JWT. In produzione impostare `Jwt:Secret`, `Jwt:Issuer` e `Jwt:Audience` tramite configurazione esterna e non usare i valori di sviluppo.

Gli analytics sono globali per impostazione predefinita. Gli endpoint analytics accettano `scope=mine`: in questo caso richiedono `Authorization: Bearer <jwt>` e filtrano le snapshot sullo `steam_id` del token. La dashboard web permette di selezionare entrambe le viste e inserire il JWT per la vista personale.
