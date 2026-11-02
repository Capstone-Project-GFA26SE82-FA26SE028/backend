

flowchart LR



&#x20;   %% ==========================================

&#x20;   %% SWIMLANE 1: CASHIER

&#x20;   %% ==========================================

&#x20;   subgraph CASHIER\["CASHIER"]

&#x20;       direction TB



&#x20;       C\_START((START))



&#x20;       C\_LOGIN\["Login / Receive Shift"]



&#x20;       C\_ENTER\_OPENING\["Enter Opening Cash"]



&#x20;       C\_CONFIRM\_OPENING\["Enter / Confirm<br/>Opening Cash"]



&#x20;       C\_RECHECK\["Recheck Opening Cash"]



&#x20;       C\_RECORD\["Record Transactions<br/>\& Cash In/Out<br/>During Shift"]



&#x20;       C\_COUNT\["Count Actual Cash"]



&#x20;       C\_REVIEW\_DIFF\["Review Difference"]



&#x20;       C\_EXPLANATION\["Enter Explanation"]



&#x20;       C\_HANDOVER\["Handover Shift"]



&#x20;       C\_LOGOUT\["Logout"]



&#x20;       C\_END((END))

&#x20;   end



&#x20;   %% ==========================================

&#x20;   %% SWIMLANE 2: SYSTEM

&#x20;   %% ==========================================

&#x20;   subgraph SYSTEM\["SYSTEM"]

&#x20;       direction TB



&#x20;       S\_VALIDATE\_OPENING\["Validate Opening Cash"]



&#x20;       S\_OPENING\_CORRECT{"Opening<br/>Cash Correct?"}



&#x20;       S\_OPEN\_SHIFT\["Open Shift"]



&#x20;       S\_UPDATE\_LEDGER\["Update Shift Cash<br/>Ledger"]



&#x20;       S\_RECONCILE\["Reconcile System<br/>Cash \& Actual Cash"]



&#x20;       S\_DIFFERENCE{"Difference?"}



&#x20;       S\_GENERATE\_REPORT\["Generate Z-Report"]



&#x20;       S\_FORK\[" "]



&#x20;       S\_CLOSE\_SHIFT\["Close Shift"]

&#x20;   end



&#x20;   %% ==========================================

&#x20;   %% SWIMLANE 3: MANAGER

&#x20;   %% ==========================================

&#x20;   subgraph MANAGER\["MANAGER"]

&#x20;       direction TB



&#x20;       M\_VIEW\_REPORT\["Receive / View<br/>Z-Report"]

&#x20;   end



&#x20;   %% ==========================================

&#x20;   %% 1. SHIFT INITIALIZATION

&#x20;   %% ==========================================

&#x20;   C\_START --> C\_LOGIN



&#x20;   C\_LOGIN --> C\_ENTER\_OPENING



&#x20;   C\_ENTER\_OPENING --> S\_VALIDATE\_OPENING



&#x20;   %% ==========================================

&#x20;   %% 2. OPENING CASH VALIDATION

&#x20;   %% ==========================================

&#x20;   S\_VALIDATE\_OPENING --> S\_OPENING\_CORRECT



&#x20;   S\_OPENING\_CORRECT -->|Yes| S\_OPEN\_SHIFT



&#x20;   S\_OPENING\_CORRECT -->|No| C\_RECHECK



&#x20;   C\_RECHECK --> C\_CONFIRM\_OPENING



&#x20;   C\_CONFIRM\_OPENING --> S\_VALIDATE\_OPENING



&#x20;   %% Preserve the opening cash confirmation flow

&#x20;   C\_ENTER\_OPENING --> S\_VALIDATE\_OPENING



&#x20;   %% ==========================================

&#x20;   %% 3. TRANSACTIONS DURING SHIFT

&#x20;   %% ==========================================

&#x20;   S\_OPEN\_SHIFT --> C\_RECORD



&#x20;   C\_RECORD --> S\_UPDATE\_LEDGER



&#x20;   S\_UPDATE\_LEDGER --> C\_COUNT



&#x20;   %% ==========================================

&#x20;   %% 4. CASH RECONCILIATION

&#x20;   %% ==========================================

&#x20;   C\_COUNT --> S\_RECONCILE



&#x20;   S\_RECONCILE --> S\_DIFFERENCE



&#x20;   %% ==========================================

&#x20;   %% 5. DIFFERENCE HANDLING

&#x20;   %% ==========================================

&#x20;   S\_DIFFERENCE -->|Yes| C\_REVIEW\_DIFF



&#x20;   C\_REVIEW\_DIFF --> C\_EXPLANATION



&#x20;   C\_EXPLANATION --> S\_GENERATE\_REPORT



&#x20;   S\_DIFFERENCE -->|No| S\_GENERATE\_REPORT



&#x20;   %% ==========================================

&#x20;   %% 6. GENERATE Z-REPORT AND CLOSE SHIFT

&#x20;   %% ==========================================

&#x20;   S\_GENERATE\_REPORT --> S\_FORK



&#x20;   %% Parallel activities after Z-Report generation

&#x20;   S\_FORK --> S\_CLOSE\_SHIFT



&#x20;   S\_FORK --> M\_VIEW\_REPORT



&#x20;   %% ==========================================

&#x20;   %% 7. SHIFT HANDOVER AND LOGOUT

&#x20;   %% ==========================================

&#x20;   S\_CLOSE\_SHIFT --> C\_HANDOVER



&#x20;   C\_HANDOVER --> C\_LOGOUT



&#x20;   C\_LOGOUT --> C\_END



&#x20;   %% ==========================================

&#x20;   %% FORK BAR STYLING

&#x20;   %% ==========================================

&#x20;   style S\_FORK fill:#000,stroke:#000,stroke-width:6px,color:#000



