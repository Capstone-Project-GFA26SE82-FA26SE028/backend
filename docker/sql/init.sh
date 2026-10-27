#!/usr/bin/env bash
set -euo pipefail
sqlcmd=(/opt/mssql-tools18/bin/sqlcmd -S sqlserver -U sa -C -b -r1 -l 30)
for database in IdentityDb RestaurantDb OrderingDb InventoryDb CatalogDb CustomerDb BillingDb; do
    "${sqlcmd[@]}" -d master -Q "IF DB_ID(N'$database') IS NULL CREATE DATABASE [$database] COLLATE SQL_Latin1_General_CP1_CI_AS;"
    initialized=$("${sqlcmd[@]}" -d "$database" -h -1 -W -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM sys.extended_properties WHERE class = 0 AND name = N'DockerBootstrapVersion';" | tr -d '[:space:]')
    if [[ "$initialized" == "1" ]]; then
        if [[ "$database" == "InventoryDb" ]]; then
        "${sqlcmd[@]}" -d "$database" -i /init/upgrades/InventoryUnits.sql
        "${sqlcmd[@]}" -d "$database" -i /init/upgrades/InventoryFifo.sql
    fi
    if [[ "$database" == "CatalogDb" ]]; then
        "${sqlcmd[@]}" -d "$database" -i /init/upgrades/CatalogCookingTime.sql
    fi
    if [[ "$database" == "OrderingDb" ]]; then
        "${sqlcmd[@]}" -d "$database" -i /init/upgrades/OrderingCookingTime.sql
    fi
    if [[ "$database" == "BillingDb" ]]; then
        "${sqlcmd[@]}" -d "$database" -i /init/upgrades/BillingCashRegisterRestaurant.sql
        "${sqlcmd[@]}" -d "$database" -i /init/upgrades/BillingBillRestaurant.sql
    fi
        echo "$database already initialized; keeping existing data."
        continue
    fi
    tables=$("${sqlcmd[@]}" -d "$database" -h -1 -W -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM sys.tables WHERE is_ms_shipped = 0;" | tr -d '[:space:]')
    if [[ "$tables" != "0" ]]; then
        echo "$database contains unmarked tables. Refusing to overwrite existing data." >&2
        exit 1
    fi
    script=$(mktemp)
    {
        printf 'SET XACT_ABORT ON;
BEGIN TRANSACTION;
GO
'
        printf ':r /init/schema/%s.sql
' "$database"
        if [[ -f "/init/seed/$database.sql" ]]; then
            printf ':r /init/seed/%s.sql
GO
' "$database"
        fi
        printf "EXEC sys.sp_addextendedproperty @name=N'DockerBootstrapVersion', @value=N'1';
COMMIT TRANSACTION;
GO
"
    } > "$script"
    "${sqlcmd[@]}" -d "$database" -i "$script"
    rm "$script"
    echo "$database initialized."
    if [[ "$database" == "InventoryDb" ]]; then
        "${sqlcmd[@]}" -d "$database" -i /init/upgrades/InventoryUnits.sql
        "${sqlcmd[@]}" -d "$database" -i /init/upgrades/InventoryFifo.sql
    fi
    if [[ "$database" == "CatalogDb" ]]; then
        "${sqlcmd[@]}" -d "$database" -i /init/upgrades/CatalogCookingTime.sql
    fi
    if [[ "$database" == "OrderingDb" ]]; then
        "${sqlcmd[@]}" -d "$database" -i /init/upgrades/OrderingCookingTime.sql
    fi
    if [[ "$database" == "BillingDb" ]]; then
        "${sqlcmd[@]}" -d "$database" -i /init/upgrades/BillingCashRegisterRestaurant.sql
        "${sqlcmd[@]}" -d "$database" -i /init/upgrades/BillingBillRestaurant.sql
    fi
done
