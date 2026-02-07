-- =====================================================
-- Payment System Migration Script for CKN Document
-- Adds PayMongo integration fields and tax columns
-- Run this against the LawFirmDMS database
-- =====================================================

USE LawFirmDMS;
GO

-- =====================================================
-- 1. Add PayMongo columns to Payment table
-- =====================================================
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Payment') AND name = 'TaxAmount')
BEGIN
    ALTER TABLE Payment ADD TaxAmount DECIMAL(12,2) NULL;
    PRINT 'Added TaxAmount to Payment';
END
GO

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Payment') AND name = 'NetAmount')
BEGIN
    ALTER TABLE Payment ADD NetAmount DECIMAL(12,2) NULL;
    PRINT 'Added NetAmount to Payment';
END
GO

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Payment') AND name = 'PayMongoPaymentIntentId')
BEGIN
    ALTER TABLE Payment ADD PayMongoPaymentIntentId NVARCHAR(255) NULL;
    PRINT 'Added PayMongoPaymentIntentId to Payment';
END
GO

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Payment') AND name = 'PayMongoPaymentId')
BEGIN
    ALTER TABLE Payment ADD PayMongoPaymentId NVARCHAR(255) NULL;
    PRINT 'Added PayMongoPaymentId to Payment';
END
GO

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Payment') AND name = 'PayMongoStatus')
BEGIN
    ALTER TABLE Payment ADD PayMongoStatus NVARCHAR(100) NULL;
    PRINT 'Added PayMongoStatus to Payment';
END
GO

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Payment') AND name = 'PayMongoCheckoutUrl')
BEGIN
    ALTER TABLE Payment ADD PayMongoCheckoutUrl NVARCHAR(500) NULL;
    PRINT 'Added PayMongoCheckoutUrl to Payment';
END
GO

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Payment') AND name = 'PayMongoCheckoutSessionId')
BEGIN
    ALTER TABLE Payment ADD PayMongoCheckoutSessionId NVARCHAR(255) NULL;
    PRINT 'Added PayMongoCheckoutSessionId to Payment';
END
GO

-- =====================================================
-- 2. Add Tax/Revenue columns to Revenue table
-- =====================================================
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Revenue') AND name = 'PaymentID')
BEGIN
    ALTER TABLE Revenue ADD PaymentID INT NULL;
    PRINT 'Added PaymentID to Revenue';
END
GO

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Revenue') AND name = 'GrossAmount')
BEGIN
    ALTER TABLE Revenue ADD GrossAmount DECIMAL(12,2) NULL;
    PRINT 'Added GrossAmount to Revenue';
END
GO

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Revenue') AND name = 'TaxAmount')
BEGIN
    ALTER TABLE Revenue ADD TaxAmount DECIMAL(12,2) NULL;
    PRINT 'Added TaxAmount to Revenue';
END
GO

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Revenue') AND name = 'NetAmount')
BEGIN
    ALTER TABLE Revenue ADD NetAmount DECIMAL(12,2) NULL;
    PRINT 'Added NetAmount to Revenue';
END
GO

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Revenue') AND name = 'TaxRate')
BEGIN
    ALTER TABLE Revenue ADD TaxRate DECIMAL(5,2) NULL;
    PRINT 'Added TaxRate to Revenue';
END
GO

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Revenue') AND name = 'Category')
BEGIN
    ALTER TABLE Revenue ADD Category NVARCHAR(50) NULL;
    PRINT 'Added Category to Revenue';
END
GO

-- =====================================================
-- 3. Add Foreign Key for Revenue -> Payment
-- =====================================================
IF NOT EXISTS (SELECT * FROM sys.foreign_keys WHERE name = 'FK_Revenue_Payment')
BEGIN
    ALTER TABLE Revenue ADD CONSTRAINT FK_Revenue_Payment
        FOREIGN KEY (PaymentID) REFERENCES Payment(PaymentID)
        ON DELETE SET NULL;
    PRINT 'Added FK_Revenue_Payment';
END
GO

-- =====================================================
-- 4. Create indexes for performance
-- =====================================================
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Payment_PayMongoCheckoutSessionId')
BEGIN
    CREATE INDEX IX_Payment_PayMongoCheckoutSessionId ON Payment(PayMongoCheckoutSessionId);
    PRINT 'Created index IX_Payment_PayMongoCheckoutSessionId';
END
GO

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Payment_Status_PaymentDate')
BEGIN
    CREATE INDEX IX_Payment_Status_PaymentDate ON Payment(Status, PaymentDate);
    PRINT 'Created index IX_Payment_Status_PaymentDate';
END
GO

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Revenue_RevenueDate')
BEGIN
    CREATE INDEX IX_Revenue_RevenueDate ON Revenue(RevenueDate);
    PRINT 'Created index IX_Revenue_RevenueDate';
END
GO

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Revenue_PaymentID')
BEGIN
    CREATE INDEX IX_Revenue_PaymentID ON Revenue(PaymentID);
    PRINT 'Created index IX_Revenue_PaymentID';
END
GO

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Invoice_Status_DueDate')
BEGIN
    CREATE INDEX IX_Invoice_Status_DueDate ON Invoice(Status, DueDate);
    PRINT 'Created index IX_Invoice_Status_DueDate';
END
GO

-- =====================================================
-- 5. Update existing Payment records with tax info
-- =====================================================
UPDATE Payment SET
    TaxAmount = ROUND(ISNULL(Amount, 0) * 0.12 / 1.12, 2),
    NetAmount = ROUND(ISNULL(Amount, 0) / 1.12, 2)
WHERE TaxAmount IS NULL AND Amount IS NOT NULL;
GO

-- =====================================================
-- 6. Update existing Revenue records with tax info
-- =====================================================
UPDATE Revenue SET
    GrossAmount = ISNULL(Amount, 0),
    TaxAmount = ROUND(ISNULL(Amount, 0) * 0.12 / 1.12, 2),
    NetAmount = ROUND(ISNULL(Amount, 0) / 1.12, 2),
    TaxRate = 12.00,
    Category = ISNULL(Category, 'Monthly')
WHERE GrossAmount IS NULL AND Amount IS NOT NULL;
GO

PRINT '=== Payment System Migration Complete ==='
PRINT 'Environment Variable Required: PAYMONGO_SECRET_KEY'
PRINT 'Set via: [System.Environment]::SetEnvironmentVariable("PAYMONGO_SECRET_KEY", "your_key", "User")'
GO
