FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY Shared/ Shared/
COPY Contracts/ Contracts/
COPY Services/ordering_service/ Services/ordering_service/
RUN dotnet restore Services/ordering_service/Ordering.API/Ordering.API.csproj
RUN dotnet publish Services/ordering_service/Ordering.API/Ordering.API.csproj -c Release --no-restore -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
USER $APP_UID
EXPOSE 5004
ENTRYPOINT ["dotnet", "Ordering.API.dll"]
