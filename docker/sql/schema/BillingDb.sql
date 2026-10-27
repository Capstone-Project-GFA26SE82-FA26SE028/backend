SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[Bill](
	[BillId] [uniqueidentifier] NOT NULL,
	[RestaurantId] [uniqueidentifier] NOT NULL,
	[BillNumber] [varchar](50) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	[CustomerRef] [uniqueidentifier] NULL,
	[Status] [varchar](50) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	[SubTotal] [decimal](18, 2) NOT NULL,
	[DiscountAmount] [decimal](18, 2) NOT NULL,
	[VatRateSnapshot] [decimal](9, 6) NOT NULL,
	[ServiceRateSnapshot] [decimal](9, 6) NOT NULL,
	[VatAmount] [decimal](18, 2) NOT NULL,
	[ServiceAmount] [decimal](18, 2) NOT NULL,
	[TotalAmount] [decimal](18, 2) NOT NULL,
	[Currency] [char](3) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	[PrebillPrintedAt] [datetime] NULL,
	[PaidAt] [datetime] NULL,
	[Version] [timestamp] NOT NULL,
 CONSTRAINT [PK_Bill] PRIMARY KEY CLUSTERED 
(
	[BillId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]

GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[CashRegister](
	[CashRegisterId] [uniqueidentifier] NOT NULL,
	[RestaurantId] [uniqueidentifier] NOT NULL,
	[Code] [varchar](50) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
 CONSTRAINT [PK_CashRegister] PRIMARY KEY CLUSTERED 
(
	[CashRegisterId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]

GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[PaymentProviderConfig](
	[PaymentProviderConfigId] [uniqueidentifier] NOT NULL,
	[Provider] [varchar](50) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	[ClientId] [varchar](255) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	[SecretReference] [varchar](500) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	[WebhookUrl] [varchar](2048) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	[IsVerified] [bit] NOT NULL,
 CONSTRAINT [PK_PaymentProviderConfig] PRIMARY KEY CLUSTERED 
(
	[PaymentProviderConfigId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]

GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[PaymentMethod](
	[MethodCode] [varchar](50) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	[IsEnabled] [bit] NOT NULL,
 CONSTRAINT [PK_PaymentMethod] PRIMARY KEY CLUSTERED 
(
	[MethodCode] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]

GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[Voucher](
	[VoucherId] [uniqueidentifier] NOT NULL,
	[NormalizedVoucherCode] [varchar](100) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	[DiscountType] [varchar](50) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	[DiscountValue] [decimal](18, 2) NOT NULL,
	[MaximumDiscount] [decimal](18, 2) NULL,
	[UsageLimit] [int] NULL,
	[StartAt] [datetime] NOT NULL,
	[EndAt] [datetime] NULL,
	[IsActive] [bit] NOT NULL,
	[Version] [timestamp] NOT NULL,
 CONSTRAINT [PK_Voucher] PRIMARY KEY CLUSTERED 
(
	[VoucherId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]

GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[TaxFeePolicy](
	[TaxFeePolicyId] [uniqueidentifier] NOT NULL,
	[Version] [int] NOT NULL,
	[VatRate] [decimal](9, 6) NOT NULL,
	[ServiceRate] [decimal](9, 6) NOT NULL,
	[ServiceEnabled] [bit] NOT NULL,
	[EffectiveFrom] [datetime] NOT NULL,
 CONSTRAINT [PK_TaxFeePolicy] PRIMARY KEY CLUSTERED 
(
	[TaxFeePolicyId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]

GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[Promotion](
	[PromotionId] [uniqueidentifier] NOT NULL,
	[PromotionName] [varchar](255) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	[DiscountType] [varchar](50) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	[Type] [varchar](50) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	[DiscountValue] [decimal](18, 2) NOT NULL,
	[UsageLimit] [int] NULL,
	[MinimumSpend] [decimal](18, 2) NULL,
	[StartAt] [datetime] NOT NULL,
	[EndAt] [datetime] NULL,
	[IsActive] [bit] NOT NULL,
 CONSTRAINT [PK_Promotion] PRIMARY KEY CLUSTERED 
(
	[PromotionId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]

GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[DiscountLimit](
	[DiscountLimitId] [uniqueidentifier] NOT NULL,
	[RoleRef] [uniqueidentifier] NULL,
	[UserRef] [uniqueidentifier] NULL,
	[LimitType] [varchar](50) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	[MaximumValue] [decimal](18, 2) NOT NULL,
	[EffectiveFrom] [datetime] NOT NULL,
 CONSTRAINT [PK_DiscountLimit] PRIMARY KEY CLUSTERED 
(
	[DiscountLimitId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]

GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[VoucherRedemption](
	[VoucherRedemptionId] [uniqueidentifier] NOT NULL,
	[VoucherId] [uniqueidentifier] NOT NULL,
	[BillId] [uniqueidentifier] NOT NULL,
	[Status] [varchar](50) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	[ReservedUntil] [datetime] NULL,
	[CommittedAt] [datetime] NULL,
 CONSTRAINT [PK_VoucherRedemption] PRIMARY KEY CLUSTERED 
(
	[VoucherRedemptionId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]

GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[ElectronicInvoice](
	[ElectronicInvoiceId] [uniqueidentifier] NOT NULL,
	[BillId] [uniqueidentifier] NOT NULL,
	[CompanyName] [varchar](255) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	[TaxCode] [varchar](50) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	[Address] [varchar](500) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	[Email] [varchar](320) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	[ProviderReference] [varchar](255) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
	[Status] [varchar](50) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	[IssuedAt] [datetime] NULL,
 CONSTRAINT [PK_ElectronicInvoice] PRIMARY KEY CLUSTERED 
(
	[ElectronicInvoiceId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]

GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[CashShift](
	[CashShiftId] [uniqueidentifier] NOT NULL,
	[CashRegisterId] [uniqueidentifier] NOT NULL,
	[CashierUserRef] [uniqueidentifier] NOT NULL,
	[OpeningCash] [decimal](18, 2) NOT NULL,
	[OpenedAt] [datetime] NOT NULL,
	[ClosedAt] [datetime] NULL,
	[Status] [varchar](50) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
 CONSTRAINT [PK_CashShift] PRIMARY KEY CLUSTERED 
(
	[CashShiftId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]

GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[ReceiptPrint](
	[ReceiptPrintId] [uniqueidentifier] NOT NULL,
	[BillId] [uniqueidentifier] NOT NULL,
	[Type] [varchar](50) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	[CopyNumber] [int] NOT NULL,
	[PrintedByRef] [uniqueidentifier] NOT NULL,
	[PrintedAt] [datetime] NOT NULL,
 CONSTRAINT [PK_ReceiptPrint] PRIMARY KEY CLUSTERED 
(
	[ReceiptPrintId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]

GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[PromotionTarget](
	[PromotionTargetId] [uniqueidentifier] NOT NULL,
	[PromotionId] [uniqueidentifier] NOT NULL,
	[ItemRef] [uniqueidentifier] NULL,
	[CategoryRef] [uniqueidentifier] NULL,
	[Role] [varchar](50) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	[RequiredQuantity] [decimal](18, 6) NOT NULL,
 CONSTRAINT [PK_PromotionTarget] PRIMARY KEY CLUSTERED 
(
	[PromotionTargetId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]

GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[BillLine](
	[BillLineId] [uniqueidentifier] NOT NULL,
	[BillId] [uniqueidentifier] NOT NULL,
	[OrderItemRef] [uniqueidentifier] NOT NULL,
	[TableSessionRef] [uniqueidentifier] NOT NULL,
	[OriginalTableRef] [uniqueidentifier] NOT NULL,
	[ComboGroupRef] [uniqueidentifier] NULL,
	[ItemSnapshot] [varchar](max) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	[Quantity] [decimal](18, 6) NOT NULL,
	[UnitPriceSnapshot] [decimal](18, 2) NOT NULL,
	[DiscountAllocated] [decimal](18, 2) NOT NULL,
	[VatAllocated] [decimal](18, 2) NOT NULL,
	[ServiceAllocated] [decimal](18, 2) NOT NULL,
	[CogsSnapshot] [decimal](18, 2) NULL,
 CONSTRAINT [PK_BillLine] PRIMARY KEY CLUSTERED 
(
	[BillLineId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]

GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[BillAdjustment](
	[BillAdjustmentId] [uniqueidentifier] NOT NULL,
	[BillId] [uniqueidentifier] NOT NULL,
	[VoucherId] [uniqueidentifier] NULL,
	[PromotionId] [uniqueidentifier] NULL,
	[Type] [varchar](50) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	[Amount] [decimal](18, 2) NOT NULL,
	[RuleSnapshot] [varchar](max) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
	[ActorUserRef] [uniqueidentifier] NULL,
	[Reason] [varchar](1000) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
 CONSTRAINT [PK_BillAdjustment] PRIMARY KEY CLUSTERED 
(
	[BillAdjustmentId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]

GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[ShiftReport](
	[ShiftReportId] [uniqueidentifier] NOT NULL,
	[CashShiftId] [uniqueidentifier] NOT NULL,
	[ExpectedCash] [decimal](18, 2) NOT NULL,
	[ActualCash] [decimal](18, 2) NOT NULL,
	[CashHandleOver] [decimal](18, 2) NOT NULL,
	[DifferenceReason] [varchar](1000) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
	[ClosedAt] [datetime] NOT NULL,
 CONSTRAINT [PK_ShiftReport] PRIMARY KEY CLUSTERED 
(
	[ShiftReportId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY],
 CONSTRAINT [UQ_ShiftReport_CashShiftId] UNIQUE NONCLUSTERED 
(
	[CashShiftId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]

GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[Payment](
	[PaymentId] [uniqueidentifier] NOT NULL,
	[BillId] [uniqueidentifier] NOT NULL,
	[CashShiftId] [uniqueidentifier] NOT NULL,
	[MethodCode] [varchar](50) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	[Status] [varchar](50) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	[Amount] [decimal](18, 2) NOT NULL,
	[CashReceived] [decimal](18, 2) NULL,
	[ChangeAmount] [decimal](18, 2) NULL,
	[ProviderOrderCode] [varchar](100) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
	[ProviderTransactionId] [varchar](255) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
	[IdempotencyKey] [varchar](128) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	[ExpiredAt] [datetime] NULL,
	[PaidAt] [datetime] NULL,
	[VerificationSource] [varchar](50) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
	[ConfirmedByRef] [uniqueidentifier] NULL,
	[EvidenceUrl] [varchar](2048) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
 CONSTRAINT [PK_Payment] PRIMARY KEY CLUSTERED 
(
	[PaymentId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]

GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[Refund](
	[RefundId] [uniqueidentifier] NOT NULL,
	[PaymentId] [uniqueidentifier] NOT NULL,
	[Amount] [decimal](18, 2) NOT NULL,
	[Status] [varchar](50) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	[Reason] [varchar](1000) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	[IdempotencyKey] [varchar](128) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	[CompletedAt] [datetime] NULL,
 CONSTRAINT [PK_Refund] PRIMARY KEY CLUSTERED 
(
	[RefundId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]

GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[CashMovement](
	[CashMovementId] [uniqueidentifier] NOT NULL,
	[CashShiftId] [uniqueidentifier] NOT NULL,
	[PaymentId] [uniqueidentifier] NULL,
	[Type] [varchar](50) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	[SignedAmount] [decimal](18, 2) NOT NULL,
	[Reason] [varchar](1000) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
	[PostedAt] [datetime] NOT NULL,
	[IdempotencyKey] [varchar](128) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
 CONSTRAINT [PK_CashMovement] PRIMARY KEY CLUSTERED 
(
	[CashMovementId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]

GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[CashHandover](
	[PaymentId] [uniqueidentifier] NOT NULL,
	[WaiterUserRef] [uniqueidentifier] NOT NULL,
	[CashierUserRef] [uniqueidentifier] NULL,
	[CollectedAt] [datetime] NOT NULL,
	[ConfirmedAt] [datetime] NULL,
	[ConfirmedAmount] [decimal](18, 2) NULL,
	[Status] [varchar](50) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
 CONSTRAINT [PK_CashHandover] PRIMARY KEY CLUSTERED 
(
	[PaymentId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]

GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[PaymentWebhook](
	[PaymentWebhookId] [uniqueidentifier] NOT NULL,
	[PaymentId] [uniqueidentifier] NULL,
	[Provider] [varchar](50) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	[DedupeKey] [varchar](128) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	[PayloadHash] [varchar](64) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	[SignatureValid] [bit] NOT NULL,
	[ReceivedAt] [datetime] NOT NULL,
	[ProcessedAt] [datetime] NULL,
	[ProcessingStatus] [varchar](50) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
 CONSTRAINT [PK_PaymentWebhook] PRIMARY KEY CLUSTERED 
(
	[PaymentWebhookId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]

GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[RefundLine](
	[RefundLineId] [uniqueidentifier] NOT NULL,
	[BillLineId] [uniqueidentifier] NOT NULL,
	[RefundId] [uniqueidentifier] NOT NULL,
	[Amount] [decimal](18, 2) NOT NULL,
	[Quantity] [decimal](18, 6) NOT NULL,
 CONSTRAINT [PK_RefundLine] PRIMARY KEY CLUSTERED 
(
	[RefundLineId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]

GO
ALTER TABLE [dbo].[VoucherRedemption]  WITH CHECK ADD  CONSTRAINT [FK_VoucherRedemption_Bill] FOREIGN KEY([BillId])
REFERENCES [dbo].[Bill] ([BillId])
GO
ALTER TABLE [dbo].[VoucherRedemption] CHECK CONSTRAINT [FK_VoucherRedemption_Bill]
GO
ALTER TABLE [dbo].[VoucherRedemption]  WITH CHECK ADD  CONSTRAINT [FK_VoucherRedemption_Voucher] FOREIGN KEY([VoucherId])
REFERENCES [dbo].[Voucher] ([VoucherId])
GO
ALTER TABLE [dbo].[VoucherRedemption] CHECK CONSTRAINT [FK_VoucherRedemption_Voucher]
GO
ALTER TABLE [dbo].[ElectronicInvoice]  WITH CHECK ADD  CONSTRAINT [FK_ElectronicInvoice_Bill] FOREIGN KEY([BillId])
REFERENCES [dbo].[Bill] ([BillId])
GO
ALTER TABLE [dbo].[ElectronicInvoice] CHECK CONSTRAINT [FK_ElectronicInvoice_Bill]
GO
ALTER TABLE [dbo].[CashShift]  WITH CHECK ADD  CONSTRAINT [FK_CashShift_CashRegister] FOREIGN KEY([CashRegisterId])
REFERENCES [dbo].[CashRegister] ([CashRegisterId])
GO
ALTER TABLE [dbo].[CashShift] CHECK CONSTRAINT [FK_CashShift_CashRegister]
GO
ALTER TABLE [dbo].[ReceiptPrint]  WITH CHECK ADD  CONSTRAINT [FK_ReceiptPrint_Bill] FOREIGN KEY([BillId])
REFERENCES [dbo].[Bill] ([BillId])
GO
ALTER TABLE [dbo].[ReceiptPrint] CHECK CONSTRAINT [FK_ReceiptPrint_Bill]
GO
ALTER TABLE [dbo].[PromotionTarget]  WITH CHECK ADD  CONSTRAINT [FK_PromotionTarget_Promotion] FOREIGN KEY([PromotionId])
REFERENCES [dbo].[Promotion] ([PromotionId])
GO
ALTER TABLE [dbo].[PromotionTarget] CHECK CONSTRAINT [FK_PromotionTarget_Promotion]
GO
ALTER TABLE [dbo].[BillLine]  WITH CHECK ADD  CONSTRAINT [FK_BillLine_Bill] FOREIGN KEY([BillId])
REFERENCES [dbo].[Bill] ([BillId])
GO
ALTER TABLE [dbo].[BillLine] CHECK CONSTRAINT [FK_BillLine_Bill]
GO
ALTER TABLE [dbo].[BillAdjustment]  WITH CHECK ADD  CONSTRAINT [FK_BillAdjustment_Bill] FOREIGN KEY([BillId])
REFERENCES [dbo].[Bill] ([BillId])
GO
ALTER TABLE [dbo].[BillAdjustment] CHECK CONSTRAINT [FK_BillAdjustment_Bill]
GO
ALTER TABLE [dbo].[BillAdjustment]  WITH CHECK ADD  CONSTRAINT [FK_BillAdjustment_Promotion] FOREIGN KEY([PromotionId])
REFERENCES [dbo].[Promotion] ([PromotionId])
GO
ALTER TABLE [dbo].[BillAdjustment] CHECK CONSTRAINT [FK_BillAdjustment_Promotion]
GO
ALTER TABLE [dbo].[BillAdjustment]  WITH CHECK ADD  CONSTRAINT [FK_BillAdjustment_Voucher] FOREIGN KEY([VoucherId])
REFERENCES [dbo].[Voucher] ([VoucherId])
GO
ALTER TABLE [dbo].[BillAdjustment] CHECK CONSTRAINT [FK_BillAdjustment_Voucher]
GO
ALTER TABLE [dbo].[ShiftReport]  WITH CHECK ADD  CONSTRAINT [FK_ShiftReport_CashShift] FOREIGN KEY([CashShiftId])
REFERENCES [dbo].[CashShift] ([CashShiftId])
GO
ALTER TABLE [dbo].[ShiftReport] CHECK CONSTRAINT [FK_ShiftReport_CashShift]
GO
ALTER TABLE [dbo].[Payment]  WITH CHECK ADD  CONSTRAINT [FK_Payment_Bill] FOREIGN KEY([BillId])
REFERENCES [dbo].[Bill] ([BillId])
GO
ALTER TABLE [dbo].[Payment] CHECK CONSTRAINT [FK_Payment_Bill]
GO
ALTER TABLE [dbo].[Payment]  WITH CHECK ADD  CONSTRAINT [FK_Payment_CashShift] FOREIGN KEY([CashShiftId])
REFERENCES [dbo].[CashShift] ([CashShiftId])
GO
ALTER TABLE [dbo].[Payment] CHECK CONSTRAINT [FK_Payment_CashShift]
GO
ALTER TABLE [dbo].[Payment]  WITH CHECK ADD  CONSTRAINT [FK_Payment_PaymentMethod] FOREIGN KEY([MethodCode])
REFERENCES [dbo].[PaymentMethod] ([MethodCode])
GO
ALTER TABLE [dbo].[Payment] CHECK CONSTRAINT [FK_Payment_PaymentMethod]
GO
ALTER TABLE [dbo].[Refund]  WITH CHECK ADD  CONSTRAINT [FK_Refund_Payment] FOREIGN KEY([PaymentId])
REFERENCES [dbo].[Payment] ([PaymentId])
GO
ALTER TABLE [dbo].[Refund] CHECK CONSTRAINT [FK_Refund_Payment]
GO
ALTER TABLE [dbo].[CashMovement]  WITH CHECK ADD  CONSTRAINT [FK_CashMovement_CashShift] FOREIGN KEY([CashShiftId])
REFERENCES [dbo].[CashShift] ([CashShiftId])
GO
ALTER TABLE [dbo].[CashMovement] CHECK CONSTRAINT [FK_CashMovement_CashShift]
GO
ALTER TABLE [dbo].[CashMovement]  WITH CHECK ADD  CONSTRAINT [FK_CashMovement_Payment] FOREIGN KEY([PaymentId])
REFERENCES [dbo].[Payment] ([PaymentId])
GO
ALTER TABLE [dbo].[CashMovement] CHECK CONSTRAINT [FK_CashMovement_Payment]
GO
ALTER TABLE [dbo].[CashHandover]  WITH CHECK ADD  CONSTRAINT [FK_CashHandover_Payment] FOREIGN KEY([PaymentId])
REFERENCES [dbo].[Payment] ([PaymentId])
GO
ALTER TABLE [dbo].[CashHandover] CHECK CONSTRAINT [FK_CashHandover_Payment]
GO
ALTER TABLE [dbo].[PaymentWebhook]  WITH CHECK ADD  CONSTRAINT [FK_PaymentWebhook_Payment] FOREIGN KEY([PaymentId])
REFERENCES [dbo].[Payment] ([PaymentId])
GO
ALTER TABLE [dbo].[PaymentWebhook] CHECK CONSTRAINT [FK_PaymentWebhook_Payment]
GO
ALTER TABLE [dbo].[RefundLine]  WITH CHECK ADD  CONSTRAINT [FK_RefundLine_BillLine] FOREIGN KEY([BillLineId])
REFERENCES [dbo].[BillLine] ([BillLineId])
GO
ALTER TABLE [dbo].[RefundLine] CHECK CONSTRAINT [FK_RefundLine_BillLine]
GO
ALTER TABLE [dbo].[RefundLine]  WITH CHECK ADD  CONSTRAINT [FK_RefundLine_Refund] FOREIGN KEY([RefundId])
REFERENCES [dbo].[Refund] ([RefundId])
GO
ALTER TABLE [dbo].[RefundLine] CHECK CONSTRAINT [FK_RefundLine_Refund]
GO

SET QUOTED_IDENTIFIER ON;
CREATE TABLE dbo.BillCheckout
(
    CheckoutId uniqueidentifier NOT NULL,
    BillId uniqueidentifier NULL,
    TableSessionRef uniqueidentifier NOT NULL,
    SourceHash char(64) NULL,
    Status varchar(20) NOT NULL,
    CreatedAt datetime2(7) NOT NULL,
    ReleasedAt datetime2(7) NULL,
    CONSTRAINT FK_BillCheckout_Bill FOREIGN KEY (BillId) REFERENCES dbo.Bill(BillId),
    CONSTRAINT PK_BillCheckout PRIMARY KEY (CheckoutId)
);
CREATE UNIQUE INDEX UX_BillCheckout_ActiveSession ON dbo.BillCheckout(TableSessionRef) WHERE ReleasedAt IS NULL;
GO
