-- Add UpdatedAt column to all tables that inherit BaseEntity but are missing it
-- Run this in SQL Server Management Studio connected to your CLOUD database
-- Table names match the [Table("...")] attributes in the C# models

-- ============================================================
-- Document table  [Table("Document")]
-- ============================================================
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Document') AND name = 'UpdatedAt')
BEGIN
    ALTER TABLE [Document] ADD UpdatedAt DATETIME2 NULL;
    PRINT 'Added UpdatedAt to Document';
END
ELSE
    PRINT 'Document already has UpdatedAt';

-- ============================================================
-- User table  [Table("User")]
-- ============================================================
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('User') AND name = 'UpdatedAt')
BEGIN
    ALTER TABLE [User] ADD UpdatedAt DATETIME2 NULL;
    PRINT 'Added UpdatedAt to User';
END
ELSE
    PRINT 'User already has UpdatedAt';

-- ============================================================
-- Firm table  [Table("Firm")]
-- ============================================================
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Firm') AND name = 'UpdatedAt')
BEGIN
    ALTER TABLE [Firm] ADD UpdatedAt DATETIME2 NULL;
    PRINT 'Added UpdatedAt to Firm';
END
ELSE
    PRINT 'Firm already has UpdatedAt';

-- ============================================================
-- DocumentVersion table  [Table("DocumentVersion")]
-- ============================================================
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('DocumentVersion') AND name = 'UpdatedAt')
BEGIN
    ALTER TABLE [DocumentVersion] ADD UpdatedAt DATETIME2 NULL;
    PRINT 'Added UpdatedAt to DocumentVersion';
END
ELSE
    PRINT 'DocumentVersion already has UpdatedAt';

-- ============================================================
-- DocumentSignature table  [Table("DocumentSignature")]
-- ============================================================
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('DocumentSignature') AND name = 'UpdatedAt')
BEGIN
    ALTER TABLE [DocumentSignature] ADD UpdatedAt DATETIME2 NULL;
    PRINT 'Added UpdatedAt to DocumentSignature';
END
ELSE
    PRINT 'DocumentSignature already has UpdatedAt';

-- ============================================================
-- DocumentReview table  [Table("DocumentReview")]
-- ============================================================
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('DocumentReview') AND name = 'UpdatedAt')
BEGIN
    ALTER TABLE [DocumentReview] ADD UpdatedAt DATETIME2 NULL;
    PRINT 'Added UpdatedAt to DocumentReview';
END
ELSE
    PRINT 'DocumentReview already has UpdatedAt';

-- ============================================================
-- DocumentChecklistItem table  [Table("DocumentChecklistItem")]
-- ============================================================
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('DocumentChecklistItem') AND name = 'UpdatedAt')
BEGIN
    ALTER TABLE [DocumentChecklistItem] ADD UpdatedAt DATETIME2 NULL;
    PRINT 'Added UpdatedAt to DocumentChecklistItem';
END
ELSE
    PRINT 'DocumentChecklistItem already has UpdatedAt';

-- ============================================================
-- DocumentAIAnalyses table  [Table("DocumentAIAnalyses")]
-- ============================================================
IF OBJECT_ID('DocumentAIAnalyses', 'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('DocumentAIAnalyses') AND name = 'UpdatedAt')
    BEGIN
        ALTER TABLE [DocumentAIAnalyses] ADD UpdatedAt DATETIME2 NULL;
        PRINT 'Added UpdatedAt to DocumentAIAnalyses';
    END
    ELSE
        PRINT 'DocumentAIAnalyses already has UpdatedAt';
END
ELSE
    PRINT 'DocumentAIAnalyses table does not exist - skipping';

-- ============================================================
-- ClientFolder table  [Table("ClientFolder")]
-- ============================================================
IF OBJECT_ID('ClientFolder', 'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('ClientFolder') AND name = 'UpdatedAt')
    BEGIN
        ALTER TABLE [ClientFolder] ADD UpdatedAt DATETIME2 NULL;
        PRINT 'Added UpdatedAt to ClientFolder';
    END
    ELSE
        PRINT 'ClientFolder already has UpdatedAt';
END
ELSE
    PRINT 'ClientFolder table does not exist - skipping';

-- ============================================================
-- Notification table  [Table("Notification")]
-- ============================================================
IF OBJECT_ID('Notification', 'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Notification') AND name = 'UpdatedAt')
    BEGIN
        ALTER TABLE [Notification] ADD UpdatedAt DATETIME2 NULL;
        PRINT 'Added UpdatedAt to Notification';
    END
    ELSE
        PRINT 'Notification already has UpdatedAt';
END
ELSE
    PRINT 'Notification table does not exist - skipping';

-- ============================================================
-- FirmSubscription table  [Table("FirmSubscription")]
-- ============================================================
IF OBJECT_ID('FirmSubscription', 'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('FirmSubscription') AND name = 'UpdatedAt')
    BEGIN
        ALTER TABLE [FirmSubscription] ADD UpdatedAt DATETIME2 NULL;
        PRINT 'Added UpdatedAt to FirmSubscription';
    END
    ELSE
        PRINT 'FirmSubscription already has UpdatedAt';
END
ELSE
    PRINT 'FirmSubscription table does not exist - skipping';

-- ============================================================
-- SecondOpinionRequest table  [Table("SecondOpinionRequest")]
-- ============================================================
IF OBJECT_ID('SecondOpinionRequest', 'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('SecondOpinionRequest') AND name = 'UpdatedAt')
    BEGIN
        ALTER TABLE [SecondOpinionRequest] ADD UpdatedAt DATETIME2 NULL;
        PRINT 'Added UpdatedAt to SecondOpinionRequest';
    END
    ELSE
        PRINT 'SecondOpinionRequest already has UpdatedAt';
END
ELSE
    PRINT 'SecondOpinionRequest table does not exist - skipping';

-- ============================================================
-- SuperAdmin table  [Table("SuperAdmin")]
-- ============================================================
IF OBJECT_ID('SuperAdmin', 'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('SuperAdmin') AND name = 'UpdatedAt')
    BEGIN
        ALTER TABLE [SuperAdmin] ADD UpdatedAt DATETIME2 NULL;
        PRINT 'Added UpdatedAt to SuperAdmin';
    END
    ELSE
        PRINT 'SuperAdmin already has UpdatedAt';
END
ELSE
    PRINT 'SuperAdmin table does not exist - skipping';

-- ============================================================
-- Payment table  [Table("Payment")]
-- ============================================================
IF OBJECT_ID('Payment', 'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Payment') AND name = 'UpdatedAt')
    BEGIN
        ALTER TABLE [Payment] ADD UpdatedAt DATETIME2 NULL;
        PRINT 'Added UpdatedAt to Payment';
    END
    ELSE
        PRINT 'Payment already has UpdatedAt';
END
ELSE
    PRINT 'Payment table does not exist - skipping';

-- ============================================================
-- Revenue table  [Table("Revenue")]
-- ============================================================
IF OBJECT_ID('Revenue', 'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Revenue') AND name = 'UpdatedAt')
    BEGIN
        ALTER TABLE [Revenue] ADD UpdatedAt DATETIME2 NULL;
        PRINT 'Added UpdatedAt to Revenue';
    END
    ELSE
        PRINT 'Revenue already has UpdatedAt';
END
ELSE
    PRINT 'Revenue table does not exist - skipping';

-- ============================================================
-- Expense table  [Table("Expense")]
-- ============================================================
IF OBJECT_ID('Expense', 'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Expense') AND name = 'UpdatedAt')
    BEGIN
        ALTER TABLE [Expense] ADD UpdatedAt DATETIME2 NULL;
        PRINT 'Added UpdatedAt to Expense';
    END
    ELSE
        PRINT 'Expense already has UpdatedAt';
END
ELSE
    PRINT 'Expense table does not exist - skipping';

-- ============================================================
-- Invoice table  [Table("Invoice")]
-- ============================================================
IF OBJECT_ID('Invoice', 'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Invoice') AND name = 'UpdatedAt')
    BEGIN
        ALTER TABLE [Invoice] ADD UpdatedAt DATETIME2 NULL;
        PRINT 'Added UpdatedAt to Invoice';
    END
    ELSE
        PRINT 'Invoice already has UpdatedAt';
END
ELSE
    PRINT 'Invoice table does not exist - skipping';

PRINT '============================================================';
PRINT 'Done. All UpdatedAt columns are now present.';
PRINT '============================================================';
