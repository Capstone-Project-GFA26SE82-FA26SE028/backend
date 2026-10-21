INSERT dbo.[Restaurant] (RestaurantId, RestaurantName, Address, TimeZone) VALUES
('20000000-0000-0000-0000-000000000001', 'Demo Restaurant', '123 Demo Street', 'Asia/Ho_Chi_Minh');
INSERT dbo.[Area] (AreaId, RestaurantId, AreaCode, AreaName, ServiceType, SpaceType, IsActive) VALUES
('20000000-0000-0000-0000-000000000002', '20000000-0000-0000-0000-000000000001', 'MAIN', 'Main Hall', 'DineIn', 'Indoor', 1);
INSERT dbo.[Table] (TableId, AreaId, TableCode, Capacity, IsActive, CleaningStatus) VALUES
('20000000-0000-0000-0000-000000000011', '20000000-0000-0000-0000-000000000002', 'T01', 4, 1, 'Clean'),
('20000000-0000-0000-0000-000000000012', '20000000-0000-0000-0000-000000000002', 'T02', 4, 1, 'Clean'),
('20000000-0000-0000-0000-000000000013', '20000000-0000-0000-0000-000000000002', 'T03', 4, 1, 'Clean');
