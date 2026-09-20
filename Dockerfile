# syntax=docker/dockerfile:1
# One image holding all five .NET services. Each compose service runs the same
# image and picks which one to start via the SERVICE env var. Build context is
# the repository root (needs src/, Directory.Build.props and nuget.config).

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

COPY Directory.Build.props nuget.config ./
COPY src/ ./src/

# Authenticate to the private GitHub Packages feed, then restore+publish each
# service. Pass the token as a build secret (see docker-compose.yml secrets:).
RUN --mount=type=secret,id=github_token \
    dotnet nuget update source github \
      --username leonardopottmayer \
      --password "$(cat /run/secrets/github_token)" \
      --store-password-in-clear-text \
      --configfile nuget.config \
    && for p in Gateway Shortening Redirect Analytics Kgs; do \
         dotnet publish "src/Pottmayer.UrlShortener.$p/Pottmayer.UrlShortener.$p.csproj" \
           -c "$BUILD_CONFIGURATION" -o "/app/$p" /p:UseAppHost=false || exit 1; \
       done

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080
COPY --from=build /app/ ./
# Overridden per compose service (Gateway | Shortening | Redirect | Analytics | Kgs).
# cd into the service folder so ContentRoot points there and its appsettings.json loads
# (each service's files live in /app/<Service>/, not in the shared /app WORKDIR).
ENV SERVICE=Gateway
ENTRYPOINT ["sh", "-c", "cd \"/app/$SERVICE\" && exec dotnet \"Pottmayer.UrlShortener.$SERVICE.dll\""]
