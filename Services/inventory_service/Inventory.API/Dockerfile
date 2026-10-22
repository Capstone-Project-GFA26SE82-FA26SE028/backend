FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY Shared/ Shared/
COPY Contracts/ Contracts/
COPY Services/inventory_service/ Services/inventory_service/
RUN dotnet restore Services/inventory_service/Inventory.API/Inventory.API.csproj
RUN dotnet publish Services/inventory_service/Inventory.API/Inventory.API.csproj -c Release --no-restore -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
USER $APP_UID
EXPOSE 5006
ENTRYPOINT ["dotnet", "Inventory.API.dll"]
