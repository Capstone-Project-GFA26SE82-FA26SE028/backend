erDiagram

&#x20;   Ingredient ||--|{ GoodsReceiptItem : "included\_in"

&#x20;   GoodsReceipt ||--|{ GoodsReceiptItem : "contains"

&#x20;   Ingredient ||--|{ StockMovement : "tracks"

&#x20;   GoodsReceiptItem |o--|| StockMovement : "generates"

&#x20;   StocktakeItem |o--|{ StockMovement : "adjusts"

&#x20;   Stocktake ||--|{ StocktakeItem : "contains"

&#x20;   Ingredient ||--|{ StocktakeItem : "counted\_in"



&#x20;   Ingredient {

&#x20;       guid IngredientId PK

&#x20;       string IngredientCode

&#x20;       string IngredientName

&#x20;       string BaseUnit

&#x20;       decimal StockQuantity

&#x20;       decimal LowStockThreshold

&#x20;       rowversion StockVersion

&#x20;       bool IsActive

&#x20;   }



&#x20;   GoodsReceiptItem {

&#x20;       guid GoodsReceiptItemId PK

&#x20;       guid GoodsReceiptId FK

&#x20;       guid IngredientId FK

&#x20;       decimal ReceivedQuantity

&#x20;       dateonly ExpiryDate

&#x20;   }



&#x20;   GoodsReceipt {

&#x20;       guid GoodsReceiptId PK

&#x20;       string ReceiptCode

&#x20;       string Status

&#x20;       guid CreatedByRef

&#x20;       datetime CreatedAt

&#x20;       datetime ComfirmAt

&#x20;       string Note

&#x20;   }



&#x20;   StockMovement {

&#x20;       guid StockMovementId PK

&#x20;       guid IngredientId FK

&#x20;       guid GoodsReceiptItemId FK "NULL"

&#x20;       guid StocktakeItemId FK "NULL"

&#x20;       string MovementType

&#x20;       decimal QuantityChange

&#x20;       guid CookingEventRef "NULL"

&#x20;       guid OrderItemRef "NULL"

&#x20;       guid PerformedByRef "NULL"

&#x20;       string Reason

&#x20;       datetime CreatedAt

&#x20;   }



&#x20;   Stocktake {

&#x20;       guid StocktakeId PK

&#x20;       string StocktakeCode

&#x20;       string Status

&#x20;       guid CreatedByRef

&#x20;       guid ApprovedByRef "NULL"

&#x20;       datetime CreatedAt

&#x20;       datetime ConfirmedAt

&#x20;   }



&#x20;   StocktakeItem {

&#x20;       guid StocktakeItemId PK

&#x20;       guid StocktakeId FK

&#x20;       guid IngredientId FK

&#x20;       decimal SystemQuantity

&#x20;       decimal ActualQuantity

&#x20;       binary SnapshotVersion

&#x20;       string DiscrepancyReason

&#x20;   }





