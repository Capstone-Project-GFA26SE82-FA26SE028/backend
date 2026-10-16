FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY Shared/ Shared/
COPY Contracts/ Contracts/
COPY Services/restaurant_service/ Services/restaurant_service/
RUN dotnet restore Services/restaurant_service/Restaurant.API/Restaurant.API.csproj
RUN dotnet publish Services/restaurant_service/Restaurant.API/Restaurant.API.csproj -c Release --no-restore -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
RUN mkdir -p /app/App_Data/qr-images && chown -R $APP_UID /app/App_Data
USER $APP_UID
EXPOSE 5002
ENTRYPOINT ["dotnet", "Restaurant.API.dll"]
