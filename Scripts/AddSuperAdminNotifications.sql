-- Add SuperAdminNotification table for SuperAdmin-only notifications
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'SuperAdminNotification')
BEGIN
    CREATE TABLE [dbo].[SuperAdminNotification] (
        [Id] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        [SuperAdminId] INT NOT NULL,
        [Title] NVARCHAR(255) NOT NULL,
        [Message] NVARCHAR(1000) NOT NULL,
        [NotificationType] NVARCHAR(50) NOT NULL,
        [ActionUrl] NVARCHAR(500) NULL,
        [Icon] NVARCHAR(50) NULL,
        [IsRead] BIT NOT NULL DEFAULT 0,
        [ReadAt] DATETIME2 NULL,
        [CreatedAt] DATETIME2 NULL DEFAULT GETDATE(),
        [UpdatedAt] DATETIME2 NULL,
        CONSTRAINT [FK_SuperAdminNotification_SuperAdmin] FOREIGN KEY ([SuperAdminId])
            REFERENCES [dbo].[SuperAdmin]([SuperAdminId]) ON DELETE CASCADE
    );
    
    CREATE INDEX [IX_SuperAdminNotification_SuperAdminId] ON [dbo].[SuperAdminNotification]([SuperAdminId]);
    CREATE INDEX [IX_SuperAdminNotification_IsRead] ON [dbo].[SuperAdminNotification]([IsRead]);
    CREATE INDEX [IX_SuperAdminNotification_CreatedAt] ON [dbo].[SuperAdminNotification]([CreatedAt] DESC);
    
    PRINT 'SuperAdminNotification table created successfully.';
END
ELSE
BEGIN
    PRINT 'SuperAdminNotification table already exists.';
END
