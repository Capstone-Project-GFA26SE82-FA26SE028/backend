# Code style for assistant changes

When writing or editing C# code in this repository:

- Use explicit braces for method bodies and control-flow blocks, including single-statement branches and loops.
- Put opening and closing braces on their own lines (Allman style).
- Write one statement per line and leave a blank line between methods.
- Use readable multiline object initializers, with one property assignment per line.
- Keep DTO auto-properties compact, for example `public string? Email { get; set; }`.
- Apply this style directly to code you generate. Do not add or change IDE formatting configuration unless the user requests it.
- Keep formatting changes within the requested scope and preserve behavior.

# Layer organization

- Keep each model's repository, business service, gRPC service, and API controller separate. For example, AuditLog and Session operations belong in their own layers, not in User classes.
- Extend an existing repository for the same model when one is available, such as SessionRepository.

# Method naming

- Name CRUD controller and business-service methods Create, GetAll, GetById, Update, and Delete where applicable. Repository async methods use the Async suffix.
- Keep descriptive names for specific operations such as GetMe, UpdateMe, Login, Logout, SetRole, and SetStatus.
- Preserve existing HTTP routes and protobuf RPC contracts during internal naming refactors unless a contract change is requested.
