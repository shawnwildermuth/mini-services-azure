# syntax=docker/dockerfile:1
# Build context is the `src` folder:  docker build -f apps/Store.Api/Dockerfile -t store-api src

# ---- build -----------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first, from the project files alone, so source edits don't invalidate the package layer.
COPY apps/Store.Domain/Store.Domain.csproj    apps/Store.Domain/
COPY apps/Store.Data/Store.Data.csproj        apps/Store.Data/
COPY apps/Store.Messaging/Store.Messaging.csproj apps/Store.Messaging/
COPY apps/Store.Api/Store.Api.csproj          apps/Store.Api/
RUN dotnet restore apps/Store.Api/Store.Api.csproj

COPY apps/Store.Domain/    apps/Store.Domain/
COPY apps/Store.Data/      apps/Store.Data/
COPY apps/Store.Messaging/ apps/Store.Messaging/
COPY apps/Store.Api/       apps/Store.Api/
RUN dotnet publish apps/Store.Api/Store.Api.csproj -c Release --no-restore -o /app/publish /p:UseAppHost=false

# ---- runtime ---------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# App Service terminates TLS and forwards X-Forwarded-*; honouring them keeps client IPs (rate limiting) and
# the request scheme correct. The port is above 1024 so the container can run as a non-root user.
ENV ASPNETCORE_HTTP_PORTS=8080 \
    ASPNETCORE_FORWARDEDHEADERS_ENABLED=true \
    DOTNET_gcServer=0
EXPOSE 8080

COPY --from=build --chown=$APP_UID:$APP_UID /app/publish .
USER $APP_UID

ENTRYPOINT ["dotnet", "Store.Api.dll"]
