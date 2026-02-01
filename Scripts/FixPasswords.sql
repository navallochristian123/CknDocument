-- Run this in SSMS to reset all passwords to simple values

-- Reset SuperAdmin password
USE [OwnerERP]
UPDATE SuperAdmin SET PasswordHash = 'Admin@123456' WHERE SuperAdminId > 0;
SELECT 'SuperAdmin password reset to: Admin@123456' AS Message;
GO

-- Reset User passwords  
USE [LawFirmDMS]
UPDATE [User] SET PasswordHash = 'User@123456' WHERE UserID > 0;
SELECT 'All user passwords reset to: User@123456' AS Message;
GO

-- Show login credentials
SELECT 'LOGIN CREDENTIALS:' AS Info;
SELECT Username, Email, 'Admin@123456' AS Password FROM [OwnerERP].[dbo].[SuperAdmin];
SELECT Username, Email, 'User@123456' AS Password FROM [LawFirmDMS].[dbo].[User];
GO
