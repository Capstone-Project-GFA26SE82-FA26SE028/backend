FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY Shared/ Shared/
COPY Contracts/ Contracts/
COPY Services/billing_service/ Services/billing_service/
RUN dotnet restore Services/billing_service/Billing.API/Billing.API.csproj
RUN dotnet publish Services/billing_service/Billing.API/Billing.API.csproj -c Release --no-restore -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
USER $APP_UID
EXPOSE 5007
ENTRYPOINT ["dotnet", "Billing.API.dll"]
