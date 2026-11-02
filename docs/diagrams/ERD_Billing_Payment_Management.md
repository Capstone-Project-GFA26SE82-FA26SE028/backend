erDiagram

&#x20;   Bill ||--|| ElectronicInvoice : "has"

&#x20;   Bill ||--|{ ReceiptPrint : "prints"

&#x20;   Bill |o--|| BillCheckout : "locks"

&#x20;   Bill ||--|{ BillLine : "contains"

&#x20;   Bill ||--|{ Payment : "paid\_by"

&#x20;   Bill ||--|{ VoucherRedemption : "redeems"

&#x20;   Bill ||--|{ BillAdjustment : "adjusts"

&#x20;   BillLine ||--|{ RefundLine : "refunded\_in"

&#x20;   Refund ||--|{ RefundLine : "contains"

&#x20;   Payment ||--|{ Refund : "has"

&#x20;   Payment |o--|{ PaymentWebhook : "receives"

&#x20;   Payment ||--|| CashHandover : "requires"

&#x20;   Payment |o--|{ CashMovement : "records"

&#x20;   PaymentMethod ||--|{ Payment : "used\_in"

&#x20;   CashShift ||--|{ Payment : "processes"

&#x20;   CashShift ||--|| ShiftReport : "summarizes"

&#x20;   CashShift ||--|{ CashMovement : "tracks"

&#x20;   CashRegister ||--|{ CashShift : "opens"

&#x20;   Voucher ||--|{ VoucherRedemption : "used\_in"

&#x20;   Voucher |o--|{ BillAdjustment : "applies\_to"

&#x20;   Promotion |o--|{ BillAdjustment : "applies\_to"

&#x20;   Promotion ||--|{ PromotionTarget : "targets"



&#x20;   ElectronicInvoice {

&#x20;       guid ElectronicInvoiceId PK

&#x20;       guid BillId FK

&#x20;       string CompanyName

&#x20;       string TaxCode

&#x20;       string Address

&#x20;       string Email

&#x20;       string ProviderReference

&#x20;       string Status

&#x20;       datetime IssuedAt

&#x20;   }



&#x20;   ReceiptPrint {

&#x20;       guid ReceiptPrintId PK

&#x20;       guid BillId FK

&#x20;       string Type

&#x20;       int CopyNumber

&#x20;       guid PrintedByRef

&#x20;       datetime PrintedAt

&#x20;   }



&#x20;   RefundLine {

&#x20;       guid RefundLineId PK

&#x20;       guid BillLineId FK

&#x20;       guid RefundId FK

&#x20;       decimal Amount

&#x20;       decimal Quantity

&#x20;   }



&#x20;   BillLine {

&#x20;       guid BillLineId PK

&#x20;       guid BillId FK

&#x20;       guid OrderItemRef

&#x20;       guid TableSessionRef

&#x20;       guid OriginalTableRef

&#x20;       guid ComboGroupRef "NULL"

&#x20;       string ItemSnapshot

&#x20;       decimal Quantity

&#x20;       decimal UnitPriceSnapshot

&#x20;       decimal DiscountAllocated

&#x20;       decimal VatAllocated

&#x20;       decimal ServiceAllocated

&#x20;       decimal CogsSnapshot "NULL"

&#x20;   }



&#x20;   Refund {

&#x20;       guid RefundId PK

&#x20;       guid PaymentId FK

&#x20;       decimal Amount

&#x20;       string Status

&#x20;       string Reason

&#x20;       string IdempotencyKey

&#x20;       datetime CompletedAt

&#x20;   }



&#x20;   PaymentWebhook {

&#x20;       guid PaymentWebhookId PK

&#x20;       guid PaymentId FK "NULL"

&#x20;       string Provider

&#x20;       string DedupeKey

&#x20;       string PayloadHash

&#x20;       bool SignatureValid

&#x20;       datetime ReceivedAt

&#x20;       datetime ProcessedAt

&#x20;       string ProcessingStatus

&#x20;   }



&#x20;   BillCheckout {

&#x20;       guid CheckoutId PK

&#x20;       guid BillId FK "NULL"

&#x20;       guid TableSessionRef

&#x20;       string SourceHash "NULL"

&#x20;       string Status

&#x20;       datetime CreatedAt

&#x20;       datetime ReleasedAt "NULL"

&#x20;   }



&#x20;   Bill {

&#x20;       guid BillId PK

&#x20;       guid RestaurantId

&#x20;       string BillNumber

&#x20;       guid CustomerRef "NULL"

&#x20;       string Status

&#x20;       decimal SubTotal

&#x20;       decimal DiscountAmount

&#x20;       decimal VatRateSnapshot

&#x20;       decimal ServiceRateSnapshot

&#x20;       decimal VatAmount

&#x20;       decimal ServiceAmount

&#x20;       decimal TotalAmount

&#x20;       string Currency

&#x20;       datetime PrebillPrintedAt

&#x20;       datetime PaidAt

&#x20;       rowversion Version

&#x20;   }



&#x20;   Payment {

&#x20;       guid PaymentId PK

&#x20;       guid BillId FK

&#x20;       guid CashShiftId FK

&#x20;       string MethodCode FK

&#x20;       string Status

&#x20;       decimal Amount

&#x20;       decimal CashReceived

&#x20;       decimal ChangeAmount

&#x20;       string ProviderOrderCode

&#x20;       string ProviderTransactionId

&#x20;       string IdempotencyKey

&#x20;       datetime ExpiredAt

&#x20;       datetime PaidAt

&#x20;       string VerificationSource

&#x20;       guid ConfirmedByRef

&#x20;       string EvidenceUrl

&#x20;   }



&#x20;   CashHandover {

&#x20;       guid PaymentId PK, FK

&#x20;       guid WaiterUserRef

&#x20;       guid CashierUserRef "NULL"

&#x20;       datetime CollectedAt

&#x20;       datetime ConfirmedAt

&#x20;       decimal ConfirmedAmount

&#x20;       string Status

&#x20;   }



&#x20;   VoucherRedemption {

&#x20;       guid VoucherRedemptionId PK

&#x20;       guid VoucherId FK

&#x20;       guid BillId FK

&#x20;       string Status

&#x20;       datetime ReservedUntil

&#x20;       datetime CommittedAt

&#x20;   }



&#x20;   BillAdjustment {

&#x20;       guid BillAdjustmentId PK

&#x20;       guid BillId FK

&#x20;       guid VoucherId FK "NULL"

&#x20;       guid PromotionId FK "NULL"

&#x20;       string Type

&#x20;       decimal Amount

&#x20;       string RuleSnapshot

&#x20;       guid ActorUserRef "NULL"

&#x20;       string Reason

&#x20;   }



&#x20;   ShiftReport {

&#x20;       guid ShiftReportId PK

&#x20;       guid CashShiftId FK

&#x20;       decimal ExpectedCash

&#x20;       decimal ActualCash

&#x20;       decimal CashHandleOver

&#x20;       string DifferenceReason

&#x20;       datetime ClosedAt

&#x20;   }



&#x20;   CashShift {

&#x20;       guid CashShiftId PK

&#x20;       guid CashRegisterId FK

&#x20;       guid CashierUserRef

&#x20;       decimal OpeningCash

&#x20;       datetime OpenedAt

&#x20;       datetime ClosedAt

&#x20;       string Status

&#x20;   }



&#x20;   PaymentMethod {

&#x20;       string MethodCode PK

&#x20;       bool IsEnabled

&#x20;   }



&#x20;   PaymentProviderConfig {

&#x20;       guid PaymentProviderConfigId PK

&#x20;       string Provider

&#x20;       string ClientId

&#x20;       string SecretReference

&#x20;       string WebhookUrl

&#x20;       bool IsVerified

&#x20;   }



&#x20;   Voucher {

&#x20;       guid VoucherId PK

&#x20;       string NomalizedVoucherCode

&#x20;       string DiscountType

&#x20;       decimal DiscountValue

&#x20;       decimal MaximumDiscount

&#x20;       int UsageLimit

&#x20;       datetime StartAt

&#x20;       datetime EndAt

&#x20;       bool IsActive

&#x20;       rowversion Version

&#x20;   }



&#x20;   Promotion {

&#x20;       guid PromotionId PK

&#x20;       string PromotionName

&#x20;       string DiscountType

&#x20;       string Type

&#x20;       decimal DiscountValue

&#x20;       int UsageLimit

&#x20;       decimal MinimumSpend

&#x20;       datetime StartAt

&#x20;       datetime EndAt

&#x20;       bool IsActive

&#x20;   }



&#x20;   PromotionTarget {

&#x20;       guid PromotionTargetId PK

&#x20;       guid PromotionId FK

&#x20;       guid ItemRef "NULL"

&#x20;       guid CategoryRef "NULL"

&#x20;       string Role

&#x20;       decimal RequiredQuantity

&#x20;   }



&#x20;   CashRegister {

&#x20;       guid CashRegisterId PK

&#x20;       guid RestaurantId

&#x20;       string Code

&#x20;   }



&#x20;   CashMovement {

&#x20;       guid CashMovementId PK

&#x20;       guid CashShiftId FK

&#x20;       guid PaymentId FK "NULL"

&#x20;       string Type

&#x20;       decimal SignedAmount

&#x20;       string Reason

&#x20;       datetime PostedAt

&#x20;       string IdempotencyKey

&#x20;   }



&#x20;   TaxFeePolicy {

&#x20;       guid TaxFeePolicyId PK

&#x20;       int Version

&#x20;       decimal VatRate

&#x20;       decimal ServiceRate

&#x20;       bool ServiceEnabled

&#x20;       datetime EffectiveFrom

&#x20;   }



&#x20;   DiscountLimit {

&#x20;       guid DiscountLimitId PK

&#x20;       guid RoleRef "NULL"

&#x20;       guid UserRef "NULL"

&#x20;       string LimitType

&#x20;       decimal MaximumValue

&#x20;       datetime EffectiveFrom

&#x20;   }





