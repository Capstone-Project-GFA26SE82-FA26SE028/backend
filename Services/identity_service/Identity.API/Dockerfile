FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY Shared/ Shared/
COPY Services/identity_service/Identity.API/Identity.API.csproj Services/identity_service/Identity.API/
COPY Services/identity_service/Identity.BLL/Identity.BLL.csproj Services/identity_service/Identity.BLL/
COPY Services/identity_service/Identity.Common/Identity.Common.csproj Services/identity_service/Identity.Common/
COPY Services/identity_service/Identity.DAL/Identity.DAL.csproj Services/identity_service/Identity.DAL/
RUN dotnet restore Services/identity_service/Identity.API/Identity.API.csproj
COPY Contracts/ Contracts/
COPY Services/identity_service/Identity.API/ Services/identity_service/Identity.API/
COPY Services/identity_service/Identity.BLL/ Services/identity_service/Identity.BLL/
COPY Services/identity_service/Identity.Common/ Services/identity_service/Identity.Common/
COPY Services/identity_service/Identity.DAL/ Services/identity_service/Identity.DAL/
RUN dotnet publish Services/identity_service/Identity.API/Identity.API.csproj -c Release --no-restore -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
USER $APP_UID
EXPOSE 5001
ENTRYPOINT ["dotnet", "Identity.API.dll"]
