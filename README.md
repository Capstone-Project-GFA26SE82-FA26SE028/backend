## Secrets and Docker startup

Keep passwords and tokens in `.env`, in the repository root, ignored by Git. On a new machine, copy `.env.example` there and fill in the empty values. Tracked appsettings contain non-secret settings only.

Run Docker (Compose automatically reads the ignored root `.env`):

```powershell
docker compose up -d --build
```

Docker uses one SQL password for service connection strings, one JWT signing key and shared Cloudinary credentials. PayOS credentials are configured per restaurant. This env file is for Docker only. Validate with `config --quiet` to avoid printing secrets.

For GitHub Actions deployment, store credentials in repository/environment Secrets and inject them as runtime environment variables. Build/test CI does not need production secrets. Previously committed credentials must be revoked or rotated; removing files does not remove Git history. Changing the env file alone does not change SQL passwords in existing volumes.

# Restaurant backend and AI assistant

## Recipe images and videos (Cloudinary)

`POST /api/recipes/media` accepts a local file as `multipart/form-data`, field `file`, using the existing menu-manager authentication and restaurant scope. Backend detects the type from file bytes; do not supply a trusted MIME type or remote file URL. It returns `result.mediaType` (`image` or `video`) and `result.url`.

Send these values in `media` when creating an item or option recipe, together with `displayOrder`. Video has two supported inputs: upload a local MP4/WebM through this endpoint, or send a pasted HTTP/HTTPS video link directly in the recipe request (`mediaType: "video"`, `url: "https://www.youtube.com/watch?v=..."`). Query strings/fragments are allowed for video links, including YouTube links. Credentials in URLs, executable schemes, local paths and control characters are rejected. Backend stores video links without downloading, uploading, fetching metadata or checking availability; rendering a provider page versus embedding a video is a client concern. The upload file-size limit does not apply to remote links. Upload alone does not create or alter a recipe. Existing recipe-version, ingredient validation and effective-date rules still apply.

```powershell
curl.exe -X POST "http://localhost:5000/api/recipes/media" -H "Authorization: Bearer YOUR_ACCESS_TOKEN" -F "file=@step.mp4"
```

Defaults in both Gateway and Catalog `RecipeMedia` configuration: `MaximumImageBytes=2097152` (JPEG/PNG/WebP), `MaximumVideoBytes=20971520` (MP4/WebM), `UploadTimeoutSeconds=45`. Limits may be lowered; the supported upper bounds remain 2 MiB/image and 20 MiB/video. Gateway allows the configured upload timeout plus 10 seconds for forwarding. Only the media-upload RPC receives the larger gRPC message limit. Rebuild Catalog and Gateway after pulling this contract change. Compose reuses the existing Cloudinary environment variables; no new secret is needed.

New images must match the configured Cloudinary cloud, image resource type and `catalog/recipes/images` folder. Uploaded video replies use `catalog/recipes/videos`; pasted video links may point to external hosts. Image validation checks source/path, not an upload receipt or asset ownership. Historical HTTP/HTTPS images may be carried forward unchanged only for the same item/option that already referenced them; existing versions are never rewritten. Empty `media` creates a version without media. Unsupported upload formats, new external image links and unsafe video-link schemes are rejected; failed validation does not close the active recipe.

No automatic retry or old-asset deletion is performed. Upload success followed by a failed recipe save may leave an unreferenced asset. Cloudinary delivery URLs are public: do not upload secrets or documents requiring restricted access. No SQL/ERD schema change is required; `RecipeMedia` and its relation to `RecipeVersion` already exist.

## Catalog item images (Cloudinary)

Keep the existing two-step flow: upload with `POST /api/items/images` (`multipart/form-data`, field `file`), then send the returned `imageUrl` to item create/update. JPEG, PNG and WebP are supported, up to 2 MiB. Backend checks the file signature; Cloudinary validates the actual image. Existing menu-management permissions and item rowversion checks remain in effect.

New image references must use HTTPS on `res.cloudinary.com`, the configured cloud, and the `catalog/items` upload folder. URLs from other clouds, avatar/QR folders, external hosts, or with query strings/fragments are rejected. This validates the source/path; it is not proof of an asset's existence or an upload receipt. Existing image URLs may remain unchanged. Omitting/null `imageUrl` on update preserves the image; an explicit empty string clears it. No migration or deletion of old images is performed.

Catalog already receives the shared Cloudinary credentials through Compose. Rebuild Catalog after pulling this change. No new endpoint, secret, SQL column or ERD relationship is needed. Uploading does not update an item until create/update succeeds; a subsequent version conflict can leave an unreferenced cloud asset. Do not retry indefinitely.

## Restaurant logos (Cloudinary)

`POST /api/restaurants/{id}/logo` uploads and immediately saves a restaurant logo. Use `multipart/form-data` with field `file`, JPEG/PNG/WebP up to 2 MiB, and a valid platform-admin bearer token. This retains the current restaurant-profile permission; it does not grant restaurant admins or managers new access. The response contains `result.restaurantId` and `result.logoUrl`.

Create the restaurant first without a logo, then upload for its returned ID. Restaurant create/update APIs reject new pasted logo URLs. Updates may keep the existing URL unchanged; omitting `logoUrl` preserves it, while an explicit empty string clears the reference. Existing URLs are not migrated or deleted.

```powershell
curl.exe -X POST "http://localhost:5000/api/restaurants/RESTAURANT_ID/logo" -H "Authorization: Bearer YOUR_ACCESS_TOKEN" -F "file=@logo.png"
```

Use the actual Gateway port. Restaurant already receives `CLOUDINARY_CLOUD_NAME`, `CLOUDINARY_API_KEY` and `CLOUDINARY_API_SECRET` from the ignored Compose `.env`; no additional secret is required. Rebuild Restaurant and Gateway after pulling this contract change. For direct .NET execution, set the corresponding `Cloudinary__*` environment variables.

Authorization is checked before upload and revalidated with Identity before saving. Invalid input returns 400, missing restaurant 404, authentication/permission failures 401/403, and cloud failures 503. Failed upload leaves the previous logo unchanged. An upload followed by a failed database save or revoked access can leave an unreferenced cloud asset; old assets are not automatically deleted. No SQL/ERD schema changes are required.

## Identity avatars (Cloudinary)

Avatar changes accept files, not newly pasted URLs. Existing avatar URLs remain readable and may be sent back unchanged. Omitting `avatarUrl` in a profile/account update preserves the saved image; an explicit empty string removes the reference without deleting the cloud file. Create an account without an avatar, then upload for that account.

- `POST /api/users/me/avatar`: authenticated account uploads its own avatar.
- `POST /api/users/{id}/avatar`: existing user-management permissions apply; restaurant admins may update only permitted staff in their own restaurant, and platform admins retain their configured scope.
- Use `multipart/form-data` with field `file`. JPEG, PNG and WebP are supported, up to 2 MiB. A successful response contains `result.userId` and `result.avatarUrl`; Identity has already saved the avatar.

```powershell
curl.exe -X POST "http://localhost:5000/api/users/me/avatar" -H "Authorization: Bearer YOUR_ACCESS_TOKEN" -F "file=@avatar.png"
```

Use the actual Gateway port when it differs from 5000. Identity uses the same `CLOUDINARY_CLOUD_NAME`, `CLOUDINARY_API_KEY` and `CLOUDINARY_API_SECRET` supplied to Compose through the ignored root `.env`. For direct .NET execution, supply `Cloudinary__CloudName`, `Cloudinary__ApiKey` and `Cloudinary__ApiSecret` as environment variables. Do not put secrets in committed appsettings.

Cloud upload failures leave the previous avatar unchanged. Missing credentials/provider failures return 503; invalid image input returns 400. No automatic retry or deletion of old cloud assets is performed. If upload succeeds but saving the account fails, an unreferenced cloud asset may remain; do not retry indefinitely. This feature changes no SQL/ERD schema.

The AI service calls Gemini API (`gemini-3.1-flash-lite` by default) for menu reasoning and cart-based recommendations. A small allowlist of exact price questions about one resolved dish is answered directly from a fresh Catalog read after checking QR access again. Questions needing interpretation continue through Gemini. It sends only the information needed for the request and keeps the existing QR and Catalog checks.

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
- `Gemini:RequestTimeoutSeconds` defaults to 20 and caps the complete generation operation, including response-body buffering, backoff and both attempts, within the remaining overall budget. `Gemini:RetryServiceUnavailable` defaults to true and permits exactly one extra HTTP attempt only after upstream 503. `Gemini:RetryDelayMilliseconds` defaults to 250. A longer `Retry-After` is honored when it fits the remaining model budget; otherwise the service returns 503 without retrying. HTTP 429, other errors, network failures, timeouts and invalid model output are not retried. Caller cancellation stops the backoff/request. The inference slot stays held across the retry and is released on every completion/error path. A retry can consume additional provider quota; missing usage metadata is not counted as zero tokens.
- Model deadline exhaustion returns HTTP 504 with a controlled message directing the guest to retry or ask staff. It does not return a fabricated successful chat reply. Cancellation limits local waiting; the provider may already have processed or billed the attempt. Settings come from the same configuration priority described above; no Gateway route or database changes are needed.
- Exact supported price lookups and local missing-food-information replies do not require inference. They can still respond when Gemini is unconfigured or busy, with QR and Catalog checks remaining in force. This is request routing before inference; failed Gemini calls do not fall back to another provider.

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

### A5 acceptance, response time and tokens

Use PowerShell 7 (`pwsh`) and an **isolated development Compose project** with the synthetic Docker seed. Keep the normal development stack and its volumes unchanged. Build the AI image from the current working tree. Give the test project a separate Gateway port and separate image tags when both stacks are running. No local model is required.

For example, create an ignored override with separate application images, then start the required services. Choose a free port if 62102 is occupied. Compose continues to read credentials from the existing ignored `.env`.

```powershell
New-Item -ItemType Directory -Force .ai/a5 | Out-Null
$override = "services:`n"
foreach ($service in @('ai', 'gateway', 'identity', 'restaurant', 'catalog', 'ordering')) {
    $override += "  ${service}:`n    image: ai-a5-${service}:verify`n"
}
$override | Set-Content -Encoding utf8 .ai/a5/compose.verify.yaml
$env:COMPOSE_FILE = 'compose.yaml;compose.override.yaml;.ai/a5/compose.verify.yaml'
$env:GATEWAY_HTTP_PORT = '62102'
$gateway = 'http://127.0.0.1:62102'
docker compose -p ai-a5 config --quiet
docker compose -p ai-a5 up -d --build ai gateway
docker compose -p ai-a5 ps
```

The explicit test fixture `scripts/ai/acceptance-menu.sql` prepares two independent restaurants, with Iced Tea priced at 10,000 VND in A and 37,000 VND in B. It also prepares a hot/light Chicken Porridge at 52,000 VND and hot Pumpkin Soup at 75,000 VND in A. **The fixture changes test data. It is not a schema upgrade and must never be applied to a shared database.** Its recipes support menu tests; they do not establish real ingredient availability or kitchen readiness.

After starting the isolated stack, apply the fixture only to its SQL container. The command below uses the password already present inside that container and does not print it. Replace `ai-a5-sqlserver-1` with the verified test container name.

```powershell
docker inspect --format '{{index .Config.Labels "com.docker.compose.project"}}' ai-a5-sqlserver-1
docker cp scripts/ai/acceptance-menu.sql ai-a5-sqlserver-1:/tmp/acceptance-menu.sql
docker exec ai-a5-sqlserver-1 sh -c 'SQLCMDPASSWORD="$MSSQL_SA_PASSWORD" /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -i /tmp/acceptance-menu.sql'
```

Prepare the session and cart through Gateway, using the synthetic Waiter account from the development seed. For restaurant A use table `20000000-0000-0000-0000-000000000011`. Open it through the existing Waiter endpoint, join through the existing guest-access endpoint and populate a cart with one rice item `20000000-0000-0000-0000-000000000051`, quantity 1, no options. Keep the guest `WebRequestSession` from join as `$web`. Save only its guest cookie in an ignored file as follows. This does not bypass QR validation.

```powershell
$login = Invoke-RestMethod "$gateway/api/auth/login" -Method Post -ContentType application/json -Body '{"email":"waiter@gmail.com","password":"123"}'
$opened = Invoke-RestMethod "$gateway/api/table-sessions/tables/20000000-0000-0000-0000-000000000011/open" -Method Post -Headers @{ Authorization = 'Bearer ' + $login.result.accessToken }
$sessionId = $opened.result.tableSessionId
$web = [Microsoft.PowerShell.Commands.WebRequestSession]::new()
$null = Invoke-RestMethod "$gateway/api/table-sessions/$sessionId/guest-access/join" -Method Post -WebSession $web -ContentType application/json -Body '{"phoneOrCode":"123"}'
$cart = Invoke-RestMethod "$gateway/api/table-sessions/$sessionId/carts" -Method Post -WebSession $web
$cartId = $cart.result.cartId
$body = @{ version = $cart.result.version; items = @(@{ itemId = '20000000-0000-0000-0000-000000000051'; quantity = 1; options = @() }) }
$null = Invoke-RestMethod "$gateway/api/table-sessions/$sessionId/carts/$cartId" -Method Put -WebSession $web -ContentType application/json -Body ($body | ConvertTo-Json -Depth 8)
$cookies = @{}
foreach ($cookie in $web.Cookies.GetCookies([Uri]"$gateway/api")) {
    if ($cookie.Name.StartsWith('guest_access_')) { $cookies[$cookie.Name] = $cookie.Value }
}
New-Item -ItemType Directory -Force .ai/a5 | Out-Null
$cookies | ConvertTo-Json | Set-Content -Encoding utf8 .ai/a5/guest-cookie.json
pwsh -NoProfile -File scripts/Test-AIAcceptance.ps1 `
    -Gateway $gateway -SessionId $sessionId -CartId $cartId `
    -CookieFile .ai/a5/guest-cookie.json -AiContainer ai-a5-ai-1
```

The versioned dataset has 23 scenarios covering price and availability, combined questions, implicit preferences, budget, conversation references/exclusions, dietary uncertainty, prompt injection, unsupported language, cart recommendations and invalid access. To add the 24th scenario, join the same table as a second guest, create that guest's cart and pass its ID with `-OtherGuestCartId`. Access must return 404 without revealing the cart. The benchmark does not mutate either cart.

For restaurant B use the synthetic `waiter-a5-b@example.test` account (the same development password), table `30000000-0000-0000-0000-000000000011`, rice item `30000000-0000-0000-0000-000000000051` and a separately joined guest. Run again with `-Dataset scripts/ai/acceptance-tenant-b.json -OutputFile .ai/a5/tenant-b.json`. Both functions must use B's menu and tea price. Never reuse A's cookie as B's credentials.

The script writes questions, actual answers, cards, expected assertions, failures, trace IDs, image ID and measurements into `.ai/a5/acceptance.json`, which Git ignores. It excludes guest cookies and protected conversation tokens. It exits 1 when any assertion fails. `-Repetitions 3` repeats the full dataset, resetting conversation contexts between runs; it consumes additional API quota. Do not discard failed runs or tune prompts against this dataset and then describe it as a blind evaluation.

Gemini numeric `usageMetadata` is logged as `GeminiUsage`, correlated with the Gateway request's W3C trace ID. No prompt, answer or API key is logged by this metric. Missing/invalid counts are `unknown`, never an invented zero. `totalTokenCount` comes from Gemini. Missing thought counts remain unknown; cache-token breakdown is not collected. The benchmark validates price/status and option requirements against Catalog immediately after each response. This is a static acceptance comparison, not proof against all concurrent menu changes; mutation races are also covered by the AI and Ordering dependency-simulation tests.

`AITiming` records correlated durations for GuestAccess, MenuRead, MenuRefresh, CartRead, Gemini and Total. Repeated calls to a stage are summed per request in the report. Total measures the AI gRPC handler. Gemini measures the complete outbound HTTP call including response buffering, not pure provider inference time. HTTP latency minus Total includes client, Gateway and transport work and cannot be assigned to one component from these logs alone. In the tested Windows environment, some `localhost` calls had roughly two seconds outside AI while IPv4 calls had only milliseconds. Use the same host and timeout settings before/after comparison; switching to IPv4 corrects this measurement condition and is not a backend speed improvement.

Cases C01, C02 and C15 now require zero model responses because their complete text matches the supported price-question grammar and identifies exactly one dish. All price/status/card assertions stay unchanged. Compound questions, uncertain food information, existing dietary concerns, ambiguous subjects and text outside that grammar continue through the model path. Prices and sellability are refreshed, never cached. Recommendation inference uses short internal reason codes; the client maps them to the same controlled Vietnamese API reasons and rejects unknown codes.

The price grammar accepts ordinary parentheses and ASCII hyphens. It matches the resolved dish name inside a complete question pattern, so a dish named “Giá” does not replace the price-question keywords. Unrecognized symbols such as allergy-warning emoji and extra clauses still keep the request on the model path. `GeminiRetry` logs only the trace ID, attempt number and delay. The acceptance report distinguishes HTTP attempts, retry events and successful responses carrying usage metadata; a failed HTTP attempt may have no measurable token count. Retry/timeout tests with simulated HTTP do not prove that a live provider failure will recover.

Reported latency is the complete Gateway HTTP request/response time, excluding subsequent assertion calls. The report separates successful chat, recommendations and responses with observed inference, and uses nearest-rank p50/p95. Three seconds is an evaluation target, not a service guarantee. The first request and later requests are separated; cloud model loading state is not observable. Sequential timings do not measure maximum throughput. Case pass rate is an assertion-based software acceptance score, not general model accuracy/F1 or a human usability score. No user study or independent frozen quality dataset is established by these scripts.

The original A5 run recorded failures C03 (additional suggestion after a separate sentence) and C17 (remembering “không muốn Iced Tea nữa”). Both remain in the dataset as regression cases after the focused conversation-recognition fix. Keep the original results separately when rerunning. Unit tests also cover English/Vietnamese sentence variants, explicit restoration, excluded dish groups, filtering cold suggestions from a warm request, and not adding suggestions to a plain or explicitly limited facts request. Passing unit tests alone does not establish that the real-model conversation cases pass; rerun the Gateway benchmark against the rebuilt AI image.

After testing, `docker compose -p ai-a5 down` stops this isolated stack and preserves its test volumes. Remove the process-level `COMPOSE_FILE` and `GATEWAY_HTTP_PORT` overrides before returning to the normal Compose project. Do not use `down -v` to reset shared data.

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

Khi chạy `docker compose up -d --build`, `db-init` khởi tạo database mới từ schema hoặc giữ nguyên database có `DockerBootstrapVersion=2`, rồi tự chạy các upgrade Billing và dependency đã liệt kê trong `docker/sql/init.sh`. Các upgrade bao gồm quầy/bill theo nhà hàng, PayOS, phương thức tiền mặt, checkout/chuyển quầy/chốt bill, thu hộ, cấu hình provider, outbox, lock Ordering, cột tài khoản hoàn tiền và ảnh QR. Cuối bước này, initializer đối chiếu sự tồn tại của bảng/cột với schema Billing/Ordering/Catalog/Restaurant/Inventory/Identity và in `Billing and AI dependency schemas are ready.` khi đạt. Identity đang chỉ được kiểm tra, chưa tự nâng cấp database cũ; nếu thiếu cột User hoặc bảng thông báo, cần nâng cấp dependency đó trước.

Script chạy lại được và không tự gán nhà hàng, quầy, nhân viên hoặc URL PayOS. Nếu dữ liệu cũ thiếu ánh xạ bắt buộc, có nhiều quầy một nhà hàng hoặc thiếu cấu hình provider cần quyết định, bước nâng cấp dừng và báo rõ để xử lý dữ liệu; không xóa hoặc đoán giá trị. Database không có marker hoặc có version khác 2 cần kiểm kê/nâng cấp riêng trước. Xem phần kiểm kê ngày 2026-11-21 trong [nhật ký SQL/ERD](docs/database-change-log.md). Không dùng `docker compose down -v` để khắc phục lỗi thiếu cột vì lệnh đó xóa volume database.

Kiểm thử nâng cấp Docker bằng `pwsh -File scripts/Test-BillingDockerBootstrap.ps1` (cần Docker Engine chạy Linux containers). Script build image `db-init` từ source hiện tại và tạo SQL Server/network riêng, tái hiện schema cũ thiếu cột/bảng, kiểm tra nâng cấp lặp giữ dữ liệu, đối chiếu metadata Billing/Ordering và các ca phải dừng an toàn. Tài nguyên thử được dọn khi kết thúc; không dùng database của Compose đang chạy. Với môi trường đã tồn tại, có thể chạy `docker compose build db-init` rồi `docker compose run --rm db-init` để xem rõ kết quả nâng cấp trước khi khởi động lại các service.

### Dữ liệu màn Bàn & hóa đơn của thu ngân

- `GET /api/restaurants/{restaurantId}/payment-requests/incoming`: yêu cầu đã chuyển đến quầy và bill chưa thanh toán cần tiếp quản.
- `GET /api/restaurants/{restaurantId}/payment-requests/paid`: bill đã Paid và có Payment Paid thuộc ca thu ngân đang đứng. Khoản Waiter vừa thu hộ hoặc PayOS chưa xác minh không thuộc tab này.
- Hai API nhận `search` (mã bàn hoặc mã bill, tối đa 50 ký tự), `areaId`, `page` (mặc định 1), `pageSize` (mặc định 20, tối đa 100). `cashShiftId` là tùy chọn và phải khớp ca đang đứng; không cho client chọn ca người khác hoặc ca cũ qua hai API màn quầy. API đọc bill/report cũ giữ phạm vi quyền hiện có.
- Mỗi thẻ có `tableSessionId`, `checkoutId`, `billId`, `billCode`, `totalAmount`, `status`, `partySize`, `openedAt`, `submittedAt`, `paidAt`, `tables[]`. `billCode` lấy từ `Bill.BillNumber`; chưa có bill thì mã và tổng bill là `null`, không dùng 0 thay thế. `openedAt` là giờ mở phiên, `submittedAt` là giờ chuyển quầy, `paidAt` là giờ bill được xác nhận Paid.
- Một bàn: trả thêm `tableId`, `tableCode`, `areaId`, `areaName`. Nhiều bàn: giữ đủ `tables[]`, hai trường bàn đơn là `null`; khu vực đơn chỉ có khi tất cả bàn cùng khu vực. Phiên mở lấy các liên kết bàn chưa release; phiên đã đóng lấy nhóm liên kết được release cuối cùng. Tên/mã bàn lấy từ Restaurant hiện tại, không phải snapshot tên bàn bất biến trên hóa đơn.
- Tìm kiếm/lọc, `totalCount` và tổng tiền tính trên toàn bộ tập kết quả trước phân trang. Với bộ lọc khu vực, tìm mã bàn chỉ xét bàn nằm trong khu vực đó; tìm mã bill vẫn phải đáp ứng khu vực. `knownTotalAmount` là tổng bill đã lập, `unbilledCount` là số thẻ chưa lập bill. `totalAmount` chỉ có khi không còn thẻ chưa lập bill; nếu còn, trả `null` để tránh coi phần chưa xác định là 0 đồng. Mỗi tab dùng `totalCount` từ chính API tương ứng và cùng bộ lọc.
- Chọn thẻ chưa có bill: Cashier gọi `POST /api/table-sessions/{sessionId}/payment-requests/{checkoutId}/prepare`. Đã có bill: đọc `GET /api/bills/{billId}` để lấy món, giá snapshot, VAT, phí và tổng. Chỉ xem thẻ không tự lập bill hoặc ghi nhận thanh toán.
- Cả hai API yêu cầu Cashier được phân công đúng nhà hàng và đang đứng ca. Billing lấy metadata phiên theo lô tối đa 200 ID từ RPC đọc `BillingSessionGrpc.GetDisplays` của Restaurant; không truy cập database chéo service. Không lấy được đầy đủ metadata thì báo lỗi, không âm thầm bỏ thẻ làm sai số lượng/tổng.

Thay đổi này không đổi luồng BM04 F-WF02-06: khách yêu cầu → Waiter kiểm tra/chuyển quầy → Cashier chốt → Waiter đưa bill → thu hộ được Cashier xác nhận hoặc PayOS được backend xác minh → ghi Paid một lần và tự đóng phiên. Không thêm nghiệp vụ ưu đãi/tích điểm từ UI mẫu.

Triển khai đồng bộ Gateway, Billing, Restaurant cùng commit bằng `docker compose up -d --build`; không chỉ cập nhật một container vì Billing dùng RPC đọc mới. Không có thay đổi schema hoặc SQL upgrade cho phần màn quầy này. Kiểm tra hồi quy bằng `dotnet test Tests/Billing.Tests` (SQL thật cần `BILLING_SQL_TEST_CONNECTION`) và `dotnet test Tests/Restaurant.Tests --filter FullyQualifiedName~PaymentSubmission` (cần `RESTAURANT_SQL_TEST_SERVER` cho SQL Server Windows/LocalDB cô lập). Những test đó dùng dữ liệu thử, không phải thao tác trên database đang dùng.

Khi review độc lập, Gemini cần kiểm tra: (1) lọc trước phân trang và tổng trên toàn bộ kết quả, kể cả thẻ chưa lập bill; (2) đúng nhà hàng/ca và không nhân đôi bill nhiều dòng; (3) nhiều bàn hoặc phiên đã đóng vẫn có metadata đúng; (4) Waiter mới thu hộ chưa vào Paid, Cashier xác nhận/PayOS đã xác minh mới vào Paid; (5) API prepare/đọc bill vẫn cung cấp giá snapshot, VAT, phí và tổng; (6) không có SQL/schema hoặc quyền production thay đổi ngoài hợp đồng đọc đã nêu.

### Chốt hóa đơn và xác nhận đã đưa khách

1. Cashier đang đứng ca tại quầy nhận gọi `POST /api/table-sessions/{sessionId}/payment-requests/{id}/prepare`. Hệ thống khóa lại tập món tại Ordering, kiểm tra đơn chờ xác nhận rồi lập hoặc cập nhật bill từ bản chụp. Sau khi đối chiếu, hệ thống ghi trạng thái checkout `Prepared`.
2. Waiter phụ trách mở `GET /api/table-sessions/{sessionId}/payment-requests/{id}/bill` để lấy bill đưa khách kiểm tra.
3. Khi đã đưa bill thật, Waiter gọi `POST /api/table-sessions/{sessionId}/payment-requests/{id}/present`. Hệ thống ghi `PresentedAt` một lần và chuyển checkout sang `Presented`.

Hai trạng thái trên vẫn là bill `Unpaid`. Các API thu tiền của checkout cũ chưa xử lý được bill qua luồng này. Docker tự áp dụng hai script nâng cấp khi chạy `db-init` từ image mới; môi trường chạy ngoài Docker vẫn cần áp dụng `BillingCheckoutSubmission.sql` rồi `BillingCheckoutPreparation.sql`. Bật đúng cấu hình chính sách VAT/phí trước khi lập bill mới.



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

Amount is a positive whole-VND value; the backend stores CashIn as positive and CashOut as negative. Reason is required, trimmed, at most 1000 characters and without control characters. Vietnamese and other Unicode text are stored in the NVARCHAR column; existing databases must run the Unicode upgrade through the current db-init image before starting services.

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

To create a test QR, call `POST /api/restaurants/{restaurantId}/payment-settings/payos/test-qr` as the assigned restaurant admin. Omit the body, send `{}`, or send `{"useSavedConfiguration":true}` to use saved credentials. To test new credentials before saving, send all three fields `clientId`, `apiKey`, and `checksumKey`. The response contains QR and transaction details for a 2,000 VND PayOS link valid for 15 minutes. This request does not update the restaurant configuration or create a local bill payment.

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
