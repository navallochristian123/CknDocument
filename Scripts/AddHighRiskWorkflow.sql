-- =============================================
-- Add High-Risk Document Workflow Support
-- Adds columns to Document table and creates SecondOpinionRequest table
-- =============================================
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

-- Add high-risk columns to Document table
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Document') AND name = 'IsHighRisk')
BEGIN
    ALTER TABLE [Document] ADD [IsHighRisk] BIT NOT NULL DEFAULT 0;
    PRINT 'Added IsHighRisk column to Document table';
END

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Document') AND name = 'SecondOpinionLawyerId')
BEGIN
    ALTER TABLE [Document] ADD [SecondOpinionLawyerId] INT NULL;
    PRINT 'Added SecondOpinionLawyerId column to Document table';
END

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Document') AND name = 'FirstOpinionLawyerId')
BEGIN
    ALTER TABLE [Document] ADD [FirstOpinionLawyerId] INT NULL;
    PRINT 'Added FirstOpinionLawyerId column to Document table';
END

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Document') AND name = 'SecondOpinionRemarks')
BEGIN
    ALTER TABLE [Document] ADD [SecondOpinionRemarks] NVARCHAR(MAX) NULL;
    PRINT 'Added SecondOpinionRemarks column to Document table';
END

-- Add foreign keys for new Document columns
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_Document_SecondOpinionLawyer')
BEGIN
    ALTER TABLE [Document] ADD CONSTRAINT [FK_Document_SecondOpinionLawyer]
        FOREIGN KEY ([SecondOpinionLawyerId]) REFERENCES [User]([UserID]);
    PRINT 'Added FK_Document_SecondOpinionLawyer';
END

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_Document_FirstOpinionLawyer')
BEGIN
    ALTER TABLE [Document] ADD CONSTRAINT [FK_Document_FirstOpinionLawyer]
        FOREIGN KEY ([FirstOpinionLawyerId]) REFERENCES [User]([UserID]);
    PRINT 'Added FK_Document_FirstOpinionLawyer';
END

-- Create SecondOpinionRequest table
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'SecondOpinionRequest')
BEGIN
    CREATE TABLE [SecondOpinionRequest] (
        [RequestId] INT IDENTITY(1,1) PRIMARY KEY,
        [DocumentId] INT NOT NULL,
        [FirmId] INT NOT NULL,
        [RequestedByLawyerId] INT NOT NULL,
        [AssignedToLawyerId] INT NOT NULL,
        [RequestRemarks] NVARCHAR(MAX) NULL,
        [ResponseRemarks] NVARCHAR(MAX) NULL,
        [Status] NVARCHAR(50) NOT NULL DEFAULT 'Pending',
        [RespondedAt] DATETIME2 NULL,
        [CreatedAt] DATETIME2 DEFAULT GETDATE(),
        [UpdatedAt] DATETIME2 NULL,

        CONSTRAINT [FK_SecondOpinionRequest_Document] FOREIGN KEY ([DocumentId]) REFERENCES [Document]([DocumentID]) ON DELETE CASCADE,
        CONSTRAINT [FK_SecondOpinionRequest_Firm] FOREIGN KEY ([FirmId]) REFERENCES [Firm]([FirmID]),
        CONSTRAINT [FK_SecondOpinionRequest_RequestedBy] FOREIGN KEY ([RequestedByLawyerId]) REFERENCES [User]([UserID]),
        CONSTRAINT [FK_SecondOpinionRequest_AssignedTo] FOREIGN KEY ([AssignedToLawyerId]) REFERENCES [User]([UserID])
    );

    CREATE INDEX [IX_SecondOpinionRequest_DocumentId] ON [SecondOpinionRequest]([DocumentId]);
    CREATE INDEX [IX_SecondOpinionRequest_RequestedByLawyerId] ON [SecondOpinionRequest]([RequestedByLawyerId]);
    CREATE INDEX [IX_SecondOpinionRequest_AssignedToLawyerId] ON [SecondOpinionRequest]([AssignedToLawyerId]);

    PRINT 'Created SecondOpinionRequest table with indexes';
END

PRINT 'High-Risk Document Workflow migration completed successfully';
