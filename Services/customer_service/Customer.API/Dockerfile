FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY Shared/ Shared/
COPY Services/customer_service/ Services/customer_service/
RUN dotnet restore Services/customer_service/Customer.API/Customer.API.csproj
RUN dotnet publish Services/customer_service/Customer.API/Customer.API.csproj -c Release --no-restore -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
USER $APP_UID
EXPOSE 5005
ENTRYPOINT ["dotnet", "Customer.API.dll"]
