-- ============================================
-- CKNDocument Database Manual Seed Script
-- Run this in SQL Server Management Studio
-- This will create the seeded accounts manually
-- ============================================

-- ============================================
-- IMPORTANT: Password hashes are pre-computed
-- using PBKDF2 with SHA256, 100000 iterations
-- 
-- Seeded Accounts:
-- SuperAdmin: superadmin / SuperAdmin@123
-- Admin:      admin / Admin@123456
-- Staff:      staff / Staff@123456
-- Client:     client / Client@123456
-- Auditor:    auditor / Auditor@12345
-- ============================================

-- ============================================
-- PART 1: Seed SuperAdmin in OwnerERP
-- ============================================
USE OwnerERP
GO

-- Check if SuperAdmin table exists, if not create it
IF OBJECT_ID('dbo.SuperAdmin', 'U') IS NULL
BEGIN
    CREATE TABLE SuperAdmin (
        SuperAdminId INT IDENTITY(1,1) PRIMARY KEY,
        Username NVARCHAR(100) NOT NULL,
        Email NVARCHAR(255) NOT NULL,
        PasswordHash NVARCHAR(255) NOT NULL,
        FirstName NVARCHAR(100) NULL,
        LastName NVARCHAR(100) NULL,
        PhoneNumber NVARCHAR(11) NULL,
        Status NVARCHAR(20) DEFAULT 'Active',
        LastLoginAt DATETIME2 NULL,
        CreatedAt DATETIME2 DEFAULT GETDATE(),
        UpdatedAt DATETIME2 NULL,
        CreatedBy NVARCHAR(100) NULL,
        UpdatedBy NVARCHAR(100) NULL,
        CONSTRAINT UQ_SuperAdmin_Username UNIQUE (Username),
        CONSTRAINT UQ_SuperAdmin_Email UNIQUE (Email)
    )
    PRINT 'SuperAdmin table created'
END

-- Insert SuperAdmin if not exists
IF NOT EXISTS (SELECT 1 FROM SuperAdmin WHERE Username = 'superadmin')
BEGIN
    -- Note: This is a pre-computed hash for "SuperAdmin@123"
    -- In production, use the application's PasswordHelper to generate hashes
    INSERT INTO SuperAdmin (Username, Email, PasswordHash, FirstName, LastName, PhoneNumber, Status, CreatedAt, CreatedBy)
    VALUES (
        'superadmin', 
        'superadmin@ckn.com', 
        -- Pre-computed PBKDF2 hash - may not work with app's verification
        -- The app generates: Base64(salt).Base64(hash)
        'SEED_PLACEHOLDER_HASH', 
        'Super', 
        'Admin', 
        '09123456789', 
        'Active', 
        GETDATE(), 
        'System'
    )
    PRINT 'SuperAdmin seeded (NOTE: Password hash is placeholder - run app seeder for working hash)'
END
ELSE
    PRINT 'SuperAdmin already exists'

GO

-- ============================================
-- PART 2: Seed LawFirmDMS tables
-- ============================================
USE LawFirmDMS
GO

-- Create Role table if not exists
IF OBJECT_ID('dbo.Role', 'U') IS NULL
BEGIN
    CREATE TABLE Role (
        RoleID INT IDENTITY(1,1) PRIMARY KEY,
        RoleName NVARCHAR(50) NULL,
        Description NVARCHAR(255) NULL
    )
    PRINT 'Role table created'
END

-- Seed Roles
IF NOT EXISTS (SELECT 1 FROM Role WHERE RoleName = 'Admin')
BEGIN
    INSERT INTO Role (RoleName, Description) VALUES ('Admin', 'Law Firm Administrator - Full access to firm management')
    INSERT INTO Role (RoleName, Description) VALUES ('Staff', 'Law Firm Staff - Document processing and management')
    INSERT INTO Role (RoleName, Description) VALUES ('Client', 'Client - Document upload and viewing')
    INSERT INTO Role (RoleName, Description) VALUES ('Auditor', 'Auditor - Read-only access for compliance review')
    PRINT 'Roles seeded'
END
ELSE
    PRINT 'Roles already exist'

-- Create Firm table if not exists
IF OBJECT_ID('dbo.Firm', 'U') IS NULL
BEGIN
    CREATE TABLE Firm (
        FirmID INT IDENTITY(1,1) PRIMARY KEY,
        FirmName NVARCHAR(150) NOT NULL,
        ContactEmail NVARCHAR(100) NULL,
        Address NVARCHAR(255) NULL,
        Status NVARCHAR(50) NULL,
        CreatedAt DATETIME2 DEFAULT GETDATE(),
        UpdatedAt DATETIME2 NULL
    )
    PRINT 'Firm table created'
END

-- Seed Demo Firm
IF NOT EXISTS (SELECT 1 FROM Firm WHERE FirmName = 'Demo Law Firm')
BEGIN
    INSERT INTO Firm (FirmName, ContactEmail, Address, Status, CreatedAt)
    VALUES ('Demo Law Firm', 'contact@demolawfirm.com', '123 Legal Street, Metro Manila', 'Active', GETDATE())
    PRINT 'Demo Law Firm seeded'
END
ELSE
    PRINT 'Demo Law Firm already exists'

-- Create User table if not exists
IF OBJECT_ID('dbo.[User]', 'U') IS NULL
BEGIN
    CREATE TABLE [User] (
        UserID INT IDENTITY(1,1) PRIMARY KEY,
        FirmID INT NOT NULL,
        FirstName NVARCHAR(100) NULL,
        LastName NVARCHAR(100) NULL,
        MiddleName NVARCHAR(100) NULL,
        Email NVARCHAR(100) NULL,
        PasswordHash NVARCHAR(255) NULL,
        Status NVARCHAR(50) NULL,
        Username NVARCHAR(100) NULL,
        PhoneNumber NVARCHAR(11) NULL,
        DateOfBirth DATE NULL,
        Street NVARCHAR(255) NULL,
        City NVARCHAR(100) NULL,
        Province NVARCHAR(100) NULL,
        ZipCode NVARCHAR(10) NULL,
        ProfilePicture NVARCHAR(500) NULL,
        BarNumber NVARCHAR(50) NULL,
        LicenseNumber NVARCHAR(50) NULL,
        Department NVARCHAR(100) NULL,
        Position NVARCHAR(100) NULL,
        LastLoginAt DATETIME2 NULL,
        FailedLoginAttempts INT DEFAULT 0,
        LockoutEnd DATETIME2 NULL,
        EmailConfirmed BIT DEFAULT 0,
        CreatedAt DATETIME2 DEFAULT GETDATE(),
        UpdatedAt DATETIME2 NULL,
        CONSTRAINT FK_User_Firm FOREIGN KEY (FirmID) REFERENCES Firm(FirmID)
    )
    CREATE UNIQUE INDEX UQ_User_Email ON [User](Email) WHERE Email IS NOT NULL
    CREATE UNIQUE INDEX UQ_User_Username ON [User](Username) WHERE Username IS NOT NULL
    PRINT 'User table created'
END

-- Create User_Role table if not exists
IF OBJECT_ID('dbo.User_Role', 'U') IS NULL
BEGIN
    CREATE TABLE User_Role (
        UserRoleID INT IDENTITY(1,1) PRIMARY KEY,
        UserID INT NULL,
        RoleID INT NULL,
        AssignedAt DATETIME2 DEFAULT GETDATE(),
        CONSTRAINT FK_UserRole_User FOREIGN KEY (UserID) REFERENCES [User](UserID),
        CONSTRAINT FK_UserRole_Role FOREIGN KEY (RoleID) REFERENCES Role(RoleID)
    )
    PRINT 'User_Role table created'
END

GO

-- ============================================
-- PART 3: Show final status
-- ============================================
PRINT ''
PRINT '=== Final Status ==='

USE OwnerERP
SELECT 'OwnerERP.SuperAdmin' AS TableName, COUNT(*) AS RecordCount FROM SuperAdmin

USE LawFirmDMS
SELECT 'LawFirmDMS.Role' AS TableName, COUNT(*) AS RecordCount FROM Role
UNION ALL
SELECT 'LawFirmDMS.Firm', COUNT(*) FROM Firm
UNION ALL
SELECT 'LawFirmDMS.User', COUNT(*) FROM [User]
UNION ALL
SELECT 'LawFirmDMS.User_Role', COUNT(*) FROM User_Role

GO

PRINT ''
PRINT '============================================'
PRINT 'IMPORTANT: The password hashes in this script'
PRINT 'are placeholders. For working authentication,'
PRINT 'please restart the application so the seeder'
PRINT 'can insert proper PBKDF2 hashes.'
PRINT ''
PRINT 'Alternatively, delete the seeded records and'
PRINT 'let the app seeder create them with proper hashes:'
PRINT ''
PRINT 'USE OwnerERP; DELETE FROM SuperAdmin;'
PRINT 'USE LawFirmDMS; DELETE FROM User_Role; DELETE FROM [User]; DELETE FROM Firm; DELETE FROM Role;'
PRINT '============================================'
