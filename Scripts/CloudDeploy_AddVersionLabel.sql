-- ============================================================
-- CLOUD DEPLOYMENT SCRIPT
-- Target: db40948.databaseasp.net / db40948
-- Migration: Add VersionLabel (NVARCHAR(20)) to version table
-- Purpose: Minor versioning v1 -> v1.1 (staff) -> v2 (lawyer)
-- Safe to re-run.
-- ============================================================

SET NOCOUNT ON;

-- ===== STEP 0: PRINT ALL TABLES IN THIS DATABASE =====
-- (So we can see what actually exists)
PRINT '========== ALL TABLES IN DATABASE ==========';

DECLARE @tname NVARCHAR(300);
DECLARE cur CURSOR LOCAL FAST_FORWARD FOR
    SELECT TABLE_SCHEMA + '.' + TABLE_NAME
    FROM INFORMATION_SCHEMA.TABLES
    WHERE TABLE_TYPE = 'BASE TABLE'
    ORDER BY TABLE_NAME;
OPEN cur;
FETCH NEXT FROM cur INTO @tname;
WHILE @@FETCH_STATUS = 0
BEGIN
    PRINT @tname;
    FETCH NEXT FROM cur INTO @tname;
END;
CLOSE cur;
DEALLOCATE cur;

PRINT '========== END TABLE LIST ==========';
PRINT '';

-- ===== STEP 1: FIND THE VERSION TABLE =====
DECLARE @tbl NVARCHAR(128) = NULL;
DECLARE @sch NVARCHAR(128) = NULL;

-- Try exact names (any schema)
IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'DocumentVersions')
    SELECT TOP 1 @sch = TABLE_SCHEMA, @tbl = TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'DocumentVersions';
ELSE IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'DocumentVersion')
    SELECT TOP 1 @sch = TABLE_SCHEMA, @tbl = TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'DocumentVersion';
-- Fuzzy: any table name containing 'version' (case-insensitive)
ELSE IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME LIKE '%ersion%')
    SELECT TOP 1 @sch = TABLE_SCHEMA, @tbl = TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME LIKE '%ersion%';

IF @tbl IS NULL
BEGIN
    PRINT '!! ERROR: No version table found anywhere.';
    PRINT 'Please check the table list printed above for a table that holds document versions.';
    RETURN;
END;

PRINT 'Found version table: [' + @sch + '].[' + @tbl + ']';

-- ===== STEP 2: VALIDATE TABLE HAS VersionNumber =====
IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_SCHEMA = @sch AND TABLE_NAME = @tbl AND COLUMN_NAME = 'VersionNumber'
)
BEGIN
    PRINT '!! WARNING: Table does not have VersionNumber column.';
    PRINT 'Columns found:';
    DECLARE @cname NVARCHAR(300);
    DECLARE ccur CURSOR LOCAL FAST_FORWARD FOR
        SELECT COLUMN_NAME + ' (' + DATA_TYPE + CASE WHEN CHARACTER_MAXIMUM_LENGTH IS NOT NULL THEN '(' + CAST(CHARACTER_MAXIMUM_LENGTH AS NVARCHAR) + ')' ELSE '' END + ')'
        FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = @sch AND TABLE_NAME = @tbl ORDER BY ORDINAL_POSITION;
    OPEN ccur;
    FETCH NEXT FROM ccur INTO @cname;
    WHILE @@FETCH_STATUS = 0 BEGIN PRINT '  ' + @cname; FETCH NEXT FROM ccur INTO @cname; END;
    CLOSE ccur; DEALLOCATE ccur;
    RETURN;
END;

-- ===== STEP 3: ADD COLUMN IF MISSING =====
IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_SCHEMA = @sch AND TABLE_NAME = @tbl AND COLUMN_NAME = 'VersionLabel'
)
BEGIN
    DECLARE @sql NVARCHAR(MAX);

    SET @sql = 'ALTER TABLE [' + @sch + '].[' + @tbl + '] ADD VersionLabel NVARCHAR(20) NULL;';
    EXEC sp_executesql @sql;
    PRINT 'VersionLabel column ADDED to [' + @sch + '].[' + @tbl + '].';

    SET @sql = 'UPDATE [' + @sch + '].[' + @tbl + '] SET VersionLabel = CAST(VersionNumber AS NVARCHAR(20)) WHERE VersionLabel IS NULL;';
    EXEC sp_executesql @sql;

    DECLARE @cnt INT;
    SET @sql = 'SELECT @c = COUNT(*) FROM [' + @sch + '].[' + @tbl + '] WHERE VersionLabel IS NOT NULL;';
    EXEC sp_executesql @sql, N'@c INT OUTPUT', @c = @cnt OUTPUT;
    PRINT 'Back-filled ' + CAST(@cnt AS NVARCHAR(10)) + ' rows.';
END
ELSE
BEGIN
    PRINT 'VersionLabel column already exists. No changes needed.';
END;

-- ===== STEP 4: VERIFY =====
PRINT '';
PRINT '=== Verification (top 10 rows) ===';
DECLARE @vfy NVARCHAR(MAX) = 'SELECT TOP 10 VersionId, VersionNumber, VersionLabel, IsCurrentVersion, CreatedAt FROM [' + @sch + '].[' + @tbl + '] ORDER BY VersionId DESC;';
EXEC sp_executesql @vfy;

PRINT 'Done.';
GO
