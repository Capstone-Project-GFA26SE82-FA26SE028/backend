-- Synthetic Docker development accounts. Password: 123.
INSERT dbo.Role (RoleId, RoleName) VALUES ('admin', 'Admin');
INSERT dbo.[User] (UserId, RoleId, UserName, Email, FullName, PasswordHash, IsActive, CreatedAt) VALUES ('10000000-0000-0000-0000-000000000001', 'admin', 'admin', 'admin@gmail.com', 'Test Admin', '$2a$11$36mwcLNIUUJz6qqA5XoxMe9TXyIYr5fKU6qy1a9GDbbNhpxVijQEa', 1, GETUTCDATE());
INSERT dbo.Role (RoleId, RoleName) VALUES ('manager', 'Manager');
INSERT dbo.[User] (UserId, RoleId, UserName, Email, FullName, PasswordHash, IsActive, CreatedAt) VALUES ('10000000-0000-0000-0000-000000000002', 'manager', 'manager', 'manager@gmail.com', 'Test Manager', '$2a$11$36mwcLNIUUJz6qqA5XoxMe9TXyIYr5fKU6qy1a9GDbbNhpxVijQEa', 1, GETUTCDATE());
INSERT dbo.Role (RoleId, RoleName) VALUES ('waiter', 'Waiter');
INSERT dbo.[User] (UserId, RoleId, UserName, Email, FullName, PasswordHash, IsActive, CreatedAt) VALUES ('10000000-0000-0000-0000-000000000003', 'waiter', 'waiter', 'waiter@gmail.com', 'Test Waiter', '$2a$11$36mwcLNIUUJz6qqA5XoxMe9TXyIYr5fKU6qy1a9GDbbNhpxVijQEa', 1, GETUTCDATE());
INSERT dbo.Role (RoleId, RoleName) VALUES ('cashier', 'Cashier');
INSERT dbo.[User] (UserId, RoleId, UserName, Email, FullName, PasswordHash, IsActive, CreatedAt) VALUES ('10000000-0000-0000-0000-000000000004', 'cashier', 'cashier', 'cashier@gmail.com', 'Test Cashier', '$2a$11$36mwcLNIUUJz6qqA5XoxMe9TXyIYr5fKU6qy1a9GDbbNhpxVijQEa', 1, GETUTCDATE());
INSERT dbo.Role (RoleId, RoleName) VALUES ('kitchen', 'Kitchen');
INSERT dbo.[User] (UserId, RoleId, UserName, Email, FullName, PasswordHash, IsActive, CreatedAt) VALUES ('10000000-0000-0000-0000-000000000005', 'kitchen', 'kitchen', 'kitchen@gmail.com', 'Test Kitchen', '$2a$11$36mwcLNIUUJz6qqA5XoxMe9TXyIYr5fKU6qy1a9GDbbNhpxVijQEa', 1, GETUTCDATE());
INSERT dbo.Role (RoleId, RoleName) VALUES ('inventory', 'Inventory');
INSERT dbo.[User] (UserId, RoleId, UserName, Email, FullName, PasswordHash, IsActive, CreatedAt) VALUES ('10000000-0000-0000-0000-000000000006', 'inventory', 'inventory', 'inventory@gmail.com', 'Test Inventory', '$2a$11$36mwcLNIUUJz6qqA5XoxMe9TXyIYr5fKU6qy1a9GDbbNhpxVijQEa', 1, GETUTCDATE());
