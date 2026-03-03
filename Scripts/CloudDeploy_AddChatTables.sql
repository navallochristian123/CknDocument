-- ============================================
-- Add Chat/LiveChat Tables for CKNDocument
-- CLOUD DEPLOYMENT SCRIPT
-- Safe to run multiple times (idempotent)
-- ============================================

-- ChatConversation table
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'ChatConversation')
BEGIN
    CREATE TABLE [dbo].[ChatConversation] (
        [ConversationID]    INT             IDENTITY(1,1) NOT NULL,
        [FirmID]            INT             NOT NULL,
        [ClientUserID]      INT             NOT NULL,
        [AdminUserID]       INT             NULL,
        [Subject]           NVARCHAR(255)   NULL,
        [Category]          NVARCHAR(50)    NULL,
        [Status]            NVARCHAR(30)    NOT NULL DEFAULT 'Active',
        [ClosedAt]          DATETIME2       NULL,
        [Rating]            INT             NULL,
        [Feedback]          NVARCHAR(500)   NULL,
        [CreatedAt]         DATETIME2       NOT NULL DEFAULT GETDATE(),
        [UpdatedAt]         DATETIME2       NULL,
        CONSTRAINT [PK_ChatConversation] PRIMARY KEY CLUSTERED ([ConversationID]),
        CONSTRAINT [FK_ChatConversation_Firm] FOREIGN KEY ([FirmID]) REFERENCES [dbo].[Firm]([FirmID]),
        CONSTRAINT [FK_ChatConversation_ClientUser] FOREIGN KEY ([ClientUserID]) REFERENCES [dbo].[User]([UserID]),
        CONSTRAINT [FK_ChatConversation_AdminUser] FOREIGN KEY ([AdminUserID]) REFERENCES [dbo].[User]([UserID])
    );

    CREATE INDEX [IX_ChatConversation_FirmID] ON [dbo].[ChatConversation]([FirmID]);
    CREATE INDEX [IX_ChatConversation_ClientUserID] ON [dbo].[ChatConversation]([ClientUserID]);
    CREATE INDEX [IX_ChatConversation_AdminUserID] ON [dbo].[ChatConversation]([AdminUserID]);
    CREATE INDEX [IX_ChatConversation_Status] ON [dbo].[ChatConversation]([Status]);
END
GO

-- ChatMessage table
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'ChatMessage')
BEGIN
    CREATE TABLE [dbo].[ChatMessage] (
        [MessageID]         INT             IDENTITY(1,1) NOT NULL,
        [ConversationID]    INT             NOT NULL,
        [SenderUserID]      INT             NULL,
        [SenderType]        NVARCHAR(20)    NOT NULL DEFAULT 'Client',
        [Content]           NVARCHAR(MAX)   NOT NULL,
        [MessageType]       NVARCHAR(20)    NOT NULL DEFAULT 'Text',
        [IsRead]            BIT             NOT NULL DEFAULT 0,
        [ReadAt]            DATETIME2       NULL,
        [CreatedAt]         DATETIME2       NOT NULL DEFAULT GETDATE(),
        [UpdatedAt]         DATETIME2       NULL,
        CONSTRAINT [PK_ChatMessage] PRIMARY KEY CLUSTERED ([MessageID]),
        CONSTRAINT [FK_ChatMessage_Conversation] FOREIGN KEY ([ConversationID]) REFERENCES [dbo].[ChatConversation]([ConversationID]) ON DELETE CASCADE,
        CONSTRAINT [FK_ChatMessage_SenderUser] FOREIGN KEY ([SenderUserID]) REFERENCES [dbo].[User]([UserID])
    );

    CREATE INDEX [IX_ChatMessage_ConversationID] ON [dbo].[ChatMessage]([ConversationID]);
    CREATE INDEX [IX_ChatMessage_SenderUserID] ON [dbo].[ChatMessage]([SenderUserID]);
    CREATE INDEX [IX_ChatMessage_CreatedAt] ON [dbo].[ChatMessage]([CreatedAt]);
END
GO
