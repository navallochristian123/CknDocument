-- Add missing columns to Document_Retention table
-- Run this script against your LawFirmDMS database
-- Execute in SQL Server Management Studio or Azure Data Studio

USE LawFirmDMS;
GO

-- First, check if Document_Retention table exists
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Document_Retention')
BEGIN
    CREATE TABLE Document_Retention (
        RetentionID INT IDENTITY(1,1) PRIMARY KEY,
        DocumentID INT NULL,
        PolicyID INT NULL,
        FirmId INT NULL,
        RetentionStartDate DATETIME NULL,
        ExpiryDate DATETIME NULL,
        RetentionYears INT NULL DEFAULT 7,
        RetentionMonths INT NULL DEFAULT 0,
        RetentionDays INT NULL DEFAULT 0,
        IsArchived BIT NULL DEFAULT 0,
        IsModified BIT NULL DEFAULT 0,
        ModificationReason NVARCHAR(500) NULL,
        ModifiedBy INT NULL,
        ModifiedAt DATETIME NULL,
        CreatedBy INT NULL,
        CreatedAt DATETIME NULL DEFAULT GETDATE()
    );
    PRINT 'Created Document_Retention table';
END
ELSE
BEGIN
    PRINT 'Document_Retention table exists, checking for missing columns...';
END
GO

-- Check and add missing columns to Document_Retention table
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Document_Retention') AND name = 'FirmId')
BEGIN
    ALTER TABLE Document_Retention ADD FirmId INT NULL;
    PRINT 'Added FirmId column';
END

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Document_Retention') AND name = 'RetentionStartDate')
BEGIN
    ALTER TABLE Document_Retention ADD RetentionStartDate DATETIME NULL;
    PRINT 'Added RetentionStartDate column';
END

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Document_Retention') AND name = 'RetentionYears')
BEGIN
    ALTER TABLE Document_Retention ADD RetentionYears INT NULL DEFAULT 7;
    PRINT 'Added RetentionYears column';
END

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Document_Retention') AND name = 'RetentionMonths')
BEGIN
    ALTER TABLE Document_Retention ADD RetentionMonths INT NULL DEFAULT 0;
    PRINT 'Added RetentionMonths column';
END

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Document_Retention') AND name = 'RetentionDays')
BEGIN
    ALTER TABLE Document_Retention ADD RetentionDays INT NULL DEFAULT 0;
    PRINT 'Added RetentionDays column';
END

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Document_Retention') AND name = 'IsModified')
BEGIN
    ALTER TABLE Document_Retention ADD IsModified BIT NULL DEFAULT 0;
    PRINT 'Added IsModified column';
END

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Document_Retention') AND name = 'ModificationReason')
BEGIN
    ALTER TABLE Document_Retention ADD ModificationReason NVARCHAR(500) NULL;
    PRINT 'Added ModificationReason column';
END

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Document_Retention') AND name = 'ModifiedBy')
BEGIN
    ALTER TABLE Document_Retention ADD ModifiedBy INT NULL;
    PRINT 'Added ModifiedBy column';
END

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Document_Retention') AND name = 'ModifiedAt')
BEGIN
    ALTER TABLE Document_Retention ADD ModifiedAt DATETIME NULL;
    PRINT 'Added ModifiedAt column';
END

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Document_Retention') AND name = 'CreatedBy')
BEGIN
    ALTER TABLE Document_Retention ADD CreatedBy INT NULL;
    PRINT 'Added CreatedBy column';
END

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Document_Retention') AND name = 'CreatedAt')
BEGIN
    ALTER TABLE Document_Retention ADD CreatedAt DATETIME NULL DEFAULT GETDATE();
    PRINT 'Added CreatedAt column';
END

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Document_Retention') AND name = 'IsArchived')
BEGIN
    ALTER TABLE Document_Retention ADD IsArchived BIT NULL DEFAULT 0;
    PRINT 'Added IsArchived column';
END
GO

-- Add foreign keys if they don't exist
-- Note: Table names may vary (Document vs Documents, Firm vs Firms)
-- Uncomment and adjust the correct version for your database

-- Try with singular table names (Document, Firm)
IF NOT EXISTS (SELECT * FROM sys.foreign_keys WHERE name = 'FK_DocumentRetention_Document')
BEGIN
    IF EXISTS (SELECT * FROM sys.tables WHERE name = 'Document')
    BEGIN
        ALTER TABLE Document_Retention ADD CONSTRAINT FK_DocumentRetention_Document 
        FOREIGN KEY (DocumentID) REFERENCES Document(DocumentID);
        PRINT 'Added FK_DocumentRetention_Document constraint';
    END
    ELSE IF EXISTS (SELECT * FROM sys.tables WHERE name = 'Documents')
    BEGIN
        ALTER TABLE Document_Retention ADD CONSTRAINT FK_DocumentRetention_Document 
        FOREIGN KEY (DocumentID) REFERENCES Documents(DocumentID);
        PRINT 'Added FK_DocumentRetention_Document constraint';
    END
    ELSE
        PRINT 'Document table not found - skipping foreign key';
END
GO

IF NOT EXISTS (SELECT * FROM sys.foreign_keys WHERE name = 'FK_DocumentRetention_Firm')
BEGIN
    IF EXISTS (SELECT * FROM sys.tables WHERE name = 'Firm')
    BEGIN
        ALTER TABLE Document_Retention ADD CONSTRAINT FK_DocumentRetention_Firm 
        FOREIGN KEY (FirmId) REFERENCES Firm(FirmID);
        PRINT 'Added FK_DocumentRetention_Firm constraint';
    END
    ELSE IF EXISTS (SELECT * FROM sys.tables WHERE name = 'Firms')
    BEGIN
        ALTER TABLE Document_Retention ADD CONSTRAINT FK_DocumentRetention_Firm 
        FOREIGN KEY (FirmId) REFERENCES Firms(FirmID);
        PRINT 'Added FK_DocumentRetention_Firm constraint';
    END
    ELSE
        PRINT 'Firm table not found - skipping foreign key';
END
GO

PRINT '========================================';
PRINT 'Document_Retention table update completed!';
PRINT '========================================';
GO

-- Verify the columns
SELECT COLUMN_NAME, DATA_TYPE, IS_NULLABLE 
FROM INFORMATION_SCHEMA.COLUMNS 
WHERE TABLE_NAME = 'Document_Retention'
ORDER BY ORDINAL_POSITION;
GO

