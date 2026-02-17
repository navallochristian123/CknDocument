-- Add MaxStorageMB column to Firm table for subscription storage limits
-- Starter=2048 MB (2GB), Professional=10240 MB (10GB), Enterprise=51200 MB (50GB)

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Firm') AND name = 'MaxStorageMB')
BEGIN
    ALTER TABLE Firm ADD MaxStorageMB BIGINT NOT NULL DEFAULT 2048;
    PRINT 'Added MaxStorageMB column to Firm table';
END
ELSE
BEGIN
    PRINT 'MaxStorageMB column already exists';
END
GO

-- Update existing firms based on their subscription plan
UPDATE f
SET f.MaxStorageMB = CASE 
    WHEN fs.PlanType = 'Starter' THEN 2048
    WHEN fs.PlanType = 'Professional' THEN 10240
    WHEN fs.PlanType = 'Enterprise' THEN 51200
    ELSE 2048
END
FROM Firm f
INNER JOIN FirmSubscription fs ON f.FirmID = fs.FirmID
WHERE fs.Status = 'Active';

PRINT 'Updated MaxStorageMB for existing firms based on subscription plans';
