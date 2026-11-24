SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

CREATE TABLE dbo.[Bill]
(
    [BillId] uniqueidentifier NOT NULL,
    [RestaurantId] uniqueidentifier NOT NULL,
    [BillNumber] varchar(50) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
    [CustomerRef] uniqueidentifier NULL,
    [Status] varchar(50) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
    [SubTotal] decimal(18,2) NOT NULL,
    [DiscountAmount] decimal(18,2) NOT NULL,
    [VatRateSnapshot] decimal(9,6) NOT NULL,
    [ServiceRateSnapshot] decimal(9,6) NOT NULL,
    [VatAmount] decimal(18,2) NOT NULL,
    [ServiceAmount] decimal(18,2) NOT NULL,
    [TotalAmount] decimal(18,2) NOT NULL,
    [Currency] char(3) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
    [PrebillPrintedAt] datetime NULL,
    [PaidAt] datetime NULL,
    [Version] timestamp NOT NULL
);
GO

CREATE TABLE dbo.[BillAdjustment]
(
    [BillAdjustmentId] uniqueidentifier NOT NULL,
    [BillId] uniqueidentifier NOT NULL,
    [VoucherId] uniqueidentifier NULL,
    [PromotionId] uniqueidentifier NULL,
    [Type] varchar(50) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
    [Amount] decimal(18,2) NOT NULL,
    [RuleSnapshot] nvarchar(max) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
    [ActorUserRef] uniqueidentifier NULL,
    [Reason] nvarchar(1000) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
    [RestaurantId] uniqueidentifier NULL
);
GO

CREATE TABLE dbo.[BillCheckout]
(
    [CheckoutId] uniqueidentifier NOT NULL,
    [BillId] uniqueidentifier NULL,
    [TableSessionRef] uniqueidentifier NOT NULL,
    [SourceHash] char(64) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
    [Status] varchar(20) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
    [CreatedAt] datetime2(7) NOT NULL,
    [ReleasedAt] datetime2(7) NULL,
    [RestaurantId] uniqueidentifier NULL,
    [CashRegisterId] uniqueidentifier NULL,
    [SubmittedAt] datetime2(7) NULL,
    [PreparedByRef] uniqueidentifier NULL,
    [PreparedAt] datetime2(7) NULL,
    [PresentedAt] datetime2(7) NULL
);
GO

CREATE TABLE dbo.[BillLine]
(
    [BillLineId] uniqueidentifier NOT NULL,
    [BillId] uniqueidentifier NOT NULL,
    [OrderItemRef] uniqueidentifier NOT NULL,
    [TableSessionRef] uniqueidentifier NOT NULL,
    [OriginalTableRef] uniqueidentifier NOT NULL,
    [ComboGroupRef] uniqueidentifier NULL,
    [ItemSnapshot] nvarchar(max) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
    [Quantity] decimal(18,6) NOT NULL,
    [UnitPriceSnapshot] decimal(18,2) NOT NULL,
    [DiscountAllocated] decimal(18,2) NOT NULL,
    [VatAllocated] decimal(18,2) NOT NULL,
    [ServiceAllocated] decimal(18,2) NOT NULL,
    [CogsSnapshot] decimal(18,2) NULL,
    [RestaurantId] uniqueidentifier NULL
);
GO

CREATE TABLE dbo.[CashHandover]
(
    [PaymentId] uniqueidentifier NOT NULL,
    [WaiterUserRef] uniqueidentifier NOT NULL,
    [CashierUserRef] uniqueidentifier NULL,
    [CollectedAt] datetime NOT NULL,
    [ConfirmedAt] datetime NULL,
    [ConfirmedAmount] decimal(18,2) NULL,
    [Status] varchar(50) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
    [RestaurantId] uniqueidentifier NULL
);
GO

CREATE TABLE dbo.[CashMovement]
(
    [CashMovementId] uniqueidentifier NOT NULL,
    [CashShiftId] uniqueidentifier NOT NULL,
    [PaymentId] uniqueidentifier NULL,
    [Type] varchar(50) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
    [SignedAmount] decimal(18,2) NOT NULL,
    [Reason] nvarchar(1000) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
    [PostedAt] datetime NOT NULL,
    [IdempotencyKey] varchar(128) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
    [RestaurantId] uniqueidentifier NULL
);
GO

CREATE TABLE dbo.[CashRegister]
(
    [CashRegisterId] uniqueidentifier NOT NULL,
    [RestaurantId] uniqueidentifier NOT NULL,
    [Code] varchar(50) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL
);
GO

CREATE TABLE dbo.[CashShift]
(
    [CashShiftId] uniqueidentifier NOT NULL,
    [CashRegisterId] uniqueidentifier NOT NULL,
    [CashierUserRef] uniqueidentifier NOT NULL,
    [OpeningCash] decimal(18,2) NOT NULL,
    [OpenedAt] datetime NOT NULL,
    [ClosedAt] datetime NULL,
    [Status] varchar(50) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
    [RestaurantId] uniqueidentifier NULL
);
GO

CREATE TABLE dbo.[DiscountLimit]
(
    [DiscountLimitId] uniqueidentifier NOT NULL,
    [RoleRef] uniqueidentifier NULL,
    [UserRef] uniqueidentifier NULL,
    [LimitType] varchar(50) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
    [MaximumValue] decimal(18,2) NOT NULL,
    [EffectiveFrom] datetime NOT NULL,
    [RestaurantId] uniqueidentifier NULL
);
GO

CREATE TABLE dbo.[ElectronicInvoice]
(
    [ElectronicInvoiceId] uniqueidentifier NOT NULL,
    [BillId] uniqueidentifier NOT NULL,
    [CompanyName] nvarchar(255) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
    [TaxCode] varchar(50) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
    [Address] nvarchar(500) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
    [Email] nvarchar(320) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
    [ProviderReference] varchar(255) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
    [Status] varchar(50) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
    [IssuedAt] datetime NULL,
    [RestaurantId] uniqueidentifier NULL
);
GO

CREATE TABLE dbo.[OnlinePayments]
(
    [OrderId] uniqueidentifier NOT NULL,
    [RestaurantId] uniqueidentifier NOT NULL,
    [CustomerUserId] uniqueidentifier NOT NULL,
    [ProviderOrderCode] bigint NOT NULL,
    [Amount] decimal(18,2) NOT NULL,
    [Status] nvarchar(30) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
    [LinkId] nvarchar(100) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
    [CheckoutUrl] nvarchar(2048) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
    [ExpiresAt] datetime2(7) NOT NULL,
    [PaidAt] datetime2(7) NULL,
    [TransactionReference] nvarchar(255) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
    [RefundReason] nvarchar(1000) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
    [RefundReference] nvarchar(255) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
    [RefundEvidence] nvarchar(2048) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
    [RefundVerifiedBy] uniqueidentifier NULL,
    [RefundedAt] datetime2(7) NULL
);
GO

CREATE TABLE dbo.[Payment]
(
    [PaymentId] uniqueidentifier NOT NULL,
    [BillId] uniqueidentifier NOT NULL,
    [CashShiftId] uniqueidentifier NOT NULL,
    [MethodCode] varchar(50) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
    [Status] varchar(50) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
    [Amount] decimal(18,2) NOT NULL,
    [CashReceived] decimal(18,2) NULL,
    [ChangeAmount] decimal(18,2) NULL,
    [ProviderPaymentLinkId] varchar(100) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
    [ProviderCheckoutUrl] nvarchar(2048) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
    [ProviderQrCode] nvarchar(max) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
    [ProviderOrderCode] varchar(100) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
    [ProviderTransactionId] varchar(255) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
    [IdempotencyKey] varchar(128) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
    [ExpiredAt] datetime NULL,
    [PaidAt] datetime NULL,
    [VerificationSource] varchar(50) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
    [ConfirmedByRef] uniqueidentifier NULL,
    [EvidenceUrl] nvarchar(2048) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
    [RestaurantId] uniqueidentifier NULL
);
GO

CREATE TABLE dbo.[PaymentMethod]
(
    [MethodCode] varchar(50) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
    [IsEnabled] bit NOT NULL
);
GO

CREATE TABLE dbo.[PaymentProviderConfig]
(
    [Enabled] bit NOT NULL,
    [ReturnUrl] nvarchar(2048) NOT NULL,
    [CancelUrl] nvarchar(2048) NOT NULL,
    [PaymentProviderConfigId] uniqueidentifier NOT NULL,
    [Provider] varchar(50) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
    [ClientId] varchar(255) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
    [SecretReference] varchar(max) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
    [WebhookUrl] nvarchar(2048) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
    [IsVerified] bit NOT NULL,
    [RestaurantId] uniqueidentifier NOT NULL
);
GO

CREATE TABLE dbo.[PaymentWebhook]
(
    [PaymentWebhookId] uniqueidentifier NOT NULL,
    [PaymentId] uniqueidentifier NULL,
    [Provider] varchar(50) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
    [DedupeKey] varchar(128) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
    [PayloadHash] varchar(64) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
    [SignatureValid] bit NOT NULL,
    [ReceivedAt] datetime NOT NULL,
    [ProcessedAt] datetime NULL,
    [ProcessingStatus] varchar(50) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
    [RestaurantId] uniqueidentifier NULL
);
GO


CREATE TABLE dbo.[Promotion]
(
    [PromotionId] uniqueidentifier NOT NULL,
    [PromotionName] nvarchar(255) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
    [DiscountType] varchar(50) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
    [Type] varchar(50) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
    [DiscountValue] decimal(18,2) NOT NULL,
    [UsageLimit] int NULL,
    [MinimumSpend] decimal(18,2) NULL,
    [StartAt] datetime NOT NULL,
    [EndAt] datetime NULL,
    [IsActive] bit NOT NULL,
    [RestaurantId] uniqueidentifier NULL
);
GO

CREATE TABLE dbo.[PromotionTarget]
(
    [PromotionTargetId] uniqueidentifier NOT NULL,
    [PromotionId] uniqueidentifier NOT NULL,
    [ItemRef] uniqueidentifier NULL,
    [CategoryRef] uniqueidentifier NULL,
    [Role] varchar(50) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
    [RequiredQuantity] decimal(18,6) NOT NULL,
    [RestaurantId] uniqueidentifier NULL
);
GO

CREATE TABLE dbo.[ReceiptPrint]
(
    [ReceiptPrintId] uniqueidentifier NOT NULL,
    [BillId] uniqueidentifier NOT NULL,
    [Type] varchar(50) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
    [CopyNumber] int NOT NULL,
    [PrintedByRef] uniqueidentifier NOT NULL,
    [PrintedAt] datetime NOT NULL,
    [RestaurantId] uniqueidentifier NULL
);
GO

CREATE TABLE dbo.[Refund]
(
    [RefundId] uniqueidentifier NOT NULL,
    [PaymentId] uniqueidentifier NOT NULL,
    [Amount] decimal(18,2) NOT NULL,
    [Status] varchar(50) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
    [Reason] nvarchar(1000) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
    [IdempotencyKey] varchar(128) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
    [CompletedAt] datetime NULL,
    [RestaurantId] uniqueidentifier NULL
);
GO

CREATE TABLE dbo.[RefundLine]
(
    [RefundLineId] uniqueidentifier NOT NULL,
    [BillLineId] uniqueidentifier NOT NULL,
    [RefundId] uniqueidentifier NOT NULL,
    [Amount] decimal(18,2) NOT NULL,
    [Quantity] decimal(18,6) NOT NULL,
    [RestaurantId] uniqueidentifier NULL
);
GO

CREATE TABLE dbo.[ShiftReport]
(
    [ShiftReportId] uniqueidentifier NOT NULL,
    [CashShiftId] uniqueidentifier NOT NULL,
    [ExpectedCash] decimal(18,2) NOT NULL,
    [ActualCash] decimal(18,2) NOT NULL,
    [CashHandleOver] decimal(18,2) NOT NULL,
    [DifferenceReason] nvarchar(1000) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
    [ClosedAt] datetime NOT NULL,
    [RestaurantId] uniqueidentifier NULL
);
GO

CREATE TABLE dbo.[TaxFeePolicy]
(
    [TaxFeePolicyId] uniqueidentifier NOT NULL,
    [Version] int NOT NULL,
    [VatRate] decimal(9,6) NOT NULL,
    [ServiceRate] decimal(9,6) NOT NULL,
    [ServiceEnabled] bit NOT NULL,
    [EffectiveFrom] datetime NOT NULL,
    [RestaurantId] uniqueidentifier NULL
);
GO

CREATE TABLE dbo.[Voucher]
(
    [VoucherId] uniqueidentifier NOT NULL,
    [NormalizedVoucherCode] varchar(100) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
    [DiscountType] varchar(50) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
    [DiscountValue] decimal(18,2) NOT NULL,
    [MaximumDiscount] decimal(18,2) NULL,
    [UsageLimit] int NULL,
    [StartAt] datetime NOT NULL,
    [EndAt] datetime NULL,
    [IsActive] bit NOT NULL,
    [Version] timestamp NOT NULL,
    [RestaurantId] uniqueidentifier NULL
);
GO

CREATE TABLE dbo.[VoucherRedemption]
(
    [VoucherRedemptionId] uniqueidentifier NOT NULL,
    [VoucherId] uniqueidentifier NOT NULL,
    [BillId] uniqueidentifier NOT NULL,
    [Status] varchar(50) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
    [ReservedUntil] datetime NULL,
    [CommittedAt] datetime NULL,
    [RestaurantId] uniqueidentifier NULL
);
GO

CREATE NONCLUSTERED INDEX [IX_Bill_RestaurantIsolation] ON dbo.[Bill] ([RestaurantId] ASC);
GO
ALTER TABLE dbo.[Bill] ADD CONSTRAINT [PK_Bill] PRIMARY KEY CLUSTERED ([BillId] ASC);
GO

CREATE NONCLUSTERED INDEX [IX_BillAdjustment_RestaurantIsolation] ON dbo.[BillAdjustment] ([RestaurantId] ASC);
GO
ALTER TABLE dbo.[BillAdjustment] ADD CONSTRAINT [PK_BillAdjustment] PRIMARY KEY CLUSTERED ([BillAdjustmentId] ASC);
GO

CREATE NONCLUSTERED INDEX [IX_BillCheckout_RestaurantIsolation] ON dbo.[BillCheckout] ([RestaurantId] ASC);
GO
ALTER TABLE dbo.[BillCheckout] ADD CONSTRAINT [PK_BillCheckout] PRIMARY KEY CLUSTERED ([CheckoutId] ASC);
GO
CREATE UNIQUE NONCLUSTERED INDEX [UX_BillCheckout_ActiveSession] ON dbo.[BillCheckout] ([TableSessionRef] ASC) WHERE ([ReleasedAt] IS NULL);
GO

CREATE NONCLUSTERED INDEX [IX_BillLine_RestaurantIsolation] ON dbo.[BillLine] ([RestaurantId] ASC);
GO
ALTER TABLE dbo.[BillLine] ADD CONSTRAINT [PK_BillLine] PRIMARY KEY CLUSTERED ([BillLineId] ASC);
GO

CREATE NONCLUSTERED INDEX [IX_CashHandover_RestaurantIsolation] ON dbo.[CashHandover] ([RestaurantId] ASC);
GO
ALTER TABLE dbo.[CashHandover] ADD CONSTRAINT [PK_CashHandover] PRIMARY KEY CLUSTERED ([PaymentId] ASC);
GO

CREATE NONCLUSTERED INDEX [IX_CashMovement_RestaurantIsolation] ON dbo.[CashMovement] ([RestaurantId] ASC);
GO
ALTER TABLE dbo.[CashMovement] ADD CONSTRAINT [PK_CashMovement] PRIMARY KEY CLUSTERED ([CashMovementId] ASC);
GO

CREATE UNIQUE NONCLUSTERED INDEX [UX_CashRegister_Restaurant] ON dbo.[CashRegister] ([RestaurantId] ASC);
GO

CREATE NONCLUSTERED INDEX [IX_CashRegister_RestaurantIsolation] ON dbo.[CashRegister] ([RestaurantId] ASC);
GO
ALTER TABLE dbo.[CashRegister] ADD CONSTRAINT [PK_CashRegister] PRIMARY KEY CLUSTERED ([CashRegisterId] ASC);
GO

CREATE NONCLUSTERED INDEX [IX_CashShift_RestaurantIsolation] ON dbo.[CashShift] ([RestaurantId] ASC);
GO
ALTER TABLE dbo.[CashShift] ADD CONSTRAINT [PK_CashShift] PRIMARY KEY CLUSTERED ([CashShiftId] ASC);
GO

CREATE NONCLUSTERED INDEX [IX_DiscountLimit_RestaurantIsolation] ON dbo.[DiscountLimit] ([RestaurantId] ASC);
GO
ALTER TABLE dbo.[DiscountLimit] ADD CONSTRAINT [PK_DiscountLimit] PRIMARY KEY CLUSTERED ([DiscountLimitId] ASC);
GO

CREATE NONCLUSTERED INDEX [IX_ElectronicInvoice_RestaurantIsolation] ON dbo.[ElectronicInvoice] ([RestaurantId] ASC);
GO
ALTER TABLE dbo.[ElectronicInvoice] ADD CONSTRAINT [PK_ElectronicInvoice] PRIMARY KEY CLUSTERED ([ElectronicInvoiceId] ASC);
GO

CREATE UNIQUE NONCLUSTERED INDEX [IX_OnlinePayments_ProviderOrderCode] ON dbo.[OnlinePayments] ([ProviderOrderCode] ASC);
GO
CREATE UNIQUE NONCLUSTERED INDEX [IX_OnlinePayments_RefundReference] ON dbo.[OnlinePayments] ([RefundReference] ASC) WHERE ([RefundReference] IS NOT NULL);
GO
CREATE NONCLUSTERED INDEX [IX_OnlinePayments_RestaurantIsolation] ON dbo.[OnlinePayments] ([RestaurantId] ASC);
GO
CREATE UNIQUE NONCLUSTERED INDEX [IX_OnlinePayments_TransactionReference] ON dbo.[OnlinePayments] ([TransactionReference] ASC) WHERE ([TransactionReference] IS NOT NULL);
GO
ALTER TABLE dbo.[OnlinePayments] ADD CONSTRAINT [PK__OnlinePa__C3905BCF94488C05] PRIMARY KEY CLUSTERED ([OrderId] ASC);
GO

CREATE NONCLUSTERED INDEX [IX_Payment_RestaurantIsolation] ON dbo.[Payment] ([RestaurantId] ASC);
GO
ALTER TABLE dbo.[Payment] ADD CONSTRAINT [PK_Payment] PRIMARY KEY CLUSTERED ([PaymentId] ASC);
GO

ALTER TABLE dbo.[PaymentMethod] ADD CONSTRAINT [PK_PaymentMethod] PRIMARY KEY CLUSTERED ([MethodCode] ASC);
GO

CREATE NONCLUSTERED INDEX [IX_PaymentProviderConfig_RestaurantIsolation] ON dbo.[PaymentProviderConfig] ([RestaurantId] ASC);
GO
ALTER TABLE dbo.[PaymentProviderConfig] ADD CONSTRAINT [PK_PaymentProviderConfig] PRIMARY KEY CLUSTERED ([PaymentProviderConfigId] ASC);
GO

CREATE NONCLUSTERED INDEX [IX_PaymentWebhook_RestaurantIsolation] ON dbo.[PaymentWebhook] ([RestaurantId] ASC);
GO
ALTER TABLE dbo.[PaymentWebhook] ADD CONSTRAINT [PK_PaymentWebhook] PRIMARY KEY CLUSTERED ([PaymentWebhookId] ASC);
GO


CREATE NONCLUSTERED INDEX [IX_Promotion_RestaurantIsolation] ON dbo.[Promotion] ([RestaurantId] ASC);
GO
ALTER TABLE dbo.[Promotion] ADD CONSTRAINT [PK_Promotion] PRIMARY KEY CLUSTERED ([PromotionId] ASC);
GO

CREATE NONCLUSTERED INDEX [IX_PromotionTarget_RestaurantIsolation] ON dbo.[PromotionTarget] ([RestaurantId] ASC);
GO
ALTER TABLE dbo.[PromotionTarget] ADD CONSTRAINT [PK_PromotionTarget] PRIMARY KEY CLUSTERED ([PromotionTargetId] ASC);
GO

CREATE NONCLUSTERED INDEX [IX_ReceiptPrint_RestaurantIsolation] ON dbo.[ReceiptPrint] ([RestaurantId] ASC);
GO
ALTER TABLE dbo.[ReceiptPrint] ADD CONSTRAINT [PK_ReceiptPrint] PRIMARY KEY CLUSTERED ([ReceiptPrintId] ASC);
GO

CREATE NONCLUSTERED INDEX [IX_Refund_RestaurantIsolation] ON dbo.[Refund] ([RestaurantId] ASC);
GO
ALTER TABLE dbo.[Refund] ADD CONSTRAINT [PK_Refund] PRIMARY KEY CLUSTERED ([RefundId] ASC);
GO

CREATE NONCLUSTERED INDEX [IX_RefundLine_RestaurantIsolation] ON dbo.[RefundLine] ([RestaurantId] ASC);
GO
ALTER TABLE dbo.[RefundLine] ADD CONSTRAINT [PK_RefundLine] PRIMARY KEY CLUSTERED ([RefundLineId] ASC);
GO

CREATE NONCLUSTERED INDEX [IX_ShiftReport_RestaurantIsolation] ON dbo.[ShiftReport] ([RestaurantId] ASC);
GO
ALTER TABLE dbo.[ShiftReport] ADD CONSTRAINT [PK_ShiftReport] PRIMARY KEY CLUSTERED ([ShiftReportId] ASC);
GO
ALTER TABLE dbo.[ShiftReport] ADD CONSTRAINT [UQ_ShiftReport_CashShiftId] UNIQUE NONCLUSTERED ([CashShiftId] ASC);
GO

CREATE NONCLUSTERED INDEX [IX_TaxFeePolicy_RestaurantIsolation] ON dbo.[TaxFeePolicy] ([RestaurantId] ASC);
GO
ALTER TABLE dbo.[TaxFeePolicy] ADD CONSTRAINT [PK_TaxFeePolicy] PRIMARY KEY CLUSTERED ([TaxFeePolicyId] ASC);
GO

CREATE NONCLUSTERED INDEX [IX_Voucher_RestaurantIsolation] ON dbo.[Voucher] ([RestaurantId] ASC);
GO
ALTER TABLE dbo.[Voucher] ADD CONSTRAINT [PK_Voucher] PRIMARY KEY CLUSTERED ([VoucherId] ASC);
GO

CREATE NONCLUSTERED INDEX [IX_VoucherRedemption_RestaurantIsolation] ON dbo.[VoucherRedemption] ([RestaurantId] ASC);
GO
ALTER TABLE dbo.[VoucherRedemption] ADD CONSTRAINT [PK_VoucherRedemption] PRIMARY KEY CLUSTERED ([VoucherRedemptionId] ASC);
GO

ALTER TABLE dbo.[BillAdjustment] WITH CHECK ADD CONSTRAINT [FK_BillAdjustment_Bill] FOREIGN KEY ([BillId]) REFERENCES dbo.[Bill] ([BillId]);
GO
ALTER TABLE dbo.[BillAdjustment] WITH CHECK ADD CONSTRAINT [FK_BillAdjustment_Promotion] FOREIGN KEY ([PromotionId]) REFERENCES dbo.[Promotion] ([PromotionId]);
GO
ALTER TABLE dbo.[BillAdjustment] WITH CHECK ADD CONSTRAINT [FK_BillAdjustment_Voucher] FOREIGN KEY ([VoucherId]) REFERENCES dbo.[Voucher] ([VoucherId]);
GO
ALTER TABLE dbo.[BillCheckout] WITH CHECK ADD CONSTRAINT [FK_BillCheckout_CashRegister] FOREIGN KEY ([CashRegisterId]) REFERENCES dbo.[CashRegister] ([CashRegisterId]);
GO

ALTER TABLE dbo.[BillCheckout] WITH CHECK ADD CONSTRAINT [FK_BillCheckout_Bill] FOREIGN KEY ([BillId]) REFERENCES dbo.[Bill] ([BillId]);
GO
ALTER TABLE dbo.[BillLine] WITH CHECK ADD CONSTRAINT [FK_BillLine_Bill] FOREIGN KEY ([BillId]) REFERENCES dbo.[Bill] ([BillId]);
GO
ALTER TABLE dbo.[CashHandover] WITH CHECK ADD CONSTRAINT [FK_CashHandover_Payment] FOREIGN KEY ([PaymentId]) REFERENCES dbo.[Payment] ([PaymentId]);
GO
ALTER TABLE dbo.[CashMovement] WITH CHECK ADD CONSTRAINT [FK_CashMovement_CashShift] FOREIGN KEY ([CashShiftId]) REFERENCES dbo.[CashShift] ([CashShiftId]);
GO
ALTER TABLE dbo.[CashMovement] WITH CHECK ADD CONSTRAINT [FK_CashMovement_Payment] FOREIGN KEY ([PaymentId]) REFERENCES dbo.[Payment] ([PaymentId]);
GO
ALTER TABLE dbo.[CashShift] WITH CHECK ADD CONSTRAINT [FK_CashShift_CashRegister] FOREIGN KEY ([CashRegisterId]) REFERENCES dbo.[CashRegister] ([CashRegisterId]);
GO
ALTER TABLE dbo.[ElectronicInvoice] WITH CHECK ADD CONSTRAINT [FK_ElectronicInvoice_Bill] FOREIGN KEY ([BillId]) REFERENCES dbo.[Bill] ([BillId]);
GO
ALTER TABLE dbo.[Payment] WITH CHECK ADD CONSTRAINT [FK_Payment_Bill] FOREIGN KEY ([BillId]) REFERENCES dbo.[Bill] ([BillId]);
GO
ALTER TABLE dbo.[Payment] WITH CHECK ADD CONSTRAINT [FK_Payment_CashShift] FOREIGN KEY ([CashShiftId]) REFERENCES dbo.[CashShift] ([CashShiftId]);
GO
ALTER TABLE dbo.[Payment] WITH CHECK ADD CONSTRAINT [FK_Payment_PaymentMethod] FOREIGN KEY ([MethodCode]) REFERENCES dbo.[PaymentMethod] ([MethodCode]);
GO
ALTER TABLE dbo.[PaymentWebhook] WITH CHECK ADD CONSTRAINT [FK_PaymentWebhook_Payment] FOREIGN KEY ([PaymentId]) REFERENCES dbo.[Payment] ([PaymentId]);
GO
ALTER TABLE dbo.[PromotionTarget] WITH CHECK ADD CONSTRAINT [FK_PromotionTarget_Promotion] FOREIGN KEY ([PromotionId]) REFERENCES dbo.[Promotion] ([PromotionId]);
GO
ALTER TABLE dbo.[ReceiptPrint] WITH CHECK ADD CONSTRAINT [FK_ReceiptPrint_Bill] FOREIGN KEY ([BillId]) REFERENCES dbo.[Bill] ([BillId]);
GO
ALTER TABLE dbo.[Refund] WITH CHECK ADD CONSTRAINT [FK_Refund_Payment] FOREIGN KEY ([PaymentId]) REFERENCES dbo.[Payment] ([PaymentId]);
GO
ALTER TABLE dbo.[RefundLine] WITH CHECK ADD CONSTRAINT [FK_RefundLine_BillLine] FOREIGN KEY ([BillLineId]) REFERENCES dbo.[BillLine] ([BillLineId]);
GO
ALTER TABLE dbo.[RefundLine] WITH CHECK ADD CONSTRAINT [FK_RefundLine_Refund] FOREIGN KEY ([RefundId]) REFERENCES dbo.[Refund] ([RefundId]);
GO
ALTER TABLE dbo.[ShiftReport] WITH CHECK ADD CONSTRAINT [FK_ShiftReport_CashShift] FOREIGN KEY ([CashShiftId]) REFERENCES dbo.[CashShift] ([CashShiftId]);
GO
ALTER TABLE dbo.[VoucherRedemption] WITH CHECK ADD CONSTRAINT [FK_VoucherRedemption_Bill] FOREIGN KEY ([BillId]) REFERENCES dbo.[Bill] ([BillId]);
GO
ALTER TABLE dbo.[VoucherRedemption] WITH CHECK ADD CONSTRAINT [FK_VoucherRedemption_Voucher] FOREIGN KEY ([VoucherId]) REFERENCES dbo.[Voucher] ([VoucherId]);
GO

CREATE UNIQUE INDEX [IX_PaymentProviderConfig_RestaurantId_Provider] ON dbo.[PaymentProviderConfig] ([RestaurantId], [Provider]);
GO

CREATE TABLE dbo.NotificationOutbox (
    NotificationOutboxId uniqueidentifier NOT NULL CONSTRAINT PK_NotificationOutbox PRIMARY KEY,
    RestaurantId uniqueidentifier NOT NULL,
    EventKey nvarchar(200) NOT NULL,
    Payload nvarchar(max) NOT NULL,
    CreatedAt datetime2 NOT NULL,
    NextAttemptAt datetime2 NOT NULL,
    ProcessedAt datetime2 NULL,
    Attempts int NOT NULL,
    LastError nvarchar(100) NULL
);
CREATE UNIQUE INDEX IX_NotificationOutbox_EventKey ON dbo.NotificationOutbox(EventKey);
CREATE INDEX IX_NotificationOutbox_ProcessedAt_NextAttemptAt ON dbo.NotificationOutbox(ProcessedAt, NextAttemptAt);
GO
