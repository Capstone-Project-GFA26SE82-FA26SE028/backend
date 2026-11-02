-- Synthetic Docker development accounts. Password: 123.
INSERT dbo.Role (RoleId, RoleName) VALUES ('admin', N'Quản trị viên');
INSERT dbo.[User] (UserId, RoleId, UserName, Email, FullName, PasswordHash, IsActive, CreatedAt) VALUES ('10000000-0000-0000-0000-000000000001', 'admin', 'admin', 'admin@gmail.com', N'Quản trị viên', '$2a$11$36mwcLNIUUJz6qqA5XoxMe9TXyIYr5fKU6qy1a9GDbbNhpxVijQEa', 1, GETUTCDATE());
INSERT dbo.Role (RoleId, RoleName) VALUES ('manager', N'Quản lý');
INSERT dbo.[User] (UserId, RoleId, UserName, Email, FullName, PasswordHash, IsActive, CreatedAt) VALUES ('10000000-0000-0000-0000-000000000002', 'manager', 'manager', 'manager@gmail.com', N'Quản lý', '$2a$11$36mwcLNIUUJz6qqA5XoxMe9TXyIYr5fKU6qy1a9GDbbNhpxVijQEa', 1, GETUTCDATE());
INSERT dbo.Role (RoleId, RoleName) VALUES ('waiter', N'Nhân viên phục vụ');
INSERT dbo.[User] (UserId, RoleId, UserName, Email, FullName, PasswordHash, IsActive, CreatedAt) VALUES ('10000000-0000-0000-0000-000000000003', 'waiter', 'waiter', 'waiter@gmail.com', N'Nhân viên phục vụ', '$2a$11$36mwcLNIUUJz6qqA5XoxMe9TXyIYr5fKU6qy1a9GDbbNhpxVijQEa', 1, GETUTCDATE());
INSERT dbo.Role (RoleId, RoleName) VALUES ('cashier', N'Thu ngân');
INSERT dbo.[User] (UserId, RoleId, UserName, Email, FullName, PasswordHash, IsActive, CreatedAt) VALUES ('10000000-0000-0000-0000-000000000004', 'cashier', 'cashier', 'cashier@gmail.com', N'Thu ngân', '$2a$11$36mwcLNIUUJz6qqA5XoxMe9TXyIYr5fKU6qy1a9GDbbNhpxVijQEa', 1, GETUTCDATE());
INSERT dbo.Role (RoleId, RoleName) VALUES ('kitchen', N'Bếp');
INSERT dbo.[User] (UserId, RoleId, UserName, Email, FullName, PasswordHash, IsActive, CreatedAt) VALUES ('10000000-0000-0000-0000-000000000005', 'kitchen', 'kitchen', 'kitchen@gmail.com', N'Nhân viên bếp', '$2a$11$36mwcLNIUUJz6qqA5XoxMe9TXyIYr5fKU6qy1a9GDbbNhpxVijQEa', 1, GETUTCDATE());
INSERT dbo.Role (RoleId, RoleName) VALUES ('inventory', N'Thủ kho');
INSERT dbo.[User] (UserId, RoleId, UserName, Email, FullName, PasswordHash, IsActive, CreatedAt) VALUES ('10000000-0000-0000-0000-000000000006', 'inventory', 'inventory', 'inventory@gmail.com', N'Thủ kho', '$2a$11$36mwcLNIUUJz6qqA5XoxMe9TXyIYr5fKU6qy1a9GDbbNhpxVijQEa', 1, GETUTCDATE());
