#!/usr/bin/env bash
set -euo pipefail
sqlcmd=(/opt/mssql-tools18/bin/sqlcmd -S sqlserver -U sa -C -b -r1 -l 30 -f 65001)
script=$(mktemp)
trap 'rm -f "$script"' EXIT

for database in IdentityDb RestaurantDb InventoryDb CatalogDb OrderingDb BillingDb; do
    version=$("${sqlcmd[@]}" -d "$database" -h -1 -W -Q "SET NOCOUNT ON; SELECT COALESCE((SELECT CONVERT(nvarchar(20), value) FROM sys.extended_properties WHERE class = 0 AND name = N'DockerBootstrapVersion'), N'0');" | tr -d '[:space:]')
    if [[ "$version" != "2" ]]; then
        echo "$database schema is not ready. Run db-init first." >&2
        exit 1
    fi
    seeded=$("${sqlcmd[@]}" -d "$database" -h -1 -W -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM sys.extended_properties WHERE class = 0 AND name = N'DockerSeedVersion';" | tr -d '[:space:]')
    if [[ "$seeded" == "1" ]]; then
        echo "$database already seeded; keeping existing data."
        continue
    fi
    {
        printf 'SET XACT_ABORT ON;\nSET QUOTED_IDENTIFIER ON;\nBEGIN TRANSACTION;\nGO\n'
        printf ':r /init/seed/%s.sql\nGO\n' "$database"
        printf "EXEC sys.sp_addextendedproperty @name=N'DockerSeedVersion', @value=N'1';\nCOMMIT TRANSACTION;\nGO\n"
    } > "$script"
    "${sqlcmd[@]}" -d "$database" -i "$script"
    echo "$database seeded."
done
