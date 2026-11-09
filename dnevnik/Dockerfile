# Этап 1: Сборка приложения
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Копируем файл проекта и восстанавливаем зависимости
COPY ["dnevnik.csproj", "./"]
RUN dotnet restore "dnevnik.csproj"

# Копируем все остальные файлы и собираем приложение
COPY . .
WORKDIR "/src/"
RUN dotnet publish "dnevnik.csproj" -c Release -o /app/publish

# Этап 2: Финальный образ для запуска
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

# Важно: Vercel передает порт через переменную окружения PORT
# Приложение должно слушать этот порт.
ENV ASPNETCORE_URLS=http://+:${PORT:-80}
EXPOSE 80

ENTRYPOINT ["dotnet", "dnevnik.dll"]
