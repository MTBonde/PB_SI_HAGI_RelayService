FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ["RelayService/RelayService.csproj", "RelayService/"]
RUN dotnet restore "RelayService/RelayService.csproj"
COPY RelayService/ RelayService/
COPY version.txt RelayService/
WORKDIR /src/RelayService
RUN dotnet build "RelayService.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "RelayService.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "RelayService.dll"]
