param([switch]$KeepEnvironment, [switch]$VerifyEf)

$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$id = [Guid]::NewGuid().ToString('N').Substring(0, 12)
$network = "unicode-$id"
$server = "unicode-sql-$id"
$tag = "backend-db-init:unicode-$id"
$legacyTag = "backend-db-init:unicode-legacy-$id"
$work = Join-Path ([IO.Path]::GetTempPath()) "backend-unicode-test-$id"
$originalPassword = $env:MSSQL_SA_PASSWORD
$originalSqlPassword = $env:SQLCMDPASSWORD
$env:MSSQL_SA_PASSWORD = 'Aa1!' + [Guid]::NewGuid().ToString('N')
$env:SQLCMDPASSWORD = $env:MSSQL_SA_PASSWORD
$databases = @('IdentityDb','RestaurantDb','InventoryDb','CatalogDb','OrderingDb','CustomerDb','BillingDb')
$testSucceeded = $false

function Check([string]$step) {
    if ($LASTEXITCODE -ne 0) {
        throw "$step failed (exit $LASTEXITCODE)."
    }
}

function Query([string]$database, [string]$sql) {
    $result = $sql | docker exec -i $server /bin/bash -c 'SQLCMDPASSWORD="$MSSQL_SA_PASSWORD" /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -I -f 65001 -h -1 -W -d "$1"' bash $database
    Check 'SQL assertion'
    return ($result | Where-Object { $_.Trim() } | ForEach-Object { $_.Trim() }) -join "`n"
}

function Start-Sql {
    docker run -d --name $server --network $network --network-alias sqlserver -p '127.0.0.1::1433' `
        --memory 2g --env ACCEPT_EULA=Y --env MSSQL_MEMORY_LIMIT_MB=1024 --env MSSQL_SA_PASSWORD `
        mcr.microsoft.com/mssql/server:2022-latest | Out-Null
    Check 'Start isolated SQL'
    for ($attempt = 0; $attempt -lt 60; $attempt++) {
        docker exec $server /bin/bash -c 'SQLCMDPASSWORD="$MSSQL_SA_PASSWORD" /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -Q "SELECT 1" -o /dev/null' 2>$null
        if ($LASTEXITCODE -eq 0) {
            return
        }
        Start-Sleep -Seconds 2
    }
    throw 'Isolated SQL did not become ready.'
}

function Initialize([string]$image) {
    $output = docker run --rm --network $network --env SQLCMDPASSWORD $image 2>&1
    if ($LASTEXITCODE -ne 0) {
        $output | Write-Output
        throw 'Database initializer failed.'
    }
}

function Seed([string]$image) {
    $output = docker run --rm --network $network --env SQLCMDPASSWORD --entrypoint /bin/bash $image /init/seed.sh 2>&1
    if ($LASTEXITCODE -ne 0) {
        $output | Write-Output
        throw 'Development seed failed.'
    }
}

$indexSql = @'
SET NOCOUNT ON;
SELECT CONCAT(t.name,N'|',i.name,N'|',i.type,N'|',i.is_unique,N'|',i.is_unique_constraint,N'|',
    i.fill_factor,N'|',i.is_padded,N'|',i.ignore_dup_key,N'|',i.allow_row_locks,N'|',i.allow_page_locks,N'|',
    COALESCE(i.filter_definition,N''),N'|',ic.index_column_id,N'|',c.name,N'|',ic.key_ordinal,N'|',ic.is_descending_key,N'|',ic.is_included_column)
FROM sys.indexes i JOIN sys.tables t ON t.object_id=i.object_id
JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id
JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
ORDER BY t.name,i.name,ic.index_column_id;
'@
$rowSql = 'SET NOCOUNT ON; SELECT CONCAT(t.name,N''|'',SUM(p.rows)) FROM sys.tables t JOIN sys.partitions p ON p.object_id=t.object_id AND p.index_id IN(0,1) GROUP BY t.name ORDER BY t.name;'

try {
    New-Item -ItemType Directory -Path $work | Out-Null
    $sqlRoot = Join-Path $repo 'docker/sql'
    $manifestSql = Get-Content (Join-Path $sqlRoot 'upgrades/UnicodeText.sql') -Raw
    $manifest = [regex]::Matches($manifestSql, "\(N'(?<db>\w+)',N'(?<table>\w+)',N'(?<column>\w+)',(?<size>-?\d+),(?<nullable>[01])\)")
    if ($manifest.Count -ne 56) {
        throw 'Update this test deliberately when the reviewed Unicode column manifest changes.'
    }
    docker build -t $tag $sqlRoot
    Check 'Build current initializer'
    docker network create $network | Out-Null
    Check 'Create isolated network'

    Start-Sql
    Initialize $tag
    Seed $tag
    Query 'InventoryDb' "IF NOT EXISTS(SELECT 1 FROM dbo.Ingredient WHERE IngredientName=N'Gạo') THROW 51000,'Fresh UTF-8/NVARCHAR seed lost Vietnamese.',1; PRINT 'PASS fresh Vietnamese seed';" | Write-Output
    Initialize $tag
    Write-Output 'PASS fresh bootstrap and repeat'

    # The legacy database is a disposable fixture on a new isolated container, never the team's volume.
    docker rm -f $server | Out-Null
    $legacy = Join-Path $work 'legacy'
    Copy-Item -LiteralPath $sqlRoot -Destination $legacy -Recurse
    foreach ($database in $databases) {
        $path = Join-Path $legacy "schema/$database.sql"
        $text = Get-Content -LiteralPath $path -Raw
        foreach ($entry in $manifest | Where-Object { $_.Groups['db'].Value -eq $database }) {
            $table = [regex]::Escape($entry.Groups['table'].Value)
            $column = [regex]::Escape($entry.Groups['column'].Value)
            $pattern = "(?ms)(^CREATE TABLE\s+(?:\[?dbo\]?\.)?\[?$table\]?\s*\(.*?^\s*\[?$column\]?\s+\[?)nvarchar(?=\]?\s*\()"
            $text = [regex]::Replace($text, $pattern, '${1}varchar', 1)
        }
        [IO.File]::WriteAllText($path, $text, [Text.UTF8Encoding]::new($false))
    }
    $legacyInitPath = Join-Path $legacy 'init.sh'
    $legacyInit = [regex]::Replace((Get-Content $legacyInitPath -Raw), '(?ms)^# UNICODE_UPGRADE_BEGIN.*?^# UNICODE_UPGRADE_END\r?\n', '')
    [IO.File]::WriteAllText($legacyInitPath, $legacyInit, [Text.UTF8Encoding]::new($false))
    docker build -t $legacyTag $legacy
    Check 'Build legacy fixture initializer'
    Start-Sql
    Initialize $legacyTag
    Seed $legacyTag
    Query 'InventoryDb' "IF EXISTS(SELECT 1 FROM dbo.Ingredient WHERE IngredientName=N'Gạo') THROW 51000,'Legacy fixture did not reproduce ANSI loss.',1; PRINT 'PASS reproduced VARCHAR loss';" | Write-Output
    # Exercise dependent objects as well as the repository baseline (which has no text indexes on these columns).
    Query 'InventoryDb' "CREATE UNIQUE INDEX IX_UnicodeTest_Name ON dbo.Ingredient(IngredientName DESC) INCLUDE(BaseUnit) WHERE IsActive=1 WITH(FILLFACTOR=90); ALTER TABLE dbo.Ingredient ADD CONSTRAINT DF_UnicodeTest_Name DEFAULT ('Legacy') FOR IngredientName; ALTER TABLE dbo.Ingredient ADD CONSTRAINT CK_UnicodeTest_Name CHECK (LEN(IngredientName)>0);" | Out-Null
    $constraintsSql = "SET NOCOUNT ON; SELECT CONCAT(OBJECT_NAME(parent_object_id),N'|',name,N'|',definition) FROM sys.default_constraints UNION ALL SELECT CONCAT(OBJECT_NAME(parent_object_id),N'|',name,N'|',definition,N'|',is_not_trusted,N'|',is_disabled) FROM sys.check_constraints ORDER BY 1;"
    $constraints = @{}
    $indexes = @{}
    $rows = @{}
    foreach ($database in $databases) {
        $constraints[$database] = Query $database $constraintsSql
        $indexes[$database] = Query $database $indexSql
        $rows[$database] = Query $database $rowSql
    }
    $oldValue = Query 'InventoryDb' "SET NOCOUNT ON; SELECT CONVERT(varchar(max),CONVERT(varbinary(max),CONVERT(nvarchar(max),IngredientName)),2) FROM dbo.Ingredient ORDER BY IngredientId;"
    Initialize $tag
    foreach ($database in $databases) {
        if ($indexes[$database] -ne (Query $database $indexSql) -or $rows[$database] -ne (Query $database $rowSql) -or $constraints[$database] -ne (Query $database $constraintsSql)) {
            throw "Index definitions or row counts changed in $database."
        }
        foreach ($entry in $manifest | Where-Object { $_.Groups['db'].Value -eq $database }) {
            $table = $entry.Groups['table'].Value
            $column = $entry.Groups['column'].Value
            $size = [int]$entry.Groups['size'].Value
            $bytes = if ($size -eq -1) { -1 } else { 2*$size }
            Query $database "IF NOT EXISTS(SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.[$table]') AND name=N'$column' AND system_type_id=231 AND max_length=$bytes) THROW 51000,'Unicode metadata mismatch.',1;" | Out-Null
        }
    }
    if ($oldValue -ne (Query 'InventoryDb' "SET NOCOUNT ON; SELECT CONVERT(varchar(max),CONVERT(varbinary(max),CONVERT(nvarchar(max),IngredientName)),2) FROM dbo.Ingredient ORDER BY IngredientId;")) {
        throw 'Upgrade rewrote previously damaged values.'
    }
    Initialize $tag
    Seed $tag
    Write-Output 'PASS legacy upgrade, unchanged values/rows/indexes, repeat initializer and seed'

    Query 'InventoryDb' "DROP INDEX IX_UnicodeTest_Name ON dbo.Ingredient; ALTER TABLE dbo.Ingredient DROP CONSTRAINT DF_UnicodeTest_Name, CK_UnicodeTest_Name;" | Out-Null

    # Fail safely on a custom dependency; the script must not silently drop user statistics.
    Query 'InventoryDb' "ALTER TABLE dbo.Ingredient ALTER COLUMN IngredientName varchar(255) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL; CREATE STATISTICS UserUnicodeTest ON dbo.Ingredient(IngredientName);" | Out-Null
    $failure = docker run --rm --network $network --env SQLCMDPASSWORD $tag 2>&1
    if ($LASTEXITCODE -eq 0 -or ($failure -join "`n") -notmatch 'User-created text statistics') {
        throw 'Custom dependency was not rejected safely.'
    }
    Query 'InventoryDb' "IF NOT EXISTS(SELECT 1 FROM sys.stats WHERE name=N'UserUnicodeTest') THROW 51000,'User statistic was removed.',1; DROP STATISTICS dbo.Ingredient.UserUnicodeTest;" | Out-Null
    Initialize $tag
    Write-Output 'PASS safe refusal and recovery after explicit custom-statistic removal'

    if ($VerifyEf) {
        $port = ((docker port $server 1433/tcp) -split ':')[-1].Trim()
        $previousConnection = $env:UNICODE_TEST_CONNECTION
        try {
            $env:UNICODE_TEST_CONNECTION = "Server=127.0.0.1,$port;Database=master;User Id=sa;Password=$env:MSSQL_SA_PASSWORD;Encrypt=True;TrustServerCertificate=True"
            dotnet test (Join-Path $repo 'Tests/Foundation.Tests/Foundation.Tests.csproj') --configuration Release --filter 'FullyQualifiedName~UnicodePersistenceTests'
            Check 'EF Unicode metadata and real SQL parameter tests'
        }
        finally {
            $env:UNICODE_TEST_CONNECTION = $previousConnection
        }
    }

    if ($KeepEnvironment) {
        $port = ((docker port $server 1433/tcp) -split ':')[-1].Trim()
        @{ Server=$server; Network=$network; InitializerImage=$tag; LegacyImage=$legacyTag; Port=$port;
            ConnectionString="Server=127.0.0.1,$port;Database=master;User Id=sa;Password=$env:MSSQL_SA_PASSWORD;Encrypt=True;TrustServerCertificate=True" } |
            ConvertTo-Json | Set-Content -LiteralPath (Join-Path $work 'environment.json') -Encoding utf8
        Write-Output "Kept isolated test environment metadata at $work (contains local test credentials)."
    }
    $testSucceeded = $true
}
finally {
    $env:MSSQL_SA_PASSWORD = $originalPassword
    $env:SQLCMDPASSWORD = $originalSqlPassword
    if (-not $KeepEnvironment -or -not $testSucceeded) {
        docker rm -f $server 2>$null | Out-Null
        docker network rm $network 2>$null | Out-Null
        docker image rm $tag $legacyTag 2>$null | Out-Null
        $resolved = [IO.Path]::GetFullPath($work)
        $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
        if ($resolved.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -and (Split-Path $resolved -Leaf) -eq "backend-unicode-test-$id") {
            Remove-Item -LiteralPath $resolved -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}
