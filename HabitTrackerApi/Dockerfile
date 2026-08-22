FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY HabitTrackerApi.csproj ./
RUN dotnet restore "HabitTrackerApi.csproj"

COPY . .
RUN dotnet publish "HabitTrackerApi.csproj" -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "HabitTrackerApi.dll"]
