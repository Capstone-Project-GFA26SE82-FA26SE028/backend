erDiagram

&#x20;   Role ||--|{ User : "has"

&#x20;   User ||--|{ UserSession : "has"

&#x20;   User |o--|{ AuditLog : "generates"



&#x20;   UserSession {

&#x20;       guid UserSessionId PK

&#x20;       guid UserId FK

&#x20;       string IpAddress

&#x20;       string UserAgent

&#x20;       string DeviceType

&#x20;       datetime LoggedInAt

&#x20;       datetime ExpiresAt

&#x20;   }



&#x20;   User {

&#x20;       guid UserId PK

&#x20;       string RoleId FK

&#x20;       string UserName

&#x20;       string Email

&#x20;       string PhoneNumber

&#x20;       string FullName

&#x20;       string AvatarUrl

&#x20;       string PasswordHash

&#x20;       bool IsActive

&#x20;       datetime CreatedAt

&#x20;       datetime UpdatedAt "NULL"

&#x20;   }



&#x20;   AuditLog {

&#x20;       guid AuditLogId PK

&#x20;       guid UserId FK "NULL"

&#x20;       string Action

&#x20;       string EntityName

&#x20;       string EntityId

&#x20;       string OldValue

&#x20;       string NewValue

&#x20;       string IpAddress

&#x20;       datetime CreatedAt

&#x20;   }



&#x20;   Role {

&#x20;       string RoleId PK

&#x20;       string RoleName

&#x20;       string Description

&#x20;   }



