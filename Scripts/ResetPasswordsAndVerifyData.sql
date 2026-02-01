-- Reset Passwords and Verify Data Script
-- Run this in SQL Server Management Studio (SSMS)
-- This will reset passwords to a known format that matches the application

USE [OwnerERP]
GO

-- First, let's see what SuperAdmin data exists
SELECT 
    SuperAdminId, 
    Username, 
    Email, 
    FirstName, 
    LastName, 
    Status,
    LEFT(PasswordHash, 50) AS PasswordHashPreview,
    LEN(PasswordHash) AS PasswordHashLength
FROM SuperAdmin;

-- Update SuperAdmin passwords to use plain text temporarily (for testing)
-- The application's PasswordHelper.VerifyPassword supports plain text comparison
-- CHANGE THIS PASSWORD IN PRODUCTION!
UPDATE SuperAdmin 
SET PasswordHash = 'Admin@12345!',
    Status = 'Active'
WHERE SuperAdminId IS NOT NULL;

PRINT 'SuperAdmin passwords reset to: Admin@12345!';
GO

USE [LawFirmDMS]
GO

-- Check Firms
SELECT FirmID, FirmName, Status FROM Firm;

-- Check Roles
SELECT RoleID, RoleName, Description FROM Role;

-- Check Users
SELECT 
    UserID, 
    Username, 
    Email, 
    FirstName, 
    LastName, 
    Status,
    FirmID,
    LEFT(PasswordHash, 50) AS PasswordHashPreview,
    LEN(PasswordHash) AS PasswordHashLength
FROM [User];

-- Check User_Role assignments
SELECT 
    ur.UserRoleID,
    u.Username,
    u.Email,
    r.RoleName
FROM User_Role ur
JOIN [User] u ON ur.UserID = u.UserID
JOIN Role r ON ur.RoleID = r.RoleID;

-- Update all User passwords to use plain text temporarily
-- CHANGE THIS PASSWORD IN PRODUCTION!
UPDATE [User] 
SET PasswordHash = 'User@12345!',
    Status = 'Active'
WHERE UserID IS NOT NULL;

PRINT 'User passwords reset to: User@12345!';
GO

-- ============================================
-- TEST LOGIN CREDENTIALS AFTER RUNNING THIS:
-- ============================================
-- SuperAdmin: Use email or username from SuperAdmin table with password "Admin@12345!"
-- Users: Use email or username from User table with password "User@12345!"
-- ============================================

PRINT 'Password reset complete!';
PRINT '';
PRINT 'Test Credentials:';
PRINT '  SuperAdmin Password: Admin@12345!';
PRINT '  All User Passwords: User@12345!';
GO
