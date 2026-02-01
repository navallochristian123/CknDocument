-- Check the password hash format in the database
USE OwnerERP
GO
SELECT Username, Email, PasswordHash FROM SuperAdmin

USE LawFirmDMS  
GO
SELECT Username, Email, PasswordHash FROM [User]
