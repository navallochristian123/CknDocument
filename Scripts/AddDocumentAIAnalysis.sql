-- =============================================
-- Script: Add DocumentAIAnalysis Table
-- Description: Creates the DocumentAIAnalyses table for storing
--              OpenAI document analysis results
-- Run this in SSMS against the LawFirmDMS database
-- Date: 2025
-- =============================================

-- Step 1: Debug - see actual table names (uncomment if needed)
-- SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = 'dbo' ORDER BY TABLE_NAME;

-- Step 2: Drop any existing FK constraints that might be broken
IF EXISTS (SELECT * FROM sys.foreign_keys WHERE name = 'FK_DocumentAIAnalyses_Document_DocumentId')
    ALTER TABLE [dbo].[DocumentAIAnalyses] DROP CONSTRAINT [FK_DocumentAIAnalyses_Document_DocumentId];
IF EXISTS (SELECT * FROM sys.foreign_keys WHERE name = 'FK_DocumentAIAnalyses_Documents_DocumentId')
    ALTER TABLE [dbo].[DocumentAIAnalyses] DROP CONSTRAINT [FK_DocumentAIAnalyses_Documents_DocumentId];
IF EXISTS (SELECT * FROM sys.foreign_keys WHERE name = 'FK_DocumentAIAnalyses_Firm_FirmId')
    ALTER TABLE [dbo].[DocumentAIAnalyses] DROP CONSTRAINT [FK_DocumentAIAnalyses_Firm_FirmId];
IF EXISTS (SELECT * FROM sys.foreign_keys WHERE name = 'FK_DocumentAIAnalyses_Firms_FirmId')
    ALTER TABLE [dbo].[DocumentAIAnalyses] DROP CONSTRAINT [FK_DocumentAIAnalyses_Firms_FirmId];
IF EXISTS (SELECT * FROM sys.foreign_keys WHERE name = 'FK_DocumentAIAnalyses_LawFirms_FirmId')
    ALTER TABLE [dbo].[DocumentAIAnalyses] DROP CONSTRAINT [FK_DocumentAIAnalyses_LawFirms_FirmId];
GO

-- Step 3: Create the table (NO foreign keys - those come later)
IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'DocumentAIAnalyses')
BEGIN
    CREATE TABLE [dbo].[DocumentAIAnalyses] (
        [AnalysisId]             INT             IDENTITY(1,1) NOT NULL,
        [DocumentId]             INT             NOT NULL,
        [FirmId]                 INT             NOT NULL,
        [DetectedDocumentType]   NVARCHAR(200)   NULL,
        [Confidence]             FLOAT           NULL,
        [Summary]                NVARCHAR(MAX)   NULL,
        [ChecklistJson]          NVARCHAR(MAX)   NULL,
        [IssuesJson]             NVARCHAR(MAX)   NULL,
        [MissingItemsJson]       NVARCHAR(MAX)   NULL,
        [RawResponseJson]        NVARCHAR(MAX)   NULL,
        [ExtractedText]          NVARCHAR(MAX)   NULL,
        [IsProcessed]            BIT             NOT NULL DEFAULT(0),
        [ProcessedAt]            DATETIME2       NULL,
        [ErrorMessage]           NVARCHAR(MAX)   NULL,
        [ModelUsed]              NVARCHAR(100)   NULL,
        [TokensUsed]             INT             NULL,
        [CreatedAt]              DATETIME2       NOT NULL DEFAULT(GETUTCDATE()),
        [UpdatedAt]              DATETIME2       NULL,
        [CreatedBy]              NVARCHAR(100)   NULL,
        [UpdatedBy]              NVARCHAR(100)   NULL,
        CONSTRAINT [PK_DocumentAIAnalyses] PRIMARY KEY CLUSTERED ([AnalysisId] ASC)
    );

    CREATE NONCLUSTERED INDEX [IX_DocumentAIAnalyses_DocumentId] 
        ON [dbo].[DocumentAIAnalyses] ([DocumentId] ASC);

    CREATE NONCLUSTERED INDEX [IX_DocumentAIAnalyses_FirmId] 
        ON [dbo].[DocumentAIAnalyses] ([FirmId] ASC);

    PRINT '✓ Table DocumentAIAnalyses created successfully.';
END
ELSE
BEGIN
    PRINT '✓ Table DocumentAIAnalyses already exists.';
END
GO

-- Step 4: Add FK to Document table (auto-detect table name and PK column)
DECLARE @docTable NVARCHAR(128);
DECLARE @docPK NVARCHAR(128);
DECLARE @sql NVARCHAR(MAX);

-- Find the Document table name
SELECT TOP 1 @docTable = TABLE_NAME 
FROM INFORMATION_SCHEMA.TABLES 
WHERE TABLE_NAME IN ('Document', 'Documents') AND TABLE_SCHEMA = 'dbo'
ORDER BY TABLE_NAME;

IF @docTable IS NOT NULL
BEGIN
    -- Find the PK column of the document table
    SELECT TOP 1 @docPK = COLUMN_NAME
    FROM INFORMATION_SCHEMA.KEY_COLUMN_USAGE
    WHERE TABLE_NAME = @docTable AND CONSTRAINT_NAME LIKE 'PK_%';

    IF @docPK IS NOT NULL
    BEGIN
        SET @sql = 'ALTER TABLE [dbo].[DocumentAIAnalyses] ADD CONSTRAINT [FK_DocumentAIAnalyses_Doc] FOREIGN KEY ([DocumentId]) REFERENCES [dbo].[' + @docTable + '] ([' + @docPK + ']) ON DELETE CASCADE';
        EXEC sp_executesql @sql;
        PRINT '✓ FK to ' + @docTable + '(' + @docPK + ') added.';
    END
    ELSE
        PRINT '⚠ Found table ' + @docTable + ' but could not determine PK column.';
END
ELSE
BEGIN
    PRINT '⚠ WARNING: No Document/Documents table found. FK not created (app will still work).';
    PRINT 'Available tables with "Document" in name:';
    SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME LIKE '%ocument%';
END
GO

-- Step 5: Add FK to Firm table (auto-detect table name and PK column)
DECLARE @firmTable NVARCHAR(128);
DECLARE @firmPK NVARCHAR(128);
DECLARE @sql2 NVARCHAR(MAX);

-- Find the Firm table name
SELECT TOP 1 @firmTable = TABLE_NAME 
FROM INFORMATION_SCHEMA.TABLES 
WHERE TABLE_NAME IN ('Firm', 'Firms', 'LawFirms', 'LawFirm') AND TABLE_SCHEMA = 'dbo'
ORDER BY TABLE_NAME;

IF @firmTable IS NOT NULL
BEGIN
    SELECT TOP 1 @firmPK = COLUMN_NAME
    FROM INFORMATION_SCHEMA.KEY_COLUMN_USAGE
    WHERE TABLE_NAME = @firmTable AND CONSTRAINT_NAME LIKE 'PK_%';

    IF @firmPK IS NOT NULL
    BEGIN
        SET @sql2 = 'ALTER TABLE [dbo].[DocumentAIAnalyses] ADD CONSTRAINT [FK_DocumentAIAnalyses_Firm] FOREIGN KEY ([FirmId]) REFERENCES [dbo].[' + @firmTable + '] ([' + @firmPK + ']) ON DELETE NO ACTION';
        EXEC sp_executesql @sql2;
        PRINT '✓ FK to ' + @firmTable + '(' + @firmPK + ') added.';
    END
    ELSE
        PRINT '⚠ Found table ' + @firmTable + ' but could not determine PK column.';
END
ELSE
BEGIN
    PRINT '⚠ WARNING: No Firm table found. FK not created (app will still work).';
    PRINT 'Available tables with "Firm" in name:';
    SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME LIKE '%irm%';
END
GO

PRINT '';
PRINT '=== DONE! You can now restart the application. ===';
PRINT 'Even if FK constraints could not be created, the app will work.';
PRINT 'The AI analysis feature will start saving data on the next document upload.';
GO
