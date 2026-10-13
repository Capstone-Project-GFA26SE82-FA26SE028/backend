FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY Shared/Shared.Common/ Shared/Shared.Common/
COPY Services/api_gateway/ApiGateway/ApiGateway.csproj Services/api_gateway/ApiGateway/
RUN dotnet restore Services/api_gateway/ApiGateway/ApiGateway.csproj
COPY Contracts/ Contracts/
COPY Services/api_gateway/ApiGateway/ Services/api_gateway/ApiGateway/
RUN dotnet publish Services/api_gateway/ApiGateway/ApiGateway.csproj -c Release --no-restore -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
USER $APP_UID
EXPOSE 5000
ENTRYPOINT ["dotnet", "ApiGateway.dll"]
