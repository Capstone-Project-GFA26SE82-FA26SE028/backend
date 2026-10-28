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

After configuring the existing backend `.env` and starting Docker Desktop, the ordinary command now includes AI through the automatically loaded `compose.override.yaml`:

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

Compose follows its existing `.env` configuration. If `.env` is absent, the script generates credentials once in ignored `.ai/compose.env` and passes that env file explicitly; preserve it while using the same database volume. Changing a configured SQL password does not change an existing SQL volume's password. No newly generated secret is stored in tracked files.

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
