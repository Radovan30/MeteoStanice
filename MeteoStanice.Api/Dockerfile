FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["MeteoStanice.Api/MeteoStanice.Api.csproj", "MeteoStanice.Api/"]
COPY ["MeteoStanice.Contracts/MeteoStanice.Contracts.csproj", "MeteoStanice.Contracts/"]
COPY ["MeteoStanice.Data/MeteoStanice.Data.csproj", "MeteoStanice.Data/"]
COPY ["MeteoStanice.Quality/MeteoStanice.Quality.csproj", "MeteoStanice.Quality/"]
RUN dotnet restore "MeteoStanice.Api/MeteoStanice.Api.csproj"
COPY . .
RUN dotnet publish "MeteoStanice.Api/MeteoStanice.Api.csproj" -c Release -o /app/publish --no-restore

FROM runtime AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "MeteoStanice.Api.dll"]
