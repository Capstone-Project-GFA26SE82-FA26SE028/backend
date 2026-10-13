# Employee accounts: password, sessions and audit

These endpoints belong to restaurant employees. Customer phone-number login is a separate domain.
All endpoints below require the employee's access token in the Authorization Bearer header.

## Change password

`POST /api/auth/change-password`

```json
{
  "currentPassword": "CurrentPassword123!",
  "newPassword": "NewPassword456!",
  "confirmPassword": "NewPassword456!"
}
```

The current password must match. The new password must differ from the current password,
contain at least 8 characters, fit within 72 UTF-8 bytes and match confirmPassword exactly.
Passwords are not trimmed.

Success returns HTTP 200 with the standard response envelope. All of the employee's sessions,
including the current session, are revoked in the same transaction as the password change
and its audit records. The frontend must clear its tokens and send the employee to login.
Validation errors and an incorrect current password return 400.

## Personal sessions

| Endpoint | Behavior |
| --- | --- |
| GET /api/sessions/me?page=1&pageSize=20 | Lists only the authenticated employee's sessions. |
| DELETE /api/sessions/me/{id} | Revokes one session belonging to that employee. |
| DELETE /api/sessions/me | Revokes all that employee's sessions, including the current one. |

The personal list includes history (expired and revoked sessions), ordered newest first.
`isCurrent` identifies the session used to call the API.
`revokedAt == null` alone does not mean a session is active: its expiry also matters.
The page size is limited to 100. No password hash or refresh token/hash is returned.

An unknown session or another employee's session returns 404 when revoking by ID.
Revoking an already revoked own session succeeds without duplicating the audit event.
After revoking the current session or all sessions, the frontend must clear its tokens.

The existing admin-only `GET /api/sessions` remains available for viewing all employees.

## Audit coverage

| Action | Event |
| --- | --- |
| LOGIN | Successful login and session creation |
| LOGIN_FAILED | Unknown account or incorrect password |
| LOGIN_BLOCKED | Correct credentials for a disabled account |
| REFRESH_TOKEN | Successful refresh-token rotation |
| LOGOUT | Revocation through the logout endpoint |
| CHANGE_PASSWORD | Successful password change |
| CHANGE_PASSWORD_FAILED | Incorrect current password during password change |
| CREATE_USER | Admin creates an employee |
| UPDATE_PROFILE | Employee edits their own profile |
| SET_ROLE | Admin assigns a role |
| SET_STATUS | Admin changes account status |
| REVOKE_SESSION | Personal revocation, logout-all, password change or access change |

Audit `userId` is the actor; `entityId` is the target record. Unknown-account login failures
have no user ID. Before/after snapshots contain only explicitly selected fields.
Passwords, password hashes, access tokens and refresh tokens/hashes are never recorded.
Read-only calls and every possible validation rejection are not logged as mutations.

Mutation audits are saved atomically with their associated changes. The gateway forwards its
observed client IP as internal gRPC metadata; internal gRPC access must be restricted to trusted callers.
Existing audit rows are not backfilled.

Admin audit listing remains `GET /api/audit-logs?page=1&pageSize=20&userId=<actor-id>`.
