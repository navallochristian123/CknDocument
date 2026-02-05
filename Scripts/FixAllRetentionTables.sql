-- ============================================
-- COMBINED FIX: All missing columns for Document Approval
-- Run this script against your LawFirmDMS database
-- Execute in SQL Server Management Studio or Azure Data Studio
-- ============================================

USE LawFirmDMS;
GO

PRINT '========================================';
PRINT 'Starting database schema update...';
PRINT '========================================';
GO

-- ============================================
-- PART 1: FIX Retention_Policy TABLE
-- ============================================

PRINT '';
PRINT '--- Fixing Retention_Policy table ---';

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

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Retention_Policy') AND name = 'FirmId')
BEGIN
    ALTER TABLE Retention_Policy ADD FirmId INT NULL;
    PRINT 'Added FirmId column to Retention_Policy';
END

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Retention_Policy') AND name = 'PolicyName')
BEGIN
    ALTER TABLE Retention_Policy ADD PolicyName NVARCHAR(100) NULL;
    PRINT 'Added PolicyName column to Retention_Policy';
END

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Retention_Policy') AND name = 'DocumentType')
BEGIN
    ALTER TABLE Retention_Policy ADD DocumentType NVARCHAR(100) NULL;
    PRINT 'Added DocumentType column to Retention_Policy';
END

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Retention_Policy') AND name = 'RetentionYears')
BEGIN
    ALTER TABLE Retention_Policy ADD RetentionYears INT NULL DEFAULT 7;
    PRINT 'Added RetentionYears column to Retention_Policy';
END

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Retention_Policy') AND name = 'RetentionMonths')
BEGIN
    ALTER TABLE Retention_Policy ADD RetentionMonths INT NULL DEFAULT 0;
    PRINT 'Added RetentionMonths column to Retention_Policy';
END

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Retention_Policy') AND name = 'RetentionDays')
BEGIN
    ALTER TABLE Retention_Policy ADD RetentionDays INT NULL DEFAULT 0;
    PRINT 'Added RetentionDays column to Retention_Policy';
END

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Retention_Policy') AND name = 'IsDefault')
BEGIN
    ALTER TABLE Retention_Policy ADD IsDefault BIT NULL DEFAULT 0;
    PRINT 'Added IsDefault column to Retention_Policy';
END

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Retention_Policy') AND name = 'IsActive')
BEGIN
    ALTER TABLE Retention_Policy ADD IsActive BIT NULL DEFAULT 1;
    PRINT 'Added IsActive column to Retention_Policy';
END

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Retention_Policy') AND name = 'Description')
BEGIN
    ALTER TABLE Retention_Policy ADD Description NVARCHAR(500) NULL;
    PRINT 'Added Description column to Retention_Policy';
END

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Retention_Policy') AND name = 'CreatedBy')
BEGIN
    ALTER TABLE Retention_Policy ADD CreatedBy INT NULL;
    PRINT 'Added CreatedBy column to Retention_Policy';
END

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Retention_Policy') AND name = 'CreatedAt')
BEGIN
    ALTER TABLE Retention_Policy ADD CreatedAt DATETIME NULL DEFAULT GETDATE();
    PRINT 'Added CreatedAt column to Retention_Policy';
END

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Retention_Policy') AND name = 'UpdatedAt')
BEGIN
    ALTER TABLE Retention_Policy ADD UpdatedAt DATETIME NULL;
    PRINT 'Added UpdatedAt column to Retention_Policy';
END
GO

-- ============================================
-- PART 2: FIX Document_Retention TABLE
-- ============================================

PRINT '';
PRINT '--- Fixing Document_Retention table ---';

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

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Document_Retention') AND name = 'FirmId')
BEGIN
    ALTER TABLE Document_Retention ADD FirmId INT NULL;
    PRINT 'Added FirmId column to Document_Retention';
END

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Document_Retention') AND name = 'RetentionStartDate')
BEGIN
    ALTER TABLE Document_Retention ADD RetentionStartDate DATETIME NULL;
    PRINT 'Added RetentionStartDate column to Document_Retention';
END

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Document_Retention') AND name = 'RetentionYears')
BEGIN
    ALTER TABLE Document_Retention ADD RetentionYears INT NULL DEFAULT 7;
    PRINT 'Added RetentionYears column to Document_Retention';
END

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Document_Retention') AND name = 'RetentionMonths')
BEGIN
    ALTER TABLE Document_Retention ADD RetentionMonths INT NULL DEFAULT 0;
    PRINT 'Added RetentionMonths column to Document_Retention';
END

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Document_Retention') AND name = 'RetentionDays')
BEGIN
    ALTER TABLE Document_Retention ADD RetentionDays INT NULL DEFAULT 0;
    PRINT 'Added RetentionDays column to Document_Retention';
END

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Document_Retention') AND name = 'IsModified')
BEGIN
    ALTER TABLE Document_Retention ADD IsModified BIT NULL DEFAULT 0;
    PRINT 'Added IsModified column to Document_Retention';
END

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Document_Retention') AND name = 'ModificationReason')
BEGIN
    ALTER TABLE Document_Retention ADD ModificationReason NVARCHAR(500) NULL;
    PRINT 'Added ModificationReason column to Document_Retention';
END

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Document_Retention') AND name = 'ModifiedBy')
BEGIN
    ALTER TABLE Document_Retention ADD ModifiedBy INT NULL;
    PRINT 'Added ModifiedBy column to Document_Retention';
END

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Document_Retention') AND name = 'ModifiedAt')
BEGIN
    ALTER TABLE Document_Retention ADD ModifiedAt DATETIME NULL;
    PRINT 'Added ModifiedAt column to Document_Retention';
END

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Document_Retention') AND name = 'CreatedBy')
BEGIN
    ALTER TABLE Document_Retention ADD CreatedBy INT NULL;
    PRINT 'Added CreatedBy column to Document_Retention';
END

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Document_Retention') AND name = 'CreatedAt')
BEGIN
    ALTER TABLE Document_Retention ADD CreatedAt DATETIME NULL DEFAULT GETDATE();
    PRINT 'Added CreatedAt column to Document_Retention';
END

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Document_Retention') AND name = 'IsArchived')
BEGIN
    ALTER TABLE Document_Retention ADD IsArchived BIT NULL DEFAULT 0;
    PRINT 'Added IsArchived column to Document_Retention';
END
GO

-- ============================================
-- PART 3: INSERT DEFAULT RETENTION POLICY
-- ============================================

PRINT '';
PRINT '--- Creating default retention policy ---';

IF NOT EXISTS (SELECT * FROM Retention_Policy WHERE IsDefault = 1)
BEGIN
    INSERT INTO Retention_Policy (FirmId, PolicyName, DocumentType, RetentionYears, RetentionMonths, RetentionDays, IsDefault, IsActive, Description, CreatedAt)
    VALUES (1, 'Default Policy', 'General', 7, 0, 0, 1, 1, 'Default retention policy - 7 years', GETDATE());
    PRINT 'Inserted default retention policy';
END
ELSE
BEGIN
    PRINT 'Default retention policy already exists';
END
GO

-- ============================================
-- VERIFICATION
-- ============================================

PRINT '';
PRINT '========================================';
PRINT 'DATABASE UPDATE COMPLETED!';
PRINT '========================================';
PRINT '';
PRINT 'Retention_Policy columns:';

SELECT COLUMN_NAME, DATA_TYPE, IS_NULLABLE 
FROM INFORMATION_SCHEMA.COLUMNS 
WHERE TABLE_NAME = 'Retention_Policy'
ORDER BY ORDINAL_POSITION;

PRINT '';
PRINT 'Document_Retention columns:';

SELECT COLUMN_NAME, DATA_TYPE, IS_NULLABLE 
FROM INFORMATION_SCHEMA.COLUMNS 
WHERE TABLE_NAME = 'Document_Retention'
ORDER BY ORDINAL_POSITION;
GO
