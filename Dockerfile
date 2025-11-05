# === Build ===
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# 1) Copiamos primero los archivos de proyecto para aprovechar la caché de capas
COPY ./BotApp.csproj ./
RUN dotnet restore ./BotApp.csproj

# 2) Ahora copiamos el resto del código
COPY . .

# 3) Publicamos (sin restaurar de nuevo)
RUN dotnet publish ./BotApp.csproj -c Release -o /out /p:UseAppHost=false

# === Run ===
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app

# Nota: .NET 8 aspnet ya corre como usuario no-root por defecto
ENV ASPNETCORE_URLS=http://0.0.0.0:8080
EXPOSE 8080

COPY --from=build /out .
ENTRYPOINT ["dotnet","BotApp.dll"]