-- Run in each application database after its normal feature upgrades.
-- Only the listed text columns change. No data repair, collation change or cross-database FK.
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;

DECLARE @Columns TABLE (DatabaseName sysname, TableName sysname, ColumnName sysname, Characters int, Nullable bit);
-- UNICODE_COLUMN_MANIFEST
INSERT @Columns VALUES
(N'IdentityDb',N'Role',N'RoleName',50,0),
(N'IdentityDb',N'Role',N'Description',-1,1),
(N'IdentityDb',N'User',N'UserName',100,0),
(N'IdentityDb',N'User',N'Email',255,0),
(N'IdentityDb',N'User',N'FullName',255,0),
(N'IdentityDb',N'User',N'AvatarUrl',-1,1),
(N'IdentityDb',N'UserSession',N'UserAgent',-1,1),
(N'IdentityDb',N'UserSession',N'DeviceType',50,1),
(N'IdentityDb',N'AuditLog',N'OldValue',-1,1),
(N'IdentityDb',N'AuditLog',N'NewValue',-1,1),
(N'RestaurantDb',N'Restaurant',N'RestaurantName',255,0),
(N'RestaurantDb',N'Restaurant',N'Address',500,0),
(N'RestaurantDb',N'Restaurant',N'LogoUrl',-1,1),
(N'RestaurantDb',N'Reservation',N'ContactName',255,0),
(N'RestaurantDb',N'Area',N'AreaCode',50,0),
(N'RestaurantDb',N'Area',N'AreaName',100,0),
(N'RestaurantDb',N'Table',N'TableCode',50,0),
(N'RestaurantDb',N'SessionTable',N'MoveReason',500,1),
(N'RestaurantDb',N'Qr',N'ImageUrl',2048,1),
(N'CatalogDb',N'Category',N'CategoryName',255,0),
(N'CatalogDb',N'Item',N'ItemName',255,0),
(N'CatalogDb',N'Item',N'Description',-1,1),
(N'CatalogDb',N'Item',N'ImageUrl',-1,1),
(N'CatalogDb',N'Option',N'OptionName',255,0),
(N'CatalogDb',N'OptionGroup',N'OptionGroupName',255,0),
(N'CatalogDb',N'RecipeMedia',N'Url',-1,0),
(N'CatalogDb',N'RecipeVersion',N'Instructions',-1,1),
(N'InventoryDb',N'GoodsReceipt',N'Note',-1,1),
(N'InventoryDb',N'Ingredient',N'IngredientName',255,0),
(N'InventoryDb',N'StockMovement',N'Reason',-1,1),
(N'InventoryDb',N'StocktakeItem',N'DiscrepancyReason',-1,1),
(N'OrderingDb',N'CartItem',N'Note',-1,1),
(N'OrderingDb',N'KitchenStation',N'KitchenStationName',255,0),
(N'OrderingDb',N'OrderItem',N'NameSnapshot',255,0),
(N'OrderingDb',N'OrderItem',N'Note',-1,1),
(N'OrderingDb',N'OrderItemOption',N'NameSnapshot',255,0),
(N'CustomerDb',N'Customer',N'CustomerName',255,0),
(N'CustomerDb',N'Feedback',N'Comment',-1,1),
(N'CustomerDb',N'FeedbackItem',N'Comment',-1,1),
(N'CustomerDb',N'LoyaltyTier',N'LoyaltyTierName',100,0),
(N'BillingDb',N'BillAdjustment',N'RuleSnapshot',-1,1),
(N'BillingDb',N'BillAdjustment',N'Reason',1000,1),
(N'BillingDb',N'BillLine',N'ItemSnapshot',-1,0),
(N'BillingDb',N'CashMovement',N'Reason',1000,1),
(N'BillingDb',N'ElectronicInvoice',N'CompanyName',255,0),
(N'BillingDb',N'ElectronicInvoice',N'Address',500,0),
(N'BillingDb',N'ElectronicInvoice',N'Email',320,0),
(N'BillingDb',N'Payment',N'ProviderCheckoutUrl',2048,1),
(N'BillingDb',N'Payment',N'ProviderQrCode',-1,1),
(N'BillingDb',N'Payment',N'EvidenceUrl',2048,1),
(N'BillingDb',N'PaymentProviderConfig',N'ReturnUrl',2048,0),
(N'BillingDb',N'PaymentProviderConfig',N'CancelUrl',2048,0),
(N'BillingDb',N'PaymentProviderConfig',N'WebhookUrl',2048,0),
(N'BillingDb',N'Promotion',N'PromotionName',255,0),
(N'BillingDb',N'Refund',N'Reason',1000,0),
(N'BillingDb',N'ShiftReport',N'DifferenceReason',1000,1);
-- END_UNICODE_COLUMN_MANIFEST

IF NOT EXISTS (SELECT 1 FROM @Columns WHERE DatabaseName = DB_NAME())
    THROW 51000, 'Unicode upgrade must run in a supported application database.', 1;

BEGIN TRY
    BEGIN TRANSACTION;
    DECLARE @lock int;
    EXEC @lock = sys.sp_getapplock @Resource=N'UnicodeText.v1', @LockMode='Exclusive',
        @LockOwner='Transaction', @LockTimeout=30000;
    IF @lock < 0 THROW 51000, 'Unicode schema upgrade is busy.', 1;

    -- Refuse unknown/missing column definitions; never infer mappings or shrink text.
    IF EXISTS (
        SELECT 1 FROM @Columns p
        LEFT JOIN sys.tables t ON t.name=p.TableName AND t.schema_id=SCHEMA_ID(N'dbo')
        LEFT JOIN sys.columns c ON c.object_id=t.object_id AND c.name=p.ColumnName
        WHERE p.DatabaseName=DB_NAME() AND (c.column_id IS NULL OR c.is_computed=1
            OR c.user_type_id<>c.system_type_id OR c.system_type_id NOT IN (167,231)
            OR c.is_nullable<>p.Nullable OR c.max_length<>CASE WHEN p.Characters=-1 THEN -1
                WHEN c.system_type_id=231 THEN 2*p.Characters ELSE p.Characters END))
        THROW 51000, 'Unicode upgrade found missing or incompatible text columns. Review schema before upgrading.', 1;

    DECLARE @Todo TABLE (ObjectId int, ColumnId int, TableName sysname, ColumnName sysname,
        Characters int, Nullable bit, CollationName sysname);
    INSERT @Todo
    SELECT c.object_id,c.column_id,p.TableName,p.ColumnName,p.Characters,p.Nullable,c.collation_name
    FROM @Columns p JOIN sys.tables t ON t.name=p.TableName AND t.schema_id=SCHEMA_ID(N'dbo')
    JOIN sys.columns c ON c.object_id=t.object_id AND c.name=p.ColumnName
    WHERE p.DatabaseName=DB_NAME() AND c.system_type_id=167;
    IF NOT EXISTS (SELECT 1 FROM @Todo)
    BEGIN
        COMMIT;
        PRINT DB_NAME()+N': Unicode text columns already ready.';
        RETURN;
    END;

    IF EXISTS (SELECT 1 FROM sys.foreign_key_columns f JOIN @Todo p
        ON (f.parent_object_id=p.ObjectId AND f.parent_column_id=p.ColumnId)
        OR (f.referenced_object_id=p.ObjectId AND f.referenced_column_id=p.ColumnId))
        THROW 51000, 'A text-column foreign key needs an explicit upgrade plan.', 1;
    IF EXISTS (SELECT 1 FROM sys.sql_expression_dependencies d JOIN @Todo p
        ON d.referenced_id=p.ObjectId AND d.referenced_minor_id=p.ColumnId WHERE d.is_schema_bound_reference=1
        AND NOT EXISTS (SELECT 1 FROM sys.check_constraints c WHERE c.object_id=d.referencing_id)
        AND NOT EXISTS (SELECT 1 FROM sys.default_constraints c WHERE c.object_id=d.referencing_id))
        THROW 51000, 'A schema-bound text dependency needs an explicit upgrade plan.', 1;
    IF EXISTS (SELECT 1 FROM sys.stats s JOIN sys.stats_columns sc ON sc.object_id=s.object_id AND sc.stats_id=s.stats_id
        JOIN @Todo p ON p.ObjectId=sc.object_id AND p.ColumnId=sc.column_id
        WHERE s.user_created=1 AND NOT EXISTS (SELECT 1 FROM sys.indexes i WHERE i.object_id=s.object_id AND i.index_id=s.stats_id))
        THROW 51000, 'User-created text statistics need an explicit upgrade plan.', 1;

    DECLARE @Indexes TABLE (ObjectId int, IndexId int);
    INSERT @Indexes SELECT DISTINCT i.object_id,i.index_id FROM sys.indexes i
    JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id
    JOIN @Todo p ON p.ObjectId=ic.object_id AND p.ColumnId=ic.column_id;
    IF EXISTS (SELECT 1 FROM @Indexes a JOIN sys.indexes i ON i.object_id=a.ObjectId AND i.index_id=a.IndexId
        JOIN sys.data_spaces ds ON ds.data_space_id=i.data_space_id
        WHERE i.type<>2 OR i.is_primary_key=1 OR i.is_disabled=1 OR i.is_hypothetical=1 OR ds.type<>N'FG'
            OR (SELECT COUNT(*) FROM sys.partitions p WHERE p.object_id=i.object_id AND p.index_id=i.index_id)<>1)
        THROW 51000, 'An unsupported text index needs an explicit upgrade plan.', 1;

    -- Recreate ordinary rowstore indexes/UQ constraints with their existing names and options.
    DECLARE @Objects TABLE (Id int IDENTITY PRIMARY KEY, DropSql nvarchar(max), CreateSql nvarchar(max));
    INSERT @Objects
    SELECT CASE WHEN kc.object_id IS NULL THEN N'DROP INDEX '+QUOTENAME(i.name)+N' ON dbo.'+QUOTENAME(t.name)
            ELSE N'ALTER TABLE dbo.'+QUOTENAME(t.name)+N' DROP CONSTRAINT '+QUOTENAME(kc.name) END,
        CASE WHEN kc.object_id IS NULL THEN N'CREATE '+CASE WHEN i.is_unique=1 THEN N'UNIQUE ' ELSE N'' END
            +N'NONCLUSTERED INDEX '+QUOTENAME(i.name)+N' ON dbo.'+QUOTENAME(t.name)
            ELSE N'ALTER TABLE dbo.'+QUOTENAME(t.name)+N' ADD CONSTRAINT '+QUOTENAME(kc.name)+N' UNIQUE NONCLUSTERED' END
        +N' ('+keys.Columns+N')'+CASE WHEN included.Columns IS NULL THEN N'' ELSE N' INCLUDE ('+included.Columns+N')' END
        +CASE WHEN i.has_filter=1 THEN N' WHERE '+i.filter_definition ELSE N'' END
        +N' WITH (PAD_INDEX='+CASE WHEN i.is_padded=1 THEN N'ON' ELSE N'OFF' END
        +N', FILLFACTOR='+CONVERT(nvarchar(3),i.fill_factor)
        +N', IGNORE_DUP_KEY='+CASE WHEN i.ignore_dup_key=1 THEN N'ON' ELSE N'OFF' END
        +N', STATISTICS_NORECOMPUTE='+CASE WHEN s.no_recompute=1 THEN N'ON' ELSE N'OFF' END
        +N', ALLOW_ROW_LOCKS='+CASE WHEN i.allow_row_locks=1 THEN N'ON' ELSE N'OFF' END
        +N', ALLOW_PAGE_LOCKS='+CASE WHEN i.allow_page_locks=1 THEN N'ON' ELSE N'OFF' END
        +N', OPTIMIZE_FOR_SEQUENTIAL_KEY='+CASE WHEN i.optimize_for_sequential_key=1 THEN N'ON' ELSE N'OFF' END
        +N', DATA_COMPRESSION='+p.data_compression_desc+N') ON '+QUOTENAME(ds.name)
    FROM @Indexes a JOIN sys.indexes i ON i.object_id=a.ObjectId AND i.index_id=a.IndexId
    JOIN sys.tables t ON t.object_id=i.object_id JOIN sys.data_spaces ds ON ds.data_space_id=i.data_space_id
    JOIN sys.stats s ON s.object_id=i.object_id AND s.stats_id=i.index_id
    JOIN sys.partitions p ON p.object_id=i.object_id AND p.index_id=i.index_id
    LEFT JOIN sys.key_constraints kc ON kc.parent_object_id=i.object_id AND kc.unique_index_id=i.index_id
    CROSS APPLY (SELECT STRING_AGG(CONVERT(nvarchar(max),QUOTENAME(c.name)+CASE WHEN ic.is_descending_key=1 THEN N' DESC' ELSE N' ASC' END),N', ')
        WITHIN GROUP (ORDER BY ic.key_ordinal) AS Columns FROM sys.index_columns ic
        JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
        WHERE ic.object_id=i.object_id AND ic.index_id=i.index_id AND ic.key_ordinal>0) keys
    CROSS APPLY (SELECT STRING_AGG(CONVERT(nvarchar(max),QUOTENAME(c.name)),N', ')
        WITHIN GROUP (ORDER BY ic.index_column_id) AS Columns FROM sys.index_columns ic
        JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
        WHERE ic.object_id=i.object_id AND ic.index_id=i.index_id AND ic.is_included_column=1) included;

    INSERT @Objects SELECT N'ALTER TABLE dbo.'+QUOTENAME(OBJECT_NAME(d.parent_object_id))+N' DROP CONSTRAINT '+QUOTENAME(d.name),
        N'ALTER TABLE dbo.'+QUOTENAME(OBJECT_NAME(d.parent_object_id))+N' ADD CONSTRAINT '+QUOTENAME(d.name)
        +N' DEFAULT '+d.definition+N' FOR '+QUOTENAME(COL_NAME(d.parent_object_id,d.parent_column_id))
    FROM sys.default_constraints d JOIN @Todo p ON p.ObjectId=d.parent_object_id AND p.ColumnId=d.parent_column_id;

    INSERT @Objects SELECT N'ALTER TABLE dbo.'+QUOTENAME(OBJECT_NAME(c.parent_object_id))+N' DROP CONSTRAINT '+QUOTENAME(c.name),
        N'ALTER TABLE dbo.'+QUOTENAME(OBJECT_NAME(c.parent_object_id))+CASE WHEN c.is_not_trusted=1 THEN N' WITH NOCHECK' ELSE N' WITH CHECK' END
        +N' ADD CONSTRAINT '+QUOTENAME(c.name)+N' CHECK '+CASE WHEN c.is_not_for_replication=1 THEN N'NOT FOR REPLICATION ' ELSE N'' END+c.definition
        +CASE WHEN c.is_disabled=1 THEN N'; ALTER TABLE dbo.'+QUOTENAME(OBJECT_NAME(c.parent_object_id))+N' NOCHECK CONSTRAINT '+QUOTENAME(c.name) ELSE N'' END
    FROM sys.check_constraints c WHERE EXISTS (SELECT 1 FROM @Todo p WHERE p.ObjectId=c.parent_object_id
        AND (p.ColumnId=c.parent_column_id OR EXISTS (SELECT 1 FROM sys.sql_expression_dependencies d
            WHERE d.referencing_id=c.object_id AND d.referenced_id=p.ObjectId AND d.referenced_minor_id=p.ColumnId)));

    DECLARE @sql nvarchar(max);
    DECLARE drop_objects CURSOR LOCAL FAST_FORWARD FOR SELECT DropSql FROM @Objects ORDER BY Id;
    OPEN drop_objects; FETCH NEXT FROM drop_objects INTO @sql;
    WHILE @@FETCH_STATUS=0 BEGIN EXEC sys.sp_executesql @sql; FETCH NEXT FROM drop_objects INTO @sql; END;
    CLOSE drop_objects; DEALLOCATE drop_objects;

    -- Only optimizer-created column statistics are disposable; manual statistics were refused above.
    DECLARE auto_stats CURSOR LOCAL FAST_FORWARD FOR
        SELECT DISTINCT N'DROP STATISTICS dbo.'+QUOTENAME(OBJECT_NAME(s.object_id))+N'.'+QUOTENAME(s.name)
        FROM sys.stats s JOIN sys.stats_columns sc ON sc.object_id=s.object_id AND sc.stats_id=s.stats_id
        JOIN @Todo p ON p.ObjectId=sc.object_id AND p.ColumnId=sc.column_id WHERE s.auto_created=1;
    OPEN auto_stats; FETCH NEXT FROM auto_stats INTO @sql;
    WHILE @@FETCH_STATUS=0 BEGIN EXEC sys.sp_executesql @sql; FETCH NEXT FROM auto_stats INTO @sql; END;
    CLOSE auto_stats; DEALLOCATE auto_stats;

    DECLARE alter_columns CURSOR LOCAL FAST_FORWARD FOR
        SELECT N'ALTER TABLE dbo.'+QUOTENAME(TableName)+N' ALTER COLUMN '+QUOTENAME(ColumnName)
            +N' nvarchar('+CASE WHEN Characters=-1 THEN N'max' ELSE CONVERT(nvarchar(10),Characters) END+N') COLLATE '
            +CollationName+CASE WHEN Nullable=1 THEN N' NULL' ELSE N' NOT NULL' END FROM @Todo;
    OPEN alter_columns; FETCH NEXT FROM alter_columns INTO @sql;
    WHILE @@FETCH_STATUS=0 BEGIN EXEC sys.sp_executesql @sql; FETCH NEXT FROM alter_columns INTO @sql; END;
    CLOSE alter_columns; DEALLOCATE alter_columns;

    DECLARE create_objects CURSOR LOCAL FAST_FORWARD FOR SELECT CreateSql FROM @Objects ORDER BY Id;
    OPEN create_objects; FETCH NEXT FROM create_objects INTO @sql;
    WHILE @@FETCH_STATUS=0 BEGIN EXEC sys.sp_executesql @sql; FETCH NEXT FROM create_objects INTO @sql; END;
    CLOSE create_objects; DEALLOCATE create_objects;
    COMMIT;
    PRINT DB_NAME()+N': Unicode text upgrade complete; existing values were preserved.';
END TRY
BEGIN CATCH
    IF XACT_STATE()<>0 ROLLBACK;
    THROW;
END CATCH;
