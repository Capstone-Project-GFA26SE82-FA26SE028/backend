

flowchart LR



&#x20;   %% ==========================================

&#x20;   %% SWIMLANE 1: KITCHEN STAFF

&#x20;   %% ==========================================

&#x20;   subgraph KITCHEN\["KITCHEN STAFF"]

&#x20;       direction TB



&#x20;       K\_REPORT\_SHORTAGE\["Report Ingredient<br/>Shortage / Out-of-Stock"]



&#x20;       K\_RECEIVE\_KDS\["Receive New KDS<br/>Order Ticket<br/>(Status: Pending)"]



&#x20;       K\_ITEM\_CANCELLED{"Item Cancelled<br/>while Pending?"}



&#x20;       K\_START\_COOKING\["Start Cooking<br/>(Update Status:<br/>Pending → Cooking)"]

&#x20;   end



&#x20;   %% ==========================================

&#x20;   %% SWIMLANE 2: INVENTORY STAFF

&#x20;   %% ==========================================

&#x20;   subgraph INVENTORY\["INVENTORY STAFF"]

&#x20;       direction TB



&#x20;       I\_START((START))



&#x20;       I\_MANAGE\_RAW\["Manage Raw<br/>Ingredients \& UOM<br/>(Base/Purchase Units)"]



&#x20;       I\_CONFIG\_BOM\["Configure Recipe \&<br/>BOM (Bill of Materials)"]



&#x20;       I\_RECEIVE\_ALERT\["Receive Alert \&<br/>Purchase Ingredients"]



&#x20;       I\_ENTER\_STOCK\["Enter Direct Stock<br/>Inbound (Item, Actual<br/>Qty, UOM, Expiry Date)"]



&#x20;       I\_MANAGE\_LOT\["Manage Lot, Expiry \&<br/>Stock Operations"]



&#x20;       I\_CREATE\_STOCKTAKE\["Create Stocktake<br/>Session \& Lock<br/>Transactions"]



&#x20;       I\_PERFORM\_COUNT\["Perform Physical<br/>Count \& Enter Actual<br/>Stock"]



&#x20;       I\_REVIEW\_VARIANCE\["Review Variance,<br/>Enter Reason \&<br/>Create Adjustment"]



&#x20;       I\_VARIANCE\_THRESHOLD{"Variance<br/>Exceeds Approval<br/>Threshold?"}

&#x20;   end



&#x20;   %% ==========================================

&#x20;   %% SWIMLANE 3: RESTAURANT MANAGER

&#x20;   %% ==========================================

&#x20;   subgraph MANAGER\["RESTAURANT MANAGER"]

&#x20;       direction TB



&#x20;       M\_REVIEW\_WASTAGE\["Review Wastage<br/>Report \& Stock<br/>Adjustment"]



&#x20;       M\_APPROVE\_ADJUSTMENT{"Approve<br/>Adjustment?"}

&#x20;   end



&#x20;   %% ==========================================

&#x20;   %% SWIMLANE 4: SYSTEM

&#x20;   %% ==========================================

&#x20;   subgraph SYSTEM\["SYSTEM"]

&#x20;       direction TB



&#x20;       S\_SAVE\_BOM\["Save BOM \& Auto-<br/>Calculate Dish COGS"]



&#x20;       S\_TRIGGER\_ALERT\["Trigger Low-Stock /<br/>Expiry Alert"]



&#x20;       S\_VALID\_INFO{"Valid<br/>Info \& Expiry<br/>Date?"}



&#x20;       S\_CREATE\_BATCH\["Create Lot/Batch Record,<br/>Increase Stock \& Clear<br/>Low-Stock Alert"]



&#x20;       S\_MARK\_CANCELLED\["Mark Ticket<br/>Cancelled (No Stock<br/>Deduction)"]



&#x20;       S\_PARSE\_BOM\["Parse BOM \& Auto-<br/>Deduct Stock by Lot<br/>(FIFO)"]



&#x20;       S\_UPDATE\_BALANCE\["Update Real-Time<br/>Stock Balance"]



&#x20;       S\_STOCK\_ALERT{"Below Min Stock or<br/>Near/Past<br/>Expiry?"}



&#x20;       S\_CALCULATE\_VARIANCE\["Calculate Stock<br/>Variance (Actual vs.<br/>System)"]



&#x20;       S\_UPDATE\_INVENTORY\["Update Inventory \&<br/>Record New Stock<br/>Balance"]



&#x20;       S\_END((END))

&#x20;   end



&#x20;   %% ==========================================

&#x20;   %% 1. INITIAL INVENTORY CONFIGURATION

&#x20;   %% ==========================================

&#x20;   I\_START --> I\_MANAGE\_RAW

&#x20;   I\_MANAGE\_RAW --> I\_CONFIG\_BOM



&#x20;   I\_CONFIG\_BOM --> S\_SAVE\_BOM



&#x20;   %% ==========================================

&#x20;   %% 2. INGREDIENT SHORTAGE AND ALERT

&#x20;   %% ==========================================

&#x20;   S\_SAVE\_BOM --> K\_REPORT\_SHORTAGE



&#x20;   K\_REPORT\_SHORTAGE --> S\_TRIGGER\_ALERT



&#x20;   S\_TRIGGER\_ALERT --> I\_RECEIVE\_ALERT



&#x20;   %% ==========================================

&#x20;   %% 3. PURCHASE AND STOCK INBOUND

&#x20;   %% ==========================================

&#x20;   I\_RECEIVE\_ALERT --> I\_ENTER\_STOCK



&#x20;   I\_ENTER\_STOCK --> S\_VALID\_INFO



&#x20;   S\_VALID\_INFO -->|No| I\_ENTER\_STOCK



&#x20;   S\_VALID\_INFO -->|Yes| S\_CREATE\_BATCH



&#x20;   %% ==========================================

&#x20;   %% 4. KDS ORDER AND CANCELLATION CHECK

&#x20;   %% ==========================================

&#x20;   S\_CREATE\_BATCH --> K\_RECEIVE\_KDS



&#x20;   K\_RECEIVE\_KDS --> K\_ITEM\_CANCELLED



&#x20;   K\_ITEM\_CANCELLED -->|Yes| S\_MARK\_CANCELLED



&#x20;   K\_ITEM\_CANCELLED -->|No| K\_START\_COOKING



&#x20;   %% ==========================================

&#x20;   %% 5. FIFO STOCK DEDUCTION

&#x20;   %% ==========================================

&#x20;   K\_START\_COOKING --> S\_PARSE\_BOM



&#x20;   S\_PARSE\_BOM --> S\_UPDATE\_BALANCE



&#x20;   %% ==========================================

&#x20;   %% 6. LOW-STOCK AND EXPIRY MONITORING

&#x20;   %% ==========================================

&#x20;   S\_UPDATE\_BALANCE --> S\_STOCK\_ALERT



&#x20;   S\_STOCK\_ALERT -->|Yes| S\_TRIGGER\_ALERT



&#x20;   S\_STOCK\_ALERT -->|No| I\_MANAGE\_LOT



&#x20;   %% ==========================================

&#x20;   %% 7. STOCKTAKE AND PHYSICAL COUNT

&#x20;   %% ==========================================

&#x20;   I\_MANAGE\_LOT --> I\_CREATE\_STOCKTAKE



&#x20;   I\_CREATE\_STOCKTAKE --> I\_PERFORM\_COUNT



&#x20;   I\_PERFORM\_COUNT --> S\_CALCULATE\_VARIANCE



&#x20;   S\_CALCULATE\_VARIANCE --> I\_REVIEW\_VARIANCE



&#x20;   %% ==========================================

&#x20;   %% 8. VARIANCE REVIEW AND APPROVAL

&#x20;   %% ==========================================

&#x20;   I\_REVIEW\_VARIANCE --> I\_VARIANCE\_THRESHOLD



&#x20;   I\_VARIANCE\_THRESHOLD -->|No| S\_UPDATE\_INVENTORY



&#x20;   I\_VARIANCE\_THRESHOLD -->|Yes| M\_REVIEW\_WASTAGE



&#x20;   M\_REVIEW\_WASTAGE --> M\_APPROVE\_ADJUSTMENT



&#x20;   M\_APPROVE\_ADJUSTMENT -->|No| I\_REVIEW\_VARIANCE



&#x20;   M\_APPROVE\_ADJUSTMENT -->|Yes| S\_UPDATE\_INVENTORY



&#x20;   %% ==========================================

&#x20;   %% 9. UPDATE INVENTORY AND END

&#x20;   %% ==========================================

&#x20;   S\_UPDATE\_INVENTORY --> S\_END



