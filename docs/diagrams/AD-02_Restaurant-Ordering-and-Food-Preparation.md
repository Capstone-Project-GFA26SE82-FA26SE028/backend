

flowchart LR



&#x20;   %% ==========================================

&#x20;   %% SWIMLANE 1: CUSTOMER

&#x20;   %% ==========================================

&#x20;   subgraph CUSTOMER\["Customer"]

&#x20;       direction TB



&#x20;       C\_START((START))

&#x20;       C\_SCAN\["Scan QR"]

&#x20;       C\_MENU\["View Menu"]

&#x20;       C\_SELECT\["Select Items"]

&#x20;       C\_NEED\_WAITER{"Need Waiter<br/>Assistance?"}

&#x20;       

&#x20;       C\_CUSTOMIZE\["Customize"]

&#x20;       C\_ACCEPT{"Accept<br/>Recommendation?"}

&#x20;       C\_ADD\["Add recommended item"]

&#x20;       C\_REVIEW\["Review Cart"]

&#x20;       C\_CONFIRM\["Confirm Order"]

&#x20;       C\_SUBMIT\["Submit Order"]



&#x20;       C\_STATUS\_RECEIVED\["View Status:<br/>ORDER RECEIVED"]

&#x20;       C\_STATUS\_COOKING\["View Status:<br/>COOKING"]

&#x20;       C\_STATUS\_READY\["View Status:<br/>READY"]

&#x20;       C\_STATUS\_SERVED\["View Status:<br/>SERVED"]



&#x20;       C\_END((END))

&#x20;   end



&#x20;   %% ==========================================

&#x20;   %% SWIMLANE 2: AI RESTAURANT ASSISTANT

&#x20;   %% ==========================================

&#x20;   subgraph AI\_ASSISTANT\["AI RESTAURANT ASSISTANT"]

&#x20;       direction TB



&#x20;       AI\_ANALYZE\["Analyze Cart"]

&#x20;       AI\_RECOMMEND\["Recommend Side<br/>Dishes / Drinks"]

&#x20;   end



&#x20;   %% ==========================================

&#x20;   %% SWIMLANE 3: WAITER

&#x20;   %% ==========================================

&#x20;   subgraph WAITER\["Waiter"]

&#x20;       direction TB



&#x20;       W\_ASSIST\["Assist Customer with<br/>Ordering"]

&#x20;       W\_CREATE\_ORDER\["Create / Adjust Order"]

&#x20;       W\_RECEIVE\_NOTIFICATION\["Receive<br/>Ready Notification"]

&#x20;       W\_SERVE\["Serve Order to<br/>Customer"]

&#x20;       W\_STATUS\_SERVED\["Set Status: SERVED"]

&#x20;   end



&#x20;   %% ==========================================

&#x20;   %% SWIMLANE 4: KITCHEN STAFF

&#x20;   %% ==========================================

&#x20;   subgraph KITCHEN\["KITCHEN STAFF"]

&#x20;       direction TB



&#x20;       K\_RECEIVE\_ORDER\["Receive Order in<br/>real-time"]

&#x20;       K\_STATUS\_RECEIVED\["Set Status:<br/>ORDER RECEIVED"]

&#x20;       K\_REVIEW\_ITEMS\["Review Items \&<br/>Notes"]

&#x20;       K\_MANAGE\_QUEUE\["Manage Order Queue"]

&#x20;       K\_START\_PREPARING\["Start Preparing Order"]

&#x20;       K\_STATUS\_COOKING\["Set Status:<br/>COOKING"]

&#x20;       K\_PREPARE\_FOOD\["Prepare Food"]

&#x20;       K\_FINISH\_PREPARING\["Finish Preparing<br/>Order"]

&#x20;       K\_STATUS\_READY\["Set Status: READY"]

&#x20;       K\_NOTIFY\_WAITER\["Notify Waiter"]

&#x20;   end



&#x20;   %% ==========================================

&#x20;   %% CUSTOMER INITIAL ACTIVITIES

&#x20;   %% ==========================================

&#x20;   C\_START --> C\_SCAN

&#x20;   C\_SCAN --> C\_MENU

&#x20;   C\_MENU --> C\_SELECT

&#x20;   C\_SELECT --> C\_NEED\_WAITER



&#x20;   %% ==========================================

&#x20;   %% WAITER ASSISTANCE DECISION

&#x20;   %% ==========================================

&#x20;   C\_NEED\_WAITER -->|Yes| W\_ASSIST

&#x20;   C\_NEED\_WAITER -->|No| C\_CUSTOMIZE



&#x20;   W\_ASSIST --> W\_CREATE\_ORDER



&#x20;   %% ==========================================

&#x20;   %% AI CART ANALYSIS AND RECOMMENDATION

&#x20;   %% ==========================================

&#x20;   C\_CUSTOMIZE --> AI\_ANALYZE

&#x20;   AI\_ANALYZE --> AI\_RECOMMEND

&#x20;   AI\_RECOMMEND --> C\_ACCEPT



&#x20;   C\_ACCEPT -->|Yes| C\_ADD

&#x20;   C\_ACCEPT -->|No| C\_REVIEW



&#x20;   C\_ADD --> C\_REVIEW



&#x20;   %% ==========================================

&#x20;   %% ORDER REVIEW AND CONFIRMATION

&#x20;   %% ==========================================

&#x20;   C\_REVIEW --> C\_CONFIRM

&#x20;   W\_CREATE\_ORDER --> C\_CONFIRM

&#x20;   C\_CONFIRM --> C\_SUBMIT



&#x20;   %% ==========================================

&#x20;   %% KITCHEN RECEIVES ORDER

&#x20;   %% ==========================================

&#x20;   C\_SUBMIT --> K\_RECEIVE\_ORDER

&#x20;   K\_RECEIVE\_ORDER --> K\_STATUS\_RECEIVED



&#x20;   K\_STATUS\_RECEIVED --> C\_STATUS\_RECEIVED

&#x20;   K\_STATUS\_RECEIVED --> K\_REVIEW\_ITEMS



&#x20;   %% ==========================================

&#x20;   %% ORDER PREPARATION PROCESS

&#x20;   %% ==========================================

&#x20;   K\_REVIEW\_ITEMS --> K\_MANAGE\_QUEUE

&#x20;   K\_MANAGE\_QUEUE --> K\_START\_PREPARING

&#x20;   K\_START\_PREPARING --> K\_STATUS\_COOKING



&#x20;   K\_STATUS\_COOKING --> C\_STATUS\_COOKING

&#x20;   K\_STATUS\_COOKING --> K\_PREPARE\_FOOD



&#x20;   K\_PREPARE\_FOOD --> K\_FINISH\_PREPARING

&#x20;   K\_FINISH\_PREPARING --> K\_STATUS\_READY



&#x20;   %% ==========================================

&#x20;   %% ORDER READY AND WAITER NOTIFICATION

&#x20;   %% ==========================================

&#x20;   K\_STATUS\_READY --> C\_STATUS\_READY

&#x20;   K\_STATUS\_READY --> K\_NOTIFY\_WAITER



&#x20;   K\_NOTIFY\_WAITER --> W\_RECEIVE\_NOTIFICATION

&#x20;   W\_RECEIVE\_NOTIFICATION --> W\_SERVE



&#x20;   %% ==========================================

&#x20;   %% SERVING ORDER AND COMPLETION

&#x20;   %% ==========================================

&#x20;   W\_SERVE --> W\_STATUS\_SERVED

&#x20;   W\_STATUS\_SERVED --> C\_STATUS\_SERVED

&#x20;   C\_STATUS\_SERVED --> C\_END



