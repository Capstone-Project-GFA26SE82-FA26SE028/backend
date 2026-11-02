erDiagram

&#x20;   Restaurant ||--|{ OpeningHour : "has"

&#x20;   Restaurant ||--|{ RestaurantStaff : "has"

&#x20;   Restaurant ||--|{ Area : "has"

&#x20;   Area ||--|{ Table : "contains"

&#x20;   Table ||--|{ Qr : "has"

&#x20;   Table ||--|{ ReservationTable : "assigned\_to"

&#x20;   Reservation ||--|{ ReservationTable : "includes"

&#x20;   Table ||--|{ SessionTable : "used\_in"

&#x20;   TableSession ||--|{ SessionTable : "has"

&#x20;   Reservation |o--|{ TableSession : "initiates"

&#x20;   TableSession ||--|{ ServiceRequest : "generates"

&#x20;   TableSession ||--|{ GuestAccess : "grants"



&#x20;   Qr {

&#x20;       guid QrId PK

&#x20;       guid TableId FK

&#x20;       string TokenHash

&#x20;       string ImageUrl

&#x20;       datetime CreatedAt

&#x20;       datetime RevokedAt

&#x20;   }



&#x20;   OpeningHour {

&#x20;       guid OpeningHourId PK

&#x20;       guid RestaurantId FK

&#x20;       int DayOfWeek

&#x20;       timeonly OpenAt

&#x20;       timeonly CloseAt

&#x20;   }



&#x20;   RestaurantStaff {

&#x20;       guid RestaurantId PK, FK

&#x20;       guid UserIdRef PK

&#x20;       bool IsActive

&#x20;   }



&#x20;   Restaurant {

&#x20;       guid RestaurantId PK

&#x20;       string RestaurantName

&#x20;       string TaxCode

&#x20;       string Address

&#x20;       string Hotline

&#x20;       string LogoUrl

&#x20;       string TimeZone

&#x20;   }



&#x20;   Table {

&#x20;       guid TableId PK

&#x20;       guid AreaId FK

&#x20;       string TableCode

&#x20;       int Capacity

&#x20;       decimal PositionX

&#x20;       decimal PositionY

&#x20;       bool IsActive

&#x20;       string CleaningStatus

&#x20;       rowversion Version

&#x20;   }



&#x20;   Area {

&#x20;       guid AreaId PK

&#x20;       guid RestaurantId FK

&#x20;       string AreaCode

&#x20;       string AreaName

&#x20;       string ServiceType

&#x20;       string SpaceType

&#x20;       bool IsActive

&#x20;   }



&#x20;   ReservationTable {

&#x20;       guid ReservationId PK, FK

&#x20;       guid TableId PK, FK

&#x20;   }



&#x20;   SessionTable {

&#x20;       guid SessionTableId PK

&#x20;       guid TableSessionId FK

&#x20;       guid TableId FK

&#x20;       datetime AssignedAt

&#x20;       datetime ReleasedAt

&#x20;       guid ChangedByRef "NULL"

&#x20;       string MoveReason

&#x20;   }



&#x20;   TableSession {

&#x20;       guid TableSessionId PK

&#x20;       guid ReservationId FK "NULL"

&#x20;       int PartySize

&#x20;       string Status

&#x20;       datetime OpenedAt

&#x20;       datetime ClosedAt

&#x20;       guid OpenedByRef "NULL"

&#x20;       string JoinCodeHash

&#x20;       datetime JoinCodeSetAt

&#x20;       guid LoyaltyCustomerRef "NULL"

&#x20;       datetime PrebillPrintedAt

&#x20;   }



&#x20;   Reservation {

&#x20;       guid ReservationId PK

&#x20;       guid RestaurantId FK "NULL"

&#x20;       guid RequestedAreaId FK "NULL"

&#x20;       string ContactName

&#x20;       string Phone

&#x20;       int PartySize

&#x20;       datetime StartAt

&#x20;       datetime EndAt

&#x20;       datetime HoldUntil

&#x20;       datetime ComfirmAt

&#x20;       datetime CancelledAt

&#x20;       datetime CancelledByRef

&#x20;       string Status

&#x20;       guid ApprovedByRef "NULL"

&#x20;   }



&#x20;   ServiceRequest {

&#x20;       guid ServiceRequestId PK

&#x20;       guid TableSessionId FK

&#x20;       string Type

&#x20;       string Status

&#x20;       datetime RequestedAt

&#x20;       datetime AcknowledgedAt

&#x20;       datetime CompletedAt

&#x20;       guid HandledByRef "NULL"

&#x20;   }



&#x20;   GuestAccess {

&#x20;       guid GuestAccessId PK

&#x20;       guid TableSessionId FK

&#x20;       string TokenHash

&#x20;       datetime CreatedAt

&#x20;       datetime ExpiredAt

&#x20;       datetime RevokedAt

&#x20;   }





