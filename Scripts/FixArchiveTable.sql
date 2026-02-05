-- ============================================
-- FIX ARCHIVE TABLE - Add all missing columns
-- Run this script against your LawFirmDMS database
-- Execute in SQL Server Management Studio or Azure Data Studio
-- ============================================

USE LawFirmDMS;
GO

PRINT '========================================';
PRINT 'Starting Archive table schema update...';
PRINT '========================================';
GO

-- ============================================
-- PART 1: CREATE OR FIX ARCHIVE TABLE
-- ============================================

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Archive')
BEGIN
    CREATE TABLE Archive (
        ArchiveID INT IDENTITY(1,1) PRIMARY KEY,
        DocumentID INT NULL,
        FirmId INT NULL,
        ArchivedDate DATETIME NULL DEFAULT GETDATE(),
        Reason NVARCHAR(500) NULL,
        ArchiveType NVARCHAR(50) NULL,  -- Manual, Retention, Rejected, Version, AutoExpired
        OriginalRetentionDate DATETIME NULL,
        VersionNumber INT NULL,
        ArchivedBy INT NULL,
        IsRestored BIT NULL DEFAULT 0,
        RestoredAt DATETIME NULL,
        RestoredBy INT NULL,
        OriginalStatus NVARCHAR(50) NULL,
        OriginalWorkflowStage NVARCHAR(50) NULL,
        OriginalFolderId INT NULL,
        ScheduledDeleteDate DATETIME NULL,
        IsDeleted BIT NULL DEFAULT 0,
        DeletedAt DATETIME NULL,
        DeletedBy INT NULL,
        CreatedAt DATETIME NULL DEFAULT GETDATE()
    );
    PRINT 'Created Archive table';
END
ELSE
BEGIN
    PRINT 'Archive table exists, checking for missing columns...';
END
GO

-- Add missing columns
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Archive') AND name = 'FirmId')
BEGIN
    ALTER TABLE Archive ADD FirmId INT NULL;
    PRINT 'Added FirmId column';
END

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Archive') AND name = 'OriginalStatus')
BEGIN
    ALTER TABLE Archive ADD OriginalStatus NVARCHAR(50) NULL;
    PRINT 'Added OriginalStatus column';
END

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Archive') AND name = 'OriginalWorkflowStage')
BEGIN
    ALTER TABLE Archive ADD OriginalWorkflowStage NVARCHAR(50) NULL;
    PRINT 'Added OriginalWorkflowStage column';
END

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Archive') AND name = 'OriginalFolderId')
BEGIN
    ALTER TABLE Archive ADD OriginalFolderId INT NULL;
    PRINT 'Added OriginalFolderId column';
END

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Archive') AND name = 'ScheduledDeleteDate')
BEGIN
    ALTER TABLE Archive ADD ScheduledDeleteDate DATETIME NULL;
    PRINT 'Added ScheduledDeleteDate column';
END

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Archive') AND name = 'IsDeleted')
BEGIN
    ALTER TABLE Archive ADD IsDeleted BIT NULL DEFAULT 0;
    PRINT 'Added IsDeleted column';
END

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Archive') AND name = 'DeletedAt')
BEGIN
    ALTER TABLE Archive ADD DeletedAt DATETIME NULL;
    PRINT 'Added DeletedAt column';
END

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Archive') AND name = 'DeletedBy')
BEGIN
    ALTER TABLE Archive ADD DeletedBy INT NULL;
    PRINT 'Added DeletedBy column';
END

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Archive') AND name = 'CreatedAt')
BEGIN
    ALTER TABLE Archive ADD CreatedAt DATETIME NULL DEFAULT GETDATE();
    PRINT 'Added CreatedAt column';
END

-- Make Reason column larger if needed (255 to 500)
IF EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Archive') AND name = 'Reason' AND max_length = 255)
BEGIN
    ALTER TABLE Archive ALTER COLUMN Reason NVARCHAR(500) NULL;
    PRINT 'Expanded Reason column to 500 characters';
END
GO

-- ============================================
-- PART 2: ADD INDEXES FOR PERFORMANCE
-- ============================================

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Archive_DocumentID')
BEGIN
    CREATE INDEX IX_Archive_DocumentID ON Archive(DocumentID);
    PRINT 'Created index IX_Archive_DocumentID';
END

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Archive_FirmId')
BEGIN
    CREATE INDEX IX_Archive_FirmId ON Archive(FirmId);
    PRINT 'Created index IX_Archive_FirmId';
END

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Archive_ArchiveType')
BEGIN
    CREATE INDEX IX_Archive_ArchiveType ON Archive(ArchiveType);
    PRINT 'Created index IX_Archive_ArchiveType';
END

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Archive_IsRestored')
BEGIN
    CREATE INDEX IX_Archive_IsRestored ON Archive(IsRestored);
    PRINT 'Created index IX_Archive_IsRestored';
END
GO

-- ============================================
-- PART 3: UPDATE EXISTING RECORDS
-- ============================================

-- Set FirmId from Document table for existing records
UPDATE a
SET a.FirmId = d.FirmID
FROM Archive a
INNER JOIN Document d ON a.DocumentID = d.DocumentID
WHERE a.FirmId IS NULL;

PRINT 'Updated FirmId for existing archive records';
GO

PRINT '';
PRINT '========================================';
PRINT 'ARCHIVE TABLE UPDATE COMPLETED!';
PRINT '========================================';
GO

-- Verify columns
SELECT COLUMN_NAME, DATA_TYPE, IS_NULLABLE, CHARACTER_MAXIMUM_LENGTH
FROM INFORMATION_SCHEMA.COLUMNS 
WHERE TABLE_NAME = 'Archive'
ORDER BY ORDINAL_POSITION;
GO
