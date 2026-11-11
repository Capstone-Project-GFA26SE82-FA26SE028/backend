## Secrets and Docker startup

Keep passwords and tokens in `.env`, in the repository root, ignored by Git. On a new machine, copy `.env.example` there and fill in the empty values. Tracked appsettings contain non-secret settings only.

Run Docker (Compose automatically reads the ignored root `.env`):

```powershell
docker compose up -d --build
```

Docker uses one SQL password for all service connection strings, one JWT signing key, and shared Cloudinary and PayOS credentials. This env file is for Docker only. Validate with `config --quiet` to avoid printing secrets.

For GitHub Actions deployment, store credentials in repository/environment Secrets and inject them as runtime environment variables. Build/test CI does not need production secrets. Previously committed credentials must be revoked or rotated; removing files does not remove Git history. Changing the env file alone does not change SQL passwords in existing volumes.

# Restaurant backend and AI assistant

Cart recommendations and menu chat use the Gemini API for inference. Menu chat uses backend-controlled Vietnamese/English response templates, with Gemini identifying language, intent, topic and known dish keys. Unsupported languages are refused without an assistant answer; dish names remain as verified in Catalog.

## Configure and start

Docker: add `GEMINI_API_KEY=<your key>` to the ignored root `.env`, retaining the existing backend credentials. Then run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Start-AI.ps1
```

The normal `docker compose up -d --build` also includes AI. Gateway is at `http://localhost:5000/swagger` unless its port is overridden. To start just the core backend, use `docker compose -f compose.yaml up -d --build`.

Local: copy `Services/ai_service/AI.API/appsettings.Local.json.example` to ignored `appsettings.Local.json` in the same directory and fill `Gemini:ApiKey`. Start the other backend services normally, then:

```powershell
dotnet run --project Services/ai_service/AI.API/AI.API.csproj
```

Configuration priority, last wins: shared/environment appsettings, Development user secrets, appsettings.Local.json, environment variables, command line. Local runtime environment variable: `Gemini__ApiKey`. Root `.env` is loaded by Compose, not by native dotnet run. Keys are server-side and must not be committed or sent by the browser.

Gemini endpoint, model (`gemini-3.1-flash-lite`), output-token limit, temperature, minimal thinking level and prompt-size limit are configurable in AI appsettings.

## Request workflow and limits

Existing routes and QR cookies remain unchanged:

```http
POST /api/ai/table-sessions/{sessionId}/chat
Content-Type: application/json

{"message":"What drinks can I order?","history":[]}
```

```http
POST /api/ai/table-sessions/{sessionId}/carts/{cartId}/recommendations
Content-Type: application/json

{"limit":3}
```

Join a real QR session first using the existing guest-access join API. Gateway forwards the HttpOnly `guest_access_<sessionId without dashes>` cookie internally; staff bearer tokens do not replace guest access. Browser calls need credentials.

AI sends only the bounded conversation and public menu/cart projection to Gemini, never guest tokens or session credentials. Gemini is called directly at `POST /v1beta/models/{model}:generateContent`, using system instructions and a JSON response schema. No search, URL retrieval, code execution or function-calling tools are enabled. No database writes or extra public endpoints are introduced.

The total request limit remains 90 seconds, including dependency reads and all Gemini calls. One active inference per AI process; busy requests return 429 immediately. Cancellation propagates to the HTTP request. There is no provider polling, automatic retry, alternate-model fallback or local model setup.

UC2 asks Gemini only for bounded language/intent/topic codes and known candidate keys, never customer-facing prose. Backend renders prices, orderability, required-option guidance and notices from controlled Vietnamese/English templates. Generated answer/usedKeys fields from legacy or malicious provider responses are ignored. Unsupported language returns 400 without an assistant answer; invalid/missing language or topic codes return 502. Language/intent recognition still depends on Gemini and must be evaluated; no claim is made that every language or question is classified correctly.

QR rechecks, candidate validation, current Catalog reads, budget checks and 409 on changed menu facts remain in place. Explicit dietary questions use a classification-only request without reading menu facts, followed by the backend safety notice; no model translation can replace it. Detailed option choices/surcharges are not in the current AI projection, so the assistant does not invent them. Orderability is not a verified ingredient-stock guarantee. UC1 and the public HTTP/gRPC contracts are unchanged.

A normal chat makes one Gemini call within the 90-second Gateway budget. Prices in response templates use VND and the current verified Catalog amount; changing language does not convert currency. No second-model semantic verifier is used. Provider duration logging, cancellation, HTTP/2 fallback, bounded history, quota behavior and the lack of automatic retries remain unchanged.

Recommendations exclude all cart variants by ItemId and require CanOrder. Ingredient/allergy/diet/spice facts are not inferred; missing facts direct customers to staff without contacting staff. Public restaurant-information contracts remain unavailable, so those questions use the existing fallback.

## Health and verification

AI uses gRPC port 5008 and HTTP health port 5009. `/health/live` reports process liveness. `/health/ready` checks that an API key is configured and returns `ConfiguredNotProviderVerified` or 503 `ApiKeyMissing`. It does not spend credits probing Gemini and does not establish that the key, balance or provider is working.

Missing key/provider authorization or network failure returns controlled 503, rate limiting 429, invalid output 502 and deadline 504. No local fallback is selected.

```powershell
dotnet test Tests/AI.Tests/AI.Tests.csproj
.\scripts\Test-AI.ps1 -Gateway http://localhost:5000 -SessionId <session-guid> -CartId <cart-guid> -CookieFile .\.ai\guest-cookie.json -OutputFile .\.ai\gateway-ai-result.json
```

Default tests use simulated HTTP/service responses and in-process Gateway integration. The script uses real running services, a valid guest cookie/cart and Gemini, may consume credits, and reports actual timings. Passing simulated tests does not establish real Gemini latency or production end-to-end readiness.

Official documentation: [Gemini generateContent](https://ai.google.dev/api/generate-content), [structured output](https://ai.google.dev/gemini-api/docs/structured-output), [pricing and free tier](https://ai.google.dev/gemini-api/docs/pricing).

## Checkout before payment

Checkout prepares the bill before collection. Cash collection is described below. Neither operation closes the table session, revokes guest access or marks the table for cleaning.

Update an existing Docker environment without deleting its databases:

```powershell
docker compose build db-init ordering billing gateway
docker compose run --rm db-init
docker compose up -d --no-deps ordering billing gateway
```

For an existing database outside Docker, run `docker/sql/upgrades/BillingCheckout.sql` against **BillingDb** and `docker/sql/upgrades/OrderingCheckout.sql` against **OrderingDb**, using the team's normal SQL Server connection. These are repeatable upgrades; do not run a database reset script. New databases use the updated schema scripts. The two new tables and ERD relationships are recorded in [the database change log](docs/database-change-log.md).

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

Payment, Sale, Bill Paid/PaidAt and BillCheckout Paid are committed together. Checkout remains unreleased; Ordering remains frozen. Guest QR access and the open TableSession are preserved, and previously confirmed cooking/serving can continue. Neither checkout retries nor synchronization may revert Paid or reopen ordering. This is cash collection and a JSON receipt, not PayOS, printing, refunds, shift closing, outside-sales cash movements or table departure/cleaning.

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

ExpectedCash = OpeningCash + SUM(CashMovement.SignedAmount). The backend supplies ExpectedCash, Difference = ActualCash - ExpectedCash, UTC ClosedAt and ownership. Reasons are mandatory for **both** shortage and surplus, max 1000 characters without controls, representable in the existing varchar/CP1252 field. Count fields, actor, expected cash and timestamps cannot be spoofed with extra JSON fields.

New close returns 201 with Location `/api/shift-reports/{id}`; repeating identical normalized inputs returns 200/the original report. No additional Idempotency-Key header is required: the shift and existing one-report-per-shift unique key identify the operation. Different closing inputs return 409; no report editing or reopening is implemented. After timeout/restart, GET the report or retry the original inputs. Never create replacement records based only on an uncertain client outcome.

Closing locks the register/shift using the same locks as cash Payment and outside-bill movements. Writes committed before close are included; new writes after close are rejected. Report creation and CashShift Closed/ClosedAt commit together. CashHandleOver is stored on the report only: it does not create another CashOut or alter the expected-cash calculation. It is not a recipient-confirmed handover workflow.

Closing accepts the implemented Cash/Paid/Cashier flow with exactly one matching Sale, and the verified PayOS flow described below. Unknown/non-Paid states, unsupported methods/provider flows, refunds, Waiter CashHandover, duplicate outstanding/settled bill payments, duplicate ledger keys, orphan/mismatched sales and inconsistent movements block close with identifiable records. A provider-confirmed zero-money terminal PayOS attempt does not count as an outstanding/settled payment. In particular, no handover status is guessed to mean completed when that workflow has no implemented contract. Resolve those records through their supported workflow; this API never silently marks them Paid, failed, expired or cancelled. Preview has the full blocker list; Close returns 409 with a short summary.

Unpaid bills/checkout attempts have no CashShift binding and are not assigned to a shift by assumption. They do not prevent closing an otherwise valid cash shift, and remain unchanged/frozen as applicable. Payment success and shift close do not close the table session, invalidate QR, reopen ordering, mark cleaning or log out Identity. Existing Open shift remains the explicit way to begin a subsequent shift; RemainingCash is not silently copied into its OpeningCash.

ShiftReport persists only the existing ExpectedCash, ActualCash, CashHandleOver, DifferenceReason and ClosedAt fields plus IDs. Difference and RemainingCash are recomputed from these stored values; reports are not recalculated from later ledger activity. CashierUserRef is derived from the shift owner, not a separate historical actor snapshot. Preview revenue/ledger breakdown is not a stored revenue breakdown; this is **cash reconciliation**, not full bank/PayOS settlement or a full immutable revenue Z-report. Manager can retrieve reports via the scoped list/detail APIs. Automatic delivery to Manager and device-session signout are not integrated in this increment.


## Billing: automatic PayOS bank transfer (increment 6)

Prerequisites: the existing authenticated Cashier, assigned restaurant, own Active shift, held Ready checkout and current Unpaid whole-VND bill. Apply `docker/sql/upgrades/BillingPayOs.sql` to an existing BillingDb before running the new binary. Fresh creation uses `docker/sql/schema/BillingDb.sql`; Compose initialization applies the upgrade automatically. The upgrade adds only three nullable Payment metadata columns and inserts BankTransfer when absent; it preserves a disabled BankTransfer row. Never run the creation script over an existing database.

PayOS is disabled by default. No merchant credentials are committed. For native development:

```powershell
Copy-Item Services/billing_service/Billing.API/appsettings.Local.example.json Services/billing_service/Billing.API/appsettings.Local.json
# Edit the ignored Local file with the real merchant credentials, HTTPS URLs and Enabled=true.
dotnet run --project Services/billing_service/Billing.API
```

Billing explicitly loads configuration in this order: standard ASP.NET configuration (appsettings/environment file, Development user secrets if any), optional ignored `appsettings.Local.json`, environment variables, command-line arguments. Later sources win. Native environment keys use `PayOs__ClientId`, `PayOs__ApiKey`, `PayOs__ChecksumKey`, etc. `PayOs:BaseUrl` defaults to the official `https://api-merchant.payos.vn`; the model of operation is merchant-configured PayOS, not a guessed bank account.

Compose accepts PAYOS_ENABLED, PAYOS_CLIENT_ID, PAYOS_API_KEY, PAYOS_CHECKSUM_KEY, PAYOS_RETURN_URL, PAYOS_CANCEL_URL and PAYOS_WEBHOOK_URL. Put those in `.env`, or the shell environment. Keep `.env` ignored by Git; never commit credentials. Only `.env.example` is tracked.

```powershell
docker compose up -d --build billing gateway
```

ReturnUrl/CancelUrl are valid customer-facing HTTPS pages. WebhookUrl is the public HTTPS URL ending `/api/payments/payos/webhook`, routed to Gateway. Configure/confirm this same URL in the merchant PayOS channel; storing it in appsettings does not register it remotely. Redirect visits do **not** settle/cancel a payment. Production requires HTTPS. `AllowInsecureTestTransport` is recognized only in the explicit `PayOsTest` environment and is for isolated test doubles; it never permits HTTP in Production/Docker/Development.

Public configuration: LinkLifetimeMinutes=15, RequestTimeoutSeconds=10, ReconcileIntervalSeconds=30, ReconcileBatchSize=20. Gateway's separate PayOsGateway:TimeoutSeconds defaults to 45, leaving existing cash/Billing request deadlines unchanged. Worker batches are bounded and rotate through unresolved/zero-terminal attempts. Turning Enabled off prevents new attempts; retained valid credentials still allow recovery of existing attempts. Removing credentials prevents reconciliation and leaves those attempts blocked for safety; do not rotate/remove them before resolving outstanding payments.

### Swagger sequence

Use employee Bearer authorization, not the QR cookie, for these payment APIs. The existing guest checkout and Cashier synchronization must already have produced a Ready checkout. Use actual IDs returned by those APIs.

```http
POST /api/bills/{billId}/payments/payos
Authorization: Bearer {cashier-access-token}
Idempotency-Key: {new-uuid-for-this-attempt}
Content-Type: application/json

{
  "checkoutId": "{ready-checkout-uuid}",
  "cashShiftId": "{own-active-shift-uuid}"
}
```

201 creates the durable attempt; 200 is a replay of the same key and same payload. Response contains expected Amount from the bill, ProviderOrderCode, provider LinkId/CheckoutUrl/QrCode when obtained, UTC ExpiresAt/PaidAt, Status and RequiresReview. No client-supplied amount, actor or paid flag is accepted. An accepted request can remain Unknown with no link if the provider outcome is uncertain: use the same key/payment ID; do not assume that HTTP 201 means money was received.

```http
GET  /api/payments/{paymentId}/payos
POST /api/payments/{paymentId}/payos/reconcile
POST /api/payments/{paymentId}/payos/cancel
POST /api/payments/payos/webhook
```

GET is a cached read: Admin can read system-wide; Manager/Cashier only assigned branches. Reconcile and Cancel require the owning Cashier in the assigned branch. Reconcile reads the provider; Cancel first persists cancellation intent, then asks PayOS and verifies the result. Neither endpoint accepts a body. Bank-specific reads are separate from the existing cash receipt endpoint.

Webhook is intentionally independent of employee Bearer authorization and requires a valid PayOS HMAC signature. Send exactly the provider JSON envelope; do not call it with a fabricated Paid body. The backend verifies the signature and financial fields, records the existing PaymentWebhook audit with hash/dedupe key (not raw payload), then verifies the provider's signed payment query. Duplicate deliveries do not settle twice. A successful webhook acknowledgement can mean durable receipt awaiting provider reconciliation; inspect payment status.

Normal path: Creating -> Pending -> Paid. Network errors can produce Unknown; requested/local-expiry cancellation can remain CancelRequested. Only provider-confirmed terminal **zero money with explicit totals/empty transactions** produces verified Cancelled/Expired/Failed. Local expiry alone never releases checkout or permits another payment. ProviderAmountPaid/ProviderAmountRemaining are returned only from a fresh successful query, otherwise null; expected Amount is not a claim about received money.

After a verified zero attempt, Cashier may create a new PayOS attempt with a new key or use Cash; checkout remains held until an explicit authorized checkout cancellation is confirmed by Ordering. IsVerifiedZero describes this one attempt, not blanket authorization to start another payment. Every creation rechecks bill/checkout/shift/all other attempts. The internal IdempotencyKey also binds the original checkout ID; reusing a UUID for a different checkout/bill/shift is rejected. Timeout/restart retries reuse the stored order code; never generate a different order code just because a response was lost.

Paid is recorded only after exactly one verified full transfer matching the frozen Ready bill and an Active owning shift. Payment, Bill Paid/PaidAt and checkout Paid commit atomically, without Sale/CashMovement. PaidAt is backend UTC verification time; it is not an invented bank-local transaction timestamp. Missing/malformed/unsigned provider totals are uncertain, not zero. Partial/extra money, multiple references, mismatched IDs, late money after another settlement/released checkout/closed shift and conflicting signed deliveries require ReviewRequired; there is no automatic refund, second settlement or review override endpoint in this increment.

Lost initial create responses may be recovered by order code. PayOS query can recover LinkId/payment status but does not promise CheckoutUrl/QR. Those stay null if unavailable; no fake link/QR is reconstructed. A still-missing provider order after local expiry remains unresolved rather than guessed failed. Merchant/provider review is required when a definitive result cannot be recovered.

Payment success keeps checkout held and Ordering frozen. It does not close TableSession, invalidate QR, advance kitchen status or mark a table cleaned. Shift reconciliation accepts verified Paid bank transfers and definite zero attempts, excludes transfers from expected cash and exposes BankTransferTotal in the live preview; unresolved/review attempts block closing. ShiftReport's existing stored cash fields are unchanged: BankTransferTotal is not a new immutable revenue-report column.

Paid PayOS payments remain eligible for background reconciliation while their shift is open. After closing, they remain eligible within `PayOs:PaidReconcileHours` from PaidAt (default 24 hours, configurable from 1 to 8760). Signed webhooks remain accepted outside that polling window. Additional money requires review; it never creates a second settlement or changes the recorded cash report. Before first closing a shift, Billing queries each PayOS attempt again outside the closing locks, then locks the shift and verifies that the same payment results still apply. Unavailable/uncertain provider responses or newly added/changed attempts block closing; retry the same closing inputs. Replaying an already closed report does not require another provider call. Preview alone is not a promise that close will succeed.

Opening cash must be whole VND: `250000` and `250000.00` are accepted, `250000.50` is rejected without rounding. CashIn and cash Sale validate `OpeningCash + existing SignedAmount total + new amount` under the same register/shift transaction locks against the `decimal(18,2)` limit. A rejected sale records no Payment, Sale or Paid bill/checkout. Existing fractional opening balances and already over-limit balances are not rewritten automatically; review those legacy records separately.

This increment excludes manual transfer confirmation, Waiter collection/handover, refunds, departure/cleaning and any payment-flow schema beyond the three approved nullable columns. [Official PayOS testing guidance](https://payos.vn/docs/moi-truong-test/) states that a separate sandbox is not available. Local mock/HTTP-provider tests do not prove a real bank transfer. Live acceptance still requires real channel credentials, registered public HTTPS webhook and an authorized real test payment; do not claim this based on redirect success or simulated callbacks.
