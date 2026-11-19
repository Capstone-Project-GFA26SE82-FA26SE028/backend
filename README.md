## Secrets and Docker startup

Keep passwords and tokens in `.env`, in the repository root, ignored by Git. On a new machine, copy `.env.example` there and fill in the empty values. Tracked appsettings contain non-secret settings only.

Run Docker (Compose automatically reads the ignored root `.env`):

```powershell
docker compose up -d --build
```

Docker uses one SQL password for service connection strings, one JWT signing key and shared Cloudinary credentials. PayOS credentials are configured per restaurant. This env file is for Docker only. Validate with `config --quiet` to avoid printing secrets.

For GitHub Actions deployment, store credentials in repository/environment Secrets and inject them as runtime environment variables. Build/test CI does not need production secrets. Previously committed credentials must be revoked or rotated; removing files does not remove Git history. Changing the env file alone does not change SQL passwords in existing volumes.

# Restaurant backend and AI assistant

The AI service calls Gemini API (`gemini-3.1-flash-lite` by default) for menu chat and cart-based recommendations. It sends only the information needed for the request and keeps the existing QR and Catalog checks.

## Run with Docker Compose

Set `GEMINI_API_KEY` in the ignored root `.env` (see `.env.example`). Then run this from the repository root:

```powershell
docker compose up -d --build
docker compose ps ai gateway
```

On Windows, `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Start-AI.ps1` checks Docker Engine and runs the same startup command. When changing the key, recreate the AI container with `docker compose up -d --force-recreate ai`. To run only the core backend, use `docker compose -f compose.yaml up -d --build`.

## Run the .NET 9 service directly

Set `Gemini__ApiKey` in the process environment, or copy `Services/ai_service/AI.API/appsettings.Local.json.example` to `appsettings.Local.json` in the same directory and fill in `Gemini:ApiKey`. The local JSON is ignored by Git and excluded from Docker build. The effective priority is command line, environment variables, local JSON, environment-specific appsettings, then shared appsettings. This native launch uses .NET configuration rather than the Compose `.env` file.

```powershell
dotnet run --project Services/ai_service/AI.API
```

Set the Restaurant, Catalog and Ordering gRPC addresses in local configuration if they differ from the defaults. For a native Gateway, set `GrpcServices:AI` to `http://localhost:5008`.

## Readiness and errors

- `/health/live` confirms the AI process can respond. Native health uses port 5009.
- `/health/ready` checks the key and model access through Gemini's model metadata, with a five-second budget. It does not run inference. Missing key or unavailable Gemini produces 503 on readiness without preventing Gateway and other backend services from starting.
- `docker compose exec -T ai dotnet AI.API.dll --check-ready` runs the container readiness check. Use `docker compose logs ai` to inspect startup. Use `docker compose config --quiet` to validate configuration without outputting secrets.
- AI requests have a 90-second total budget and the Restaurant, Catalog and Ordering RPCs have at most 10 seconds each within that budget. One inference runs per AI instance with no waiting queue. Busy or Gemini rate-limited requests return 429, unavailable provider 503, deadline 504, invalid model output 502 and missing QR credentials 401.

## Chat and recommendations through Gateway

Use the existing QR flow: resolve/scan QR, join the table session using its real join code, then use the **HttpOnly** `guest_access_<sessionId without dashes>` cookie set by `POST /api/table-sessions/{sessionId}/guest-access/join`. Staff bearer tokens do not replace QR access for AI. Browser requests must include credentials; the token is never included in the AI JSON body or sent to Gemini.

Menu chat:

```http
POST /api/ai/table-sessions/{sessionId}/chat
Content-Type: application/json

{"message":"What drinks can I order?","history":[]}
```

Optional history contains only `{"role":"user|assistant","content":"..."}` turns (500 characters per message/turn, four history turns by default). Chat defaults to Vietnamese. Explicit English questions receive English replies, and short neutral replies retain the conversation language. To continue, pass `result.conversationContext` from the last reply as `conversationContext` on the next chat request. The opaque context preserves the actual order of presented dishes, budget, supported preferences and exclusions for the same QR guest, for up to 30 minutes per reply. Start a new chat by omitting the context and history. Older clients can still send bounded history with the assistant's previous answer as returned by the API.

Example follow-up: `{"message":"Món thứ hai dưới 80.000 không? Mình không ăn cay nhé.","conversationContext":"<value-from-previous-response>"}`. The assistant rereads that dish from Catalog, compares its current price and directs the guest to staff for unverified dietary information. Warm, cool or light meal suggestions require supporting Catalog descriptions. Combined warm/cool and light preferences remain effective together. Excluding a dish group by name (for example, cháo) also applies to matching dishes on later turns until restored. If a dish disappears, its previous position is retained for follow-up references. Compose stores the context protection keys in the `ai-chat-keys` volume.

Cart recommendations, after creating/populating the real cart through existing Ordering endpoints:

```http
POST /api/ai/table-sessions/{sessionId}/carts/{cartId}/recommendations
Content-Type: application/json

{"limit":3}
```

Cart recommendation messages and controlled reasons are always Vietnamese. Quick add uses the existing cart PUT API with the latest cart version, the complete intended cart and required option selections. Ordering validates current Catalog prices and sellability before accepting the update. AI excludes **all option variants by ItemId**, only offers `CanOrder == true` items with verified prices, and rereads guest/cart/menu before returning cards. Cart changes during inference return 409. Required options must still be selected via the existing cart workflow. AI never calls cart Create/Update/Delete.

Missing verified ingredient/allergy/diet/spice facts: the assistant directs the customer to ask staff; it does not contact staff itself. There is currently no suitable public Restaurant-info RPC, so restaurant contact/hours/policy answers use the same missing-information approach. Both AI endpoints use the restaurant resolved from the verified table session. Catalog reads and cart checks stay within that restaurant. Catalog's `CanOrder` covers menu availability, price, active recipe and required options. Ingredient stock is checked by Inventory when the kitchen starts cooking.

## Verify the AI integration

```powershell
dotnet test Tests/AI.Tests/AI.Tests.csproj
```

These tests use test data and simulated dependencies. To test Gemini with Docker, start Compose, join an active QR test session through Gateway, and add at least one item to the test cart while leaving another orderable menu item available. Use the real guest cookie with the two requests above. The existing smoke script can run both requests and check that the cart version stays unchanged:

```powershell
.\scripts\Test-AI.ps1 -Gateway http://localhost:5000 -SessionId <session-guid> -CartId <cart-guid> -CookieFile <private-cookie-json>
```

## Checkout before payment

Checkout prepares the bill before collection. Cash collection is described below. Neither operation closes the table session, revokes guest access or marks the table for cleaning.

Update an existing Docker environment without deleting its databases:

```powershell
docker compose build db-init ordering billing gateway
docker compose run --rm db-init
docker compose up -d --no-deps ordering billing gateway
```

New databases use the checkout tables from the current BillingDb and OrderingDb schema scripts. For an existing database with an older checkout schema, verify the restaurant mappings and prepare a data-preserving upgrade for that database before running the current services. See the [database change log](docs/database-change-log.md) for the original checkout schema and ERD relationships.

### Chuyển yêu cầu thanh toán đến quầy mặc định

1. Thu ngân đúng nhà hàng mở ca bằng `POST /api/restaurants/{restaurantId}/cash-shifts/open`, body ví dụ `{ "openingCash": 0 }`. Nếu chưa có quầy, hệ thống tạo quầy mặc định cùng lúc mở ca. Nếu nhà hàng đã có đúng một quầy, dùng lại quầy đó. Với nhiều quầy cũ, kiểm tra cấu hình trước khi dùng luồng mặc định.
2. Phục vụ tiếp nhận yêu cầu thanh toán, sau đó gọi `GET /api/table-sessions/{sessionId}/payment-requests/{id}/review` để xem đơn chờ xác nhận và các món còn tính tiền theo Ordering.
3. Phục vụ phụ trách gọi `POST /api/table-sessions/{sessionId}/payment-requests/{id}/submit`, không gửi mã quầy. Đơn còn chờ xác nhận hoặc nhà hàng chưa có quầy mặc định sẽ trả `409`. Khi quầy đã tồn tại, yêu cầu gửi trong lúc đổi ca vẫn vào danh sách chờ. Thử lại cùng ID yêu cầu sẽ dùng lượt đã lưu.
4. Thu ngân đang đứng ca xem `GET /api/restaurants/{restaurantId}/payment-requests/incoming`. Sau khi mở ca mới, danh sách hiện cả yêu cầu tồn từ ca trước, yêu cầu gửi giữa hai ca và bill chưa thanh toán có một phiên bàn xác định. Bill cũ có thể trả `billId` cùng `checkoutId: null` và `submittedAt: null`. Khoản đã thanh toán và lịch sử giao dịch vẫn thuộc ca gốc.

Với BillingDb đã khởi tạo, áp dụng `docker/sql/upgrades/BillingCheckoutSubmission.sql` trước khi chạy bản mới. Compose khởi tạo database mới từ schema và giữ nguyên database đã có.

### Chốt hóa đơn và xác nhận đã đưa khách

1. Cashier đang đứng ca tại quầy nhận gọi `POST /api/table-sessions/{sessionId}/payment-requests/{id}/prepare`. Hệ thống khóa lại tập món tại Ordering, kiểm tra đơn chờ xác nhận rồi lập hoặc cập nhật bill từ bản chụp. Sau khi đối chiếu, hệ thống ghi trạng thái checkout `Prepared`.
2. Waiter phụ trách mở `GET /api/table-sessions/{sessionId}/payment-requests/{id}/bill` để lấy bill đưa khách kiểm tra.
3. Khi đã đưa bill thật, Waiter gọi `POST /api/table-sessions/{sessionId}/payment-requests/{id}/present`. Hệ thống ghi `PresentedAt` một lần và chuyển checkout sang `Presented`.

Hai trạng thái trên vẫn là bill `Unpaid`. Các API thu tiền của checkout cũ chưa xử lý được bill qua luồng này. BillingDb đang dùng cần áp dụng `BillingCheckoutSubmission.sql` rồi `BillingCheckoutPreparation.sql`. Bật đúng cấu hình chính sách VAT/phí trước khi lập bill mới.



Ordering uses `GrpcServices:Billing` and `Ordering:CheckoutCashierRoleIds`; both local and Docker appsettings contain the repository's current Cashier role ID. Identity and Restaurant still verify the actual account, login session and branch assignment. No account IDs or credentials are supplied by the checkout payload.

API sequence in Gateway Swagger:

1. Join a real open QR session through `POST /api/table-sessions/{sessionId}/guest-access/join`. Preserve the returned HttpOnly guest cookie, as with carts/orders.
2. Guest calls `POST /api/table-sessions/{sessionId}/checkout`, without a body. Set `Idempotency-Key` to a new UUID for this checkout and **reuse that same UUID on retries**. A second device can retrieve the existing checkout with `GET` on the same route; a different new key cannot replace an active checkout.
3. `WaitingConfirmation` means new orders are blocked, but already received pending orders still require the existing Waiter confirmation API. Nothing is automatically confirmed or omitted. `Frozen` means Ordering holds the item set and is waiting for Cashier synchronization.
4. Cashier sends `POST /api/table-sessions/{sessionId}/checkout/{checkoutId}/synchronize` with the normal bearer token, without a body. This resumes the same checkout and creates or refreshes the unpaid bill. `Ready` is returned only after its financial hash matches Ordering. Read bill amounts through the existing authorized `GET /api/bills/{billId}` endpoint. Missing tax/fee policy or invalid source data leaves checkout unready and returns a controlled error.
5. Only an assigned Cashier may send `POST /api/table-sessions/{sessionId}/checkout/{checkoutId}/cancel`, before any Payment exists. Only `Released` with `orderingReopened: true` confirms that Ordering acknowledged reopening. An old cancelled key cannot resurrect checkout; a deliberate new checkout needs a new UUID.

For PowerShell testing, use `Invoke-RestMethod -WebSession $guestSession` for QR requests and `-Headers @{ Authorization = "Bearer $cashierAccessToken" }` for Cashier requests. Do not send a staff token in place of the guest cookie when testing the customer flow.

`Preparing` or `ReleasePending`, HTTP 503, or a client timeout must not be treated as permission to order again. Retrieve the checkout and retry its original operation with the same ID. There is no automatic expiry/unlock. Cooking, Ready and Served progress does not change the financial hash; creating more orders or cancelling frozen items is blocked for the whole session. Cart editing alone does not submit an order.

Cash payment now consumes the matching Ready checkout through Billing's session lock and keeps Ordering blocked after success. Unknown outcomes must not release checkout. Customer departure, QR invalidation and cleaning confirmation remain separate staff workflows.


## Cash payment

Requires the existing Billing/Ordering checkout upgrades above, an enabled `Cash` payment method, a Ready checkout and the collecting Cashier's own Active shift at a register in the bill's assigned restaurant. Admin/Manager cannot collect payments. Admin can read receipts globally; Manager/Cashier can read within active restaurant assignments.

Update Docker without resetting data:

```powershell
docker compose build db-init billing gateway
docker compose run --rm db-init
docker compose up -d --no-deps billing gateway
```

For SQL Server outside Docker, run `docker/sql/upgrades/BillingCashMethod.sql` against BillingDb using the team's connection. This is **data only**: it inserts `Cash`/enabled if missing and preserves an existing disabled method. New databases also use `docker/sql/seed/BillingDb.sql`. No tables, columns, constraints or ERD relationships are added in this increment. Billing local/Docker configuration contains `CashPayment:MethodCode = Cash`.

After synchronizing checkout to Ready, the Cashier sends:

```http
POST /api/bills/{billId}/payments/cash
Authorization: Bearer <cashier-access-token>
Idempotency-Key: <new-UUID-for-this-payment>
Content-Type: application/json
```

```json
{
  "checkoutId": "<Ready-checkout-UUID>",
  "cashShiftId": "<your-Active-shift-UUID>",
  "cashReceived": 200000
}
```

Use the returned IDs rather than literal placeholder strings. The backend reads the bill total; the request cannot supply total, change, cashier identity or payment status. Cash received must be whole VND and cover a positive whole-VND bill. Zero-total bills and partial/mixed payments are not supported in this increment.

A new receipt returns 201 with Location `/api/payments/{paymentId}`. Read it through GET on that route. Response includes server amount, tender, change, cashier, UTC paid time and the Sale movement ID. For example, total 177871 and tender 200000 yield change 22129. Exactly one Sale movement records **177871**, not 200000; no second change movement is written.

Reuse the **same payment UUID and exact payload** after timeout/retry; successful replay returns 200 with the original receipt, even if that shift has since closed or Cash has been disabled. Current employee session/branch access is still required. Changed payload with the same UUID, a second payment key on a Paid bill, insufficient cash, pending/stale checkout, or another Cashier's shift are rejected. After an unknown outcome, read/retry the existing payment; do not start a new payment or cancel checkout based only on a client timeout.

Payment, Sale, Bill Paid/PaidAt and BillCheckout Paid are committed together. Checkout remains unreleased and Ordering remains frozen. For the current dine-in flow, Cashier confirmation of the Waiter's cash handover triggers session closure as described in B6. Checkout retries and synchronization preserve the paid checkout.

Verification distinguishes service tests with fake upstream clients, SQL Server locking/rollback tests, and Gateway tests using real services against isolated seeded test databases. See the database change log for the current increment's evidence and limits. Conditional skipped tests are not counted as verified SQL integration.


## Outside-bill cash movements

An assigned Cashier can record `CashIn` or `CashOut` only in their own single Active shift. Admin/Manager cannot post movements; Admin can read globally and Manager/Cashier can read within assigned restaurants. Closing/transferring a shift, editing/deleting entries, refunds and reconciliation are outside this increment.

No database upgrade is needed: **no schema changes or data seed changes**. Rebuild/restart the two affected services:

```powershell
docker compose build billing gateway
docker compose up -d --no-deps billing gateway
```

With a valid Cashier bearer token, use a new UUID in `Idempotency-Key` for each real transaction:

```http
POST /api/cash-shifts/{cashShiftId}/movements
Authorization: Bearer <cashier-access-token>
Idempotency-Key: <UUID>
Content-Type: application/json
```

```json
{
  "type": "CashOut",
  "amount": 100000,
  "reason": "Purchase cleaning supplies"
}
```

Amount is a positive whole-VND value; the backend stores CashIn as positive and CashOut as negative. Reason is required, trimmed, at most 1000 characters, without controls, and must be representable in the existing SQL varchar/CP1252 column. Unsupported text is rejected rather than silently corrupted; this increment does not change the database to Unicode.

A new entry returns 201 and Location `/api/cash-movements/{id}`. Retry the exact payload with the same UUID to receive 200/the original entry without posting again. A changed payload with that UUID returns 409. Successful replay remains readable after closing the shift, but new postings are rejected. Current account/session, branch access and ownership still apply. After a timeout, retry the same UUID rather than guessing the result or making a new request key.

Read:

```text
GET /api/cash-movements/{id}
GET /api/cash-shifts/{cashShiftId}/movements?page=1&pageSize=20
```

The ledger returns `openingCash`, `movementTotal`, `expectedCash`, and `movements` (paged items/count). Expected cash = opening cash + sum of **all** SignedAmount entries, regardless of page. A withdrawal cannot exceed this expected amount. Checking and writing use the same register/shift locks as cash payment, so simultaneous withdrawals and Payment sales cannot bypass this check. This is a book balance, not physically counted cash.

Payment already creates its own `Sale`. The manual endpoint rejects `Sale`, `PaymentId`, `SignedAmount`, performer fields and any unknown fields; do not enter a Payment receipt again as CashIn. The ledger sums CashMovement once and does not add Payment amounts again. A different UUID represents a different manual event; the system cannot determine whether two separately declared outside-bill events describe the same physical money.

No performer column is added. For new manual entries, `performedByRef` is derived from CashShift.CashierUserRef and `performerSource` is `ShiftOwnerDerived`, as approved. Shift owner transfer is not supported. For a linked Payment, the actor comes from Payment.ConfirmedByRef (`PaymentConfirmation`). Legacy entries without a verifiable source return null/`Unknown`. This is not an independent historical actor snapshot; changing owners directly in SQL would invalidate that inference. Actor IDs are never hidden in reason or idempotency keys.

Only Billing, its Gateway/protobuf integration and tests are changed. SQL Server locking tests use isolated databases with fake source/access clients; the separate Gateway run uses real services and seeded test databases. The database change log records results and any unverified cases.


## Close and reconcile a cash shift

Only an assigned Cashier can close their own Active shift. Admin reads globally; Manager/Cashier read within assigned restaurants. No database upgrade is required: ShiftReport and its unique CashShiftId relationship already exist.

```powershell
docker compose build billing gateway
docker compose up -d --no-deps billing gateway
```

Use the normal employee bearer token in Gateway Swagger:

```text
GET  /api/cash-shifts/{cashShiftId}/reconciliation
POST /api/cash-shifts/{cashShiftId}/close
GET  /api/cash-shifts/{cashShiftId}/report
GET  /api/shift-reports/{id}
GET  /api/shift-reports?restaurantId={optional-UUID}&page=1&pageSize=20
```

Preview returns opening cash, signed ledger total, expected cash, Sale/CashIn/CashOut totals, payment count and blocker codes/PaymentId/CashMovementId. CashOutTotal is the positive amount withdrawn for valid entries; MovementTotal keeps the stored signs. `canClose` indicates financial/lifecycle readiness, not permission for Admin/Manager to close. Preview is advisory: Close locks and checks the data again.

Count **all physical cash before removing any money for handover**. For example, if expected cash is 121221 and the entire drawer contains 121220:

```json
{
  "actualCash": 121220,
  "cashHandleOver": 100000,
  "differenceReason": "One VND shortage after counting all cash before removal"
}
```

ActualCash and CashHandleOver are both explicitly required, nonnegative whole VND within decimal(18,2); CashHandleOver cannot exceed ActualCash. If no money is handed over, enter 0 explicitly. Do not enter only the cash remaining after removal as ActualCash. The example yields Difference = -1 and RemainingCash = 21220.

ExpectedCash = OpeningCash + MovementTotal. For a valid cash ledger, MovementTotal is the sum of its signed entries. The backend supplies ExpectedCash, Difference = ActualCash - ExpectedCash, UTC ClosedAt and ownership. Reasons are mandatory for **both** shortage and surplus, max 1000 characters without controls, representable in the existing varchar/CP1252 field. Count fields, actor, expected cash and timestamps cannot be spoofed with extra JSON fields.

New close returns 201 with Location `/api/shift-reports/{id}`; repeating identical normalized inputs returns 200/the original report. No additional Idempotency-Key header is required: the shift and existing one-report-per-shift unique key identify the operation. Different closing inputs return 409; no report editing or reopening is implemented. After timeout/restart, GET the report or retry the original inputs. Never create replacement records based only on an uncertain client outcome.

Closing locks the register/shift using the same locks as cash Payment and outside-bill movements. Writes committed before close are included; new writes after close are rejected. Report creation and CashShift Closed/ClosedAt commit together. CashHandleOver is stored on the report only: it does not create another CashOut or alter the expected-cash calculation. It is not a recipient-confirmed handover workflow.

Closing accepts Cashier-verified cash payments with one matching Sale, including cash handed over by a Waiter and confirmed by the Cashier. Pending handovers block closing. Preview excludes any Sale linked to an unverified handover from the cash totals and lists the inconsistent movement. Verified PayOS payments appear under BankTransferTotal, and provider-confirmed zero-money attempts can be closed without a sale. Unresolved payments, refunds, duplicate payments or ledger keys, and inconsistent movements require review. Preview lists the blockers, and Close returns 409 with a summary.

Unpaid bills and checkout requests with no payment assigned to the shift stay available to the next Cashier at the same register. Closing the cash shift preserves their table sessions and checkout locks. Verified settlement closes its table session through B6. The next Cashier opens a new shift and enters its opening cash.

ShiftReport persists only the existing ExpectedCash, ActualCash, CashHandleOver, DifferenceReason and ClosedAt fields plus IDs. Difference and RemainingCash are recomputed from these stored values; reports are not recalculated from later ledger activity. CashierUserRef is derived from the shift owner, not a separate historical actor snapshot. Preview revenue/ledger breakdown is not a stored revenue breakdown; this is **cash reconciliation**, not full bank/PayOS settlement or a full immutable revenue Z-report. Manager can retrieve reports via the scoped list/detail APIs. Automatic delivery to Manager and device-session signout are not integrated in this increment.


## Billing B5: PayOS QR and bank transfer

The Cashier must own the active shift at the receiving register. The Waiter must have presented the bill and recorded that step as `Presented`. The bill must be current, unpaid and in whole VND. Existing PayOS attempts from a legacy `Ready` checkout can still be reconciled.

Apply `docker/sql/upgrades/BillingPayOs.sql` if an existing BillingDb lacks the three PayOS Payment fields or the BankTransfer method. The upgrade preserves disabled methods and existing data. Compose creates new databases from the backend schema and leaves upgrades for existing databases to the operator.

Set the public HTTPS Gateway base URL as `PAYOS_WEBHOOK_BASE_URL` for Compose or `PaymentProviderConfiguration__WebhookBaseUrl` for native development. An authorized restaurant admin uses `PUT /api/restaurants/{restaurantId}/payment-settings/payos` with `clientId`, `apiKey`, `checksumKey`, `returnUrl`, `cancelUrl` and `enabled`. Billing stores the keys encrypted and reads the selected restaurant's channel at runtime. Preserve the Data Protection key directory, which Compose mounts in the `payment-keys` volume.

```powershell
docker compose up -d --build billing gateway
```

Return and cancel URLs are customer-facing HTTPS pages. Register the returned `webhookUrl` in that restaurant's PayOS channel. The URL ends in `/api/restaurants/{restaurantId}/payments/payos/webhook` and routes to Gateway. Redirect visits do **not** settle or cancel a payment.

Public configuration: LinkLifetimeMinutes=15, RequestTimeoutSeconds=10, ReconcileIntervalSeconds=30, ReconcileBatchSize=20. Gateway's separate PayOsGateway:TimeoutSeconds defaults to 45, leaving existing cash/Billing request deadlines unchanged. Worker batches are bounded and rotate through unresolved/zero-terminal attempts. Turning Enabled off prevents new attempts; retained valid credentials still allow recovery of existing attempts. Removing credentials prevents reconciliation and leaves those attempts blocked for safety; do not rotate/remove them before resolving outstanding payments.

### Swagger sequence

Use employee Bearer authorization and the IDs returned by the payment-request workflow. The cashier creates the QR after the responsible waiter has recorded `Presented`.

```http
POST /api/bills/{billId}/payments/payos
Authorization: Bearer {cashier-access-token}
Idempotency-Key: {new-uuid-for-this-attempt}
Content-Type: application/json

{
  "checkoutId": "{presented-checkout-uuid}",
  "cashShiftId": "{own-active-shift-uuid}"
}
```

201 creates the durable attempt; 200 is a replay of the same key and same payload. Response contains expected Amount from the bill, ProviderOrderCode, provider LinkId/CheckoutUrl/QrCode when obtained, UTC ExpiresAt/PaidAt, Status and RequiresReview. No client-supplied amount, actor or paid flag is accepted. An accepted request can remain Unknown with no link if the provider outcome is uncertain: use the same key/payment ID; do not assume that HTTP 201 means money was received.

```http
GET  /api/payments/{paymentId}/payos
POST /api/payments/{paymentId}/payos/reconcile
POST /api/payments/{paymentId}/payos/cancel
GET  /api/table-sessions/{sessionId}/checkout/{checkoutId}/payments/payos
GET  /api/table-sessions/{sessionId}/checkout/{checkoutId}/payments/{paymentId}/payos
POST /api/restaurants/{restaurantId}/payments/payos/webhook
```

GET reads stored state. The two table-session routes let the responsible waiter read the payment result and obtain the QR while the link is payable. The waiter receives `paymentConfirmed` after verified settlement. Reconcile and Cancel require the owning Cashier in the assigned restaurant. Reconcile reads the provider. Cancel persists cancellation intent, then asks PayOS and verifies the result.

Webhook is intentionally independent of employee Bearer authorization and requires a valid PayOS HMAC signature. Send exactly the provider JSON envelope; do not call it with a fabricated Paid body. The backend verifies the signature and financial fields, records the existing PaymentWebhook audit with hash/dedupe key (not raw payload), then verifies the provider's signed payment query. Duplicate deliveries do not settle twice. A successful webhook acknowledgement can mean durable receipt awaiting provider reconciliation; inspect payment status.

When an authenticated success callback leaves a payment in `ReviewRequired`, Billing records the callback and completes its earlier pending webhook audits as `ReviewRequired` in the same transaction. The payment remains under review, and the callback is acknowledged without another automatic PayOS query.

Normal path: Creating -> Pending -> Paid. Network errors can produce Unknown; requested/local-expiry cancellation can remain CancelRequested. Only provider-confirmed terminal **zero money with explicit totals/empty transactions** produces verified Cancelled/Expired/Failed. Local expiry alone never releases checkout or permits another payment. ProviderAmountPaid/ProviderAmountRemaining are returned only from a fresh successful query, otherwise null; expected Amount is not a claim about received money.

After a verified zero attempt, the Cashier can create a new PayOS attempt with a new key or the Waiter can begin cash handover. Checkout remains held. Every creation rechecks the bill, checkout, shift and other attempts. Reusing a UUID for a different checkout, bill or shift is rejected. Timeout and restart retries reuse the stored order code.

Paid is recorded after exactly one verified full transfer matching the frozen bill and an active owning shift. Payment, Bill Paid/PaidAt and checkout Paid commit atomically. The transfer does not add a cash movement. Missing or unsigned provider totals leave the outcome unresolved. Partial or excess money, mismatched references, late settlements and conflicting signed deliveries require `ReviewRequired`.

Lost initial create responses may be recovered by order code. PayOS query can recover LinkId/payment status but does not promise CheckoutUrl/QR. Those stay null if unavailable; no fake link/QR is reconstructed. A still-missing provider order after local expiry remains unresolved rather than guessed failed. Merchant/provider review is required when a definitive result cannot be recovered.

Payment success keeps checkout held and Ordering frozen. Dine-in session closure follows B6 below. Shift reconciliation accepts verified Paid bank transfers and definite zero attempts, excludes transfers from expected cash and exposes BankTransferTotal in the live preview. Unresolved or review attempts block closing.

Paid PayOS payments remain eligible for background reconciliation while their shift is open. After closing, they remain eligible within `PayOs:PaidReconcileHours` from PaidAt (default 24 hours, configurable from 1 to 8760). Signed webhooks remain accepted outside that polling window. Additional money requires review; it never creates a second settlement or changes the recorded cash report. Before first closing a shift, Billing queries each PayOS attempt again outside the closing locks, then locks the shift and verifies that the same payment results still apply. Unavailable/uncertain provider responses or newly added/changed attempts block closing; retry the same closing inputs. Replaying an already closed report does not require another provider call. Preview alone is not a promise that close will succeed.

Opening cash must be whole VND: `250000` and `250000.00` are accepted, `250000.50` is rejected without rounding. CashIn and cash Sale validate `OpeningCash + existing SignedAmount total + new amount` under the same register/shift transaction locks against the `decimal(18,2)` limit. A rejected sale records no Payment, Sale or Paid bill/checkout. Existing fractional opening balances and already over-limit balances are not rewritten automatically; review those legacy records separately.

The Gateway integration tests use a signed PayOS HTTP test provider. Live acceptance requires real channel credentials, a registered public HTTPS webhook and an authorized test payment. See the [official PayOS testing guidance](https://payos.vn/docs/moi-truong-test/).

## Billing B6: Close the table session after payment

After a verified PayOS payment or Cashier confirmation of cash handover, Billing commits payment and asks Restaurant to close the session. Restaurant first checks the settled checkout with Billing. In one database transaction, it closes the session, releases its table links, revokes the existing guest access and completes the payment request. A new guest session can then be opened for the table. The Waiter responsible for the old request can still read its PayOS result.

Restaurant retries acknowledged payment requests for sessions still open. If Restaurant or Billing is temporarily unavailable, the payment already committed remains paid and closure resumes after connectivity returns. Repeat requests preserve the original closure time. B6 uses the existing `Notifications:ServiceKey` to authenticate both internal gRPC calls. Compose already supplies this key to both services using `NOTIFICATION_SERVICE_KEY` or its existing JWT key fallback. For native runs, the two services use the same existing `Notifications__ServiceKey`. There is no new public closure endpoint.

Confirmed items can be billed before they are served. Kitchen tasks can still reach Ready after session closure, and an assigned Waiter can read the order and mark a ready item from the paid checkout Served through the existing endpoints. The closed guest session cannot submit more orders.

## Billing B8: Recover uncertain PayOS outcomes

When the PayOS response times out, use the existing payment ID for status or reconciliation and the original idempotency key for a create replay. Billing keeps the checkout held while the outcome is unknown. The reconciliation worker queries the stored PayOS order code, including after a restart. If link creation succeeded but its response was lost, a query can recover the provider link ID and financial status. The QR and checkout URL remain unavailable when PayOS does not return them.

After a timed-out cancellation, another payment remains blocked until PayOS confirms a terminal state with zero money received. Billing then allows a new payment attempt for the same checkout. Disabling link creation for a restaurant still permits reconciliation of existing attempts. Cancellation of the local request stops its provider calls and leaves the saved attempt available to the worker.
