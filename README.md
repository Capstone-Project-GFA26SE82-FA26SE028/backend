## Secrets and Docker startup

Keep passwords and tokens in `.env`, in the repository root, ignored by Git. On a new machine, copy `.env.example` there and fill in the empty values. Tracked appsettings contain non-secret settings only.

Run Docker (Compose automatically reads the ignored root `.env`):

```powershell
docker compose up -d --build
```

Docker uses one SQL password for all service connection strings, one JWT signing key, and shared Cloudinary and PayOS credentials. This env file is for Docker only. Validate with `config --quiet` to avoid printing secrets.

For GitHub Actions deployment, store credentials in repository/environment Secrets and inject them as runtime environment variables. Build/test CI does not need production secrets. Previously committed credentials must be revoked or rotated; removing files does not remove Git history. Changing the env file alone does not change SQL passwords in existing volumes.

# Restaurant backend and AI assistant

The backend keeps its existing .NET 9 services, Gateway and SQL Server workflow. AI adds UC1 (cart-based suggestions) and UC2 (customer menu chat), using only `qwen3:4b-instruct-2507-q4_K_M`, context 2048, through Ollama. No cloud fallback, training, automatic cart writes, orders, reservations or payments.

## Windows: one command with the existing Docker backend

Requirements: Windows 10 22H2+/11 x64, Docker Desktop running **Linux containers**, Internet for the first image/model download, enough disk for Docker/SQL and the 2.5 GB model. Recommend at least 16 GB host RAM and 8 GB allocated to Docker. No GPU is required; CPU inference may take longer. The script checks .NET; Docker uses the repository's .NET 9 SDK images, so the host SDK is optional in this mode.

From the cloned repository root:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Start-AI.ps1
```

This validates Compose and starts the existing backend/Gateway first. It then starts/reuses its Ollama service, builds AI, downloads the approved model **only if missing**, checks Qwen3/4B/Q4_K_M metadata, runs a `READY` smoke test with context 2048, and starts AI. The model setup may take up to 30 minutes for a first download; temporary registry failures are retried at most three times using cached layers, and smoke inference is bounded to 90 seconds. An AI setup failure does not stop already-started backend services. It does not change existing SQL scripts or demo seed data. Existing `db-init` initializes new Docker databases as described in [the Docker guide](docs/docker.md).

Gateway: `http://localhost:5000/swagger` unless `GATEWAY_HTTP_PORT` is changed. Database creation and service startup can take longer than AI setup: check `docker compose ps` and logs if a dependency is still starting.

Native Windows Ollama and Compose Ollama are separate runtimes/model stores. Docker mode reuses the Compose runtime and persistent volume; it never replaces the native installation or silently reconfigures it to expose a network port. To reuse a native Ollama installation/model, use Local mode below.

Optional NVIDIA acceleration, if Docker Desktop GPU passthrough and NVIDIA drivers are already working:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Start-AI.ps1 -Gpu
```

The model is identical in CPU/GPU modes. Omit `-Gpu` if the driver/runtime is unavailable. A GPU failure never selects a different model.

Setup/warm up without starting backend applications:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Start-AI.ps1 -SetupOnly
```

Run the same command again after a restart. Installed models are reused; the smoke test runs again. Weights persist in the `<project>_ai-models` volume. `docker compose -f compose.yaml -f compose.ai.yaml down` keeps data/models; **do not use `down -v`** to retain them. Existing images/runtimes are not automatically upgraded. `-ProjectName name` selects an isolated Compose project; use the same name on subsequent commands.

## Keep the normal .NET local workflow

Start/configure Identity, Restaurant, Catalog, Ordering and Gateway as usual in Visual Studio or `dotnet run`. Their databases and authentication remain unchanged. Then run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Start-AI.ps1 -Mode Local
```

Local mode requires a .NET **9 SDK**. It reuses an existing reachable Ollama runtime without replacing, upgrading or changing its settings. If no installation exists, it downloads the Windows x64 standalone archive from the official [Ollama GitHub releases](https://github.com/ollama/ollama/releases), verifies its published SHA-256 digest and extracts into `%LOCALAPPDATA%\RestaurantAI\Ollama\<version>`. Unsupported architecture or an unverifiable archive produces a specific manual-install instruction; no arbitrary third-party installer is used. Official Windows guidance: [Ollama on Windows](https://docs.ollama.com/windows).

When needed, the script starts `ollama serve` in the background and stores logs under ignored `.ai/`. It respects the existing `OLLAMA_MODELS` location, otherwise Ollama uses its normal user profile model directory. It does not change user/machine environment settings or install a Windows service. Ctrl+C stops the foreground AI service; Ollama may remain running for reuse. Exit Ollama normally if needed; model files remain on disk.

## Automatic setup during normal startup

After configuring the ignored root `.env` and starting Docker Desktop, the ordinary command now includes AI through the automatically loaded `compose.override.yaml`:

```powershell
docker compose up -d --build
```

Compose starts Ollama, runs the model preparation container before AI, and stores weights in the persistent `ai-models` volume. Existing weights are reused. Core backend services do not wait for AI readiness. Compose itself cannot install Docker Desktop or generate missing backend credentials; use `Start-AI.ps1` above for prerequisite checks and initial environment setup. To explicitly run only the original backend without the AI override, use `docker compose -f compose.yaml up -d --build`.

On Windows, F5 with the `AI.API` project selected, or the following command, automatically prepares the native Ollama runtime and model before starting the AI server:

```powershell
dotnet run --project Services/ai_service/AI.API/AI.API.csproj
```

The supplied launch profile selects Development. Setup uses the effective Ollama endpoint configuration, reuses an existing installation, installs the official runtime only if absent, and downloads the approved model only if missing. The first startup can take several minutes. It runs once at application startup, never during build or a customer request, and never recursively starts another AI server. Starting Gateway or another service alone does not trigger native AI setup. The .NET 9 SDK must already be installed to run/build C# code.

Native automatic setup runs only in Development. It is disabled in Docker/Production and during the standalone `--prepare-model` / `--check-ready` commands. To disable it locally, set `DevelopmentBootstrap:Enabled` to `false` in ignored `AI.API/appsettings.Local.json`, or set `DevelopmentBootstrap__Enabled=false` in the environment. If setup fails, diagnostics identify the missing dependency; AI still starts and returns controlled dependency errors until it is ready. Other backend applications remain independent.

## Shared and machine configuration

Shared settings are in `Services/ai_service/AI.API/appsettings.json`; container addresses are in `appsettings.Docker.json`. Defaults are local service addresses, not a developer's drive path. Gateway has `GrpcServices:AI` and `AIClient:TimeoutSeconds`.

For Local mode, copy `Services/ai_service/AI.API/appsettings.Local.json.example` to `appsettings.Local.json` in the same folder and adjust only necessary values. Gateway can also load its own `appsettings.Local.json`, for example:

```json
{
  "GrpcServices": { "AI": "http://localhost:5008" }
}
```

Both applications explicitly load configuration in this order (last wins): shared `appsettings.json`, environment-specific settings, Development User Secrets if the project has them, **appsettings.Local.json**, environment variables, command-line values. Local files are Git-ignored and excluded from Docker context; they are never copied into images. Runtime model/token/secret files and GGUF weights must not be committed.

Compose and Start-AI.ps1 explicitly read `.env`. Create it from `.env.example` and fill in credentials. Preserve it with the database volume: changing the file does not change the SQL password stored in an existing volume.

## Readiness and errors

AI has gRPC on local port 5008 and HTTP health on 5009 (Docker health port is internal):

- `/health/live`: AI process is alive.
- `/health/ready`: Ollama responds, the approved model is installed and loaded with Q4_K_M/context 2048. Returns 503 with a status such as `ModelMissing`, `ModelNotLoadedWithConfiguredContext` or `RuntimeUnavailableOrIncompatible` otherwise. Ollama being alive alone is insufficient.

After the five-minute keep-alive expires, readiness reports cold/not loaded. Rerun setup to warm up. A normal AI inference can also load the installed model within the request budget; a missing/offline model returns a controlled 503. Docker does not restart applications solely because a health check is unhealthy. Gateway/other backend functions do **not** depend on AI/model health at startup.

AI request budget: **90 seconds total**, including dependency reads and inference. Other service RPCs: at most 10 seconds each, also bounded by the remaining request time. Ollama is not capped at 10 seconds. One inference per AI process; no waiting queue. Busy returns HTTP 429, unavailable 503, deadline 504, unverified model output 502, invalid QR 401. Cancellation reaches model/dependency calls. There is no model download/installation in a controller or build.

## UC1/UC2 requests through Gateway

Use the existing QR flow: resolve/scan QR, join the table session using its real join code, then use the **HttpOnly** `guest_access_<sessionId without dashes>` cookie set by `POST /api/table-sessions/{sessionId}/guest-access/join`. Staff bearer tokens do not replace QR access for AI. Browser requests must include credentials; the token is never included in the AI JSON body or sent to Ollama.

UC2:

```http
POST /api/ai/table-sessions/{sessionId}/chat
Content-Type: application/json

{"message":"What drinks can I order?","history":[]}
```

Optional history contains only `{"role":"user|assistant","content":"..."}` turns. AI's configured limits are authoritative: 500 characters per message/turn, four history turns by default. Messages are answered in English; dish names/descriptions retain verified Catalog data.

UC1, after creating/populating the real cart through existing Ordering endpoints:

```http
POST /api/ai/table-sessions/{sessionId}/carts/{cartId}/recommendations
Content-Type: application/json

{"limit":3}
```

AI excludes **all option variants by ItemId**, only offers `CanOrder == true` items with verified prices, and rereads guest/cart/menu before returning cards. Cart changes during inference return 409. Required options must still be selected via the existing cart workflow. AI never calls cart Create/Update/Delete.

Missing verified ingredient/allergy/diet/spice facts: the assistant directs the customer to ask staff; it does not contact staff itself. There is currently no suitable public Restaurant-info RPC, so restaurant contact/hours/policy answers use the same missing-information approach. Catalog's `CanOrder` is **not proof of sufficient ingredient stock**; its public menu is not branch-filtered. No restaurant permissions or database were expanded to hide these gaps.

## Tests and reproducibility

```powershell
dotnet test Tests/AI.Tests/AI.Tests.csproj
```

AI projects/tests are registered in `Backend.sln`, so the existing GitHub Actions solution test step discovers them. Default tests use client fakes and an in-process Gateway; they do not install/download/call a real model. An optional model benchmark is explicitly separate:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Start-AI.ps1 -Mode Local -SetupOnly
$env:RUN_LOCAL_AI_TESTS='1'
dotnet test Tests/AI.Tests/AI.Tests.csproj --filter FullyQualifiedName~LocalQwenTests
Remove-Item Env:RUN_LOCAL_AI_TESTS
```

To exercise the **real** running stack, keep an existing session/cart and export its cookie into an ignored JSON file (property name is the actual cookie name). This script only reads the cart and calls AI; it does not provision or alter restaurant data:

```powershell
.\scripts\Test-AI.ps1 -Gateway http://localhost:5000 -SessionId <session-guid> -CartId <cart-guid> -CookieFile .\.ai\guest-cookie.json -OutputFile .\.ai\gateway-ai-result.json
```

The cart needs at least one dish and the real menu must have another orderable suggestion. The test checks chat, recommendations, cart exclusion, allergy fallback, off-topic refusal and an unchanged cart version. Do not share cookie/credential files. Actual timings include model load state and hardware; neither a model smoke test nor fake-client tests establish end-to-end performance below three seconds.

## Troubleshooting

- **Docker unavailable:** start Docker Desktop Linux engine; inspect `docker info`. The script does not silently install a hypervisor or change Windows settings.
- **Native SDK missing:** install .NET 9 SDK, restart the shell, run `dotnet --list-sdks`.
- **Download failure/disk full:** keep the persistent model store, free space/check HTTPS access to official GitHub/Ollama registry, rerun. Never substitute a different model.
- **Ollama port occupied by another application:** inspect `.ai/ollama.stderr.log`; stop that application or configure a different native endpoint. Existing installations are not overwritten.
- **Invalid model metadata:** inspect `ollama show qwen3:4b-instruct-2507-q4_K_M`. Setup refuses incompatible weights; it does not overwrite them automatically.
- **503/timeout/OOM:** inspect `docker compose -f compose.yaml -f compose.ai.yaml logs ai ai-model-init ollama`; check allocated RAM/GPU and rerun setup. Stop unrelated workloads if needed. CPU mode can exceed the latency target.
- **401/409:** rejoin a real open QR session / refresh the real cart. Do not bypass access checks or seed fake credentials.
- **Gateway route missing:** rebuild/restart Gateway after pulling; AI running alone does not update an already-running old Gateway binary.

### Verified on 23 October 2026

The procedure was exercised on a **new local clone of HEAD with the uncommitted candidate changes applied**, without copying `.ai/`, native model files, build output, User Secrets or `appsettings.Local.json`. The source ran from a separate temporary checkout, not the original developer workspace. This verifies the candidate source; the remote repository will only contain it after these changes are committed/pushed.

Executed from that checkout:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Start-AI.ps1 -ProjectName ai-verify-20261023
```

- Fresh official Compose Ollama/runtime and a new persistent model volume: approved model downloaded, metadata verified, smoke returned `READY`, AI healthy. An initial registry EOF/HTTP 500 was diagnosed; rerunning resumed cached layers. Setup now also retries temporary failures at most three times.
- Repeated setup: printed `Approved model already installed; skipping download.` Existing databases/model volume were reused.
- Native Windows `-Mode Local -SetupOnly`: reused the existing native Ollama/model without upgrading, and returned `READY`. The native **missing-installation ZIP branch was not tested on a pristine Windows machine**.
- Automatic-start follow-up: plain `dotnet run --project Services/ai_service/AI.API/AI.API.csproj` prepared the cached native model and returned `ready: true` from `/health/ready`. Ordinary Compose startup with only `-p ai-verify-20261023` added automatically (no explicit AI file) ran the model initializer successfully and started a healthy AI container. These follow-up checks used the working checkout and existing test model stores. Updated Windows AI tests: **68 passed, 1 opt-in benchmark skipped, 0 failed**.
- Windows Release solution run: **533 passed, 37 conditional tests skipped, 0 failed**. A physical Windows reboot was not performed; model persistence is provided by the retained native model directory / Docker named volume.
- Real Gateway → AI → real Restaurant/Catalog/Ordering → real Ollama, using the existing QR join/cart APIs against isolated Docker demo databases: UC1 suggested **Iced Tea, price 10000**, excluding Steamed Rice already in the cart. UC2 returned **Iced Tea: 10000.00. Available to order.** Allergy fallback and unrelated-question refusal passed. Cart version was unchanged.
- Real missing-cookie test: HTTP **401**. Two simultaneous real AI requests: **200 + 429**.
- Docker CPU timing through Gateway: corrected UC2 cold chat **23.93 s**, warm chat **3.88 s**; UC1 measured **4.55 s**. This hardware/configuration **does not meet the under-three-second target**. Cold readiness probe failed as expected before inference loaded the model.
- AI fake-client/in-process Gateway tests in a clean Linux .NET 9 SDK container: **61 passed, 1 opt-in real-model benchmark skipped**. GitHub Actions itself was not triggered from this task.

An automated approval check blocked the additional command that would temporarily stop AI to test other live endpoints during the outage; that live outage experiment was not performed. HTTP 503 mapping was instead verified through the separate fake-client Gateway test. No unavailable dependency was replaced by fabricated production data.

Test models/data persist in the isolated `ai-verify-20261023` project. If that project is still running on port 5000, reuse that project name or stop it manually before starting a different project on the same port:

```powershell
docker compose -p ai-verify-20261023 -f compose.yaml -f compose.ai.yaml down
```

This keeps SQL/model volumes. Do not append `-v` unless you intend to delete them.

## Checkout before payment

Waiter-to-register routing requires `docker/sql/upgrades/BillingCheckoutRouting.sql` on **BillingDb**, after the existing CashRegister restaurant upgrade. It adds only the five approved nullable BillCheckout fields; legacy rows remain unassigned. Compose db-init runs this repeatable upgrade. Do not run a creation/reset script over an existing database.

After the assigned Waiter acknowledges and reviews the Payment ServiceRequest:

- Waiter lists registers with an active shift: `GET /api/checkout/cash-registers?restaurantId={restaurantId}`. This does not grant general register-management access.
- Responsible Waiter submits: `POST /api/table-sessions/{sessionId}/checkout/{checkoutId}/submit` with `{ "cashRegisterId": "<register UUID>" }`. Same target is idempotent; another target returns 409. Registers must belong to the session's restaurant and have exactly one unclosed Active shift.
- Owning Cashier reads `GET /api/cash-registers/{cashRegisterId}/checkouts?page=1&pageSize=20`, then calls the existing `/synchronize`. Ready records PreparedByRef/PreparedAt; no money is collected.
- Responsible Waiter reads `GET /api/table-sessions/{sessionId}/checkout/{checkoutId}/prebill`, gives the bill to the customer, then calls `POST /api/table-sessions/{sessionId}/checkout/{checkoutId}/present` without a body. PresentedAt is stored once. Other Waiters cannot read/present it; general bill readers retain existing permissions.
- New cash/PayOS collection requires the receiving register and presentation milestone. Existing payment replay and PayOS settlement recovery remain available. Cash handover through Waiter uses the Cash payment endpoints below. Customer departure and cleaning are explicit staff actions described below.

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
4. After the responsible Waiter submits the reviewed request to an open register and before payment collection (see the routing steps above), the Cashier owning that register's active shift sends `POST /api/table-sessions/{sessionId}/checkout/{checkoutId}/synchronize` with the normal bearer token, without a body. This resumes the same checkout and creates or refreshes the unpaid bill. `Ready` is returned only after its financial hash matches Ordering. Read bill amounts through the existing authorized `GET /api/bills/{billId}` endpoint. Missing tax/fee policy or invalid source data leaves checkout unready and returns a controlled error.
5. Only an assigned Cashier may send `POST /api/table-sessions/{sessionId}/checkout/{checkoutId}/cancel`, before any Payment exists. Only `Released` with `orderingReopened: true` confirms that Ordering acknowledged reopening. An old cancelled key cannot resurrect checkout; a deliberate new checkout needs a new UUID.

For PowerShell testing, use `Invoke-RestMethod -WebSession $guestSession` for QR requests and `-Headers @{ Authorization = "Bearer $cashierAccessToken" }` for Cashier requests. Do not send a staff token in place of the guest cookie when testing the customer flow.

`Preparing` or `ReleasePending`, HTTP 503, or a client timeout must not be treated as permission to order again. Retrieve the checkout and retry its original operation with the same ID. There is no automatic expiry/unlock. Cooking, Ready and Served progress does not change the financial hash; creating more orders or cancelling frozen items is blocked for the whole session. Cart editing alone does not submit an order.

Cash payment now consumes the matching Ready checkout through Billing's session lock and keeps Ordering blocked after success. Unknown outcomes must not release checkout. Customer departure, QR invalidation and cleaning confirmation remain separate staff workflows.


## Customer departure and table cleaning

Payment success does not mean the customer has left. Any currently assigned **Waiter** at the session's restaurant may confirm departure; this is not limited to the Waiter who handled checkout. Admin, Manager, Cashier and QR guests cannot perform departure or cleaning confirmation through these APIs.

```http
POST /api/table-sessions/{sessionId}/depart
Authorization: Bearer {waiter-access-token}
```

There is no request body. Restaurant first asks Billing to verify the session's completed payment through the read-only `PaymentGrpc.RequirePaid` RPC, forwarding the employee identity. Billing requires a matching Paid bill/checkout, frozen financial hash and exactly one consistent settled Cash receipt/Sale or verified PayOS receipt; unresolved/review attempts block departure. A closed Cashier shift does not invalidate an already settled receipt. This is a read of persisted verification, not a new bank query or a client-supplied paid flag. Restaurant configuration includes `GrpcServices:Billing` for native/Docker operation; Billing unavailability prevents departure without changing the session. No startup dependency cycle or database upgrade is added.

After verification, Restaurant locks the session's current tables in stable order and then the session, rechecks staff scope and table assignments, and commits `TableSession.Status = Closed`, UTC `ClosedAt`, current `SessionTable.ReleasedAt/ChangedByRef`, guest `RevokedAt` and `Table.CleaningStatus = Dirty` together. Previous table-assignment history stays unchanged. A replay returns the original close time and cannot affect a later session or its guests. Billing's checkout/Ordering lock remains held as historical payment state; closing the session does not reopen ordering for it.

The table QR remains reusable. Its old **guest session access** is revoked. Both anonymous QR scan and staff open refuse to create a new session for a table whose cleaning status is not `Clean`; scanning a clean table retains the original create-or-reuse behavior.

Read the latest table `version` from `GET /api/tables/service?restaurantId={restaurantId}` and, after physically cleaning it, send:

```http
POST /api/tables/{tableId}/confirm-clean
Authorization: Bearer {waiter-access-token}
Content-Type: application/json

{ "version": "<latest Base64 rowversion>" }
```

Cleaning uses the same table lock as opening/scanning, rejects occupied tables and stale versions, and returns the current table DTO/version. Confirm each table separately for a multi-table session. Reload after 409; do not reuse an old cleaning confirmation for a subsequent party. Schema is unchanged: there is no dedicated historical cleaner/time or session ClosedBy field; release attribution uses the existing SessionTable.ChangedByRef.

Tests: run `dotnet test Tests/Restaurant.Tests/Restaurant.Tests.csproj` and `dotnet test Tests/Billing.Tests/Billing.Tests.csproj`. To enable the isolated SQL Server tests, set `RESTAURANT_DEPARTURE_SQL_CONNECTION` and `BILLING_SQL_TEST_CONNECTION` to a private dev SQL Server connection with permission to create/drop owned test databases. These tests create uniquely named databases; never put credentials in Git. Gateway/Restaurant tests use fake upstream identity/payment verification; Docker workflow checks and real-bank acceptance are reported separately in the existing database change log.

## Cash payment

Requires the existing Billing/Ordering checkout upgrades above, an enabled `Cash` payment method, a Ready checkout already presented by the responsible Waiter, and the collecting Cashier's own Active shift at the selected receiving register in the bill's restaurant. Admin/Manager cannot collect payments. Admin can read receipts globally; Manager/Cashier can read within active restaurant assignments.

Update Docker without resetting data:

```powershell
docker compose build db-init billing gateway
docker compose run --rm db-init
docker compose up -d --no-deps billing gateway
```

For SQL Server outside Docker, run `docker/sql/upgrades/BillingCashMethod.sql` against BillingDb using the team's connection. This is **data only**: it inserts `Cash`/enabled if missing and preserves an existing disabled method. New databases also use `docker/sql/seed/BillingDb.sql`. No tables, columns, constraints or ERD relationships are added in this increment. Billing local/Docker configuration contains `CashPayment:MethodCode = Cash`.

Cash collection now uses the existing **CashHandover** table. No schema changes are required for this workflow. After the responsible Waiter has presented the Ready bill:

1. Waiter records all cash handed over by the customer, using a new UUID `Idempotency-Key` and the Waiter's normal Bearer token:

```http
POST /api/bills/{billId}/cash-handovers
Idempotency-Key: <UUID-reused-on-retry>
```

```json
{
  "checkoutId": "<Ready-checkout-UUID>",
  "cashReceived": 200000
}
```

The response is 201 for creation or 200 for the exact replay. Save PaymentId. Payment remains `PendingHandover`, CashHandover remains `Pending`, Bill stays `Unpaid`, and no Sale is recorded. Backend selects the receiving register's unique active shift; the client cannot select the employee, shift, bill amount or paid status. Insufficient/fractional VND is rejected without rounding.

2. Cashier reads pending handovers for their register with `GET /api/cash-registers/{cashRegisterId}/cash-handovers?page=1&pageSize=20`.
3. Waiter brings **all customer tender** to Cashier. After physically receiving it, the owning Cashier confirms:

```http
POST /api/cash-handovers/{paymentId}/confirm
```

```json
{
  "confirmedAmount": 200000
}
```

ConfirmedAmount is the gross tender, not the bill total, and must equal the saved CashReceived. A discrepancy is rejected and remains pending for staff investigation. Confirmation updates Payment/Bill/checkout to Paid, CashHandover to Confirmed with receiving Cashier/time/gross amount, and posts exactly one net Sale equal to the bill amount in the same transaction. For a bill of 177871 and tender 200000, ChangeAmount is 22129 and Sale is 177871. No additional change movement is written. Cashier gives the change to Waiter to return to the customer; the schema does not record a separate change-return acknowledgement.

4. Responsible Waiter reads `GET /api/cash-handovers/{paymentId}` to see confirmation/change. Admin/Manager/Cashier retain restaurant-scoped read rights; another Waiter cannot read it. The existing `GET /api/payments/{paymentId}` remains the authorized final cash receipt.

Retry creation with the same key/payload and retry confirmation by the same PaymentId. Pending cash blocks another payment, checkout cancellation and shift closing. A write/Ordering failure must not mark the bill Paid or erase collected cash. Completed confirmation replays after shift close or method disable without a second Sale. No automatic expiry/unlock/refund or adjustment of already collected cash is implemented.

The legacy `POST /api/bills/{billId}/payments/cash` no longer creates an immediate payment without a Waiter collection. It can confirm an existing matching pending handover using its original key, or replay a legacy completed receipt. New integrations should use the handover routes above.

Checkout remains unreleased and Ordering frozen after payment. Payment does not close TableSession, revoke QR, stop cooking/serving or mark tables for cleaning. Departure and cleaning are later staff workflows.


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

Closing accepts the implemented Cash/Paid/Cashier flow with exactly one matching Sale, and the verified PayOS flow described below. Unknown/non-Paid states, unsupported methods/provider flows, refunds, incomplete/inconsistent Waiter CashHandover, duplicate outstanding/settled bill payments, duplicate ledger keys, orphan/mismatched sales and inconsistent movements block close with identifiable records. A provider-confirmed zero-money terminal PayOS attempt does not count as an outstanding/settled payment. A cash handover must be Confirmed and match the receiving Cashier, gross tender and paid time before closing. Resolve those records through their supported workflow; this API never silently marks them Paid, failed, expired or cancelled. Preview has the full blocker list; Close returns 409 with a short summary.

Unpaid bills/checkout attempts have no CashShift binding and are not assigned to a shift by assumption. They do not prevent closing an otherwise valid cash shift, and remain unchanged/frozen as applicable. Payment success and shift close do not close the table session, invalidate QR, reopen ordering, mark cleaning or log out Identity. Existing Open shift remains the explicit way to begin a subsequent shift; RemainingCash is not silently copied into its OpeningCash.

ShiftReport persists only the existing ExpectedCash, ActualCash, CashHandleOver, DifferenceReason and ClosedAt fields plus IDs. Difference and RemainingCash are recomputed from these stored values; reports are not recalculated from later ledger activity. CashierUserRef is derived from the shift owner, not a separate historical actor snapshot. Preview revenue/ledger breakdown is not a stored revenue breakdown; this is **cash reconciliation**, not full bank/PayOS settlement or a full immutable revenue Z-report. Manager can retrieve reports via the scoped list/detail APIs. Automatic delivery to Manager and device-session signout are not integrated in this increment.


## Billing: automatic PayOS bank transfer (increment 6)

Prerequisites: the existing authenticated Cashier, assigned restaurant, own Active shift, held Ready checkout already presented by the responsible Waiter at the selected receiving register, and current Unpaid whole-VND bill. Apply `docker/sql/upgrades/BillingPayOs.sql` to an existing BillingDb before running the new binary. Fresh creation uses `docker/sql/schema/BillingDb.sql`; Compose initialization applies the upgrade automatically. The upgrade adds only three nullable Payment metadata columns and inserts BankTransfer when absent; it preserves a disabled BankTransfer row. Never run the creation script over an existing database.

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

### Waiter presents the Cashier-created PayOS QR

After the responsible Waiter has presented the prepared bill, the owning Cashier creates the PayOS attempt using the endpoint above. The responsible Waiter uses these separate employee Bearer endpoints:

```http
GET /api/table-sessions/{sessionId}/checkout/{checkoutId}/payments/payos?page=1&pageSize=20
GET /api/table-sessions/{sessionId}/checkout/{checkoutId}/payments/{paymentId}/payos
```

Both reads require an active restaurant assignment and the same Waiter recorded as handling the reviewed payment ServiceRequest. They validate the session, checkout, receiving register, presented bill and frozen financial hash. They do not grant Waiter the existing Cashier create/reconcile/cancel operations or the general bill/payment administration APIs.

The paged response contains attempts correlated to this checkout by the stored payment request key and bill, including retired attempts. Do not select a payment by a guessed latest timestamp. Present only an attempt with `canPresentQr = true`; its `qrCode` and `checkoutUrl` are the provider values already stored by Billing. Unknown, cancelled, locally expired, paid, conflicting or incomplete attempts return these fields as null. Local expiry hides the QR without claiming that the bank confirmed cancellation or reopening ordering.

`paymentConfirmed = true` requires a verified full PayOS receipt and the matching Paid bill/checkout. `status` is the attempt state; `billStatus` is separate. `requiresReview = true` signals inconsistent, ambiguous or review-required records; do not display these as a new successful payment. A verified zero-money retired attempt does not itself invalidate a valid replacement.

These GETs serialize with Billing's session updates and return the last persisted verification state with UTC `evaluatedAt`. They do not call PayOS, settle money, create ledger movements, release checkout or close the table session. Existing webhook/reconciliation updates provide subsequent state; this increment adds no push notification transport. The platform-wide merchant configuration remains unchanged. No schema changes are required for these reads.

### Verification and recovery

Webhook is intentionally independent of employee Bearer authorization and requires a valid PayOS HMAC signature. Send exactly the provider JSON envelope; do not call it with a fabricated Paid body. The backend verifies the signature and financial fields, records the existing PaymentWebhook audit with hash/dedupe key (not raw payload), then verifies the provider's signed payment query. Duplicate deliveries do not settle twice. A successful webhook acknowledgement can mean durable receipt awaiting provider reconciliation; inspect payment status.

Normal path: Creating -> Pending -> Paid. Network errors can produce Unknown; requested/local-expiry cancellation can remain CancelRequested. Only provider-confirmed terminal **zero money with explicit totals/empty transactions** produces verified Cancelled/Expired/Failed. Local expiry alone never releases checkout or permits another payment. ProviderAmountPaid/ProviderAmountRemaining are returned only from a fresh successful query, otherwise null; expected Amount is not a claim about received money.

After a verified zero attempt, Cashier may create a new PayOS attempt with a new key or use Cash; checkout remains held until an explicit authorized checkout cancellation is confirmed by Ordering. IsVerifiedZero describes this one attempt, not blanket authorization to start another payment. Every creation rechecks bill/checkout/shift/all other attempts. The internal IdempotencyKey also binds the original checkout ID; reusing a UUID for a different checkout/bill/shift is rejected. Timeout/restart retries reuse the stored order code; never generate a different order code just because a response was lost.

Paid is recorded only after exactly one verified full transfer matching the frozen Ready bill and an Active owning shift. Payment, Bill Paid/PaidAt and checkout Paid commit atomically, without Sale/CashMovement. PaidAt is backend UTC verification time; it is not an invented bank-local transaction timestamp. Missing/malformed/unsigned provider totals are uncertain, not zero. Partial/extra money, multiple references, mismatched IDs, late money after another settlement/released checkout/closed shift and conflicting signed deliveries require ReviewRequired; there is no automatic refund, second settlement or review override endpoint in this increment.

Lost initial create responses may be recovered by order code. PayOS query can recover LinkId/payment status but does not promise CheckoutUrl/QR. Those stay null if unavailable; no fake link/QR is reconstructed. A still-missing provider order after local expiry remains unresolved rather than guessed failed. Merchant/provider review is required when a definitive result cannot be recovered.

Payment success keeps checkout held and Ordering frozen. It does not close TableSession, invalidate QR, advance kitchen status or mark a table cleaned. Shift reconciliation accepts verified Paid bank transfers and definite zero attempts, excludes transfers from expected cash and exposes BankTransferTotal in the live preview; unresolved/review attempts block closing. ShiftReport's existing stored cash fields are unchanged: BankTransferTotal is not a new immutable revenue-report column.

Paid PayOS payments remain eligible for background reconciliation while their shift is open. After closing, they remain eligible within `PayOs:PaidReconcileHours` from PaidAt (default 24 hours, configurable from 1 to 8760). Signed webhooks remain accepted outside that polling window. Additional money requires review; it never creates a second settlement or changes the recorded cash report. Before first closing a shift, Billing queries each PayOS attempt again outside the closing locks, then locks the shift and verifies that the same payment results still apply. Unavailable/uncertain provider responses or newly added/changed attempts block closing; retry the same closing inputs. Replaying an already closed report does not require another provider call. Preview alone is not a promise that close will succeed.

Opening cash must be whole VND: `250000` and `250000.00` are accepted, `250000.50` is rejected without rounding. CashIn and cash Sale validate `OpeningCash + existing SignedAmount total + new amount` under the same register/shift transaction locks against the `decimal(18,2)` limit. A rejected sale records no Payment, Sale or Paid bill/checkout. Existing fractional opening balances and already over-limit balances are not rewritten automatically; review those legacy records separately.

Manual transfer confirmation and refunds remain outside this PayOS increment. Waiter cash collection/handover and departure/cleaning are implemented separately in the staff workflows above. [Official PayOS testing guidance](https://payos.vn/docs/moi-truong-test/) states that a separate sandbox is not available. Local mock/HTTP-provider tests do not prove a real bank transfer. Live acceptance still requires real channel credentials, registered public HTTPS webhook and an authorized real test payment; do not claim this based on redirect success or simulated callbacks.
