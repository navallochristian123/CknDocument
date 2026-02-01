-- ============================================
-- CKNDocument Database Diagnostic and Seed Script
-- Run this in SQL Server Management Studio
-- ============================================

-- ============================================
-- PART 1: CHECK DATABASE EXISTENCE
-- ============================================
PRINT '=== Checking Databases ===' 

IF EXISTS (SELECT name FROM sys.databases WHERE name = N'OwnerERP')
    PRINT 'OwnerERP database EXISTS'
ELSE
    PRINT 'OwnerERP database DOES NOT EXIST'

IF EXISTS (SELECT name FROM sys.databases WHERE name = N'LawFirmDMS')
    PRINT 'LawFirmDMS database EXISTS'
ELSE
    PRINT 'LawFirmDMS database DOES NOT EXIST'

GO

-- ============================================
-- PART 2: CHECK OwnerERP TABLES
-- ============================================
USE OwnerERP
GO

PRINT ''
PRINT '=== OwnerERP Tables ===' 

IF OBJECT_ID('dbo.SuperAdmin', 'U') IS NOT NULL
    PRINT 'SuperAdmin table EXISTS'
ELSE
    PRINT 'SuperAdmin table DOES NOT EXIST'

-- Check SuperAdmin count
IF OBJECT_ID('dbo.SuperAdmin', 'U') IS NOT NULL
BEGIN
    DECLARE @SuperAdminCount INT
    SELECT @SuperAdminCount = COUNT(*) FROM SuperAdmin
    PRINT 'SuperAdmin count: ' + CAST(@SuperAdminCount AS VARCHAR(10))
END

GO

-- ============================================
-- PART 3: CHECK LawFirmDMS TABLES
-- ============================================
USE LawFirmDMS
GO

PRINT ''
PRINT '=== LawFirmDMS Tables ===' 

IF OBJECT_ID('dbo.Firm', 'U') IS NOT NULL
    PRINT 'Firm table EXISTS'
ELSE
    PRINT 'Firm table DOES NOT EXIST'

IF OBJECT_ID('dbo.Role', 'U') IS NOT NULL
    PRINT 'Role table EXISTS'
ELSE
    PRINT 'Role table DOES NOT EXIST'

IF OBJECT_ID('dbo.[User]', 'U') IS NOT NULL
    PRINT 'User table EXISTS'
ELSE
    PRINT 'User table DOES NOT EXIST'

IF OBJECT_ID('dbo.User_Role', 'U') IS NOT NULL
    PRINT 'User_Role table EXISTS'
ELSE
    PRINT 'User_Role table DOES NOT EXIST'

-- Check counts
IF OBJECT_ID('dbo.Firm', 'U') IS NOT NULL
BEGIN
    DECLARE @FirmCount INT
    SELECT @FirmCount = COUNT(*) FROM Firm
    PRINT 'Firm count: ' + CAST(@FirmCount AS VARCHAR(10))
END

IF OBJECT_ID('dbo.Role', 'U') IS NOT NULL
BEGIN
    DECLARE @RoleCount INT
    SELECT @RoleCount = COUNT(*) FROM Role
    PRINT 'Role count: ' + CAST(@RoleCount AS VARCHAR(10))
END

IF OBJECT_ID('dbo.[User]', 'U') IS NOT NULL
BEGIN
    DECLARE @UserCount INT
    SELECT @UserCount = COUNT(*) FROM [User]
    PRINT 'User count: ' + CAST(@UserCount AS VARCHAR(10))
END

IF OBJECT_ID('dbo.User_Role', 'U') IS NOT NULL
BEGIN
    DECLARE @UserRoleCount INT
    SELECT @UserRoleCount = COUNT(*) FROM User_Role
    PRINT 'User_Role count: ' + CAST(@UserRoleCount AS VARCHAR(10))
END

GO

-- ============================================
-- PART 4: VIEW EXISTING DATA
-- ============================================
PRINT ''
PRINT '=== Existing Data ===' 

USE OwnerERP
GO

IF OBJECT_ID('dbo.SuperAdmin', 'U') IS NOT NULL
BEGIN
    PRINT ''
    PRINT 'SuperAdmins:'
    SELECT SuperAdminId, Username, Email, FirstName, LastName, Status FROM SuperAdmin
END

USE LawFirmDMS
GO

IF OBJECT_ID('dbo.Role', 'U') IS NOT NULL
BEGIN
    PRINT ''
    PRINT 'Roles:'
    SELECT * FROM Role
END

IF OBJECT_ID('dbo.Firm', 'U') IS NOT NULL
BEGIN
    PRINT ''
    PRINT 'Firms:'
    SELECT * FROM Firm
END

IF OBJECT_ID('dbo.[User]', 'U') IS NOT NULL
BEGIN
    PRINT ''
    PRINT 'Users:'
    SELECT UserID, FirmID, Username, Email, FirstName, LastName, Status FROM [User]
END

IF OBJECT_ID('dbo.User_Role', 'U') IS NOT NULL
BEGIN
    PRINT ''
    PRINT 'User_Roles:'
    SELECT * FROM User_Role
END

GO
