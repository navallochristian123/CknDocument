-- =====================================================
-- SQL Migration Script: Merge OwnerERP into LawFirmDMS
-- =====================================================
-- This script creates a unified database structure in LawFirmDMS
-- Run this script BEFORE starting the application
-- 
-- IMPORTANT: 
-- 1. Backup your existing data before running this script
-- 2. Run this script against SQL Server
-- 3. The script will drop existing tables and recreate them
-- =====================================================

USE [LawFirmDMS];
GO

-- =====================================================
-- PART 1: Drop OwnerERP Database (if exists)
-- =====================================================

USE [master];
GO
IF EXISTS (SELECT name FROM sys.databases WHERE name = N'OwnerERP')
BEGIN
    ALTER DATABASE [OwnerERP] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE [OwnerERP];
    PRINT 'OwnerERP database dropped successfully';
END
ELSE
BEGIN
    PRINT 'OwnerERP database does not exist, skipping drop';
END
GO

-- Switch back to LawFirmDMS database
USE [LawFirmDMS];
GO

-- =====================================================
-- PART 2: Add new columns to existing tables
-- =====================================================

-- Add new columns to Firm table
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE Name = N'PhoneNumber' AND Object_ID = Object_ID(N'Firm'))
BEGIN
    ALTER TABLE [Firm] ADD [PhoneNumber] NVARCHAR(20) NULL;
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE Name = N'LogoUrl' AND Object_ID = Object_ID(N'Firm'))
BEGIN
    ALTER TABLE [Firm] ADD [LogoUrl] NVARCHAR(500) NULL;
END
GO

-- Update Audit_Log table with new columns
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE Name = N'SuperAdminId' AND Object_ID = Object_ID(N'Audit_Log'))
BEGIN
    ALTER TABLE [Audit_Log] ADD [SuperAdminId] INT NULL;
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE Name = N'FirmID' AND Object_ID = Object_ID(N'Audit_Log'))
BEGIN
    ALTER TABLE [Audit_Log] ADD [FirmID] INT NULL;
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE Name = N'Description' AND Object_ID = Object_ID(N'Audit_Log'))
BEGIN
    ALTER TABLE [Audit_Log] ADD [Description] NVARCHAR(1000) NULL;
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE Name = N'OldValues' AND Object_ID = Object_ID(N'Audit_Log'))
BEGIN
    ALTER TABLE [Audit_Log] ADD [OldValues] NVARCHAR(2000) NULL;
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE Name = N'NewValues' AND Object_ID = Object_ID(N'Audit_Log'))
BEGIN
    ALTER TABLE [Audit_Log] ADD [NewValues] NVARCHAR(2000) NULL;
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE Name = N'UserAgent' AND Object_ID = Object_ID(N'Audit_Log'))
BEGIN
    ALTER TABLE [Audit_Log] ADD [UserAgent] NVARCHAR(500) NULL;
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE Name = N'ActionCategory' AND Object_ID = Object_ID(N'Audit_Log'))
BEGIN
    ALTER TABLE [Audit_Log] ADD [ActionCategory] NVARCHAR(50) NULL;
END
GO

-- Make Action required and extend size
IF EXISTS (SELECT 1 FROM sys.columns WHERE Name = N'Action' AND Object_ID = Object_ID(N'Audit_Log'))
BEGIN
    ALTER TABLE [Audit_Log] ALTER COLUMN [Action] NVARCHAR(100) NOT NULL;
END
GO

-- Make Timestamp required with default (only if default doesn't exist)
IF EXISTS (SELECT 1 FROM sys.columns WHERE Name = N'Timestamp' AND Object_ID = Object_ID(N'Audit_Log'))
BEGIN
    -- Check if default constraint already exists
    IF NOT EXISTS (SELECT 1 FROM sys.default_constraints dc
                   JOIN sys.columns c ON dc.parent_object_id = c.object_id AND dc.parent_column_id = c.column_id
                   WHERE c.name = 'Timestamp' AND c.object_id = OBJECT_ID('Audit_Log'))
    BEGIN
        ALTER TABLE [Audit_Log] ADD CONSTRAINT DF_AuditLog_Timestamp DEFAULT GETDATE() FOR [Timestamp];
        PRINT 'Added default constraint for Timestamp';
    END
    ELSE
    BEGIN
        PRINT 'Default constraint for Timestamp already exists, skipping';
    END
END
GO

-- =====================================================
-- PART 3: Create new tables (merged from OwnerERP)
-- =====================================================

-- SuperAdmin Table
IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='SuperAdmin' AND xtype='U')
BEGIN
    CREATE TABLE [SuperAdmin] (
        [SuperAdminId] INT IDENTITY(1,1) PRIMARY KEY,
        [Username] NVARCHAR(100) NOT NULL,
        [Email] NVARCHAR(255) NOT NULL,
        [PasswordHash] NVARCHAR(255) NOT NULL,
        [FirstName] NVARCHAR(100) NULL,
        [LastName] NVARCHAR(100) NULL,
        [PhoneNumber] NVARCHAR(11) NULL,
        [Status] NVARCHAR(20) DEFAULT 'Active',
        [LastLoginAt] DATETIME NULL,
        [CreatedAt] DATETIME DEFAULT GETDATE(),
        [UpdatedAt] DATETIME NULL,
        CONSTRAINT [UQ_SuperAdmin_Username] UNIQUE ([Username]),
        CONSTRAINT [UQ_SuperAdmin_Email] UNIQUE ([Email])
    );
    PRINT 'Created SuperAdmin table';
END
GO

-- FirmSubscription Table (replaces Client from OwnerERP)
IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='FirmSubscription' AND xtype='U')
BEGIN
    CREATE TABLE [FirmSubscription] (
        [SubscriptionID] INT IDENTITY(1,1) PRIMARY KEY,
        [FirmID] INT NOT NULL,
        [SubscriptionName] NVARCHAR(150) NULL,
        [ContactEmail] NVARCHAR(100) NULL,
        [BillingAddress] NVARCHAR(255) NULL,
        [Status] NVARCHAR(50) DEFAULT 'Active',
        [PlanType] NVARCHAR(50) NULL,
        [StartDate] DATE NULL,
        [EndDate] DATE NULL,
        [CreatedAt] DATETIME DEFAULT GETDATE(),
        [UpdatedAt] DATETIME NULL,
        CONSTRAINT [FK_FirmSubscription_Firm] FOREIGN KEY ([FirmID]) REFERENCES [Firm]([FirmID])
    );
    PRINT 'Created FirmSubscription table';
END
GO

-- Invoice Table
IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='Invoice' AND xtype='U')
BEGIN
    CREATE TABLE [Invoice] (
        [InvoiceID] INT IDENTITY(1,1) PRIMARY KEY,
        [SubscriptionID] INT NULL,
        [InvoiceNumber] NVARCHAR(50) NULL,
        [InvoiceDate] DATE NULL,
        [DueDate] DATE NULL,
        [TotalAmount] DECIMAL(12,2) NULL,
        [PaidAmount] DECIMAL(12,2) NULL,
        [Status] NVARCHAR(50) DEFAULT 'Pending',
        [Notes] NVARCHAR(500) NULL,
        [CreatedAt] DATETIME DEFAULT GETDATE(),
        [UpdatedAt] DATETIME NULL,
        CONSTRAINT [FK_Invoice_FirmSubscription] FOREIGN KEY ([SubscriptionID]) REFERENCES [FirmSubscription]([SubscriptionID])
    );
    PRINT 'Created Invoice table';
END
GO

-- InvoiceItem Table
IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='InvoiceItem' AND xtype='U')
BEGIN
    CREATE TABLE [InvoiceItem] (
        [ItemID] INT IDENTITY(1,1) PRIMARY KEY,
        [InvoiceID] INT NULL,
        [Description] NVARCHAR(255) NULL,
        [Quantity] INT DEFAULT 1,
        [UnitPrice] DECIMAL(12,2) NULL,
        [SubTotal] DECIMAL(12,2) NULL,
        CONSTRAINT [FK_InvoiceItem_Invoice] FOREIGN KEY ([InvoiceID]) REFERENCES [Invoice]([InvoiceID])
    );
    PRINT 'Created InvoiceItem table';
END
GO

-- Payment Table
IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='Payment' AND xtype='U')
BEGIN
    CREATE TABLE [Payment] (
        [PaymentID] INT IDENTITY(1,1) PRIMARY KEY,
        [SubscriptionID] INT NULL,
        [InvoiceID] INT NULL,
        [PaymentReference] NVARCHAR(50) NULL,
        [Amount] DECIMAL(12,2) NULL,
        [PaymentMethod] NVARCHAR(50) NULL,
        [PaymentDate] DATE NULL,
        [Status] NVARCHAR(50) DEFAULT 'Pending',
        [Notes] NVARCHAR(500) NULL,
        [CreatedAt] DATETIME DEFAULT GETDATE(),
        [UpdatedAt] DATETIME NULL,
        CONSTRAINT [FK_Payment_FirmSubscription] FOREIGN KEY ([SubscriptionID]) REFERENCES [FirmSubscription]([SubscriptionID]),
        CONSTRAINT [FK_Payment_Invoice] FOREIGN KEY ([InvoiceID]) REFERENCES [Invoice]([InvoiceID])
    );
    PRINT 'Created Payment table';
END
GO

-- Revenue Table
IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='Revenue' AND xtype='U')
BEGIN
    CREATE TABLE [Revenue] (
        [RevenueID] INT IDENTITY(1,1) PRIMARY KEY,
        [SubscriptionID] INT NULL,
        [Source] NVARCHAR(100) NULL,
        [Amount] DECIMAL(12,2) NULL,
        [RevenueDate] DATE NULL,
        [Description] NVARCHAR(500) NULL,
        [CreatedAt] DATETIME DEFAULT GETDATE(),
        [UpdatedAt] DATETIME NULL,
        CONSTRAINT [FK_Revenue_FirmSubscription] FOREIGN KEY ([SubscriptionID]) REFERENCES [FirmSubscription]([SubscriptionID])
    );
    PRINT 'Created Revenue table';
END
GO

-- Expense Table
IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='Expense' AND xtype='U')
BEGIN
    CREATE TABLE [Expense] (
        [ExpenseID] INT IDENTITY(1,1) PRIMARY KEY,
        [Description] NVARCHAR(255) NULL,
        [Amount] DECIMAL(12,2) NULL,
        [Category] NVARCHAR(100) NULL,
        [ExpenseDate] DATE NULL,
        [Notes] NVARCHAR(500) NULL,
        [Status] NVARCHAR(50) DEFAULT 'Pending',
        [CreatedAt] DATETIME DEFAULT GETDATE(),
        [UpdatedAt] DATETIME NULL
    );
    PRINT 'Created Expense table';
END
GO

-- =====================================================
-- PART 4: Add foreign key constraints for Audit_Log
-- =====================================================

-- Add FK for SuperAdmin in Audit_Log
IF NOT EXISTS (SELECT * FROM sys.foreign_keys WHERE name = 'FK_AuditLog_SuperAdmin')
BEGIN
    ALTER TABLE [Audit_Log] ADD CONSTRAINT [FK_AuditLog_SuperAdmin] 
        FOREIGN KEY ([SuperAdminId]) REFERENCES [SuperAdmin]([SuperAdminId]);
END
GO

-- Add FK for Firm in Audit_Log
IF NOT EXISTS (SELECT * FROM sys.foreign_keys WHERE name = 'FK_AuditLog_Firm')
BEGIN
    ALTER TABLE [Audit_Log] ADD CONSTRAINT [FK_AuditLog_Firm] 
        FOREIGN KEY ([FirmID]) REFERENCES [Firm]([FirmID]);
END
GO

-- =====================================================
-- PART 5: Create indexes for performance
-- =====================================================

-- Audit_Log indexes
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_AuditLog_SuperAdminId')
    CREATE INDEX [IX_AuditLog_SuperAdminId] ON [Audit_Log]([SuperAdminId]);
GO

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_AuditLog_FirmID')
    CREATE INDEX [IX_AuditLog_FirmID] ON [Audit_Log]([FirmID]);
GO

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_AuditLog_Timestamp')
    CREATE INDEX [IX_AuditLog_Timestamp] ON [Audit_Log]([Timestamp]);
GO

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_AuditLog_Action')
    CREATE INDEX [IX_AuditLog_Action] ON [Audit_Log]([Action]);
GO

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_AuditLog_ActionCategory')
    CREATE INDEX [IX_AuditLog_ActionCategory] ON [Audit_Log]([ActionCategory]);
GO

-- FirmSubscription indexes
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_FirmSubscription_FirmID')
    CREATE INDEX [IX_FirmSubscription_FirmID] ON [FirmSubscription]([FirmID]);
GO

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_FirmSubscription_Status')
    CREATE INDEX [IX_FirmSubscription_Status] ON [FirmSubscription]([Status]);
GO

-- Invoice indexes
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Invoice_SubscriptionID')
    CREATE INDEX [IX_Invoice_SubscriptionID] ON [Invoice]([SubscriptionID]);
GO

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Invoice_Status')
    CREATE INDEX [IX_Invoice_Status] ON [Invoice]([Status]);
GO

-- Payment indexes
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Payment_SubscriptionID')
    CREATE INDEX [IX_Payment_SubscriptionID] ON [Payment]([SubscriptionID]);
GO

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Payment_InvoiceID')
    CREATE INDEX [IX_Payment_InvoiceID] ON [Payment]([InvoiceID]);
GO

PRINT 'All indexes created successfully';
GO

-- =====================================================
-- PART 6: Seed SuperAdmin account (if not exists)
-- =====================================================

-- Check if SuperAdmin exists, if not, seed one
IF NOT EXISTS (SELECT 1 FROM [SuperAdmin])
BEGIN
    -- The password hash below is for: SuperAdmin@123
    -- Hash: PBKDF2 format (salt.hash)
    DECLARE @PasswordHash NVARCHAR(255);
    
    -- Note: This is a placeholder. The actual hash will be generated by the application
    -- Run the app to seed or use: /Auth/GeneratePasswordHash?password=SuperAdmin@123
    SET @PasswordHash = 'PLACEHOLDER_WILL_BE_UPDATED_BY_APP';
    
    INSERT INTO [SuperAdmin] ([Username], [Email], [PasswordHash], [FirstName], [LastName], [PhoneNumber], [Status], [CreatedAt])
    VALUES ('superadmin', 'superadmin@ckn.com', @PasswordHash, 'Super', 'Admin', '09123456789', 'Active', GETDATE());
    
    PRINT 'SuperAdmin seeded. NOTE: Password hash needs to be updated by running the application or using /Auth/GeneratePasswordHash';
END
ELSE
BEGIN
    PRINT 'SuperAdmin already exists, skipping seed';
END
GO

-- =====================================================
-- PART 7: Create demo FirmSubscription (if firm exists but no subscription)
-- =====================================================

IF EXISTS (SELECT 1 FROM [Firm]) AND NOT EXISTS (SELECT 1 FROM [FirmSubscription])
BEGIN
    DECLARE @FirmId INT;
    SELECT TOP 1 @FirmId = [FirmID] FROM [Firm];
    
    INSERT INTO [FirmSubscription] ([FirmID], [SubscriptionName], [ContactEmail], [Status], [PlanType], [StartDate], [EndDate], [CreatedAt])
    SELECT [FirmID], [FirmName] + ' Subscription', [ContactEmail], 'Active', 'Premium', GETDATE(), DATEADD(YEAR, 1, GETDATE()), GETDATE()
    FROM [Firm]
    WHERE [FirmID] = @FirmId;
    
    PRINT 'Demo FirmSubscription created';
END
GO

-- =====================================================
-- PART 8: Verification queries
-- =====================================================

PRINT '';
PRINT '=====================================================';
PRINT 'VERIFICATION - Database Structure';
PRINT '=====================================================';

-- List all tables
PRINT 'Tables in LawFirmDMS database:';
SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE = 'BASE TABLE' ORDER BY TABLE_NAME;

-- Count records in key tables
PRINT '';
PRINT 'Record counts:';
SELECT 'SuperAdmin' AS TableName, COUNT(*) AS RecordCount FROM [SuperAdmin]
UNION ALL
SELECT 'Firm', COUNT(*) FROM [Firm]
UNION ALL
SELECT 'FirmSubscription', COUNT(*) FROM [FirmSubscription]
UNION ALL
SELECT 'User', COUNT(*) FROM [User]
UNION ALL
SELECT 'Role', COUNT(*) FROM [Role]
UNION ALL
SELECT 'Invoice', COUNT(*) FROM [Invoice]
UNION ALL
SELECT 'Payment', COUNT(*) FROM [Payment]
UNION ALL
SELECT 'Expense', COUNT(*) FROM [Expense]
UNION ALL
SELECT 'Audit_Log', COUNT(*) FROM [Audit_Log];

PRINT '';
PRINT '=====================================================';
PRINT 'Migration completed successfully!';
PRINT '=====================================================';
PRINT '';
PRINT 'NEXT STEPS:';
PRINT '1. Run the application to auto-seed users with proper password hashes';
PRINT '2. Or manually update password hashes using /Auth/GeneratePasswordHash endpoint';
PRINT '3. Test login with:';
PRINT '   - SuperAdmin: superadmin / SuperAdmin@123';
PRINT '   - Admin: admin / Admin@123456';
PRINT '   - Staff: staff / Staff@123456';
PRINT '   - Client: client / Client@123456';
PRINT '   - Auditor: auditor / Auditor@12345';
PRINT '';
GO
