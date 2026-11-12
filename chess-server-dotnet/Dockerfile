FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY chess-server-dotnet/ChessServer.csproj chess-server-dotnet/
RUN dotnet restore chess-server-dotnet/ChessServer.csproj
COPY chess-server-dotnet/ chess-server-dotnet/
WORKDIR /src/chess-server-dotnet
RUN dotnet publish ChessServer.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 10000
ENTRYPOINT ["dotnet", "ChessServer.dll"]
