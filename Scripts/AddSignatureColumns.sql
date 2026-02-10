-- Add Signature columns to User table for AI signature verification
-- Run this script to add required columns for the signature feature

USE LawFirmDMS;
GO

-- Add SignaturePath column to store path to user's signature image
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.[User]') AND name = 'SignaturePath')
BEGIN
    ALTER TABLE dbo.[User] ADD SignaturePath NVARCHAR(500) NULL;
    PRINT 'Added SignaturePath column to User table';
END
ELSE
BEGIN
    PRINT 'SignaturePath column already exists';
END
GO

-- Add SignatureName column to store user's full name as it appears on signature
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.[User]') AND name = 'SignatureName')
BEGIN
    ALTER TABLE dbo.[User] ADD SignatureName NVARCHAR(200) NULL;
    PRINT 'Added SignatureName column to User table';
END
ELSE
BEGIN
    PRINT 'SignatureName column already exists';
END
GO

PRINT 'Signature columns migration complete!';
