-- =============================================
-- Add Post-Retention Workflow Columns to Archive table
-- CLOUD DATABASE VERSION (Azure SQL / Remote SQL Server)
-- Supports: Legal Hold, Grace Period, Destruction Certificate, Notifications
-- =============================================

-- Legal Hold columns
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Archive' AND COLUMN_NAME = 'IsOnHold')
BEGIN
    ALTER TABLE [Archive] ADD [IsOnHold] BIT NULL DEFAULT 0;
    PRINT 'Added IsOnHold column';
END

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Archive' AND COLUMN_NAME = 'HoldPlacedAt')
BEGIN
    ALTER TABLE [Archive] ADD [HoldPlacedAt] DATETIME NULL;
    PRINT 'Added HoldPlacedAt column';
END

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Archive' AND COLUMN_NAME = 'HoldPlacedBy')
BEGIN
    ALTER TABLE [Archive] ADD [HoldPlacedBy] INT NULL;
    PRINT 'Added HoldPlacedBy column';
END

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Archive' AND COLUMN_NAME = 'HoldReason')
BEGIN
    ALTER TABLE [Archive] ADD [HoldReason] NVARCHAR(500) NULL;
    PRINT 'Added HoldReason column';
END

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Archive' AND COLUMN_NAME = 'HoldReleasedAt')
BEGIN
    ALTER TABLE [Archive] ADD [HoldReleasedAt] DATETIME NULL;
    PRINT 'Added HoldReleasedAt column';
END

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Archive' AND COLUMN_NAME = 'HoldReleasedBy')
BEGIN
    ALTER TABLE [Archive] ADD [HoldReleasedBy] INT NULL;
    PRINT 'Added HoldReleasedBy column';
END

-- Disposition Status
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Archive' AND COLUMN_NAME = 'RetentionDispositionStatus')
BEGIN
    ALTER TABLE [Archive] ADD [RetentionDispositionStatus] NVARCHAR(50) NULL;
    PRINT 'Added RetentionDispositionStatus column';
END

-- Grace Period columns
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Archive' AND COLUMN_NAME = 'GracePeriodStartDate')
BEGIN
    ALTER TABLE [Archive] ADD [GracePeriodStartDate] DATETIME NULL;
    PRINT 'Added GracePeriodStartDate column';
END

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Archive' AND COLUMN_NAME = 'GracePeriodEndDate')
BEGIN
    ALTER TABLE [Archive] ADD [GracePeriodEndDate] DATETIME NULL;
    PRINT 'Added GracePeriodEndDate column';
END

-- Destruction Certificate columns
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Archive' AND COLUMN_NAME = 'HasDestructionCertificate')
BEGIN
    ALTER TABLE [Archive] ADD [HasDestructionCertificate] BIT NULL DEFAULT 0;
    PRINT 'Added HasDestructionCertificate column';
END

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Archive' AND COLUMN_NAME = 'DestructionCertificatePath')
BEGIN
    ALTER TABLE [Archive] ADD [DestructionCertificatePath] NVARCHAR(500) NULL;
    PRINT 'Added DestructionCertificatePath column';
END

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Archive' AND COLUMN_NAME = 'DestroyedAt')
BEGIN
    ALTER TABLE [Archive] ADD [DestroyedAt] DATETIME NULL;
    PRINT 'Added DestroyedAt column';
END

-- Notification tracking columns
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Archive' AND COLUMN_NAME = 'ExpiryNotificationSent')
BEGIN
    ALTER TABLE [Archive] ADD [ExpiryNotificationSent] BIT NULL DEFAULT 0;
    PRINT 'Added ExpiryNotificationSent column';
END

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Archive' AND COLUMN_NAME = 'ExpiryNotifiedAt')
BEGIN
    ALTER TABLE [Archive] ADD [ExpiryNotifiedAt] BIT NULL DEFAULT 0;
    PRINT 'Added ExpiryNotifiedAt column';
END

-- Update existing AutoExpired/Retention archives to have PendingReview status
-- Using EXEC to avoid compile-time column validation error
EXEC sp_executesql N'
UPDATE [Archive]
SET [RetentionDispositionStatus] = ''PendingReview'',
    [GracePeriodStartDate] = [ScheduledDeleteDate],
    [GracePeriodEndDate] = DATEADD(DAY, 30, [ScheduledDeleteDate])
WHERE [ArchiveType] IN (''AutoExpired'', ''Retention'')
  AND ([IsDeleted] IS NULL OR [IsDeleted] != 1)
  AND [RetentionDispositionStatus] IS NULL;
';

PRINT 'Post-Retention Workflow columns added successfully (Cloud Database)';
