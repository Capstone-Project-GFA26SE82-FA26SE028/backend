#!/usr/bin/env bash
set -euo pipefail
sqlcmd=(/opt/mssql-tools18/bin/sqlcmd -S sqlserver -U sa -C -b -r1 -l 30)
script=$(mktemp)
trap 'rm -f "$script"' EXIT

for database in IdentityDb RestaurantDb InventoryDb CatalogDb OrderingDb CustomerDb BillingDb; do
    "${sqlcmd[@]}" -d master -Q "IF DB_ID(N'$database') IS NULL CREATE DATABASE [$database] COLLATE SQL_Latin1_General_CP1_CI_AS;"
    version=$("${sqlcmd[@]}" -d "$database" -h -1 -W -Q "SET NOCOUNT ON; SELECT COALESCE((SELECT CONVERT(nvarchar(20), value) FROM sys.extended_properties WHERE class = 0 AND name = N'DockerBootstrapVersion'), N'0');" | tr -d '[:space:]')
    if [[ "$version" == "2" ]]; then
        echo "$database already initialized; keeping existing data."
        continue
    fi
    if [[ "$version" != "0" ]]; then
        echo "$database uses an older schema. This initializer requires a new empty database and does not upgrade or delete existing data." >&2
        exit 1
    fi
    tables=$("${sqlcmd[@]}" -d "$database" -h -1 -W -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM sys.tables WHERE is_ms_shipped = 0;" | tr -d '[:space:]')
    if [[ "$tables" != "0" ]]; then
        echo "$database contains unmarked tables. Refusing to overwrite existing data." >&2
        exit 1
    fi

    {
        printf 'SET XACT_ABORT ON;\nSET QUOTED_IDENTIFIER ON;\nBEGIN TRANSACTION;\nGO\n'
        printf ':r /init/schema/%s.sql\nGO\n' "$database"
        printf "EXEC sys.sp_addextendedproperty @name=N'DockerBootstrapVersion', @value=N'2';\nCOMMIT TRANSACTION;\nGO\n"
    } > "$script"
    "${sqlcmd[@]}" -d "$database" -i "$script"
    echo "$database initialized."
done
