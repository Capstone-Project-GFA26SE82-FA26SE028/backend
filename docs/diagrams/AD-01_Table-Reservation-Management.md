flowchart LR



&#x20;   %% =========================

&#x20;   %% SWIMLANE 1: CUSTOMER

&#x20;   %% =========================

&#x20;   subgraph Customer\["Customer"]

&#x20;       direction TB



&#x20;       C\_Start(\[Start])

&#x20;       C\_Submit\[Customer submits a table reservation request]

&#x20;       C\_Invalid\[Invalid information notification]

&#x20;       C\_Resubmit\[Resubmit revised detail]



&#x20;       C\_GetSuggestion\[Get suggestions for a different time]

&#x20;       C\_Accept{Accepting the new time?}

&#x20;       C\_Cancel\[Cancel request]

&#x20;       C\_EndCancel(\[End])



&#x20;       C\_ReceiveRejection\[Receive a rejection result]

&#x20;       C\_EndReject(\[End])



&#x20;       C\_ReceiveConfirmation\[Receive reservation confirmation]

&#x20;       C\_Arrive\[Arrive at the restaurant]

&#x20;       C\_Overdue\[Overdue reservation notification]

&#x20;       C\_EndOverdue(\[End])

&#x20;   end



&#x20;   %% =========================

&#x20;   %% SWIMLANE 2: WAITER / CASHIER

&#x20;   %% =========================

&#x20;   subgraph Staff\["Waiter / Cashier"]

&#x20;       direction TB



&#x20;       W\_Review\[Review reservation request]

&#x20;       W\_Approve{Approve?}

&#x20;       W\_Rejection\[Rejection notice]

&#x20;       W\_ConfirmArrival\[Confirm guest arrival]

&#x20;   end



&#x20;   %% =========================

&#x20;   %% SWIMLANE 3: SYSTEM

&#x20;   %% =========================

&#x20;   subgraph System\["System"]

&#x20;       direction TB



&#x20;       S\_Valid{Valid details?}

&#x20;       S\_Available{Has table available?}

&#x20;       S\_Suggest\[Suggest another time]

&#x20;       S\_Forward\[Forward the request to the staff member]



&#x20;       S\_Hold\[Hold the table for the scheduled time]

&#x20;       S\_Confirm\[Confirm reservation]



&#x20;       S\_ArrivalPeriod{Guests arriving during the holding period?}

&#x20;       S\_NoShow\[No-show]

&#x20;       S\_CancelRelease\[Cancel and release table]



&#x20;       S\_UpdateStatus\[Update table status]

&#x20;       S\_ReceiveGuests\[Receive guests]

&#x20;       S\_End(\[End])

&#x20;   end



&#x20;   %% =========================

&#x20;   %% RESERVATION REQUEST

&#x20;   %% =========================

&#x20;   C\_Start --> C\_Submit

&#x20;   C\_Submit --> S\_Valid



&#x20;   S\_Valid -->|No| C\_Invalid

&#x20;   C\_Invalid --> C\_Resubmit

&#x20;   C\_Resubmit --> C\_Submit



&#x20;   S\_Valid -->|Yes| S\_Available



&#x20;   %% =========================

&#x20;   %% TABLE AVAILABILITY

&#x20;   %% =========================

&#x20;   S\_Available -->|No| S\_Suggest

&#x20;   S\_Suggest --> C\_GetSuggestion

&#x20;   C\_GetSuggestion --> C\_Accept



&#x20;   C\_Accept -->|No| C\_Cancel

&#x20;   C\_Cancel --> C\_EndCancel



&#x20;   C\_Accept -->|Yes| W\_Review



&#x20;   S\_Available -->|Yes| S\_Forward

&#x20;   S\_Forward --> W\_Review



&#x20;   %% =========================

&#x20;   %% STAFF REVIEW AND APPROVAL

&#x20;   %% =========================

&#x20;   W\_Review --> W\_Approve



&#x20;   W\_Approve -->|No| W\_Rejection

&#x20;   W\_Rejection --> C\_ReceiveRejection

&#x20;   C\_ReceiveRejection --> C\_EndReject



&#x20;   W\_Approve -->|Yes| S\_Hold

&#x20;   S\_Hold --> S\_Confirm

&#x20;   S\_Confirm --> C\_ReceiveConfirmation



&#x20;   %% =========================

&#x20;   %% GUEST ARRIVAL / NO-SHOW

&#x20;   %% =========================

&#x20;   C\_ReceiveConfirmation --> S\_ArrivalPeriod



&#x20;   S\_ArrivalPeriod -->|Yes| C\_Arrive

&#x20;   C\_Arrive --> W\_ConfirmArrival

&#x20;   W\_ConfirmArrival --> S\_UpdateStatus

&#x20;   S\_UpdateStatus --> S\_ReceiveGuests

&#x20;   S\_ReceiveGuests --> S\_End



&#x20;   S\_ArrivalPeriod -->|No| S\_NoShow

&#x20;   S\_NoShow --> S\_CancelRelease

&#x20;   S\_CancelRelease --> C\_Overdue

&#x20;   C\_Overdue --> C\_EndOverdue

