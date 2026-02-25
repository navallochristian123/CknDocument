-- ============================================================
-- Migration: Add VersionLabel column to the document versions table
-- Supports minor versioning: v1 -> v1.1 (staff meta) -> v2 (lawyer)
-- Auto-discovers the correct table name. Safe to re-run.
-- ============================================================

SET NOCOUNT ON;

-- ===== STEP 0: PRINT ALL TABLES (diagnostics) =====
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

IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'DocumentVersions')
    SELECT TOP 1 @sch = TABLE_SCHEMA, @tbl = TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'DocumentVersions';
ELSE IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'DocumentVersion')
    SELECT TOP 1 @sch = TABLE_SCHEMA, @tbl = TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'DocumentVersion';
ELSE IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME LIKE '%ersion%')
    SELECT TOP 1 @sch = TABLE_SCHEMA, @tbl = TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME LIKE '%ersion%';

IF @tbl IS NULL
BEGIN
    PRINT '!! ERROR: No version table found. Check table list above.';
    RETURN;
END;

PRINT 'Found version table: [' + @sch + '].[' + @tbl + ']';

-- ===== STEP 2: ADD COLUMN IF MISSING =====
IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_SCHEMA = @sch AND TABLE_NAME = @tbl AND COLUMN_NAME = 'VersionLabel'
)
BEGIN
    DECLARE @sql NVARCHAR(MAX);
    SET @sql = 'ALTER TABLE [' + @sch + '].[' + @tbl + '] ADD VersionLabel NVARCHAR(20) NULL;';
    EXEC sp_executesql @sql;
    PRINT 'VersionLabel column ADDED.';

    SET @sql = 'UPDATE [' + @sch + '].[' + @tbl + '] SET VersionLabel = CAST(VersionNumber AS NVARCHAR(20)) WHERE VersionLabel IS NULL;';
    EXEC sp_executesql @sql;
    PRINT 'Back-filled existing rows.';
END
ELSE
    PRINT 'VersionLabel column already exists. No changes needed.';

PRINT 'Done.';
GO
