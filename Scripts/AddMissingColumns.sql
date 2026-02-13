-- Add missing columns for client registration features
-- Run this script on your LawFirmDMS database

-- Add columns to User table (note: singular, not Users)
IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'User' AND COLUMN_NAME = 'Barangay')
BEGIN
    ALTER TABLE [dbo].[User] ADD [Barangay] NVARCHAR(100) NULL;
    PRINT 'Added Barangay column to User table';
END
GO

IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'User' AND COLUMN_NAME = 'CompanyName')
BEGIN
    ALTER TABLE [dbo].[User] ADD [CompanyName] NVARCHAR(200) NULL;
    PRINT 'Added CompanyName column to User table';
END
GO

IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'User' AND COLUMN_NAME = 'Purpose')
BEGIN
    ALTER TABLE [dbo].[User] ADD [Purpose] NVARCHAR(1000) NULL;
    PRINT 'Added Purpose column to User table';
END
GO

-- Add FirmCode column to Firm table
IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Firm' AND COLUMN_NAME = 'FirmCode')
BEGIN
    ALTER TABLE [dbo].[Firm] ADD [FirmCode] NVARCHAR(20) NULL;
    PRINT 'Added FirmCode column to Firm table';
END
GO

-- Set default FirmCode for Demo Law Firm
UPDATE [dbo].[Firm] SET [FirmCode] = 'DEMO2024' WHERE [FirmCode] IS NULL;
PRINT 'Set default FirmCode DEMO2024 for existing firms';
GO

-- Add Lawyer-related columns to Document table
IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Document' AND COLUMN_NAME = 'AssignedLawyerId')
BEGIN
    ALTER TABLE [dbo].[Document] ADD [AssignedLawyerId] INT NULL;
    PRINT 'Added AssignedLawyerId column to Document table';
END
GO

IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Document' AND COLUMN_NAME = 'LawyerReviewedAt')
BEGIN
    ALTER TABLE [dbo].[Document] ADD [LawyerReviewedAt] DATETIME2 NULL;
    PRINT 'Added LawyerReviewedAt column to Document table';
END
GO

PRINT 'All missing columns added successfully!';
