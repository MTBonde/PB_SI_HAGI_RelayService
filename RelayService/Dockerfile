FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ["RelayService.csproj", "."]
RUN dotnet restore
COPY . .
RUN dotnet build "RelayService.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "RelayService.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "RelayService.dll"]
