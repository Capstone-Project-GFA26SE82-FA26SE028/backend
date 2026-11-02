erDiagram

&#x20;   Cart ||--|{ CartItem : "contains"

&#x20;   CartItem ||--|{ CartItemOption : "has"

&#x20;   Order ||--|{ OrderItem : "contains"

&#x20;   OrderItem ||--|{ ServiceRecord : "has"

&#x20;   OrderItem ||--|{ CancellationRequest : "has"

&#x20;   OrderItem ||--|{ OrderItemOption : "has"

&#x20;   OrderItem ||--|{ KitchenTask : "generates"

&#x20;   KitchenStation ||--|{ KitchenTask : "handles"

&#x20;   KitchenStation ||--|{ KitchenAssignment : "has"

&#x20;   KitchenTask ||--|{ Event : "logs"



&#x20;   Cart {

&#x20;       guid CartId PK

&#x20;       guid TableSessionRef

&#x20;       guid GuestAccessRef "NULL"

&#x20;       datetime ExpiredAt

&#x20;   }



&#x20;   CartItem {

&#x20;       guid CartItemId PK

&#x20;       guid CartId FK

&#x20;       guid ItemRef

&#x20;       int Quantity

&#x20;       string Note

&#x20;   }



&#x20;   CartItemOption {

&#x20;       guid CartItemOptionId PK

&#x20;       guid CartItemId FK

&#x20;       guid OptionRef

&#x20;       int Quantity

&#x20;   }



&#x20;   CheckoutLock {

&#x20;       guid CheckoutId PK

&#x20;       guid BillIdRef "NULL"

&#x20;       guid TableSessionRef

&#x20;       string SourceHash "NULL"

&#x20;       string Status

&#x20;       datetime CreatedAt

&#x20;       datetime ReleasedAt "NULL"

&#x20;   }



&#x20;   OrderItem {

&#x20;       guid OrderItemId PK

&#x20;       guid OrderId FK

&#x20;       guid ItemRef

&#x20;       guid RecipeVersionRef "NULL"

&#x20;       guid OriginalTableRef "NULL"

&#x20;       guid ComboGroupId "NULL"

&#x20;       string NameSnapshot

&#x20;       decimal UnitPriceSnapshot

&#x20;       int Quantity

&#x20;       int CancelledQuantity

&#x20;       string Note

&#x20;       string Status

&#x20;       rowversion Version

&#x20;   }



&#x20;   ServiceRecord {

&#x20;       guid ServiceRecordId PK

&#x20;       guid OrderItemId FK

&#x20;       int QuantityServed

&#x20;       guid WaiterUserRef

&#x20;       datetime ServedAt

&#x20;       string IdempotencyKey

&#x20;   }



&#x20;   CancellationRequest {

&#x20;       guid CancellationRequestId PK

&#x20;       guid OrderItemId FK

&#x20;       int Quantity

&#x20;       string Reason

&#x20;       guid RequestedByRef

&#x20;       guid DecidedByRef "NULL"

&#x20;       string Status

&#x20;       datetime RequestedAt

&#x20;   }



&#x20;   Order {

&#x20;       guid OrderId PK

&#x20;       string OrderNumber

&#x20;       guid TableSessionRef "NULL"

&#x20;       guid CustomerRef "NULL"

&#x20;       string Channel

&#x20;       guid CreatedByRef "NULL"

&#x20;       string IdempotencyKey

&#x20;       string RequestHash

&#x20;       string CreatedAt

&#x20;       string Status

&#x20;       rowversion Version

&#x20;       datetime ComfirmedAt

&#x20;       guid ComfirmedByRef "NULL"

&#x20;   }



&#x20;   KitchenStation {

&#x20;       guid KitchenStationId PK

&#x20;       string KitchenStationCode

&#x20;       string KitchenStationName

&#x20;       bool IsActive

&#x20;   }



&#x20;   KitchenAssignment {

&#x20;       guid KitchenStationId PK, FK

&#x20;       guid UserRef

&#x20;   }



&#x20;   KitchenTask {

&#x20;       guid KitchenTaskId PK

&#x20;       guid KitchenStationId FK

&#x20;       guid OrderItemId FK

&#x20;       int BatchNumber

&#x20;       int Quantity

&#x20;       string Status

&#x20;       int Priority

&#x20;       datetime QueuedAt

&#x20;       datetime CookingAt

&#x20;       datetime ReadyAt

&#x20;       int EstimatedMinutes

&#x20;       rowversion Version

&#x20;   }



&#x20;   Event {

&#x20;       guid EventId PK

&#x20;       guid KitchenTaskId FK

&#x20;       string FromStatus

&#x20;       string ToStatus

&#x20;       guid ActorUserRef "NULL"

&#x20;       datetime OccurredAt

&#x20;   }



&#x20;   OrderItemOption {

&#x20;       guid OrderItemOptionId PK

&#x20;       guid OrderItemId FK

&#x20;       guid OptionRef

&#x20;       string NameSnapshot

&#x20;       decimal ExtraPriceSnapshot

&#x20;       int Quantity

&#x20;   }





