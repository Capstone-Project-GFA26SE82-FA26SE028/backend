# KitchenStation API

Uses the existing Ordering KitchenStation table. No schema change or migration.

Authorization is enforced in the business service, including direct gRPC calls.
Ordering calls Identity GetMe using the forwarded bearer token. Inactive accounts,
invalid sessions and roles outside the configured lists are denied.

KitchenStationManagement:WriterRoleIds is configured as ["manager"] and
ReaderRoleIds as ["kitchen"] in local and Docker appsettings. Manager can create,
read, update and enable/disable stations. Kitchen can only read. Admin, Waiter,
Cashier, Inventory and guests have no station-management access.
This policy was explicitly selected by the user. Empty lists deny access.
Role IDs are matched exactly and case-sensitively.
GrpcServices:Identity is configured for local and Docker environments.

| Method | Route | Operation |
|---|---|---|
| POST | /api/kitchen-stations | Create an active station (201) |
| GET | /api/kitchen-stations | Paginated list |
| GET | /api/kitchen-stations/{id} | Detail |
| PUT | /api/kitchen-stations/{id} | Update code and name |
| PATCH | /api/kitchen-stations/{id}/status | Enable/disable |

POST/PUT body: {"kitchenStationCode":"HOT","kitchenStationName":"Bep nong"}

PATCH body: {"isActive":false}

GET query: page=1, pageSize=20 (maximum 100), optional search (maximum 255),
optional isActive. Response uses the existing result envelope. Lists contain
items, totalCount, page and pageSize. Each station has kitchenStationId,
kitchenStationCode, kitchenStationName and isActive.

Codes are trimmed, uppercased and restricted to ASCII letters, digits, underscores
and hyphens (1-50 characters). Names are trimmed (1-255 characters, no control
characters). Duplicate codes return 409, invalid input 400, missing IDs 404,
missing authentication 401 and disallowed roles 403. Writes use the existing
Serializable transaction implementation, without adding database constraints.

Disabling prevents subsequent routing to that station through OrderService.
Existing tasks and orders are retained, not cancelled or automatically rerouted.
No delete endpoint. The schema has no station version; ETag/optimistic concurrency
is not supported, and the last sequential update wins.

Item assignment and KDS workflow are outside this change. After configuring roles,
rebuild/restart Gateway and Ordering. TestUI API Console discovers these endpoints
from live Swagger. No live database writes were performed during implementation.