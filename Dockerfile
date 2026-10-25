# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Restore first so the package layer is cached until the csproj changes
COPY *.csproj ./
RUN dotnet restore

COPY . ./
RUN dotnet publish -c Release -o /app

# Stage 2: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime

# The app writes relative paths at runtime (Logs/, PaperTradingState.json, PaperTrades.log) and
# Identity's Data Protection keys go under $HOME. Running from /data puts all of that on one volume
# so it survives redeploys, while the content root stays at /app where wwwroot was published.
ENV ASPNETCORE_CONTENTROOT=/app \
    HOME=/data \
    DOTNET_RUNNING_IN_CONTAINER=true \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false

RUN mkdir /data && chown $APP_UID /data
WORKDIR /data

COPY --from=build /app /app

# Non-root user that ships with the .NET 8+ images. A compromised process can't touch the rest of
# the container filesystem or bind privileged ports.
USER $APP_UID

# .NET 8+ images listen on 8080 by default (ASPNETCORE_HTTP_PORTS)
EXPOSE 8080

ENTRYPOINT ["dotnet", "/app/dotnetApp.dll"]
