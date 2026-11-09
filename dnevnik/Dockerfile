# Этап 1: Сборка приложения
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Копируем файл проекта из папки dnevnik и восстанавливаем зависимости
COPY ["dnevnik/dnevnik.csproj", "dnevnik/"]
RUN dotnet restore "dnevnik/dnevnik.csproj"

# Копируем все остальные файлы
COPY . .

# Переходим в папку с проектом и собираем приложение
WORKDIR "/src/dnevnik"
RUN dotnet publish "dnevnik.csproj" -c Release -o /app/publish

# Этап 2: Финальный образ для запуска
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

# Render передает порт через переменную PORT, по умолчанию 8080
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "dnevnik.dll"]
