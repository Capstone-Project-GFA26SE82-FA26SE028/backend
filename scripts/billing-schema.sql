-- WARNING: Running this script deletes BillingDb and all its existing data.
-- Ref columns reference other services and do not have foreign keys.
-- Promotion.MinimumSpend is duplicated in the ERD; included only once here.
USE master;
GO

IF DB_ID(N'BillingDb') IS NOT NULL
BEGIN
    ALTER DATABASE BillingDb
        SET SINGLE_USER WITH ROLLBACK IMMEDIATE;

    DROP DATABASE BillingDb;
END;
GO

CREATE DATABASE BillingDb;
GO

USE BillingDb;
GO

CREATE TABLE dbo.Bill
(
    BillId              uniqueidentifier NOT NULL,
    BillNumber          varchar(50)      NOT NULL,
    CustomerRef         uniqueidentifier NULL,
    Status              varchar(50)      NOT NULL,
    SubTotal            decimal(18, 2)   NOT NULL,
    DiscountAmount      decimal(18, 2)   NOT NULL,
    VatRateSnapshot     decimal(9, 6)    NOT NULL,
    ServiceRateSnapshot decimal(9, 6)    NOT NULL,
    VatAmount           decimal(18, 2)   NOT NULL,
    ServiceAmount       decimal(18, 2)   NOT NULL,
    TotalAmount         decimal(18, 2)   NOT NULL,
    Currency            char(3)          NOT NULL,
    PrebillPrintedAt    datetime         NULL,
    PaidAt              datetime         NULL,
    Version             rowversion       NOT NULL,

    CONSTRAINT PK_Bill PRIMARY KEY (BillId)
);
GO

CREATE TABLE dbo.BillLine
(
    BillLineId        uniqueidentifier NOT NULL,
    BillId            uniqueidentifier NOT NULL,
    OrderItemRef      uniqueidentifier NOT NULL,
    TableSessionRef   uniqueidentifier NOT NULL,
    OriginalTableRef  uniqueidentifier NOT NULL,
    ComboGroupRef     uniqueidentifier NULL,
    ItemSnapshot      varchar(max)     NOT NULL,
    Quantity          decimal(18, 6)   NOT NULL,
    UnitPriceSnapshot decimal(18, 2)   NOT NULL,
    DiscountAllocated decimal(18, 2)   NOT NULL,
    VatAllocated      decimal(18, 2)   NOT NULL,
    ServiceAllocated  decimal(18, 2)   NOT NULL,
    CogsSnapshot      decimal(18, 2)   NOT NULL,

    CONSTRAINT PK_BillLine PRIMARY KEY (BillLineId),
    CONSTRAINT FK_BillLine_Bill FOREIGN KEY (BillId)
        REFERENCES dbo.Bill(BillId)
);
GO

CREATE TABLE dbo.Voucher
(
    VoucherId             uniqueidentifier NOT NULL,
    NormalizedVoucherCode varchar(100)     NOT NULL,
    DiscountType          varchar(50)      NOT NULL,
    DiscountValue         decimal(18, 2)   NOT NULL,
    MaximumDiscount       decimal(18, 2)   NULL,
    UsageLimit            int              NULL,
    StartAt               datetime         NOT NULL,
    EndAt                 datetime         NULL,
    IsActive              bit              NOT NULL,
    Version               rowversion       NOT NULL,

    CONSTRAINT PK_Voucher PRIMARY KEY (VoucherId)
);
GO

CREATE TABLE dbo.Promotion
(
    PromotionId   uniqueidentifier NOT NULL,
    PromotionName varchar(255)     NOT NULL,
    DiscountType  varchar(50)      NOT NULL,
    Type          varchar(50)      NOT NULL,
    DiscountValue decimal(18, 2)   NOT NULL,
    UsageLimit    int              NULL,
    MinimumSpend  decimal(18, 2)   NULL,
    StartAt       datetime         NOT NULL,
    EndAt         datetime         NULL,
    IsActive      bit              NOT NULL,

    CONSTRAINT PK_Promotion PRIMARY KEY (PromotionId)
);
GO

CREATE TABLE dbo.PromotionTarget
(
    PromotionTargetId uniqueidentifier NOT NULL,
    PromotionId       uniqueidentifier NOT NULL,
    ItemRef           uniqueidentifier NULL,
    CategoryRef       uniqueidentifier NULL,
    Role              varchar(50)      NOT NULL,
    RequiredQuantity  decimal(18, 6)   NOT NULL,

    CONSTRAINT PK_PromotionTarget PRIMARY KEY (PromotionTargetId),
    CONSTRAINT FK_PromotionTarget_Promotion FOREIGN KEY (PromotionId)
        REFERENCES dbo.Promotion(PromotionId)
);
GO

CREATE TABLE dbo.BillAdjustment
(
    BillAdjustmentId uniqueidentifier NOT NULL,
    BillId           uniqueidentifier NOT NULL,
    VoucherId        uniqueidentifier NULL,
    PromotionId      uniqueidentifier NULL,
    Type             varchar(50)      NOT NULL,
    Amount           decimal(18, 2)   NOT NULL,
    RuleSnapshot     varchar(max)     NULL,
    ActorUserRef     uniqueidentifier NULL,
    Reason           varchar(1000)    NULL,

    CONSTRAINT PK_BillAdjustment PRIMARY KEY (BillAdjustmentId),
    CONSTRAINT FK_BillAdjustment_Bill FOREIGN KEY (BillId)
        REFERENCES dbo.Bill(BillId),
    CONSTRAINT FK_BillAdjustment_Voucher FOREIGN KEY (VoucherId)
        REFERENCES dbo.Voucher(VoucherId),
    CONSTRAINT FK_BillAdjustment_Promotion FOREIGN KEY (PromotionId)
        REFERENCES dbo.Promotion(PromotionId)
);
GO

CREATE TABLE dbo.VoucherRedemption
(
    VoucherRedemptionId uniqueidentifier NOT NULL,
    VoucherId           uniqueidentifier NOT NULL,
    BillId              uniqueidentifier NOT NULL,
    Status              varchar(50)      NOT NULL,
    ReservedUntil       datetime         NULL,
    CommittedAt         datetime         NULL,

    CONSTRAINT PK_VoucherRedemption PRIMARY KEY (VoucherRedemptionId),
    CONSTRAINT FK_VoucherRedemption_Voucher FOREIGN KEY (VoucherId)
        REFERENCES dbo.Voucher(VoucherId),
    CONSTRAINT FK_VoucherRedemption_Bill FOREIGN KEY (BillId)
        REFERENCES dbo.Bill(BillId)
);
GO

CREATE TABLE dbo.ElectronicInvoice
(
    ElectronicInvoiceId uniqueidentifier NOT NULL,
    BillId              uniqueidentifier NOT NULL,
    CompanyName         varchar(255)     NOT NULL,
    TaxCode             varchar(50)      NOT NULL,
    Address             varchar(500)     NOT NULL,
    Email               varchar(320)     NOT NULL,
    ProviderReference   varchar(255)     NULL,
    Status              varchar(50)      NOT NULL,
    IssuedAt            datetime         NULL,

    CONSTRAINT PK_ElectronicInvoice PRIMARY KEY (ElectronicInvoiceId),
    CONSTRAINT FK_ElectronicInvoice_Bill FOREIGN KEY (BillId)
        REFERENCES dbo.Bill(BillId)
);
GO

CREATE TABLE dbo.ReceiptPrint
(
    ReceiptPrintId uniqueidentifier NOT NULL,
    BillId         uniqueidentifier NOT NULL,
    Type           varchar(50)      NOT NULL,
    CopyNumber     int              NOT NULL,
    PrintedByRef   uniqueidentifier NOT NULL,
    PrintedAt      datetime         NOT NULL,

    CONSTRAINT PK_ReceiptPrint PRIMARY KEY (ReceiptPrintId),
    CONSTRAINT FK_ReceiptPrint_Bill FOREIGN KEY (BillId)
        REFERENCES dbo.Bill(BillId)
);
GO

CREATE TABLE dbo.CashRegister
(
    CashRegisterId uniqueidentifier NOT NULL,
    Code           varchar(50)      NOT NULL,

    CONSTRAINT PK_CashRegister PRIMARY KEY (CashRegisterId)
);
GO

CREATE TABLE dbo.CashShift
(
    CashShiftId    uniqueidentifier NOT NULL,
    CashRegisterId uniqueidentifier NOT NULL,
    CashierUserRef uniqueidentifier NOT NULL,
    OpeningCash    decimal(18, 2)   NOT NULL,
    OpenedAt       datetime         NOT NULL,
    ClosedAt       datetime         NULL,
    Status         varchar(50)      NOT NULL,

    CONSTRAINT PK_CashShift PRIMARY KEY (CashShiftId),
    CONSTRAINT FK_CashShift_CashRegister FOREIGN KEY (CashRegisterId)
        REFERENCES dbo.CashRegister(CashRegisterId)
);
GO

CREATE TABLE dbo.ShiftReport
(
    ShiftReportId    uniqueidentifier NOT NULL,
    CashShiftId      uniqueidentifier NOT NULL,
    ExpectedCash     decimal(18, 2)   NOT NULL,
    ActualCash       decimal(18, 2)   NOT NULL,
    CashHandleOver   decimal(18, 2)   NOT NULL,
    DifferenceReason varchar(1000)    NULL,
    ClosedAt         datetime         NOT NULL,

    CONSTRAINT PK_ShiftReport PRIMARY KEY (ShiftReportId),
    CONSTRAINT UQ_ShiftReport_CashShiftId UNIQUE (CashShiftId),
    CONSTRAINT FK_ShiftReport_CashShift FOREIGN KEY (CashShiftId)
        REFERENCES dbo.CashShift(CashShiftId)
);
GO

CREATE TABLE dbo.PaymentMethod
(
    MethodCode varchar(50) NOT NULL,
    IsEnabled  bit         NOT NULL,

    CONSTRAINT PK_PaymentMethod PRIMARY KEY (MethodCode)
);
GO

CREATE TABLE dbo.Payment
(
    PaymentId             uniqueidentifier NOT NULL,
    BillId                uniqueidentifier NOT NULL,
    CashShiftId           uniqueidentifier NOT NULL,
    MethodCode            varchar(50)      NOT NULL,
    Status                varchar(50)      NOT NULL,
    Amount                decimal(18, 2)   NOT NULL,
    CashReceived          decimal(18, 2)   NULL,
    ChangeAmount          decimal(18, 2)   NULL,
    ProviderOrderCode     varchar(100)     NULL,
    ProviderTransactionId varchar(255)     NULL,
    IdempotencyKey        varchar(128)     NOT NULL,
    ExpiredAt             datetime         NULL,
    PaidAt                datetime         NULL,
    VerificationSource    varchar(50)      NULL,
    ConfirmedByRef        uniqueidentifier NULL,
    EvidenceUrl           varchar(2048)    NULL,

    CONSTRAINT PK_Payment PRIMARY KEY (PaymentId),
    CONSTRAINT FK_Payment_Bill FOREIGN KEY (BillId)
        REFERENCES dbo.Bill(BillId),
    CONSTRAINT FK_Payment_CashShift FOREIGN KEY (CashShiftId)
        REFERENCES dbo.CashShift(CashShiftId),
    CONSTRAINT FK_Payment_PaymentMethod FOREIGN KEY (MethodCode)
        REFERENCES dbo.PaymentMethod(MethodCode)
);
GO

CREATE TABLE dbo.Refund
(
    RefundId       uniqueidentifier NOT NULL,
    PaymentId      uniqueidentifier NOT NULL,
    Amount         decimal(18, 2)   NOT NULL,
    Status         varchar(50)      NOT NULL,
    Reason         varchar(1000)    NOT NULL,
    IdempotencyKey varchar(128)     NOT NULL,
    CompletedAt    datetime         NULL,

    CONSTRAINT PK_Refund PRIMARY KEY (RefundId),
    CONSTRAINT FK_Refund_Payment FOREIGN KEY (PaymentId)
        REFERENCES dbo.Payment(PaymentId)
);
GO

CREATE TABLE dbo.RefundLine
(
    RefundLineId uniqueidentifier NOT NULL,
    BillLineId   uniqueidentifier NOT NULL,
    RefundId     uniqueidentifier NOT NULL,
    Amount       decimal(18, 2)   NOT NULL,
    Quantity     decimal(18, 6)   NOT NULL,

    CONSTRAINT PK_RefundLine PRIMARY KEY (RefundLineId),
    CONSTRAINT FK_RefundLine_BillLine FOREIGN KEY (BillLineId)
        REFERENCES dbo.BillLine(BillLineId),
    CONSTRAINT FK_RefundLine_Refund FOREIGN KEY (RefundId)
        REFERENCES dbo.Refund(RefundId)
);
GO

CREATE TABLE dbo.PaymentWebhook
(
    PaymentWebhookId uniqueidentifier NOT NULL,
    PaymentId        uniqueidentifier NULL,
    Provider         varchar(50)      NOT NULL,
    DedupeKey        varchar(128)     NOT NULL,
    PayloadHash      varchar(64)      NOT NULL,
    SignatureValid   bit              NOT NULL,
    ReceivedAt       datetime         NOT NULL,
    ProcessedAt      datetime         NULL,
    ProcessingStatus varchar(50)      NOT NULL,

    CONSTRAINT PK_PaymentWebhook PRIMARY KEY (PaymentWebhookId),
    CONSTRAINT FK_PaymentWebhook_Payment FOREIGN KEY (PaymentId)
        REFERENCES dbo.Payment(PaymentId)
);
GO

CREATE TABLE dbo.CashHandover
(
    PaymentId       uniqueidentifier NOT NULL,
    WaiterUserRef   uniqueidentifier NOT NULL,
    CashierUserRef  uniqueidentifier NULL,
    CollectedAt     datetime         NOT NULL,
    ConfirmedAt     datetime         NULL,
    ConfirmedAmount decimal(18, 2)   NULL,
    Status          varchar(50)      NOT NULL,

    CONSTRAINT PK_CashHandover PRIMARY KEY (PaymentId),
    CONSTRAINT FK_CashHandover_Payment FOREIGN KEY (PaymentId)
        REFERENCES dbo.Payment(PaymentId)
);
GO

CREATE TABLE dbo.CashMovement
(
    CashMovementId uniqueidentifier NOT NULL,
    CashShiftId    uniqueidentifier NOT NULL,
    PaymentId      uniqueidentifier NULL,
    Type           varchar(50)      NOT NULL,
    SignedAmount   decimal(18, 2)   NOT NULL,
    Reason         varchar(1000)    NULL,
    PostedAt       datetime         NOT NULL,
    IdempotencyKey varchar(128)     NOT NULL,

    CONSTRAINT PK_CashMovement PRIMARY KEY (CashMovementId),
    CONSTRAINT FK_CashMovement_CashShift FOREIGN KEY (CashShiftId)
        REFERENCES dbo.CashShift(CashShiftId),
    CONSTRAINT FK_CashMovement_Payment FOREIGN KEY (PaymentId)
        REFERENCES dbo.Payment(PaymentId)
);
GO

CREATE TABLE dbo.PaymentProviderConfig
(
    PaymentProviderConfigId uniqueidentifier NOT NULL,
    Provider                varchar(50)      NOT NULL,
    ClientId                varchar(255)     NOT NULL,
    SecretReference         varchar(500)     NOT NULL,
    WebhookUrl              varchar(2048)    NOT NULL,
    IsVerified              bit              NOT NULL,

    CONSTRAINT PK_PaymentProviderConfig PRIMARY KEY (PaymentProviderConfigId)
);
GO

CREATE TABLE dbo.TaxFeePolicy
(
    TaxFeePolicyId uniqueidentifier NOT NULL,
    Version        int              NOT NULL,
    VatRate        decimal(9, 6)    NOT NULL,
    ServiceRate    decimal(9, 6)    NOT NULL,
    ServiceEnabled bit              NOT NULL,
    EffectiveFrom  datetime         NOT NULL,

    CONSTRAINT PK_TaxFeePolicy PRIMARY KEY (TaxFeePolicyId)
);
GO

CREATE TABLE dbo.DiscountLimit
(
    DiscountLimitId uniqueidentifier NOT NULL,
    RoleRef         uniqueidentifier NULL,
    UserRef         uniqueidentifier NULL,
    LimitType       varchar(50)      NOT NULL,
    MaximumValue    decimal(18, 2)   NOT NULL,
    EffectiveFrom   datetime         NOT NULL,

    CONSTRAINT PK_DiscountLimit PRIMARY KEY (DiscountLimitId)
);
GO
