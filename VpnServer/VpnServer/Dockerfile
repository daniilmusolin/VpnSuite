# Этап 1: Build
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Копируем csproj и восстанавливаем зависимости
COPY VpnServer.csproj .
RUN dotnet restore

# Копируем весь код и собираем
COPY . .
RUN dotnet publish -c Release -o /app/publish

# Этап 2: Runtime
FROM mcr.microsoft.com/dotnet/runtime:9.0 AS runtime
WORKDIR /app

# Устанавливаем необходимые пакеты для сети
RUN apt-get update && apt-get install -y \
    iproute2 \
    iptables \
    && rm -rf /var/lib/apt/lists/*

# Копируем собранное приложение
COPY --from=build /app/publish .

# Создаем директории для данных
RUN mkdir -p /app/config /app/logs

# Открываем порты
EXPOSE 51820/udp  # VPN порт
EXPOSE 5000/tcp   # API порт

# Запускаем приложение
ENTRYPOINT ["dotnet", "VpnServer.dll"]