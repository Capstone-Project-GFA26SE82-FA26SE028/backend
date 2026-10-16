> Cập nhật: vai trò hiện là danh mục cố định theo UC_ADM_06. Các mô tả CRUD bên dưới là thiết kế cũ; API ghi đã bị chặn. Xem [admin-identity.md](admin-identity.md).

# Employee roles

All endpoints require a Bearer token for an active administrator. Administrators are
identified by the RoleId values in UserManagement:AdminRoleIds, not by RoleName.
Creating or renaming a role does not automatically grant new permissions.

| Method | Route | Behavior |
| --- | --- | --- |
| GET | /api/roles | Lists all roles, retaining the existing response shape |
| GET | /api/roles/{id} | Returns one role |
| POST | /api/roles | Creates a role |
| PUT | /api/roles/{id} | Replaces the name and description |
| DELETE | /api/roles/{id} | Deletes an unused, non-admin role |

Create body:

```json
{
  "roleId": "cashier",
  "roleName": "Cashier",
  "description": "Restaurant cashier"
}
```

Update body:

```json
{
  "roleName": "Senior cashier",
  "description": null
}
```

RoleId is immutable and allows ASCII letters, digits, underscores and hyphens, up to 50 characters.
RoleName is required and allows up to 50 characters after trimming.
Description allows up to 2048 characters; null or whitespace clears it.
Duplicate IDs return 409; missing roles return 404; invalid input returns 400.
Deletion returns 409 for configured admin roles or roles assigned to any account, including disabled accounts.

CREATE_ROLE, UPDATE_ROLE and DELETE_ROLE audits include the actor, observed IP and
appropriate before/after snapshots. They commit in the same transaction as the change.

Assign an existing role using the unchanged PATCH /api/users/{id}/role endpoint.
The old UserGrpc.ListRoles RPC remains a compatibility adapter to RoleService.
New callers use RoleGrpc; role repositories and business logic are separate from User.
