#!/usr/bin/env bash
set -euo pipefail
sqlcmd=(/opt/mssql-tools18/bin/sqlcmd -S sqlserver -U sa -C -b -r1 -l 30 -f 65001)
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
        echo "$database has an unsupported bootstrap version ($version). Review its schema and explicit data mappings before upgrading. Existing data was not deleted." >&2
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

echo "Applying IdentityDb upgrade: IdentityStaffTransfer.sql"
"${sqlcmd[@]}" -d IdentityDb -i /init/upgrades/IdentityStaffTransfer.sql

# Do not infer required restaurant mappings or payment configuration from old rows.
for upgrade in BillingCashRegisterRestaurant.sql BillingBillRestaurant.sql BillingPayOs.sql \
    BillingCashMethod.sql BillingCheckoutTables.sql BillingCheckoutSubmission.sql BillingCheckoutPreparation.sql \
    BillingCashHandoverRestaurantIsolation.sql BillingProviderConfiguration.sql BillingCreatedAt.sql NotificationOutbox.sql; do
    echo "Applying BillingDb upgrade: $upgrade"
    "${sqlcmd[@]}" -d BillingDb -i "/init/upgrades/$upgrade"
done
for upgrade in OrderingCheckoutLock.sql OrderingRefundAccount.sql OrderingCancellationRequest.sql \
    OrderingTableTransfer.sql OrderingCreatedAt.sql NotificationOutbox.sql; do
    echo "Applying OrderingDb upgrade: $upgrade"
    "${sqlcmd[@]}" -d OrderingDb -i "/init/upgrades/$upgrade"
done

echo "Applying CatalogDb upgrade: CatalogCreatedAt.sql"
"${sqlcmd[@]}" -d CatalogDb -i /init/upgrades/CatalogCreatedAt.sql

echo "Applying CustomerDb upgrade: CustomerCreatedAt.sql"
"${sqlcmd[@]}" -d CustomerDb -i /init/upgrades/CustomerCreatedAt.sql

echo "Applying InventoryDb upgrade: InventoryCreatedAt.sql"
"${sqlcmd[@]}" -d InventoryDb -i /init/upgrades/InventoryCreatedAt.sql

echo "Applying RestaurantDb upgrade: RestaurantRegistration.sql"
"${sqlcmd[@]}" -d RestaurantDb -i /init/upgrades/RestaurantRegistration.sql

echo "Applying RestaurantDb upgrade: RestaurantCloudinary.sql"
"${sqlcmd[@]}" -d RestaurantDb -i /init/upgrades/RestaurantCloudinary.sql

echo "Applying RestaurantDb upgrade: RestaurantReservationOptionalArea.sql"
"${sqlcmd[@]}" -d RestaurantDb -i /init/upgrades/RestaurantReservationOptionalArea.sql

for upgrade in RestaurantStaffTransferReceipt.sql RestaurantTableTransfer.sql RestaurantCreatedAt.sql NotificationOutbox.sql; do
    echo "Applying RestaurantDb upgrade: $upgrade"
    "${sqlcmd[@]}" -d RestaurantDb -i "/init/upgrades/$upgrade"
done

# UNICODE_UPGRADE_BEGIN
for database in IdentityDb RestaurantDb InventoryDb CatalogDb OrderingDb CustomerDb BillingDb; do
    echo "Applying Unicode text upgrade: $database"
    "${sqlcmd[@]}" -d "$database" -i /init/upgrades/UnicodeText.sql
done
# UNICODE_UPGRADE_END

# Compile and execute zero-row reads from the same schema files used for fresh installs.
# This catches missing columns/tables before services accept requests, without reading data.
for database in BillingDb OrderingDb CatalogDb RestaurantDb InventoryDb IdentityDb; do
    echo "Validating $database columns against the current schema."
    awk '
        BEGIN { print "SET NOCOUNT ON;" }
        /^CREATE TABLE / {
            if (in_table) { exit 1 }
            table = $3; sub(/\(.*/, "", table); columns = ""; in_table = 1; next
        }
        in_table && (/^[[:space:]]*\);/ || /^[[:space:]]*CONSTRAINT /) {
            print "SELECT TOP (0) " columns " FROM " table ";"
            in_table = 0; validated++; next
        }
        in_table && /^[[:space:]]*[A-Za-z\[]/ {
            line = $0; sub(/^[[:space:]]*/, "", line); split(line, parts, /[[:space:]]+/)
            columns = columns (columns == "" ? "" : ", ") parts[1]
        }
        END { if (in_table || validated == 0) { exit 1 } }
    ' "/init/schema/$database.sql" > "$script"
    if [[ ! -s "$script" ]]; then
        echo "No schema validation queries were generated for $database." >&2
        exit 1
    fi
    "${sqlcmd[@]}" -d "$database" -i "$script" -h -1
done
echo "Billing and AI dependency schemas are ready."

echo "Seeding initial development and operational data..."
/bin/bash /init/seed.sh
