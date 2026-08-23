FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY HabitTrackerApi.csproj ./
RUN dotnet restore "HabitTrackerApi.csproj"

COPY . .
RUN dotnet publish "HabitTrackerApi.csproj" -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app


RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*


RUN addgroup --gid 1654 appgroup \
    && adduser --uid 1654 --gid 1654 --disabled-password --gecos "" appuser

COPY --from=build --chown=appuser:appgroup /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

USER appuser

ENTRYPOINT ["dotnet", "HabitTrackerApi.dll"]