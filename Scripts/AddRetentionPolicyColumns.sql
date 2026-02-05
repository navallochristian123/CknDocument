-- Add missing columns to Retention_Policy table
-- Run this script against your LawFirmDMS database
-- Execute in SQL Server Management Studio or Azure Data Studio

USE LawFirmDMS;
GO

-- First, check if Retention_Policy table exists
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Retention_Policy')
BEGIN
    CREATE TABLE Retention_Policy (
        PolicyID INT IDENTITY(1,1) PRIMARY KEY,
        FirmId INT NULL,
        PolicyName NVARCHAR(100) NULL,
        DocumentType NVARCHAR(100) NULL,
        RetentionYears INT NULL DEFAULT 7,
        RetentionMonths INT NULL DEFAULT 0,
        RetentionDays INT NULL DEFAULT 0,
        IsDefault BIT NULL DEFAULT 0,
        IsActive BIT NULL DEFAULT 1,
        Description NVARCHAR(500) NULL,
        CreatedBy INT NULL,
        CreatedAt DATETIME NULL DEFAULT GETDATE(),
        UpdatedAt DATETIME NULL
    );
    PRINT 'Created Retention_Policy table';
END
ELSE
BEGIN
    PRINT 'Retention_Policy table exists, checking for missing columns...';
END
GO

-- Check and add missing columns to Retention_Policy table
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Retention_Policy') AND name = 'FirmId')
BEGIN
    ALTER TABLE Retention_Policy ADD FirmId INT NULL;
    PRINT 'Added FirmId column';
END

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Retention_Policy') AND name = 'PolicyName')
BEGIN
    ALTER TABLE Retention_Policy ADD PolicyName NVARCHAR(100) NULL;
    PRINT 'Added PolicyName column';
END

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Retention_Policy') AND name = 'DocumentType')
BEGIN
    ALTER TABLE Retention_Policy ADD DocumentType NVARCHAR(100) NULL;
    PRINT 'Added DocumentType column';
END

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Retention_Policy') AND name = 'RetentionYears')
BEGIN
    ALTER TABLE Retention_Policy ADD RetentionYears INT NULL DEFAULT 7;
    PRINT 'Added RetentionYears column';
END

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Retention_Policy') AND name = 'RetentionMonths')
BEGIN
    ALTER TABLE Retention_Policy ADD RetentionMonths INT NULL DEFAULT 0;
    PRINT 'Added RetentionMonths column';
END

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Retention_Policy') AND name = 'RetentionDays')
BEGIN
    ALTER TABLE Retention_Policy ADD RetentionDays INT NULL DEFAULT 0;
    PRINT 'Added RetentionDays column';
END

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Retention_Policy') AND name = 'IsDefault')
BEGIN
    ALTER TABLE Retention_Policy ADD IsDefault BIT NULL DEFAULT 0;
    PRINT 'Added IsDefault column';
END

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Retention_Policy') AND name = 'IsActive')
BEGIN
    ALTER TABLE Retention_Policy ADD IsActive BIT NULL DEFAULT 1;
    PRINT 'Added IsActive column';
END

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Retention_Policy') AND name = 'Description')
BEGIN
    ALTER TABLE Retention_Policy ADD Description NVARCHAR(500) NULL;
    PRINT 'Added Description column';
END

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Retention_Policy') AND name = 'CreatedBy')
BEGIN
    ALTER TABLE Retention_Policy ADD CreatedBy INT NULL;
    PRINT 'Added CreatedBy column';
END

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Retention_Policy') AND name = 'CreatedAt')
BEGIN
    ALTER TABLE Retention_Policy ADD CreatedAt DATETIME NULL DEFAULT GETDATE();
    PRINT 'Added CreatedAt column';
END

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Retention_Policy') AND name = 'UpdatedAt')
BEGIN
    ALTER TABLE Retention_Policy ADD UpdatedAt DATETIME NULL;
    PRINT 'Added UpdatedAt column';
END
GO

-- Insert a default policy if none exists
IF NOT EXISTS (SELECT * FROM Retention_Policy WHERE IsDefault = 1)
BEGIN
    INSERT INTO Retention_Policy (FirmId, PolicyName, DocumentType, RetentionYears, RetentionMonths, RetentionDays, IsDefault, IsActive, Description, CreatedAt)
    VALUES (1, 'Default Policy', 'General', 7, 0, 0, 1, 1, 'Default retention policy - 7 years', GETDATE());
    PRINT 'Inserted default retention policy';
END
GO

PRINT '========================================';
PRINT 'Retention_Policy table update completed!';
PRINT '========================================';
GO

-- Verify the columns
SELECT COLUMN_NAME, DATA_TYPE, IS_NULLABLE 
FROM INFORMATION_SCHEMA.COLUMNS 
WHERE TABLE_NAME = 'Retention_Policy'
ORDER BY ORDINAL_POSITION;
GO
