-- Test fixture only. Run through Test-RestaurantReports.ps1 in its isolated Docker project.
SET XACT_ABORT ON;
BEGIN TRANSACTION;
INSERT dbo.Restaurant(RestaurantId,RestaurantName,Address,TimeZone)
VALUES('20000000-0000-0000-0000-000000000099',N'Nhà hàng B kiểm thử báo cáo',N'Dữ liệu tổng hợp','Asia/Ho_Chi_Minh');
-- Manager A is deliberately NOT assigned to B until the access-denial assertion has run.
COMMIT TRANSACTION;
