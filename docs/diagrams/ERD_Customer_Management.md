erDiagram

&#x20;   LoyaltyTier ||--|{ Customer : "classifies"

&#x20;   Customer ||--|{ LoyaltyEntry : "has"

&#x20;   Customer ||--|{ PointReservation : "makes"

&#x20;   Customer |o--|{ Feedback : "submits"

&#x20;   LoyaltyPolicy ||--|{ LoyaltyEntry : "governs"

&#x20;   FeedbackAccess ||--|| Feedback : "authorizes"

&#x20;   Feedback ||--|{ FeedbackItem : "contains"



&#x20;   Customer {

&#x20;       guid CustomerId PK

&#x20;       string NormalizedPhone

&#x20;       string CustomerName

&#x20;       guid LoyaltyTierId FK

&#x20;       rowversion Version

&#x20;   }



&#x20;   LoyaltyEntry {

&#x20;       guid LoyaltyEntryId PK

&#x20;       guid CustomerId FK

&#x20;       guid BillRef "NULL"

&#x20;       guid RefundRef "NULL"

&#x20;       guid PolicyId FK

&#x20;       string Type

&#x20;       int SignedPoint

&#x20;       decimal EligibleSpend

&#x20;       string SourceEventId

&#x20;       datetime PostedAt

&#x20;   }



&#x20;   PointReservation {

&#x20;       guid PointReservationId PK

&#x20;       guid CustomerId FK

&#x20;       guid BillRef

&#x20;       int Point

&#x20;       string Status

&#x20;       datetime ExpiredAt

&#x20;   }



&#x20;   LoyaltyPolicy {

&#x20;       guid PolicyId PK

&#x20;       int Version

&#x20;       decimal SpendPerPoint

&#x20;       decimal MoneyPerRedeemedPoint

&#x20;       datetime EffectiveFrom

&#x20;   }



&#x20;   LoyaltyTier {

&#x20;       guid LoyaltyTierId PK

&#x20;       string LoyaltyTierCode

&#x20;       string LoyaltyTierName

&#x20;       decimal RequiredSpend

&#x20;       decimal DiscountPercent

&#x20;   }



&#x20;   FeedbackAccess {

&#x20;       guid FeedbackAccessId PK

&#x20;       guid OrderRef

&#x20;       string TokenHash

&#x20;       datetime ExpiredAt

&#x20;       datetime UsedAt

&#x20;   }



&#x20;   Feedback {

&#x20;       guid FeedbackId PK

&#x20;       guid AccessId FK

&#x20;       guid OrderRef

&#x20;       guid CustomerId FK "NULL"

&#x20;       int Star

&#x20;       string Comment

&#x20;       datetime SubmitedAt

&#x20;   }



&#x20;   FeedbackItem {

&#x20;       guid FeedbackItemId PK

&#x20;       guid FeedbackId FK

&#x20;       guid OrderItemRef

&#x20;       int Star

&#x20;       string Comment

&#x20;   }





