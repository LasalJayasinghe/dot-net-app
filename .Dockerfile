# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy solution and project files
COPY *.sln ./
COPY dotnetApp/*.csproj ./dotnetApp/
RUN dotnet restore

# Copy all source files
COPY dotnetApp/. ./dotnetApp/
WORKDIR /src/dotnetApp

# Publish app to /app folder
RUN dotnet publish -c Release -o /app

# Stage 2: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

# Copy published app
COPY --from=build /app ./

# Expose port (Render sets $PORT automatically)
ENV DOTNET_RUNNING_IN_CONTAINER=true
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false
ENV ASPNETCORE_URLS=http://+:$PORT

# Start the app
ENTRYPOINT ["dotnet", "dotnetApp.dll"]
