-- =============================================
-- Script: AddWorkflowColumns.sql
-- Purpose: Add new columns to support document workflow management
-- Run this script against the LawFirmDMS database
-- =============================================

USE LawFirmDMS;
GO

-- =============================================
-- DOCUMENT TABLE - Add workflow columns if they don't exist
-- =============================================

-- Add CreatedAt column to Document if not exists
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Document') AND name = 'CreatedAt')
BEGIN
    ALTER TABLE dbo.Document ADD CreatedAt DATETIME2 DEFAULT GETUTCDATE();
    PRINT 'Added CreatedAt column to Document table';
END
GO

-- Add UpdatedAt column to Document if not exists
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Document') AND name = 'UpdatedAt')
BEGIN
    ALTER TABLE dbo.Document ADD UpdatedAt DATETIME2 NULL;
    PRINT 'Added UpdatedAt column to Document table';
END
GO

-- Add WorkflowStage column
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Document') AND name = 'WorkflowStage')
BEGIN
    ALTER TABLE dbo.Document ADD WorkflowStage NVARCHAR(50) DEFAULT 'ClientUpload';
    PRINT 'Added WorkflowStage column to Document table';
END
GO

-- Add CurrentRemarks column
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Document') AND name = 'CurrentRemarks')
BEGIN
    ALTER TABLE dbo.Document ADD CurrentRemarks NVARCHAR(MAX) NULL;
    PRINT 'Added CurrentRemarks column to Document table';
END
GO

-- Add AssignedStaffId column
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Document') AND name = 'AssignedStaffId')
BEGIN
    ALTER TABLE dbo.Document ADD AssignedStaffId INT NULL;
    PRINT 'Added AssignedStaffId column to Document table';
END
GO

-- Add AssignedAdminId column
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Document') AND name = 'AssignedAdminId')
BEGIN
    ALTER TABLE dbo.Document ADD AssignedAdminId INT NULL;
    PRINT 'Added AssignedAdminId column to Document table';
END
GO

-- Add DocumentType column
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Document') AND name = 'DocumentType')
BEGIN
    ALTER TABLE dbo.Document ADD DocumentType NVARCHAR(100) NULL;
    PRINT 'Added DocumentType column to Document table';
END
GO

-- Add OriginalFileName column
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Document') AND name = 'OriginalFileName')
BEGIN
    ALTER TABLE dbo.Document ADD OriginalFileName NVARCHAR(500) NULL;
    PRINT 'Added OriginalFileName column to Document table';
END
GO

-- Add FileExtension column
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Document') AND name = 'FileExtension')
BEGIN
    ALTER TABLE dbo.Document ADD FileExtension NVARCHAR(20) NULL;
    PRINT 'Added FileExtension column to Document table';
END
GO

-- Add MimeType column
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Document') AND name = 'MimeType')
BEGIN
    ALTER TABLE dbo.Document ADD MimeType NVARCHAR(100) NULL;
    PRINT 'Added MimeType column to Document table';
END
GO

-- Add TotalFileSize column
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Document') AND name = 'TotalFileSize')
BEGIN
    ALTER TABLE dbo.Document ADD TotalFileSize BIGINT NULL;
    PRINT 'Added TotalFileSize column to Document table';
END
GO

-- Add CurrentVersion column
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Document') AND name = 'CurrentVersion')
BEGIN
    ALTER TABLE dbo.Document ADD CurrentVersion INT DEFAULT 1;
    PRINT 'Added CurrentVersion column to Document table';
END
GO

-- Add IsAIProcessed column
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Document') AND name = 'IsAIProcessed')
BEGIN
    ALTER TABLE dbo.Document ADD IsAIProcessed BIT DEFAULT 0;
    PRINT 'Added IsAIProcessed column to Document table';
END
GO

-- Add IsDuplicate column
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Document') AND name = 'IsDuplicate')
BEGIN
    ALTER TABLE dbo.Document ADD IsDuplicate BIT DEFAULT 0;
    PRINT 'Added IsDuplicate column to Document table';
END
GO

-- Add DuplicateOfDocumentId column
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Document') AND name = 'DuplicateOfDocumentId')
BEGIN
    ALTER TABLE dbo.Document ADD DuplicateOfDocumentId INT NULL;
    PRINT 'Added DuplicateOfDocumentId column to Document table';
END
GO

-- Add StaffReviewedAt column
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Document') AND name = 'StaffReviewedAt')
BEGIN
    ALTER TABLE dbo.Document ADD StaffReviewedAt DATETIME2 NULL;
    PRINT 'Added StaffReviewedAt column to Document table';
END
GO

-- Add AdminReviewedAt column
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Document') AND name = 'AdminReviewedAt')
BEGIN
    ALTER TABLE dbo.Document ADD AdminReviewedAt DATETIME2 NULL;
    PRINT 'Added AdminReviewedAt column to Document table';
END
GO

-- Add ApprovedAt column
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Document') AND name = 'ApprovedAt')
BEGIN
    ALTER TABLE dbo.Document ADD ApprovedAt DATETIME2 NULL;
    PRINT 'Added ApprovedAt column to Document table';
END
GO

-- Add RetentionExpiryDate column
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Document') AND name = 'RetentionExpiryDate')
BEGIN
    ALTER TABLE dbo.Document ADD RetentionExpiryDate DATETIME2 NULL;
    PRINT 'Added RetentionExpiryDate column to Document table';
END
GO

-- Add CreatedBy column (IAuditableEntity)
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Document') AND name = 'CreatedBy')
BEGIN
    ALTER TABLE dbo.Document ADD CreatedBy NVARCHAR(100) NULL;
    PRINT 'Added CreatedBy column to Document table';
END
GO

-- Add UpdatedBy column (IAuditableEntity)
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Document') AND name = 'UpdatedBy')
BEGIN
    ALTER TABLE dbo.Document ADD UpdatedBy NVARCHAR(100) NULL;
    PRINT 'Added UpdatedBy column to Document table';
END
GO

-- =============================================
-- DOCUMENT VERSION TABLE - Ensure structure
-- =============================================

-- Add CreatedAt column to DocumentVersion if not exists
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.DocumentVersion') AND name = 'CreatedAt')
BEGIN
    ALTER TABLE dbo.DocumentVersion ADD CreatedAt DATETIME2 DEFAULT GETUTCDATE();
    PRINT 'Added CreatedAt column to DocumentVersion table';
END
GO

-- Add UpdatedAt column to DocumentVersion if not exists
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.DocumentVersion') AND name = 'UpdatedAt')
BEGIN
    ALTER TABLE dbo.DocumentVersion ADD UpdatedAt DATETIME2 NULL;
    PRINT 'Added UpdatedAt column to DocumentVersion table';
END
GO

-- Add ChangeDescription column to DocumentVersion if not exists
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.DocumentVersion') AND name = 'ChangeDescription')
BEGIN
    ALTER TABLE dbo.DocumentVersion ADD ChangeDescription NVARCHAR(MAX) NULL;
    PRINT 'Added ChangeDescription column to DocumentVersion table';
END
GO

-- Add FileHash column to DocumentVersion if not exists
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.DocumentVersion') AND name = 'FileHash')
BEGIN
    ALTER TABLE dbo.DocumentVersion ADD FileHash NVARCHAR(128) NULL;
    PRINT 'Added FileHash column to DocumentVersion table';
END
GO

-- =============================================
-- DOCUMENT REVIEW TABLE - Ensure structure
-- =============================================

-- Add CreatedAt column to DocumentReview if not exists
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.DocumentReview') AND name = 'CreatedAt')
BEGIN
    ALTER TABLE dbo.DocumentReview ADD CreatedAt DATETIME2 DEFAULT GETUTCDATE();
    PRINT 'Added CreatedAt column to DocumentReview table';
END
GO

-- Add UpdatedAt column to DocumentReview if not exists
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.DocumentReview') AND name = 'UpdatedAt')
BEGIN
    ALTER TABLE dbo.DocumentReview ADD UpdatedAt DATETIME2 NULL;
    PRINT 'Added UpdatedAt column to DocumentReview table';
END
GO

-- Add ReviewerType column to DocumentReview if not exists
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.DocumentReview') AND name = 'ReviewerType')
BEGIN
    ALTER TABLE dbo.DocumentReview ADD ReviewerType NVARCHAR(50) NULL;
    PRINT 'Added ReviewerType column to DocumentReview table';
END
GO

-- =============================================
-- NOTIFICATION TABLE - Ensure structure
-- =============================================

-- Add CreatedAt column to Notification if not exists
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Notification') AND name = 'CreatedAt')
BEGIN
    ALTER TABLE dbo.Notification ADD CreatedAt DATETIME2 DEFAULT GETUTCDATE();
    PRINT 'Added CreatedAt column to Notification table';
END
GO

-- Add UpdatedAt column to Notification if not exists
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Notification') AND name = 'UpdatedAt')
BEGIN
    ALTER TABLE dbo.Notification ADD UpdatedAt DATETIME2 NULL;
    PRINT 'Added UpdatedAt column to Notification table';
END
GO

-- Add Type column to Notification if not exists
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Notification') AND name = 'Type')
BEGIN
    ALTER TABLE dbo.Notification ADD Type NVARCHAR(100) NULL;
    PRINT 'Added Type column to Notification table';
END
GO

-- Add ActionUrl column to Notification if not exists
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Notification') AND name = 'ActionUrl')
BEGIN
    ALTER TABLE dbo.Notification ADD ActionUrl NVARCHAR(500) NULL;
    PRINT 'Added ActionUrl column to Notification table';
END
GO

-- Add DocumentId column to Notification if not exists
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Notification') AND name = 'DocumentId')
BEGIN
    ALTER TABLE dbo.Notification ADD DocumentId INT NULL;
    PRINT 'Added DocumentId column to Notification table';
END
GO

-- =============================================
-- DOCUMENT CHECKLIST ITEM TABLE - Add DocumentType column if missing
-- =============================================

-- Add CreatedAt column to DocumentChecklistItem if not exists
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.DocumentChecklistItem') AND name = 'CreatedAt')
BEGIN
    ALTER TABLE dbo.DocumentChecklistItem ADD CreatedAt DATETIME2 DEFAULT GETUTCDATE();
    PRINT 'Added CreatedAt column to DocumentChecklistItem table';
END
GO

-- Add UpdatedAt column to DocumentChecklistItem if not exists
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.DocumentChecklistItem') AND name = 'UpdatedAt')
BEGIN
    ALTER TABLE dbo.DocumentChecklistItem ADD UpdatedAt DATETIME2 NULL;
    PRINT 'Added UpdatedAt column to DocumentChecklistItem table';
END
GO

-- Add DocumentType column to DocumentChecklistItem if not exists
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.DocumentChecklistItem') AND name = 'DocumentType')
BEGIN
    ALTER TABLE dbo.DocumentChecklistItem ADD DocumentType NVARCHAR(100) NULL;
    PRINT 'Added DocumentType column to DocumentChecklistItem table';
END
GO

-- =============================================
-- DOCUMENT CHECKLIST RESULT TABLE - Ensure structure
-- =============================================

-- Add CreatedAt column to DocumentChecklistResult if not exists
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.DocumentChecklistResult') AND name = 'CreatedAt')
BEGIN
    ALTER TABLE dbo.DocumentChecklistResult ADD CreatedAt DATETIME2 DEFAULT GETUTCDATE();
    PRINT 'Added CreatedAt column to DocumentChecklistResult table';
END
GO

-- Add UpdatedAt column to DocumentChecklistResult if not exists
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.DocumentChecklistResult') AND name = 'UpdatedAt')
BEGIN
    ALTER TABLE dbo.DocumentChecklistResult ADD UpdatedAt DATETIME2 NULL;
    PRINT 'Added UpdatedAt column to DocumentChecklistResult table';
END
GO

-- Add ReviewID column to DocumentChecklistResult if not exists
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.DocumentChecklistResult') AND name = 'ReviewID')
BEGIN
    ALTER TABLE dbo.DocumentChecklistResult ADD ReviewID INT NULL;
    PRINT 'Added ReviewID column to DocumentChecklistResult table';
END
GO

-- Add Notes column to DocumentChecklistResult if not exists
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.DocumentChecklistResult') AND name = 'Notes')
BEGIN
    ALTER TABLE dbo.DocumentChecklistResult ADD Notes NVARCHAR(MAX) NULL;
    PRINT 'Added Notes column to DocumentChecklistResult table';
END
GO

-- =============================================
-- FOREIGN KEY CONSTRAINTS
-- =============================================

-- =============================================
-- CLIENT FOLDER TABLE - Ensure structure
-- =============================================

-- Add CreatedAt column to ClientFolder if not exists
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.ClientFolder') AND name = 'CreatedAt')
BEGIN
    ALTER TABLE dbo.ClientFolder ADD CreatedAt DATETIME2 DEFAULT GETUTCDATE();
    PRINT 'Added CreatedAt column to ClientFolder table';
END
GO

-- Add UpdatedAt column to ClientFolder if not exists
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.ClientFolder') AND name = 'UpdatedAt')
BEGIN
    ALTER TABLE dbo.ClientFolder ADD UpdatedAt DATETIME2 NULL;
    PRINT 'Added UpdatedAt column to ClientFolder table';
END
GO

-- Add FirmId column to ClientFolder if not exists
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.ClientFolder') AND name = 'FirmId')
BEGIN
    ALTER TABLE dbo.ClientFolder ADD FirmId INT NULL;
    PRINT 'Added FirmId column to ClientFolder table';
END
GO

-- =============================================
-- ADD FOREIGN KEY CONSTRAINTS
-- =============================================

-- Add FK for AssignedStaffId if not exists
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_Document_AssignedStaff')
BEGIN
    ALTER TABLE dbo.Document 
    ADD CONSTRAINT FK_Document_AssignedStaff 
    FOREIGN KEY (AssignedStaffId) REFERENCES dbo.[User](UserID);
    PRINT 'Added FK_Document_AssignedStaff constraint';
END
GO

-- Add FK for AssignedAdminId if not exists
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_Document_AssignedAdmin')
BEGIN
    ALTER TABLE dbo.Document 
    ADD CONSTRAINT FK_Document_AssignedAdmin 
    FOREIGN KEY (AssignedAdminId) REFERENCES dbo.[User](UserID);
    PRINT 'Added FK_Document_AssignedAdmin constraint';
END
GO

-- Add FK for DuplicateOfDocumentId if not exists
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_Document_DuplicateOf')
BEGIN
    ALTER TABLE dbo.Document 
    ADD CONSTRAINT FK_Document_DuplicateOf 
    FOREIGN KEY (DuplicateOfDocumentId) REFERENCES dbo.Document(DocumentID);
    PRINT 'Added FK_Document_DuplicateOf constraint';
END
GO

-- Add FK for Notification DocumentId if not exists
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_Notification_Document')
BEGIN
    ALTER TABLE dbo.Notification 
    ADD CONSTRAINT FK_Notification_Document 
    FOREIGN KEY (DocumentId) REFERENCES dbo.Document(DocumentID);
    PRINT 'Added FK_Notification_Document constraint';
END
GO

-- =============================================
-- INDEXES FOR PERFORMANCE
-- =============================================

-- Index on WorkflowStage for filtering
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Document_WorkflowStage')
BEGIN
    CREATE NONCLUSTERED INDEX IX_Document_WorkflowStage 
    ON dbo.Document(WorkflowStage) 
    INCLUDE (FirmID, AssignedStaffId, AssignedAdminId);
    PRINT 'Created IX_Document_WorkflowStage index';
END
GO

-- Index on AssignedStaffId for staff queries
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Document_AssignedStaffId')
BEGIN
    CREATE NONCLUSTERED INDEX IX_Document_AssignedStaffId 
    ON dbo.Document(AssignedStaffId) 
    WHERE AssignedStaffId IS NOT NULL;
    PRINT 'Created IX_Document_AssignedStaffId index';
END
GO

-- Index on AssignedAdminId for admin queries
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Document_AssignedAdminId')
BEGIN
    CREATE NONCLUSTERED INDEX IX_Document_AssignedAdminId 
    ON dbo.Document(AssignedAdminId) 
    WHERE AssignedAdminId IS NOT NULL;
    PRINT 'Created IX_Document_AssignedAdminId index';
END
GO

-- Index on Notification for user queries
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Notification_UserId_IsRead')
BEGIN
    CREATE NONCLUSTERED INDEX IX_Notification_UserId_IsRead 
    ON dbo.Notification(UserId, IsRead) 
    INCLUDE (Type, CreatedAt);
    PRINT 'Created IX_Notification_UserId_IsRead index';
END
GO

-- =============================================
-- SEED DEFAULT CHECKLIST ITEMS (for FirmId = 1)
-- Uses actual column names: ItemName, Description, DisplayOrder, DocumentType
-- =============================================

-- Insert default checklist items for common document types
IF NOT EXISTS (SELECT 1 FROM dbo.DocumentChecklistItem WHERE FirmId = 1)
BEGIN
    INSERT INTO dbo.DocumentChecklistItem (FirmId, ItemName, Description, IsRequired, DisplayOrder, DocumentType, IsActive)
    VALUES 
    -- Contract checklist items
    (1, 'Verify Parties', 'Verify all parties are correctly identified', 1, 1, 'Contract', 1),
    (1, 'Check Dates', 'Check effective date and term', 1, 2, 'Contract', 1),
    (1, 'Review Payment Terms', 'Review payment terms and conditions', 1, 3, 'Contract', 1),
    (1, 'Verify Termination', 'Verify termination clauses', 1, 4, 'Contract', 1),
    (1, 'Check Signatures', 'Check signature requirements', 1, 5, 'Contract', 1),
    (1, 'Review Confidentiality', 'Review confidentiality provisions', 0, 6, 'Contract', 1),
    
    -- Invoice checklist items
    (1, 'Verify Invoice Number', 'Verify invoice number and date', 1, 1, 'Invoice', 1),
    (1, 'Check Billing Details', 'Check billing details', 1, 2, 'Invoice', 1),
    (1, 'Verify Amounts', 'Verify amounts and calculations', 1, 3, 'Invoice', 1),
    (1, 'Confirm Payment Terms', 'Confirm payment terms', 1, 4, 'Invoice', 1),
    
    -- Legal Brief checklist items
    (1, 'Verify Citations', 'Verify case citation accuracy', 1, 1, 'Legal Brief', 1),
    (1, 'Check Formatting', 'Check formatting compliance', 1, 2, 'Legal Brief', 1),
    (1, 'Review Arguments', 'Review legal arguments', 1, 3, 'Legal Brief', 1),
    (1, 'Verify Evidence', 'Verify supporting evidence references', 1, 4, 'Legal Brief', 1),
    
    -- Affidavit checklist items
    (1, 'Verify Affiant', 'Verify affiant identity', 1, 1, 'Affidavit', 1),
    (1, 'Check Notarization', 'Check notarization requirements', 1, 2, 'Affidavit', 1),
    (1, 'Review Statement', 'Review statement accuracy', 1, 3, 'Affidavit', 1),
    
    -- Power of Attorney checklist items
    (1, 'Verify Principal/Agent', 'Verify principal and agent details', 1, 1, 'Power of Attorney', 1),
    (1, 'Check Scope', 'Check scope of authority', 1, 2, 'Power of Attorney', 1),
    (1, 'Verify Duration', 'Verify effective date and duration', 1, 3, 'Power of Attorney', 1),
    (1, 'Review Revocation', 'Review revocation provisions', 1, 4, 'Power of Attorney', 1),
    
    -- General document checklist items
    (1, 'Check Legibility', 'Document is legible and complete', 1, 1, 'General', 1),
    (1, 'Verify Pages', 'All pages are present', 1, 2, 'General', 1),
    (1, 'Check Date', 'Document is properly dated', 1, 3, 'General', 1);
    
    PRINT 'Inserted default checklist items';
END
GO

PRINT '============================================='
PRINT 'Migration script completed successfully!'
PRINT '============================================='
GO
