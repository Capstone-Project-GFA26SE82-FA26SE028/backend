

flowchart LR



&#x20;   %% ==========================================

&#x20;   %% SWIMLANE 1: CUSTOMER

&#x20;   %% ==========================================

&#x20;   subgraph CUSTOMER\["Customer"]

&#x20;       direction TB



&#x20;       C\_START((START))

&#x20;       C\_SEND\_REQUEST\["Send request"]



&#x20;       C\_RECEIVE\_COMPLETION\["Receive Completion<br/>Confirmation"]

&#x20;       C\_END\_ASSISTANCE((END))



&#x20;       C\_CHOOSE\_PAYMENT\["Choose payment<br/>method"]

&#x20;       C\_PAYMENT\_METHOD{"Payment<br/>Method?"}



&#x20;       C\_CASH\["Cash"]

&#x20;       C\_BANKING\["Banking"]



&#x20;       C\_RECEIVE\_RECEIPT\["Receive Receipt /<br/>Loyalty Points"]

&#x20;       C\_END\_PAYMENT((END))

&#x20;   end



&#x20;   %% ==========================================

&#x20;   %% SWIMLANE 2: WAITER

&#x20;   %% ==========================================

&#x20;   subgraph WAITER\["Waiter"]

&#x20;       direction TB



&#x20;       W\_RECEIVE\_REQUEST\["Receive Request"]

&#x20;       W\_REQUEST\_TYPE{"Request<br/>Type?"}



&#x20;       W\_ASSISTANCE\["ASSISTANCE"]

&#x20;       W\_HANDLE\_REQUEST\["Handle Request"]

&#x20;       W\_CONFIRM\_COMPLETION\["Confirm completion"]



&#x20;       W\_PAYMENT\["PAYMENT"]

&#x20;   end



&#x20;   %% ==========================================

&#x20;   %% SWIMLANE 3: CASHIER

&#x20;   %% ==========================================

&#x20;   subgraph CASHIER\["Cashier"]

&#x20;       direction TB



&#x20;       CA\_REVIEW\_BILL\["Review Bill"]

&#x20;       CA\_SPLIT\_MERGE{"Split / Merge<br/>Bill?"}

&#x20;       CA\_SPLIT\_MERGE\_BILL\["Split / Merge Bill"]



&#x20;       CA\_APPLY\_PROMOTIONS\["Apply promotions /<br/>vouchers / points"]

&#x20;       CA\_DETERMINE\_TOTAL\["Determine total<br/>amount"]



&#x20;       CA\_RECEIVE\_CASH\["Receive Cash"]

&#x20;       CA\_FULL\_AMOUNT{"Full Amount<br/>Received?"}

&#x20;       CA\_WAIT\_REMAINING\["Wait for remaining<br/>amount"]

&#x20;       CA\_CONFIRM\_CASH\["Confirm Cash<br/>Payment"]



&#x20;       CA\_ISSUE\_RECEIPT\["Issue Receipt"]

&#x20;   end



&#x20;   %% ==========================================

&#x20;   %% SWIMLANE 4: SYSTEM

&#x20;   %% ==========================================

&#x20;   subgraph SYSTEM\["System"]

&#x20;       direction TB



&#x20;       S\_WAIT\_CONFIRMATION\["Wait for<br/>Transaction Confirmation"]

&#x20;       S\_PAYMENT\_CONFIRMED{"Payment<br/>Confirmed?"}



&#x20;       S\_SET\_PAID\["Set Status: PAID"]

&#x20;       S\_CONTINUE\_WAITING\["Continue Waiting"]



&#x20;       S\_RECORD\_POINTS\["Record Loyalty Points"]

&#x20;   end



&#x20;   %% ==========================================

&#x20;   %% INITIAL REQUEST

&#x20;   %% ==========================================

&#x20;   C\_START --> C\_SEND\_REQUEST

&#x20;   C\_SEND\_REQUEST --> W\_RECEIVE\_REQUEST



&#x20;   W\_RECEIVE\_REQUEST --> W\_REQUEST\_TYPE



&#x20;   %% ==========================================

&#x20;   %% REQUEST TYPE DECISION

&#x20;   %% ==========================================

&#x20;   W\_REQUEST\_TYPE --> W\_ASSISTANCE

&#x20;   W\_REQUEST\_TYPE --> W\_PAYMENT



&#x20;   %% ASSISTANCE FLOW

&#x20;   W\_ASSISTANCE --> W\_HANDLE\_REQUEST

&#x20;   W\_HANDLE\_REQUEST --> W\_CONFIRM\_COMPLETION



&#x20;   W\_CONFIRM\_COMPLETION --> C\_RECEIVE\_COMPLETION

&#x20;   C\_RECEIVE\_COMPLETION --> C\_END\_ASSISTANCE



&#x20;   %% ==========================================

&#x20;   %% PAYMENT AND BILLING FLOW

&#x20;   %% ==========================================

&#x20;   W\_PAYMENT --> CA\_REVIEW\_BILL



&#x20;   CA\_REVIEW\_BILL --> CA\_SPLIT\_MERGE



&#x20;   CA\_SPLIT\_MERGE -->|Yes| CA\_SPLIT\_MERGE\_BILL

&#x20;   CA\_SPLIT\_MERGE -->|No| CA\_APPLY\_PROMOTIONS



&#x20;   CA\_SPLIT\_MERGE\_BILL --> CA\_APPLY\_PROMOTIONS



&#x20;   CA\_APPLY\_PROMOTIONS --> CA\_DETERMINE\_TOTAL



&#x20;   CA\_DETERMINE\_TOTAL --> C\_CHOOSE\_PAYMENT



&#x20;   %% ==========================================

&#x20;   %% PAYMENT METHOD DECISION

&#x20;   %% ==========================================

&#x20;   C\_CHOOSE\_PAYMENT --> C\_PAYMENT\_METHOD



&#x20;   C\_PAYMENT\_METHOD --> C\_CASH

&#x20;   C\_PAYMENT\_METHOD --> C\_BANKING



&#x20;   %% ==========================================

&#x20;   %% CASH PAYMENT FLOW

&#x20;   %% ==========================================

&#x20;   C\_CASH --> CA\_RECEIVE\_CASH



&#x20;   CA\_RECEIVE\_CASH --> CA\_FULL\_AMOUNT



&#x20;   CA\_FULL\_AMOUNT -->|No| CA\_WAIT\_REMAINING

&#x20;   CA\_WAIT\_REMAINING --> CA\_RECEIVE\_CASH



&#x20;   CA\_FULL\_AMOUNT -->|Yes| CA\_CONFIRM\_CASH



&#x20;   CA\_CONFIRM\_CASH --> S\_SET\_PAID



&#x20;   %% ==========================================

&#x20;   %% BANKING PAYMENT FLOW

&#x20;   %% ==========================================

&#x20;   C\_BANKING --> S\_WAIT\_CONFIRMATION



&#x20;   S\_WAIT\_CONFIRMATION --> S\_PAYMENT\_CONFIRMED



&#x20;   S\_PAYMENT\_CONFIRMED -->|Yes| S\_SET\_PAID



&#x20;   S\_PAYMENT\_CONFIRMED -->|No| S\_CONTINUE\_WAITING



&#x20;   S\_CONTINUE\_WAITING --> S\_WAIT\_CONFIRMATION



&#x20;   %% ==========================================

&#x20;   %% PAYMENT COMPLETION

&#x20;   %% ==========================================

&#x20;   S\_SET\_PAID --> CA\_ISSUE\_RECEIPT



&#x20;   CA\_ISSUE\_RECEIPT --> S\_RECORD\_POINTS



&#x20;   S\_RECORD\_POINTS --> C\_RECEIVE\_RECEIPT



&#x20;   C\_RECEIVE\_RECEIPT --> C\_END\_PAYMENT



