erDiagram

&#x20;   Category ||--|{ Item : "contains"

&#x20;   Item ||--|{ Price : "has"

&#x20;   Item |o--|{ RecipeVersion : "has"

&#x20;   Option |o--|{ RecipeVersion : "has"

&#x20;   RecipeVersion ||--|{ RecipeIngredient : "includes"

&#x20;   RecipeVersion ||--|{ RecipeMedia : "has"

&#x20;   OptionGroup ||--|{ Option : "contains"

&#x20;   Item ||--|{ ItemOptionGroup : "maps\_to"

&#x20;   OptionGroup ||--|{ ItemOptionGroup : "used\_in"



&#x20;   RecipeIngredient {

&#x20;       guid RecipeIngredientId PK

&#x20;       guid RecipeVersionId FK

&#x20;       guid IngredientRef

&#x20;       guid UnitRef

&#x20;       decimal Quantity

&#x20;       decimal BaseQuantitySnapshot

&#x20;       decimal ConversionFactorSnapshot

&#x20;   }



&#x20;   RecipeMedia {

&#x20;       guid RecipeMediaId PK

&#x20;       guid RecipeVersionId FK

&#x20;       string MediaType

&#x20;       string Url

&#x20;       int DisplayOrder

&#x20;   }



&#x20;   RecipeVersion {

&#x20;       guid RecipeVersionId PK

&#x20;       guid ItemId FK "NULL"

&#x20;       guid OptionId FK "NULL"

&#x20;       int Version

&#x20;       datetime EffectiveFrom

&#x20;       datetime EffectiveTo

&#x20;       string Instructions

&#x20;   }



&#x20;   Price {

&#x20;       guid PriceId PK

&#x20;       guid ItemId FK

&#x20;       string Channel

&#x20;       decimal Amount

&#x20;       datetime EffectiveFrom

&#x20;       datetime EffectiveTo

&#x20;       timeonly DailyStart

&#x20;       timeonly DailyEnd

&#x20;   }



&#x20;   Category {

&#x20;       guid CategoryId PK

&#x20;       string CategoryCode

&#x20;       string CategoryName

&#x20;       bool IsActive

&#x20;   }



&#x20;   Item {

&#x20;       guid ItemId PK

&#x20;       guid CategoryId FK

&#x20;       string ItemCode

&#x20;       string ItemName

&#x20;       string Description

&#x20;       string ImageUrl

&#x20;       string Availability

&#x20;       int PreparationTimeMinutes

&#x20;       guid KitchenStationRef "NULL"

&#x20;       rowversion Version

&#x20;   }



&#x20;   Option {

&#x20;       guid OptionId PK

&#x20;       guid OptionGroupId FK

&#x20;       string OptionName

&#x20;       decimal ExtraPrice

&#x20;       string Availability

&#x20;   }



&#x20;   OptionGroup {

&#x20;       guid OptionGroupId PK

&#x20;       string OptionGroupName

&#x20;   }



&#x20;   ItemOptionGroup {

&#x20;       guid ItemId PK, FK

&#x20;       guid OptionGroupId PK, FK

&#x20;       int MinSelection

&#x20;       int MaxSelection

&#x20;   }





