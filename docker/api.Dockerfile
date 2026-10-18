FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY src/BatomonTracker.Server/BatomonTracker.Server.csproj src/BatomonTracker.Server/
RUN dotnet restore src/BatomonTracker.Server/BatomonTracker.Server.csproj
COPY src/BatomonTracker.Server/ src/BatomonTracker.Server/
RUN dotnet publish src/BatomonTracker.Server/BatomonTracker.Server.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
RUN adduser --disabled-password --gecos "" appuser && chown -R appuser:appuser /app
USER appuser
EXPOSE 8080
ENTRYPOINT ["dotnet", "BatomonTracker.Server.dll"]
