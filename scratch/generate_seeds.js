const fs = require('fs');
const path = require('path');
const crypto = require('crypto');

const SEED_DIR = path.resolve(__dirname, '..', 'docker', 'sql', 'seed');
if (!fs.existsSync(SEED_DIR)) {
  fs.mkdirSync(SEED_DIR, { recursive: true });
}

function escapeSql(val) {
  if (val === null || val === undefined) return 'NULL';
  if (typeof val === 'number') return val.toString();
  if (typeof val === 'boolean') return val ? '1' : '0';
  if (typeof val === 'string') {
    if (val.startsWith('0x')) return val;
    return `N'${val.replace(/'/g, "''")}'`;
  }
  return `N'${val.toString().replace(/'/g, "''")}'`;
}

function buildInserts(table, columns, rows) {
  if (!rows || rows.length === 0) return '';
  const colList = columns.map(c => `[${c}]`).join(', ');
  const chunks = [];
  const chunkSize = 100;
  for (let i = 0; i < rows.length; i += chunkSize) {
    const slice = rows.slice(i, i + chunkSize);
    const valuesList = slice.map(row => {
      const vals = columns.map(c => escapeSql(row[c]));
      return `(${vals.join(', ')})`;
    }).join(',\n');
    if (table === 'PaymentMethod') {
      const selected = columns.map(c => `seed.[${c}]`).join(', ');
      chunks.push(`INSERT dbo.[${table}] (${colList})\nSELECT ${selected}\nFROM (VALUES\n${valuesList}) AS seed (${colList})\nWHERE NOT EXISTS (\n    SELECT 1 FROM dbo.[PaymentMethod] WITH (UPDLOCK, HOLDLOCK)\n    WHERE [MethodCode] = seed.[MethodCode]\n);`);
    } else {
      chunks.push(`INSERT dbo.[${table}] (${colList}) VALUES\n${valuesList};`);
    }
  }
  return chunks.join('\n\n') + '\n\n';
}

function uuid(prefix, num) {
  // Generates consistent predictable UUIDs
  // prefix is like '10000000-0000-0000-0000-' (24 chars), num is hex padded to 12 chars
  const hexNum = num.toString(16).padStart(12, '0');
  return `${prefix}${hexNum}`;
}

function pad(n) { return n < 10 ? '0' + n : n.toString(); }

function fmtDate(d) {
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())} ${pad(d.getHours())}:${pad(d.getMinutes())}:${pad(d.getSeconds())}`;
}

function fmtDateOnly(d) {
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
}

console.log('Generating seed data...');

// ==========================================
// 1. BASE IDENTIFIERS
// ==========================================
const R1_ID = '20000000-0000-0000-0000-000000000001'; // Nhà hàng Sen Việt (Chính)
const R2_ID = '20000000-0000-0000-0000-000000000009'; // Nhà hàng Sen Việt (Chi nhánh 2)

// Staff Users
const U_ADMIN = '10000000-0000-0000-0000-000000000001';
const U_MGR   = '10000000-0000-0000-0000-000000000002';
const U_W1    = '10000000-0000-0000-0000-000000000003';
const U_W2    = '10000000-0000-0000-0000-000000000013';
const U_C1    = '10000000-0000-0000-0000-000000000004';
const U_C2    = '10000000-0000-0000-0000-000000000014';
const U_K1    = '10000000-0000-0000-0000-000000000005';
const U_K2    = '10000000-0000-0000-0000-000000000015';
const U_INV   = '10000000-0000-0000-0000-000000000006';
const U_PLT   = '10000000-0000-0000-0000-000000000007';

// Online Customer Users
const U_CUST = [
  { id: '10000000-0000-0000-0000-000000000021', name: 'Nguyễn Văn An', email: 'an.nguyen@gmail.com', phone: '0901234567' },
  { id: '10000000-0000-0000-0000-000000000022', name: 'Trần Thị Bình', email: 'binh.tran@gmail.com', phone: '0912345678' },
  { id: '10000000-0000-0000-0000-000000000023', name: 'Lê Hoàng Cường', email: 'cuong.le@gmail.com', phone: '0923456789' },
  { id: '10000000-0000-0000-0000-000000000024', name: 'Phạm Thị Dung', email: 'dung.pham@gmail.com', phone: '0934567890' },
  { id: '10000000-0000-0000-0000-000000000025', name: 'Hoàng Văn Em', email: 'em.hoang@gmail.com', phone: '0945678901' },
];

const PWD_HASH = '$2a$11$36mwcLNIUUJz6qqA5XoxMe9TXyIYr5fKU6qy1a9GDbbNhpxVijQEa'; // '123'

// CustomerDb Loyalty Tiers
const TIER_BRONZE  = '30000000-0000-0000-0000-000000000001';
const TIER_SILVER  = '30000000-0000-0000-0000-000000000002';
const TIER_GOLD    = '30000000-0000-0000-0000-000000000003';
const TIER_DIAMOND = '30000000-0000-0000-0000-000000000004';
const LOYALTY_POLICY_ID = '30000000-0000-0000-0000-000000000010';

const CUSTOMERS = [
  { id: '30000000-0000-0000-0000-000000000101', phone: '0901234567', name: 'Nguyễn Văn An', tier: TIER_DIAMOND, userRef: U_CUST[0].id },
  { id: '30000000-0000-0000-0000-000000000102', phone: '0912345678', name: 'Trần Thị Bình', tier: TIER_GOLD, userRef: U_CUST[1].id },
  { id: '30000000-0000-0000-0000-000000000103', phone: '0923456789', name: 'Lê Hoàng Cường', tier: TIER_SILVER, userRef: U_CUST[2].id },
  { id: '30000000-0000-0000-0000-000000000104', phone: '0934567890', name: 'Phạm Thị Dung', tier: TIER_SILVER, userRef: U_CUST[3].id },
  { id: '30000000-0000-0000-0000-000000000105', phone: '0945678901', name: 'Hoàng Văn Em', tier: TIER_BRONZE, userRef: U_CUST[4].id },
  { id: '30000000-0000-0000-0000-000000000106', phone: '0988776655', name: 'Vũ Thị Phương', tier: TIER_GOLD, userRef: null },
  { id: '30000000-0000-0000-0000-000000000107', phone: '0977665544', name: 'Đặng Quốc Huy', tier: TIER_BRONZE, userRef: null },
  { id: '30000000-0000-0000-0000-000000000108', phone: '0966554433', name: 'Bùi Mai Lan', tier: TIER_SILVER, userRef: null },
  { id: '30000000-0000-0000-0000-000000000109', phone: '0911223344', name: 'Ngô Kiến Huy', tier: TIER_BRONZE, userRef: null },
  { id: '30000000-0000-0000-0000-000000000110', phone: '0933445566', name: 'Đỗ Thùy Trang', tier: TIER_GOLD, userRef: null },
];

// RestaurantDb Areas & Tables
const AREA_MAIN   = '20000000-0000-0000-0000-000000000002'; // Tầng 1 - Sảnh chính
const AREA_VIP    = '20000000-0000-0000-0000-000000000003'; // Tầng 2 - Phòng VIP
const AREA_GARDEN = '20000000-0000-0000-0000-000000000004'; // Sân vườn ngoài trời

const TABLES = [
  { id: '20000000-0000-0000-0000-000000000011', area: AREA_MAIN, code: 'T01', cap: 4, x: 10, y: 10 },
  { id: '20000000-0000-0000-0000-000000000012', area: AREA_MAIN, code: 'T02', cap: 4, x: 20, y: 10 },
  { id: '20000000-0000-0000-0000-000000000013', area: AREA_MAIN, code: 'T03', cap: 4, x: 30, y: 10 },
  { id: '20000000-0000-0000-0000-000000000014', area: AREA_MAIN, code: 'T04', cap: 6, x: 40, y: 10 },
  { id: '20000000-0000-0000-0000-000000000015', area: AREA_MAIN, code: 'T05', cap: 6, x: 50, y: 10 },
  { id: '20000000-0000-0000-0000-000000000016', area: AREA_MAIN, code: 'T06', cap: 2, x: 60, y: 10 },
  { id: '20000000-0000-0000-0000-000000000017', area: AREA_VIP,  code: 'VIP01', cap: 10, x: 10, y: 30 },
  { id: '20000000-0000-0000-0000-000000000018', area: AREA_VIP,  code: 'VIP02', cap: 8,  x: 20, y: 30 },
  { id: '20000000-0000-0000-0000-000000000019', area: AREA_GARDEN, code: 'G01', cap: 4, x: 10, y: 50 },
  { id: '20000000-0000-0000-0000-000000000020', area: AREA_GARDEN, code: 'G02', cap: 6, x: 20, y: 50 },
];

// Ordering Kitchen Stations
const KS_HOT  = '20000000-0000-0000-0000-000000000021'; // Bếp nóng
const KS_BAR  = '20000000-0000-0000-0000-000000000022'; // Quầy bar & tráng miệng
const KS_COLD = '20000000-0000-0000-0000-000000000023'; // Bếp nguội / salad

// Inventory Units
const U_GRAM = '20000000-0000-0000-0000-000000000100';
const U_KG   = '20000000-0000-0000-0000-000000000101';
const U_ML   = '20000000-0000-0000-0000-000000000102';
const U_L    = '20000000-0000-0000-0000-000000000103';
const U_CAN  = '20000000-0000-0000-0000-000000000104';
const U_PIECE= '20000000-0000-0000-0000-000000000105';

// Inventory Ingredients
const ING = [
  { id: '20000000-0000-0000-0000-000000000031', code: 'RICE', name: 'Gạo ST25', unit: 'g', stock: 150000, low: 20000, cost: 25 },
  { id: '20000000-0000-0000-0000-000000000032', code: 'BEEF', name: 'Thịt bò Úc tươi', unit: 'g', stock: 80000, low: 10000, cost: 280 },
  { id: '20000000-0000-0000-0000-000000000033', code: 'PORK', name: 'Thịt ba chỉ heo', unit: 'g', stock: 95000, low: 15000, cost: 130 },
  { id: '20000000-0000-0000-0000-000000000034', code: 'CHICKEN', name: 'Gà ta làm sạch', unit: 'g', stock: 70000, low: 10000, cost: 120 },
  { id: '20000000-0000-0000-0000-000000000035', code: 'SHRIMP', name: 'Tôm sú tươi', unit: 'g', stock: 50000, low: 8000, cost: 260 },
  { id: '20000000-0000-0000-0000-000000000036', code: 'NOODLE', name: 'Bún phở tươi', unit: 'g', stock: 60000, low: 10000, cost: 20 },
  { id: '20000000-0000-0000-0000-000000000037', code: 'VEG', name: 'Rau sống & củ quả', unit: 'g', stock: 45000, low: 5000, cost: 35 },
  { id: '20000000-0000-0000-0000-000000000038', code: 'HOTPOT_BASE', name: 'Cốt nước dùng lẩu', unit: 'g', stock: 40000, low: 5000, cost: 80 },
  { id: '20000000-0000-0000-0000-000000000039', code: 'LOTUS_SEED', name: 'Hạt sen Huế', unit: 'g', stock: 25000, low: 3000, cost: 150 },
  { id: '20000000-0000-0000-0000-00000000003A', code: 'TEA_LEAF', name: 'Trà búp xanh', unit: 'g', stock: 30000, low: 4000, cost: 90 },
  { id: '20000000-0000-0000-0000-00000000003B', code: 'MILK', name: 'Sữa tươi thanh trùng', unit: 'ml', stock: 60000, low: 8000, cost: 35 },
  { id: '20000000-0000-0000-0000-00000000003C', code: 'COCA', name: 'Coca/Pepsi lon 330ml', unit: 'lon', stock: 350, low: 50, cost: 10000 },
];

// Catalog Categories
const CAT_APPETIZER = '20000000-0000-0000-0000-000000000040';
const CAT_MAIN      = '20000000-0000-0000-0000-000000000041';
const CAT_HOTPOT    = '20000000-0000-0000-0000-000000000042';
const CAT_DESSERT   = '20000000-0000-0000-0000-000000000043';
const CAT_DRINK     = '20000000-0000-0000-0000-000000000044';

// Catalog Items
const ITEMS = [
  { id: '20000000-0000-0000-0000-000000000051', cat: CAT_APPETIZER, code: 'APP-01', name: 'Gỏi cuốn tôm thịt (3 cuốn)', price: 45000, ks: KS_COLD, prep: 10, ings: [{ ing: ING[4].id, qty: 60 }, { ing: ING[2].id, qty: 50 }, { ing: ING[6].id, qty: 50 }] },
  { id: '20000000-0000-0000-0000-000000000052', cat: CAT_APPETIZER, code: 'APP-02', name: 'Nem rán Hà Nội giòn rụm', price: 55000, ks: KS_HOT, prep: 15, ings: [{ ing: ING[2].id, qty: 80 }, { ing: ING[6].id, qty: 40 }] },
  { id: '20000000-0000-0000-0000-000000000053', cat: CAT_MAIN, code: 'PHO-01', name: 'Phở bò tái lăn đặc biệt', price: 65000, ks: KS_HOT, prep: 12, ings: [{ ing: ING[1].id, qty: 120 }, { ing: ING[5].id, qty: 180 }, { ing: ING[6].id, qty: 30 }] },
  { id: '20000000-0000-0000-0000-000000000054', cat: CAT_MAIN, code: 'BUN-01', name: 'Bún chả nướng than hoa', price: 60000, ks: KS_HOT, prep: 15, ings: [{ ing: ING[2].id, qty: 130 }, { ing: ING[5].id, qty: 180 }, { ing: ING[6].id, qty: 50 }] },
  { id: '20000000-0000-0000-0000-000000000055', cat: CAT_MAIN, code: 'COM-01', name: 'Cơm sườn nướng mật ong', price: 55000, ks: KS_HOT, prep: 12, ings: [{ ing: ING[0].id, qty: 150 }, { ing: ING[2].id, qty: 140 }] },
  { id: '20000000-0000-0000-0000-000000000056', cat: CAT_MAIN, code: 'BO-01',  name: 'Bò lúc lắc khoai tây', price: 85000, ks: KS_HOT, prep: 15, ings: [{ ing: ING[1].id, qty: 150 }, { ing: ING[6].id, qty: 80 }] },
  { id: '20000000-0000-0000-0000-000000000057', cat: CAT_HOTPOT, code: 'LAU-01', name: 'Lẩu Thái hải sản chua cay', price: 299000, ks: KS_HOT, prep: 20, ings: [{ ing: ING[4].id, qty: 250 }, { ing: ING[1].id, qty: 150 }, { ing: ING[7].id, qty: 100 }, { ing: ING[5].id, qty: 250 }] },
  { id: '20000000-0000-0000-0000-000000000058', cat: CAT_HOTPOT, code: 'LAU-02', name: 'Lẩu gà lá é Tao Ngộ', price: 259000, ks: KS_HOT, prep: 20, ings: [{ ing: ING[3].id, qty: 500 }, { ing: ING[6].id, qty: 150 }, { ing: ING[5].id, qty: 250 }] },
  { id: '20000000-0000-0000-0000-000000000059', cat: CAT_DESSERT, code: 'DES-01', name: 'Chè hạt sen long nhãn', price: 35000, ks: KS_BAR, prep: 5, ings: [{ ing: ING[8].id, qty: 50 }] },
  { id: '20000000-0000-0000-0000-00000000005A', cat: CAT_DESSERT, code: 'DES-02', name: 'Kem dừa Côn Đảo', price: 35000, ks: KS_BAR, prep: 5, ings: [{ ing: ING[10].id, qty: 80 }] },
  { id: '20000000-0000-0000-0000-00000000005B', cat: CAT_DRINK, code: 'DRK-01', name: 'Trà sen vàng kem cheese', price: 40000, ks: KS_BAR, prep: 5, ings: [{ ing: ING[9].id, qty: 10 }, { ing: ING[8].id, qty: 20 }, { ing: ING[10].id, qty: 50 }] },
  { id: '20000000-0000-0000-0000-00000000005C', cat: CAT_DRINK, code: 'DRK-02', name: 'Trà đào cam sả thanh nhiệt', price: 35000, ks: KS_BAR, prep: 5, ings: [{ ing: ING[9].id, qty: 10 }] },
  { id: '20000000-0000-0000-0000-00000000005D', cat: CAT_DRINK, code: 'DRK-03', name: 'Cà phê sữa đá Sài Gòn', price: 28000, ks: KS_BAR, prep: 5, ings: [{ ing: ING[10].id, qty: 40 }] },
  { id: '20000000-0000-0000-0000-00000000005E', cat: CAT_DRINK, code: 'DRK-04', name: 'Nước ngọt Coca Cola lon', price: 18000, ks: KS_BAR, prep: 2, ings: [{ ing: ING[11].id, qty: 1 }] },
];

// Option Groups & Options
const OG_TOPPING = '20000000-0000-0000-0000-000000000071';
const OG_SUGAR   = '20000000-0000-0000-0000-000000000072';
const OG_ICE     = '20000000-0000-0000-0000-000000000073';

const OPTIONS = [
  { id: '20000000-0000-0000-0000-000000000081', og: OG_TOPPING, name: 'Thêm thịt bò tươi', price: 25000 },
  { id: '20000000-0000-0000-0000-000000000082', og: OG_TOPPING, name: 'Thêm cơm trắng', price: 10000 },
  { id: '20000000-0000-0000-0000-000000000083', og: OG_TOPPING, name: 'Thêm bún tươi', price: 10000 },
  { id: '20000000-0000-0000-0000-000000000084', og: OG_TOPPING, name: 'Thêm trứng ốp la', price: 10000 },
  { id: '20000000-0000-0000-0000-000000000085', og: OG_SUGAR,   name: '100% đường (chuẩn)', price: 0 },
  { id: '20000000-0000-0000-0000-000000000086', og: OG_SUGAR,   name: '50% đường (ít ngọt)', price: 0 },
  { id: '20000000-0000-0000-0000-000000000087', og: OG_SUGAR,   name: 'Không đường', price: 0 },
  { id: '20000000-0000-0000-0000-000000000088', og: OG_ICE,     name: 'Bình thường (nhiều đá)', price: 0 },
  { id: '20000000-0000-0000-0000-000000000089', og: OG_ICE,     name: 'Ít đá', price: 0 },
  { id: '20000000-0000-0000-0000-00000000008A', og: OG_ICE,     name: 'Không đá', price: 0 },
];

// Billing Config
const CASH_REG_ID = '40000000-0000-0000-0000-000000000001';
const TAX_POLICY_ID = '40000000-0000-0000-0000-000000000002';
const PROMO_1_ID = '40000000-0000-0000-0000-000000000011'; // Giảm 10%
const PROMO_2_ID = '40000000-0000-0000-0000-000000000012'; // Giảm 20k
const VOUCHER_1_ID = '40000000-0000-0000-0000-000000000021'; // SENVIET2026 (50k)
const VOUCHER_2_ID = '40000000-0000-0000-0000-000000000022'; // VIPMEMBER (100k)
const VOUCHER_3_ID = '40000000-0000-0000-0000-000000000023'; // FREESHIP (30k)

console.log('Building datasets...');

// ==========================================
// 2. SIMULATION GENERATION: 30 DAYS (2026-10-25 to 2026-11-25)
// ==========================================

// Collections for all 7 databases
const db_identity = {
  Role: [],
  User: [],
  UserSession: [],
  AuditLog: [],
  Notification: [],
  NotificationRecipient: [],
  Announcement: [],
  NotificationSetting: [],
  StaffTransfer: []
};

const db_restaurant = {
  Restaurant: [],
  Area: [],
  OpeningHour: [],
  Table: [],
  RestaurantStaff: [],
  Qr: [],
  Reservation: [],
  ReservationTable: [],
  TableSession: [],
  SessionTable: [],
  GuestAccess: [],
  ServiceRequest: [],
  StaffTransferReceipt: [],
  TableTransferSession: [],
  NotificationOutbox: []
};

const db_catalog = {
  Category: [],
  Item: [],
  Price: [],
  OptionGroup: [],
  Option: [],
  ItemOptionGroup: [],
  RecipeVersion: [],
  RecipeIngredient: [],
  RecipeMedia: []
};

const db_inventory = {
  Unit: [],
  Ingredient: [],
  IngredientUnit: [],
  GoodsReceipt: [],
  GoodsReceiptItem: [],
  StockIssue: [],
  Stocktake: [],
  StocktakeItem: [],
  StockMovement: []
};

const db_ordering = {
  KitchenStation: [],
  KitchenAssignment: [],
  Cart: [],
  CartItem: [],
  CartItemOption: [],
  CheckoutLock: [],
  Order: [],
  OrderItem: [],
  OrderItemOption: [],
  KitchenTask: [],
  Event: [],
  ServiceRecord: [],
  CancellationRequest: [],
  TableTransfer: [],
  NotificationOutbox: []
};

const db_customer = {
  LoyaltyTier: [],
  LoyaltyPolicy: [],
  Customer: [],
  LoyaltyEntry: [],
  PointReservation: [],
  FeedbackAccess: [],
  Feedback: [],
  FeedbackItem: []
};

const db_billing = {
  PaymentMethod: [],
  PaymentProviderConfig: [],
  DiscountLimit: [],
  TaxFeePolicy: [],
  Promotion: [],
  PromotionTarget: [],
  Voucher: [],
  CashRegister: [],
  CashShift: [],
  ShiftReport: [],
  Bill: [],
  BillLine: [],
  BillCheckout: [],
  BillAdjustment: [],
  Payment: [],
  CashMovement: [],
  CashHandover: [],
  OnlinePayments: [],
  PaymentWebhook: [],
  ReceiptPrint: [],
  ElectronicInvoice: [],
  Refund: [],
  RefundLine: [],
  VoucherRedemption: [],
  NotificationOutbox: []
};

// ----------------------------------------------------
// POPULATE STATIC MASTER DATA
// ----------------------------------------------------

// IdentityDb Roles
[
  { RoleId: 'admin', RoleName: 'Quản trị viên', Description: 'Quản trị toàn quyền hệ thống' },
  { RoleId: 'manager', RoleName: 'Quản lý', Description: 'Quản lý vận hành nhà hàng' },
  { RoleId: 'waiter', RoleName: 'Nhân viên phục vụ', Description: 'Phục vụ bàn và đón tiếp khách' },
  { RoleId: 'cashier', RoleName: 'Thu ngân', Description: 'Quản lý ca thanh toán và sổ quỹ' },
  { RoleId: 'kitchen', RoleName: 'Nhân viên bếp', Description: 'Chế biến món ăn và đồ uống' },
  { RoleId: 'inventory', RoleName: 'Thủ kho', Description: 'Quản lý nhập xuất kiểm kê kho' },
  { RoleId: 'customer', RoleName: 'Khách hàng', Description: 'Khách hàng đặt món online' },
  { RoleId: 'platform_admin', RoleName: 'Quản trị nền tảng', Description: 'Quản trị hệ thống đa chi nhánh' }
].forEach(r => db_identity.Role.push(r));

// IdentityDb Users
[
  { UserId: U_ADMIN, RoleId: 'admin', UserName: 'admin', Email: 'admin@sen-viet.test', PhoneNumber: '0900000001', FullName: 'Nguyễn Văn Minh (Admin)', AvatarUrl: 'https://images.unsplash.com/photo-1534528741775-53994a69daeb', PasswordHash: PWD_HASH, IsActive: true, CreatedAt: '2026-01-01 00:00:00', UpdatedAt: null, RestaurantId: R1_ID, ProvisioningPending: false },
  { UserId: U_MGR, RoleId: 'manager', UserName: 'manager', Email: 'manager@sen-viet.test', PhoneNumber: '0900000002', FullName: 'Trần Thị Thu (Quản lý)', AvatarUrl: 'https://images.unsplash.com/photo-1544005313-94ddf0286df2', PasswordHash: PWD_HASH, IsActive: true, CreatedAt: '2026-01-01 00:00:00', UpdatedAt: null, RestaurantId: R1_ID, ProvisioningPending: false },
  { UserId: U_W1, RoleId: 'waiter', UserName: 'waiter1', Email: 'waiter1@sen-viet.test', PhoneNumber: '0900000003', FullName: 'Lê Hoàng Long (Phục vụ 1)', AvatarUrl: null, PasswordHash: PWD_HASH, IsActive: true, CreatedAt: '2026-01-01 00:00:00', UpdatedAt: null, RestaurantId: R1_ID, ProvisioningPending: false },
  { UserId: U_W2, RoleId: 'waiter', UserName: 'waiter2', Email: 'waiter2@sen-viet.test', PhoneNumber: '0900000013', FullName: 'Vũ Quốc Bảo (Phục vụ 2)', AvatarUrl: null, PasswordHash: PWD_HASH, IsActive: true, CreatedAt: '2026-01-01 00:00:00', UpdatedAt: null, RestaurantId: R1_ID, ProvisioningPending: false },
  { UserId: U_C1, RoleId: 'cashier', UserName: 'cashier1', Email: 'cashier1@sen-viet.test', PhoneNumber: '0900000004', FullName: 'Nguyễn Mai Anh (Thu ngân 1)', AvatarUrl: null, PasswordHash: PWD_HASH, IsActive: true, CreatedAt: '2026-01-01 00:00:00', UpdatedAt: null, RestaurantId: R1_ID, ProvisioningPending: false },
  { UserId: U_C2, RoleId: 'cashier', UserName: 'cashier2', Email: 'cashier2@sen-viet.test', PhoneNumber: '0900000014', FullName: 'Đặng Thùy Linh (Thu ngân 2)', AvatarUrl: null, PasswordHash: PWD_HASH, IsActive: true, CreatedAt: '2026-01-01 00:00:00', UpdatedAt: null, RestaurantId: R1_ID, ProvisioningPending: false },
  { UserId: U_K1, RoleId: 'kitchen', UserName: 'kitchen_hot', Email: 'kitchen1@sen-viet.test', PhoneNumber: '0900000005', FullName: 'Phạm Đức Thành (Bếp trưởng)', AvatarUrl: null, PasswordHash: PWD_HASH, IsActive: true, CreatedAt: '2026-01-01 00:00:00', UpdatedAt: null, RestaurantId: R1_ID, ProvisioningPending: false },
  { UserId: U_K2, RoleId: 'kitchen', UserName: 'kitchen_bar', Email: 'kitchen2@sen-viet.test', PhoneNumber: '0900000015', FullName: 'Hoàng Kim Ngân (Pha chế)', AvatarUrl: null, PasswordHash: PWD_HASH, IsActive: true, CreatedAt: '2026-01-01 00:00:00', UpdatedAt: null, RestaurantId: R1_ID, ProvisioningPending: false },
  { UserId: U_INV, RoleId: 'inventory', UserName: 'inventory', Email: 'inventory@sen-viet.test', PhoneNumber: '0900000006', FullName: 'Bùi Tuấn Kiệt (Thủ kho)', AvatarUrl: null, PasswordHash: PWD_HASH, IsActive: true, CreatedAt: '2026-01-01 00:00:00', UpdatedAt: null, RestaurantId: R1_ID, ProvisioningPending: false },
  { UserId: U_PLT, RoleId: 'platform_admin', UserName: 'platform-admin', Email: 'platform-admin@sen-viet.test', PhoneNumber: '0900000007', FullName: 'Quản trị Nền tảng Sen Việt', AvatarUrl: null, PasswordHash: PWD_HASH, IsActive: true, CreatedAt: '2026-01-01 00:00:00', UpdatedAt: null, RestaurantId: null, ProvisioningPending: false }
].forEach(u => db_identity.User.push(u));

U_CUST.forEach((c, idx) => {
  db_identity.User.push({
    UserId: c.id,
    RoleId: 'customer',
    UserName: `cust_${idx + 1}`,
    Email: c.email,
    PhoneNumber: c.phone,
    FullName: c.name,
    AvatarUrl: null,
    PasswordHash: PWD_HASH,
    IsActive: true,
    CreatedAt: '2026-01-01 00:00:00',
    UpdatedAt: null,
    RestaurantId: R1_ID,
    ProvisioningPending: false
  });
});

// Identity NotificationSettings
['OrderCreated', 'LowStockAlert', 'ShiftClosed', 'ServiceRequestCall', 'CancellationRequested'].forEach(t => {
  db_identity.NotificationSetting.push({ RestaurantId: R1_ID, Type: t, Enabled: true });
});

// RestaurantDb Restaurants
db_restaurant.Restaurant.push(
  { RestaurantId: R1_ID, RestaurantName: 'Nhà hàng Sen Việt - Tràng Tiền', TaxCode: '0109876543', Address: 'Số 18 Tràng Tiền, Quận Hoàn Kiếm, Hà Nội', Hotline: '02439876543', LogoUrl: 'https://images.unsplash.com/photo-1517248135467-4c7edcad34c4', TimeZone: 'Asia/Ho_Chi_Minh' },
  { RestaurantId: R2_ID, RestaurantName: 'Nhà hàng Sen Việt - Chi nhánh Cầu Giấy', TaxCode: '0109876543-001', Address: 'Số 102 Cầu Giấy, Quận Cầu Giấy, Hà Nội', Hotline: '02437654321', LogoUrl: 'https://images.unsplash.com/photo-1517248135467-4c7edcad34c4', TimeZone: 'Asia/Ho_Chi_Minh' }
);

// RestaurantDb Areas
db_restaurant.Area.push(
  { AreaId: AREA_MAIN, RestaurantId: R1_ID, AreaCode: 'MAIN', AreaName: 'Tầng 1 - Sảnh chính', ServiceType: 'DineIn', SpaceType: 'Indoor', IsActive: true },
  { AreaId: AREA_VIP, RestaurantId: R1_ID, AreaCode: 'VIP', AreaName: 'Tầng 2 - Phòng VIP', ServiceType: 'DineIn', SpaceType: 'Indoor', IsActive: true },
  { AreaId: AREA_GARDEN, RestaurantId: R1_ID, AreaCode: 'GARDEN', AreaName: 'Sân vườn ngoài trời', ServiceType: 'DineIn', SpaceType: 'Outdoor', IsActive: true }
);

// RestaurantDb Tables & QR
TABLES.forEach((t, index) => {
  db_restaurant.Table.push({
    TableId: t.id,
    AreaId: t.area,
    TableCode: t.code,
    Capacity: t.cap,
    PositionX: t.x,
    PositionY: t.y,
    IsActive: true,
    CleaningStatus: 'Clean'
  });
  db_restaurant.Qr.push({
    QrId: uuid('20000000-0000-0000-0001-', index + 1),
    TableId: t.id,
    TokenHash: crypto.createHash('sha256').update(`TABLE_${t.code}_SECRET`).digest('hex'),
    CreatedAt: '2026-01-01 00:00:00',
    RevokedAt: null,
    ImageUrl: `https://api.qrserver.com/v1/create-qr-code/?size=300x300&data=http://localhost:5173/menu?table=${t.id}`
  });
});

// RestaurantStaff
[U_ADMIN, U_MGR, U_W1, U_W2, U_C1, U_C2, U_K1, U_K2, U_INV].forEach(uid => {
  db_restaurant.RestaurantStaff.push({ RestaurantId: R1_ID, UserIdRef: uid, IsActive: true });
});

// OpeningHour for all 7 days (0..6)
for (let d = 0; d <= 6; d++) {
  db_restaurant.OpeningHour.push({
    OpeningHourId: uuid('20000000-0000-0000-0002-', d + 1),
    RestaurantId: R1_ID,
    DayOfWeek: d,
    OpenAt: '08:00:00',
    CloseAt: '22:30:00'
  });
}

// Catalog Categories
db_catalog.Category.push(
  { CategoryId: CAT_APPETIZER, CategoryCode: 'APPETIZER', CategoryName: 'Khai vị đặc sắc', IsActive: true, RestaurantId: R1_ID },
  { CategoryId: CAT_MAIN, CategoryCode: 'MAIN', CategoryName: 'Món chính truyền thống', IsActive: true, RestaurantId: R1_ID },
  { CategoryId: CAT_HOTPOT, CategoryCode: 'HOTPOT', CategoryName: 'Lẩu & Đặc sản Sen Việt', IsActive: true, RestaurantId: R1_ID },
  { CategoryId: CAT_DESSERT, CategoryCode: 'DESSERT', CategoryName: 'Tráng miệng thanh tao', IsActive: true, RestaurantId: R1_ID },
  { CategoryId: CAT_DRINK, CategoryCode: 'DRINK', CategoryName: 'Đồ uống & Trà hoa quả', IsActive: true, RestaurantId: R1_ID }
);

// Catalog Items, Prices, RecipeVersions, RecipeIngredients, RecipeMedia
ITEMS.forEach((it, idx) => {
  db_catalog.Item.push({
    ItemId: it.id,
    CategoryId: it.cat,
    ItemCode: it.code,
    ItemName: it.name,
    Description: `Món ngon tinh hoa ẩm thực Sen Việt - ${it.name}`,
    ImageUrl: 'https://images.unsplash.com/photo-1546069901-ba9599a7e63c',
    Availability: 'Available',
    KitchenStationRef: it.ks,
    PreparationTimeMinutes: it.prep,
    RestaurantRef: R1_ID
  });

  db_catalog.Price.push({
    PriceId: uuid('20000000-0000-0000-0003-', idx + 1),
    ItemId: it.id,
    Channel: 'AllChannels',
    Amount: it.price,
    EffectiveFrom: '2026-01-01 00:00:00',
    EffectiveTo: null,
    DailyStart: null,
    DailyEnd: null,
    RestaurantId: R1_ID
  });

  const recipeVerId = uuid('20000000-0000-0000-0004-', idx + 1);
  db_catalog.RecipeVersion.push({
    RecipeVersionId: recipeVerId,
    ItemId: it.id,
    OptionId: null,
    Version: 1,
    EffectiveFrom: '2026-01-01 00:00:00',
    EffectiveTo: null,
    Instructions: `Chế biến ${it.name} theo đúng tiêu chuẩn công thức nhà hàng.`,
    RestaurantId: R1_ID
  });

  it.ings.forEach((ing, ingIdx) => {
    db_catalog.RecipeIngredient.push({
      RecipeIngredientId: uuid('20000000-0000-0000-0005-', idx * 10 + ingIdx + 1),
      RecipeVersionId: recipeVerId,
      IngredientRef: ing.ing,
      UnitRef: U_GRAM,
      Quantity: ing.qty,
      BaseQuantitySnapshot: ing.qty,
      ConversionFactorSnapshot: 1,
      RestaurantId: R1_ID
    });
  });

  db_catalog.RecipeMedia.push({
    RecipeMediaId: uuid('20000000-0000-0000-0006-', idx + 1),
    RecipeVersionId: recipeVerId,
    MediaType: 'image',
    Url: 'https://images.unsplash.com/photo-1546069901-ba9599a7e63c',
    DisplayOrder: 1,
    RestaurantId: R1_ID
  });
});

// OptionGroups & Options
db_catalog.OptionGroup.push(
  { OptionGroupId: OG_TOPPING, OptionGroupName: 'Món ăn kèm & Topping thêm', RestaurantId: R1_ID },
  { OptionGroupId: OG_SUGAR, OptionGroupName: 'Độ ngọt (Mức đường)', RestaurantId: R1_ID },
  { OptionGroupId: OG_ICE, OptionGroupName: 'Lượng đá lạnh', RestaurantId: R1_ID }
);

OPTIONS.forEach((opt, idx) => {
  db_catalog.Option.push({
    OptionId: opt.id,
    OptionGroupId: opt.og,
    OptionName: opt.name,
    ExtraPrice: opt.price,
    Availability: 'Available',
    RestaurantId: R1_ID
  });

  // Recipe for options if needed
  const rVerId = uuid('20000000-0000-0000-0007-', idx + 1);
  db_catalog.RecipeVersion.push({
    RecipeVersionId: rVerId,
    ItemId: null,
    OptionId: opt.id,
    Version: 1,
    EffectiveFrom: '2026-01-01 00:00:00',
    EffectiveTo: null,
    Instructions: `Chuẩn bị option: ${opt.name}`,
    RestaurantId: R1_ID
  });
});

// ItemOptionGroups: Main dishes get OG_TOPPING; Drinks get OG_SUGAR and OG_ICE
ITEMS.forEach(it => {
  if (it.cat === CAT_MAIN) {
    db_catalog.ItemOptionGroup.push({
      ItemId: it.id,
      OptionGroupId: OG_TOPPING,
      MinSelection: 0,
      MaxSelection: 2,
      RestaurantId: R1_ID
    });
  } else if (it.cat === CAT_DRINK && it.id !== ITEMS[13].id) {
    db_catalog.ItemOptionGroup.push(
      { ItemId: it.id, OptionGroupId: OG_SUGAR, MinSelection: 0, MaxSelection: 1, RestaurantId: R1_ID },
      { ItemId: it.id, OptionGroupId: OG_ICE, MinSelection: 0, MaxSelection: 1, RestaurantId: R1_ID }
    );
  }
});

// Inventory Units
db_inventory.Unit.push(
  { UnitId: U_GRAM, Code: 'g', Name: 'Gram', RestaurantId: R1_ID },
  { UnitId: U_KG, Code: 'kg', Name: 'Kilogram', RestaurantId: R1_ID },
  { UnitId: U_ML, Code: 'ml', Name: 'Milliliter', RestaurantId: R1_ID },
  { UnitId: U_L, Code: 'l', Name: 'Lít', RestaurantId: R1_ID },
  { UnitId: U_CAN, Code: 'lon', Name: 'Lon', RestaurantId: R1_ID },
  { UnitId: U_PIECE, Code: 'qua', Name: 'Quả', RestaurantId: R1_ID }
);

// Inventory Ingredients & Conversions
ING.forEach(ing => {
  db_inventory.Ingredient.push({
    IngredientId: ing.id,
    IngredientCode: ing.code,
    IngredientName: ing.name,
    BaseUnit: ing.unit,
    StockQuantity: ing.stock,
    LowStockThreshold: ing.low,
    IsActive: true,
    RestaurantId: R1_ID
  });

  if (ing.unit === 'g') {
    db_inventory.IngredientUnit.push({
      IngredientId: ing.id,
      UnitId: U_KG,
      ConversionFactor: 1000,
      RestaurantId: R1_ID
    });
  } else if (ing.unit === 'ml') {
    db_inventory.IngredientUnit.push({
      IngredientId: ing.id,
      UnitId: U_L,
      ConversionFactor: 1000,
      RestaurantId: R1_ID
    });
  }
});

// Ordering KitchenStations & Assignments
db_ordering.KitchenStation.push(
  { KitchenStationId: KS_HOT, KitchenStationCode: 'HOT', KitchenStationName: 'Bếp nóng & Lẩu chính', IsActive: true, RestaurantId: R1_ID },
  { KitchenStationId: KS_BAR, KitchenStationCode: 'BAR', KitchenStationName: 'Quầy pha chế & Tráng miệng', IsActive: true, RestaurantId: R1_ID },
  { KitchenStationId: KS_COLD, KitchenStationCode: 'COLD', KitchenStationName: 'Bếp nguội & Khai vị', IsActive: true, RestaurantId: R1_ID }
);

db_ordering.KitchenAssignment.push(
  { KitchenStationId: KS_HOT, UserRef: U_K1, RestaurantId: R1_ID },
  { KitchenStationId: KS_BAR, UserRef: U_K2, RestaurantId: R1_ID },
  { KitchenStationId: KS_COLD, UserRef: U_K1, RestaurantId: R1_ID }
);

// CustomerDb Loyalty Tiers & Policy
db_customer.LoyaltyTier.push(
  { LoyaltyTierId: TIER_BRONZE, LoyaltyTierCode: 'BRONZE', LoyaltyTierName: 'Thành viên Đồng', RequiredSpend: 0, DiscountPercent: 0, RestaurantId: R1_ID },
  { LoyaltyTierId: TIER_SILVER, LoyaltyTierCode: 'SILVER', LoyaltyTierName: 'Thành viên Bạc', RequiredSpend: 2000000, DiscountPercent: 5.0, RestaurantId: R1_ID },
  { LoyaltyTierId: TIER_GOLD, LoyaltyTierCode: 'GOLD', LoyaltyTierName: 'Thành viên Vàng', RequiredSpend: 5000000, DiscountPercent: 10.0, RestaurantId: R1_ID },
  { LoyaltyTierId: TIER_DIAMOND, LoyaltyTierCode: 'DIAMOND', LoyaltyTierName: 'Thành viên Kim Cương', RequiredSpend: 10000000, DiscountPercent: 15.0, RestaurantId: R1_ID }
);

db_customer.LoyaltyPolicy.push({
  PolicyId: LOYALTY_POLICY_ID,
  Version: 1,
  SpendPerPoint: 1000,
  MoneyPerRedeemedPoint: 100,
  EffectiveFrom: '2026-01-01 00:00:00',
  RestaurantId: R1_ID
});

// Customers
CUSTOMERS.forEach(c => {
  db_customer.Customer.push({
    CustomerId: c.id,
    NormalizedPhone: c.phone,
    CustomerName: c.name,
    LoyaltyTierId: c.tier,
    RestaurantId: R1_ID
  });
});

// Billing Master Data
db_billing.PaymentMethod.push(
  { MethodCode: 'Cash', IsEnabled: true },
  { MethodCode: 'BankTransfer', IsEnabled: true },
  { MethodCode: 'PayOS', IsEnabled: true },
  { MethodCode: 'Card', IsEnabled: true }
);

db_billing.PaymentProviderConfig.push({
  PaymentProviderConfigId: uuid('40000000-0000-0000-0000-', 99),
  RestaurantId: R1_ID,
  Provider: 'PayOS',
  ClientId: 'e5222e10-1b40-41dd-8d7f-dded6f5b7a3b',
  SecretReference: 'payos_secret_key_ref',
  ReturnUrl: 'http://localhost:5173/payment/success',
  CancelUrl: 'http://localhost:5173/payment/cancel',
  WebhookUrl: 'http://localhost:5000/api/billing/webhook/payos',
  IsVerified: true,
  Enabled: true
});

db_billing.DiscountLimit.push(
  { DiscountLimitId: uuid('40000000-0000-0000-0001-', 1), RoleRef: null, UserRef: U_MGR, LimitType: 'Percentage', MaximumValue: 20, EffectiveFrom: '2026-01-01 00:00:00', RestaurantId: R1_ID },
  { DiscountLimitId: uuid('40000000-0000-0000-0001-', 2), RoleRef: null, UserRef: U_C1,  LimitType: 'Percentage', MaximumValue: 5,  EffectiveFrom: '2026-01-01 00:00:00', RestaurantId: R1_ID }
);

db_billing.TaxFeePolicy.push({
  TaxFeePolicyId: TAX_POLICY_ID,
  Version: 1,
  VatRate: 0.08,
  ServiceRate: 0.0,
  ServiceEnabled: false,
  EffectiveFrom: '2026-01-01 00:00:00',
  RestaurantId: R1_ID
});

db_billing.Promotion.push(
  { PromotionId: PROMO_1_ID, PromotionName: 'Khuyến mãi Thu Đông - Giảm 10% tổng hóa đơn', DiscountType: 'Percentage', Type: 'Order', DiscountValue: 10, UsageLimit: 500, MinimumSpend: 200000, StartAt: '2026-10-01 00:00:00', EndAt: '2026-12-31 23:59:59', IsActive: true, RestaurantId: R1_ID },
  { PromotionId: PROMO_2_ID, PromotionName: 'Combo Trưa Vui Vẻ - Giảm 20.000đ', DiscountType: 'FixedAmount', Type: 'Order', DiscountValue: 20000, UsageLimit: 200, MinimumSpend: 100000, StartAt: '2026-10-01 00:00:00', EndAt: '2026-12-31 23:59:59', IsActive: true, RestaurantId: R1_ID }
);

db_billing.PromotionTarget.push(
  { PromotionTargetId: uuid('40000000-0000-0000-0002-', 1), PromotionId: PROMO_1_ID, ItemRef: null, CategoryRef: null, Role: 'AppliesTo', RequiredQuantity: 1, RestaurantId: R1_ID },
  { PromotionTargetId: uuid('40000000-0000-0000-0002-', 2), PromotionId: PROMO_2_ID, ItemRef: null, CategoryRef: CAT_MAIN, Role: 'AppliesTo', RequiredQuantity: 1, RestaurantId: R1_ID }
);

db_billing.Voucher.push(
  { VoucherId: VOUCHER_1_ID, NormalizedVoucherCode: 'SENVIET2026', DiscountType: 'FixedAmount', DiscountValue: 50000, MaximumDiscount: 50000, UsageLimit: 100, StartAt: '2026-10-01 00:00:00', EndAt: '2026-12-31 23:59:59', IsActive: true, RestaurantId: R1_ID },
  { VoucherId: VOUCHER_2_ID, NormalizedVoucherCode: 'VIPMEMBER', DiscountType: 'FixedAmount', DiscountValue: 100000, MaximumDiscount: 100000, UsageLimit: 50, StartAt: '2026-10-01 00:00:00', EndAt: '2026-12-31 23:59:59', IsActive: true, RestaurantId: R1_ID },
  { VoucherId: VOUCHER_3_ID, NormalizedVoucherCode: 'FREESHIP', DiscountType: 'FixedAmount', DiscountValue: 30000, MaximumDiscount: 30000, UsageLimit: 100, StartAt: '2026-10-01 00:00:00', EndAt: '2026-12-31 23:59:59', IsActive: true, RestaurantId: R1_ID }
);

db_billing.CashRegister.push({
  CashRegisterId: CASH_REG_ID,
  RestaurantId: R1_ID,
  Code: 'POS-01'
});

console.log('Master data created.');

// ==========================================
// 3. INVENTORY WEEKLY GOODS RECEIPTS & STOCKTAKES
// ==========================================
const receiptDates = ['2026-10-25 08:30:00', '2026-11-01 08:30:00', '2026-11-08 08:30:00', '2026-11-15 08:30:00', '2026-11-22 08:30:00'];
receiptDates.forEach((rd, rIdx) => {
  const grId = uuid('50000000-0000-0000-0001-', rIdx + 1);
  db_inventory.GoodsReceipt.push({
    GoodsReceiptId: grId,
    ReceiptCode: `GR-202611${pad(rIdx + 1)}`,
    Status: 'Confirmed',
    CreatedByRef: U_INV,
    CreatedAt: rd,
    ConfirmAt: rd,
    Note: `Nhập nguyên liệu định kỳ tuần ${rIdx + 1}`,
    RestaurantId: R1_ID
  });

  ING.forEach((ing, iIdx) => {
    const griId = uuid('50000000-0000-0000-0002-', rIdx * 20 + iIdx + 1);
    const qty = ing.unit === 'g' ? 30000 : (ing.unit === 'ml' ? 20000 : 100);
    db_inventory.GoodsReceiptItem.push({
      GoodsReceiptItemId: griId,
      GoodsReceiptId: grId,
      IngredientId: ing.id,
      ReceivedQuantity: qty,
      ExpiryDate: '2026-12-31',
      InputUnitId: ing.unit === 'g' ? U_KG : (ing.unit === 'ml' ? U_L : U_CAN),
      InputQuantity: ing.unit === 'g' ? qty / 1000 : (ing.unit === 'ml' ? qty / 1000 : qty),
      ConversionFactorSnapshot: ing.unit === 'g' || ing.unit === 'ml' ? 1000 : 1,
      RestaurantId: R1_ID
    });

    db_inventory.StockMovement.push({
      StockMovementId: uuid('50000000-0000-0000-0003-', rIdx * 20 + iIdx + 1),
      IngredientId: ing.id,
      GoodsReceiptItemId: griId,
      StocktakeItemId: null,
      MovementType: 'Receipt',
      QuantityChange: qty,
      CookingEventRef: null,
      OrderItemRef: null,
      PerformedByRef: U_INV,
      Reason: `Nhập kho từ phiếu ${db_inventory.GoodsReceipt[rIdx].ReceiptCode}`,
      CreatedAt: rd,
      StockIssueId: null,
      RestaurantId: R1_ID
    });
  });
});

// 2 Stocktakes
['2026-10-31 22:45:00', '2026-11-20 22:45:00'].forEach((sd, sIdx) => {
  const stkId = uuid('50000000-0000-0000-0004-', sIdx + 1);
  db_inventory.Stocktake.push({
    StocktakeId: stkId,
    StocktakeCode: `STK-202611${pad(sIdx + 1)}`,
    Status: 'Confirmed',
    CreatedByRef: U_INV,
    ApprovedByRef: U_MGR,
    CreatedAt: sd,
    ConfirmedAt: sd,
    RestaurantId: R1_ID
  });

  ING.forEach((ing, iIdx) => {
    const stkiId = uuid('50000000-0000-0000-0005-', sIdx * 20 + iIdx + 1);
    db_inventory.StocktakeItem.push({
      StocktakeItemId: stkiId,
      StocktakeId: stkId,
      IngredientId: ing.id,
      SystemQuantity: ing.stock,
      ActualQuantity: ing.stock - 50,
      SnapshotVersion: '0x0000000000000001',
      DiscrepancyReason: 'Hao hụt tự nhiên trong quá trình sơ chế bảo quản',
      RestaurantId: R1_ID
    });

    db_inventory.StockMovement.push({
      StockMovementId: uuid('50000000-0000-0000-0006-', sIdx * 20 + iIdx + 1),
      IngredientId: ing.id,
      GoodsReceiptItemId: null,
      StocktakeItemId: stkiId,
      MovementType: 'Adjustment',
      QuantityChange: -50,
      CookingEventRef: null,
      OrderItemRef: null,
      PerformedByRef: U_INV,
      Reason: `Cân đối kho sau kiểm kê ${db_inventory.Stocktake[sIdx].StocktakeCode}`,
      CreatedAt: sd,
      StockIssueId: null,
      RestaurantId: R1_ID
    });
  });
});

// 2 Stock Issues (Hao hụt/Hết hạn)
['2026-11-05 16:00:00', '2026-11-18 16:00:00'].forEach((idate, iIdx) => {
  const siId = uuid('50000000-0000-0000-0007-', iIdx + 1);
  db_inventory.StockIssue.push({
    StockIssueId: siId,
    CreatedByRef: U_INV,
    CreatedAt: idate,
    Reason: 'Xuất hủy rau thơm dập nát và nguyên liệu cận date',
    RequestHash: crypto.createHash('sha256').update(`STOCK_ISSUE_${iIdx}`).digest('hex'),
    RestaurantId: R1_ID
  });

  db_inventory.StockMovement.push({
    StockMovementId: uuid('50000000-0000-0000-0008-', iIdx + 1),
    IngredientId: ING[6].id, // Rau củ
    GoodsReceiptItemId: null,
    StocktakeItemId: null,
    MovementType: 'Issue',
    QuantityChange: -2000,
    CookingEventRef: null,
    OrderItemRef: null,
    PerformedByRef: U_INV,
    Reason: 'Xuất hủy rau dập hỏng theo phiếu hủy',
    CreatedAt: idate,
    StockIssueId: siId,
    RestaurantId: R1_ID
  });
});

// Staff Transfer sample
const staffTransferId = uuid('10000000-0000-0000-0008-', 1);
db_identity.StaffTransfer.push({
  StaffTransferId: staffTransferId,
  UserId: U_W2,
  SourceRestaurantId: R2_ID,
  TargetRestaurantId: R1_ID,
  RequestedBy: U_MGR,
  WasActive: true,
  CompletedAt: '2026-10-24 10:00:00',
  Status: 'Completed',
  Message: 'Điều chuyển nhân viên hỗ trợ tăng cường vận hành Sen Việt Tràng Tiền',
  CreatedAt: '2026-10-24 08:00:00'
});

db_restaurant.StaffTransferReceipt.push({
  StaffTransferReceiptId: staffTransferId,
  UserId: U_W2,
  SourceRestaurantId: R2_ID,
  TargetRestaurantId: R1_ID,
  Status: 'Completed',
  Message: 'Đã tiếp nhận nhân sự vào chi nhánh Tràng Tiền',
  CreatedAt: '2026-10-24 10:00:00'
});

// Announcements
[
  { id: uuid('10000000-0000-0000-0009-', 1), title: 'Kế hoạch vệ sinh an toàn thực phẩm tháng 11', msg: 'Yêu cầu toàn bộ các bộ phận bếp, kho và sảnh kiểm tra kỹ hạn sử dụng và lưu mẫu thực phẩm đúng quy trình.', date: '2026-11-01 09:00:00' },
  { id: uuid('10000000-0000-0000-0009-', 2), title: 'Triển khai chương trình tri ân thành viên Sen Việt', msg: 'Nhân viên thu ngân và phục vụ hướng dẫn khách hàng quét mã tích điểm và áp dụng voucher khi thanh toán.', date: '2026-11-10 09:00:00' }
].forEach(a => {
  db_identity.Announcement.push({
    AnnouncementId: a.id,
    RestaurantId: R1_ID,
    CreatedBy: U_MGR,
    Title: a.title,
    Message: a.msg,
    AllStaff: true,
    RecipientRoles: 'waiter,cashier,kitchen,inventory',
    Version: uuid('10000000-0000-0000-000A-', 1),
    CreatedAt: a.date,
    SentAt: a.date,
    NotificationId: null
  });
});

// ==========================================
// 4. DAILY SIMULATION: 30 DAYS SHIFTS, ORDERS & BILLS
// ==========================================
let orderGlobalIndex = 1;
let billGlobalIndex = 1;
let sessionGlobalIndex = 1;
let feedbackGlobalIndex = 1;
let taskGlobalIndex = 1;

const startDate = new Date('2026-10-25T00:00:00Z');

for (let dayOffset = 0; dayOffset < 31; dayOffset++) {
  const curDay = new Date(startDate.getTime() + dayOffset * 86400000);
  const dayStr = fmtDateOnly(curDay);

  // -------------------------
  // 4.1. Shifts (Morning & Evening)
  // -------------------------
  // Morning Shift (08:00 - 15:30) Cashier 1
  const mShiftId = uuid('60000000-0000-0000-0001-', dayOffset * 2 + 1);
  const mOpenAt = `${dayStr} 08:00:00`;
  const mCloseAt = `${dayStr} 15:30:00`;
  
  db_billing.CashShift.push({
    CashShiftId: mShiftId,
    CashRegisterId: CASH_REG_ID,
    CashierUserRef: U_C1,
    OpeningCash: 1000000,
    OpenedAt: mOpenAt,
    ClosedAt: mCloseAt,
    Status: 'Closed',
    RestaurantId: R1_ID
  });

  db_billing.CashMovement.push({
    CashMovementId: uuid('60000000-0000-0000-0002-', dayOffset * 4 + 1),
    CashShiftId: mShiftId,
    PaymentId: null,
    Type: 'OpeningFloat',
    SignedAmount: 1000000,
    Reason: 'Tiền mặt đầu ca sáng',
    PostedAt: mOpenAt,
    IdempotencyKey: `OPEN_${dayStr}_MORN`,
    RestaurantId: R1_ID
  });

  // Evening Shift (15:30 - 22:30) Cashier 2
  const eShiftId = uuid('60000000-0000-0000-0001-', dayOffset * 2 + 2);
  const eOpenAt = `${dayStr} 15:30:00`;
  const isLastDay = dayOffset === 30;
  const eCloseAt = isLastDay ? null : `${dayStr} 22:30:00`;

  db_billing.CashShift.push({
    CashShiftId: eShiftId,
    CashRegisterId: CASH_REG_ID,
    CashierUserRef: U_C2,
    OpeningCash: 1500000,
    OpenedAt: eOpenAt,
    ClosedAt: eCloseAt,
    Status: isLastDay ? 'Open' : 'Closed',
    RestaurantId: R1_ID
  });

  db_billing.CashMovement.push({
    CashMovementId: uuid('60000000-0000-0000-0002-', dayOffset * 4 + 2),
    CashShiftId: eShiftId,
    PaymentId: null,
    Type: 'OpeningFloat',
    SignedAmount: 1500000,
    Reason: 'Tiền mặt đầu ca tối',
    PostedAt: eOpenAt,
    IdempotencyKey: `OPEN_${dayStr}_EVE`,
    RestaurantId: R1_ID
  });

  // -------------------------
  // 4.2. Daily TableSessions, Orders & Bills
  // -------------------------
  // Generate 2 orders per day (1 in morning, 1 in evening)
  const dayOrders = [
    { time: `${dayStr} 11:45:00`, closeTime: `${dayStr} 12:45:00`, shiftId: mShiftId, cashier: U_C1, table: TABLES[dayOffset % 6], channel: 'DineIn' },
    { time: `${dayStr} 18:30:00`, closeTime: `${dayStr} 20:00:00`, shiftId: eShiftId, cashier: U_C2, table: TABLES[(dayOffset + 1) % 10], channel: dayOffset % 5 === 0 ? 'Takeaway' : 'DineIn' }
  ];

  let morningCashTotal = 1000000;
  let eveningCashTotal = 1500000;

  dayOrders.forEach((oInfo, orderIdxInDay) => {
    const sId = uuid('70000000-0000-0000-0001-', sessionGlobalIndex);
    const ordId = uuid('70000000-0000-0000-0002-', orderGlobalIndex);
    const billId = uuid('70000000-0000-0000-0003-', billGlobalIndex);
    const cust = CUSTOMERS[(sessionGlobalIndex - 1) % CUSTOMERS.length];

    // Reservation for some sessions
    let resId = null;
    if (dayOffset % 3 === 0 && orderIdxInDay === 1) {
      resId = uuid('70000000-0000-0000-0004-', sessionGlobalIndex);
      db_restaurant.Reservation.push({
        ReservationId: resId,
        RestaurantId: R1_ID,
        RequestedAreaId: oInfo.table.area,
        ContactName: cust.name,
        Phone: cust.phone,
        PartySize: oInfo.table.cap,
        StartAt: `${dayStr} 18:00:00`,
        EndAt: `${dayStr} 20:30:00`,
        HoldUntil: `${dayStr} 18:30:00`,
        Status: 'Completed',
        ApprovedByRef: U_MGR,
        ConfirmedAt: `${dayStr} 10:00:00`,
        CancelledAt: null,
        CancelledByRef: null
      });

      db_restaurant.ReservationTable.push({
        ReservationId: resId,
        TableId: oInfo.table.id
      });
    }

    // TableSession
    db_restaurant.TableSession.push({
      TableSessionId: sId,
      ReservationId: resId,
      PartySize: oInfo.table.cap,
      Status: 'Closed',
      OpenedAt: oInfo.time,
      ClosedAt: oInfo.closeTime,
      OpenedByRef: U_W1,
      JoinCodeHash: crypto.createHash('sha256').update(`SESSION_${sId}`).digest('hex'),
      JoinCodeSetAt: oInfo.time,
      LoyaltyCustomerRef: cust.id,
      PrebillPrintedAt: oInfo.closeTime
    });

    db_restaurant.SessionTable.push({
      SessionTableId: uuid('70000000-0000-0000-0005-', sessionGlobalIndex),
      TableSessionId: sId,
      TableId: oInfo.table.id,
      AssignedAt: oInfo.time,
      ReleasedAt: oInfo.closeTime,
      ChangedByRef: null,
      MoveReason: null
    });

    // GuestAccess
    db_restaurant.GuestAccess.push({
      GuestAccessId: uuid('70000000-0000-0000-0006-', sessionGlobalIndex),
      TableSessionId: sId,
      TokenHash: crypto.createHash('sha256').update(`GUEST_${sId}`).digest('hex'),
      CreatedAt: oInfo.time,
      ExpiredAt: oInfo.closeTime,
      RevokedAt: null
    });

    // ServiceRequest
    db_restaurant.ServiceRequest.push({
      ServiceRequestId: uuid('70000000-0000-0000-0007-', sessionGlobalIndex),
      TableSessionId: sId,
      Type: 'WaterIce',
      Status: 'Completed',
      RequestedAt: oInfo.time,
      AcknowledgedAt: oInfo.time,
      CompletedAt: oInfo.time,
      HandledByRef: U_W1
    });

    // Cart sample
    const cartId = uuid('70000000-0000-0000-0008-', sessionGlobalIndex);
    db_ordering.Cart.push({
      CartId: cartId,
      TableSessionRef: sId,
      GuestAccessRef: db_restaurant.GuestAccess[db_restaurant.GuestAccess.length - 1].GuestAccessId,
      ExpiredAt: oInfo.closeTime,
      CustomerUserRef: cust.userRef,
      RestaurantRef: R1_ID
    });

    // Pick 2-3 items for this order
    const item1 = ITEMS[(orderGlobalIndex * 3) % ITEMS.length];
    const item2 = ITEMS[(orderGlobalIndex * 3 + 1) % ITEMS.length];
    const item3 = ITEMS[(orderGlobalIndex * 3 + 2) % ITEMS.length];

    const selItems = [
      { it: item1, qty: 2, opt: (item1.cat === CAT_MAIN ? OPTIONS[0] : null) },
      { it: item2, qty: 1, opt: (item2.cat === CAT_DRINK ? OPTIONS[8] : null) },
      { it: item3, qty: 2, opt: null }
    ];

    let subTotal = 0;
    selItems.forEach(si => {
      subTotal += si.it.price * si.qty;
      if (si.opt) subTotal += si.opt.price * si.qty;
    });

    // Discounts
    let discountAmount = 0;
    let promoRef = null;
    let voucherRef = null;

    if (orderGlobalIndex % 5 === 0) {
      promoRef = PROMO_1_ID;
      discountAmount = Math.round(subTotal * 0.1);
    } else if (orderGlobalIndex % 7 === 0) {
      voucherRef = VOUCHER_1_ID;
      discountAmount = 50000;
    }

    const vatAmount = Math.round((subTotal - discountAmount) * 0.08);
    const totalAmount = subTotal - discountAmount + vatAmount;

    const isPayOS = (orderGlobalIndex % 2 === 0);
    const payMethod = isPayOS ? 'PayOS' : 'Cash';

    // Ordering: Order
    db_ordering.Order.push({
      OrderId: ordId,
      OrderNumber: `ORD-${dayStr.replace(/-/g, '')}-${pad(orderGlobalIndex)}`,
      TableSessionRef: sId,
      CustomerRef: cust.id,
      Channel: oInfo.channel,
      CreatedByRef: U_W1,
      IdempotencyKey: `ORD_IDEM_${ordId}`,
      RequestHash: crypto.createHash('sha256').update(ordId).digest('hex'),
      CreatedAt: oInfo.time,
      Status: 'Completed',
      ConfirmedAt: oInfo.time,
      ConfirmedByRef: U_W1,
      RestaurantRef: R1_ID,
      CustomerUserRef: cust.userRef,
      PickupName: oInfo.channel === 'Takeaway' ? cust.name : null,
      PickupPhone: oInfo.channel === 'Takeaway' ? cust.phone : null,
      PickupAt: oInfo.channel === 'Takeaway' ? oInfo.closeTime : null,
      PickupCode: oInfo.channel === 'Takeaway' ? `PK-${pad(orderGlobalIndex)}` : null,
      PaymentStatus: 'PAID',
      CheckoutUrl: isPayOS ? 'https://pay.payos.vn/web/demo' : null,
      CancellationRequested: false,
      RejectionRequested: false,
      CancellationReason: null,
      ReadyForPickupAt: oInfo.closeTime,
      CompletedAt: oInfo.closeTime,
      RefundedAt: null,
      RefundBankName: null,
      RefundAccountNumber: null,
      RefundAccountHolder: null,
      RefundReference: null
    });

    // CheckoutLock
    db_ordering.CheckoutLock.push({
      CheckoutId: uuid('70000000-0000-0000-0009-', orderGlobalIndex),
      BillIdRef: billId,
      TableSessionRef: sId,
      SourceHash: crypto.createHash('sha256').update(`LOCK_${sId}`).digest('hex'),
      Status: 'Released',
      CreatedAt: oInfo.time,
      ReleasedAt: oInfo.closeTime,
      RestaurantId: R1_ID
    });

    // OrderItems & BillLines
    selItems.forEach((si, siIdx) => {
      const orderItemId = uuid('70000000-0000-0000-000A-', orderGlobalIndex * 10 + siIdx);
      const billLineId = uuid('70000000-0000-0000-000B-', orderGlobalIndex * 10 + siIdx);

      // CartItem
      const cartItemId = uuid('70000000-0000-0000-000C-', orderGlobalIndex * 10 + siIdx);
      db_ordering.CartItem.push({
        CartItemId: cartItemId,
        CartId: cartId,
        ItemRef: si.it.id,
        Quantity: si.qty,
        Note: 'Phục vụ nóng hổi',
        RestaurantId: R1_ID
      });

      if (si.opt) {
        db_ordering.CartItemOption.push({
          CartItemOptionId: uuid('70000000-0000-0000-001C-', orderGlobalIndex * 10 + siIdx),
          CartItemId: cartItemId,
          OptionRef: si.opt.id,
          Quantity: 1,
          RestaurantId: R1_ID
        });
      }

      db_ordering.OrderItem.push({
        OrderItemId: orderItemId,
        OrderId: ordId,
        ItemRef: si.it.id,
        RecipeVersionRef: uuid('20000000-0000-0000-0004-', ITEMS.indexOf(si.it) + 1),
        OriginalTableRef: oInfo.table.id,
        ComboGroupId: null,
        NameSnapshot: si.it.name,
        UnitPriceSnapshot: si.it.price,
        Quantity: si.qty,
        CancelledQuantity: 0,
        Note: 'Không cay',
        Status: 'Served',
        RestaurantId: R1_ID
      });

      if (si.opt) {
        db_ordering.OrderItemOption.push({
          Id: uuid('70000000-0000-0000-000D-', orderGlobalIndex * 10 + siIdx),
          OrderItemId: orderItemId,
          OptionRef: si.opt.id,
          NameSnapshot: si.opt.name,
          ExtraPriceSnapshot: si.opt.price,
          Quantity: 1,
          RestaurantId: R1_ID
        });
      }

      // KitchenTask
      const taskId = uuid('70000000-0000-0000-000E-', taskGlobalIndex++);
      db_ordering.KitchenTask.push({
        KitchenTaskId: taskId,
        KitchenStationId: si.it.ks,
        OrderItemId: orderItemId,
        BatchNumber: 1,
        Quantity: si.qty,
        Status: 'Completed',
        Priority: 1,
        EstimatedMinutes: si.it.prep,
        QueuedAt: oInfo.time,
        CookingAt: oInfo.time,
        ReadyAt: oInfo.closeTime,
        RestaurantId: R1_ID
      });

      db_ordering.Event.push(
        { EventId: uuid('70000000-0000-0000-000F-', taskGlobalIndex * 2 + 1), KitchenTaskId: taskId, FromStatus: 'Queued', ToStatus: 'Cooking', ActorUserRef: U_K1, OccurredAt: oInfo.time, RestaurantId: R1_ID },
        { EventId: uuid('70000000-0000-0000-000F-', taskGlobalIndex * 2 + 2), KitchenTaskId: taskId, FromStatus: 'Cooking', ToStatus: 'Completed', ActorUserRef: U_K1, OccurredAt: oInfo.closeTime, RestaurantId: R1_ID }
      );

      // ServiceRecord
      db_ordering.ServiceRecord.push({
        ServiceRecordId: uuid('70000000-0000-0000-0010-', orderGlobalIndex * 10 + siIdx),
        OrderItemId: orderItemId,
        QuantityServed: si.qty,
        WaiterUserRef: U_W1,
        ServedAt: oInfo.closeTime,
        IdempotencyKey: `SRV_${orderItemId}`,
        RestaurantId: R1_ID
      });

      // StockMovement for consumed ingredient
      si.it.ings.forEach((ing, ingIdx) => {
        db_inventory.StockMovement.push({
          StockMovementId: uuid('70000000-0000-0000-0011-', (orderGlobalIndex * 10 + siIdx) * 10 + ingIdx),
          IngredientId: ing.ing,
          GoodsReceiptItemId: null,
          StocktakeItemId: null,
          MovementType: 'Cooking',
          QuantityChange: - (ing.qty * si.qty),
          CookingEventRef: null,
          OrderItemRef: orderItemId,
          PerformedByRef: U_K1,
          Reason: `Chế biến ${si.it.name}`,
          CreatedAt: oInfo.time,
          StockIssueId: null,
          RestaurantId: R1_ID
        });
      });

      // BillLine
      const lineTotal = si.it.price * si.qty;
      const lineDisc = subTotal > 0 ? Math.round((lineTotal / subTotal) * discountAmount) : 0;
      const lineVat = Math.round((lineTotal - lineDisc) * 0.08);

      db_billing.BillLine.push({
        BillLineId: billLineId,
        BillId: billId,
        OrderItemRef: orderItemId,
        TableSessionRef: sId,
        OriginalTableRef: oInfo.table.id,
        ComboGroupRef: null,
        ItemSnapshot: JSON.stringify({ name: si.it.name, code: si.it.code, option: si.opt ? si.opt.name : null }),
        Quantity: si.qty,
        UnitPriceSnapshot: si.it.price,
        DiscountAllocated: lineDisc,
        VatAllocated: lineVat,
        ServiceAllocated: 0,
        CogsSnapshot: Math.round(si.it.price * 0.4),
        RestaurantId: R1_ID
      });
    });

    // Bill
    db_billing.Bill.push({
      BillId: billId,
      RestaurantId: R1_ID,
      BillNumber: `HD-${dayStr.replace(/-/g, '')}-${pad(billGlobalIndex)}`,
      CustomerRef: cust.id,
      Status: 'Paid',
      SubTotal: subTotal,
      DiscountAmount: discountAmount,
      VatRateSnapshot: 0.08,
      ServiceRateSnapshot: 0.0,
      VatAmount: vatAmount,
      ServiceAmount: 0.0,
      TotalAmount: totalAmount,
      Currency: 'VND',
      PrebillPrintedAt: oInfo.closeTime,
      PaidAt: oInfo.closeTime
    });

    // BillCheckout
    db_billing.BillCheckout.push({
      CheckoutId: uuid('70000000-0000-0000-0012-', billGlobalIndex),
      BillId: billId,
      TableSessionRef: sId,
      SourceHash: crypto.createHash('sha256').update(`CHECKOUT_${billId}`).digest('hex'),
      Status: 'Completed',
      CreatedAt: oInfo.time,
      ReleasedAt: oInfo.closeTime,
      RestaurantId: R1_ID,
      CashRegisterId: CASH_REG_ID,
      SubmittedAt: oInfo.closeTime,
      PreparedByRef: oInfo.cashier,
      PreparedAt: oInfo.closeTime,
      PresentedAt: oInfo.closeTime
    });

    // Adjustments
    if (promoRef) {
      db_billing.BillAdjustment.push({
        BillAdjustmentId: uuid('70000000-0000-0000-0013-', billGlobalIndex),
        BillId: billId,
        VoucherId: null,
        PromotionId: promoRef,
        Type: 'Promotion',
        Amount: discountAmount,
        RuleSnapshot: 'Giảm giá 10% mùa thu',
        ActorUserRef: oInfo.cashier,
        Reason: 'Khuyến mãi mùa thu áp dụng tự động',
        RestaurantId: R1_ID
      });
    } else if (voucherRef) {
      db_billing.BillAdjustment.push({
        BillAdjustmentId: uuid('70000000-0000-0000-0013-', billGlobalIndex),
        BillId: billId,
        VoucherId: voucherRef,
        PromotionId: null,
        Type: 'Voucher',
        Amount: discountAmount,
        RuleSnapshot: 'Voucher 50.000đ',
        ActorUserRef: oInfo.cashier,
        Reason: 'Khách hàng áp dụng mã voucher SENVIET2026',
        RestaurantId: R1_ID
      });

      db_billing.VoucherRedemption.push({
        VoucherRedemptionId: uuid('70000000-0000-0000-0014-', billGlobalIndex),
        VoucherId: voucherRef,
        BillId: billId,
        Status: 'Committed',
        ReservedUntil: oInfo.closeTime,
        CommittedAt: oInfo.closeTime,
        RestaurantId: R1_ID
      });
    }

    // Payment
    const paymentId = uuid('70000000-0000-0000-0015-', billGlobalIndex);
    const providerOrderCode = 26100000000 + billGlobalIndex;
    const transRef = `TX202611${pad(billGlobalIndex)}`;

    db_billing.Payment.push({
      PaymentId: paymentId,
      BillId: billId,
      CashShiftId: oInfo.shiftId,
      MethodCode: payMethod,
      Status: 'Paid',
      Amount: totalAmount,
      CashReceived: payMethod === 'Cash' ? Math.ceil(totalAmount / 10000) * 10000 : null,
      ChangeAmount: payMethod === 'Cash' ? (Math.ceil(totalAmount / 10000) * 10000) - totalAmount : null,
      ProviderPaymentLinkId: isPayOS ? `LINK_${billGlobalIndex}` : null,
      ProviderCheckoutUrl: isPayOS ? 'https://pay.payos.vn/web/demo' : null,
      ProviderQrCode: isPayOS ? `00020101021238540010A0000007270124${billGlobalIndex}` : null,
      ProviderOrderCode: isPayOS ? providerOrderCode.toString() : null,
      ProviderTransactionId: isPayOS ? transRef : null,
      IdempotencyKey: `PAY_IDEM_${paymentId}`,
      ExpiredAt: isPayOS ? oInfo.closeTime : null,
      PaidAt: oInfo.closeTime,
      VerificationSource: isPayOS ? 'Webhook' : 'CashRegister',
      ConfirmedByRef: oInfo.cashier,
      EvidenceUrl: null,
      RestaurantId: R1_ID
    });

    if (payMethod === 'Cash') {
      if (orderIdxInDay === 0) morningCashTotal += totalAmount;
      else eveningCashTotal += totalAmount;

      // CashMovement for bill payment
      db_billing.CashMovement.push({
        CashMovementId: uuid('70000000-0000-0000-0016-', billGlobalIndex),
        CashShiftId: oInfo.shiftId,
        PaymentId: paymentId,
        Type: 'PaymentCash',
        SignedAmount: totalAmount,
        Reason: `Thu tiền mặt hóa đơn ${db_billing.Bill[billGlobalIndex - 1].BillNumber}`,
        PostedAt: oInfo.closeTime,
        IdempotencyKey: `CASH_MOV_${paymentId}`,
        RestaurantId: R1_ID
      });

      // CashHandover
      db_billing.CashHandover.push({
        PaymentId: paymentId,
        WaiterUserRef: U_W1,
        CashierUserRef: oInfo.cashier,
        CollectedAt: oInfo.closeTime,
        ConfirmedAt: oInfo.closeTime,
        ConfirmedAmount: totalAmount,
        Status: 'Confirmed',
        RestaurantId: R1_ID
      });
    } else {
      // OnlinePayments & PaymentWebhook
      db_billing.OnlinePayments.push({
        OrderId: ordId,
        RestaurantId: R1_ID,
        CustomerUserId: cust.userRef || U_CUST[0].id,
        ProviderOrderCode: providerOrderCode,
        Amount: totalAmount,
        Status: 'PAID',
        LinkId: `LINK_${billGlobalIndex}`,
        CheckoutUrl: 'https://pay.payos.vn/web/demo',
        ExpiresAt: oInfo.closeTime,
        PaidAt: oInfo.closeTime,
        TransactionReference: transRef,
        RefundReason: null,
        RefundReference: null,
        RefundEvidence: null,
        RefundVerifiedBy: null,
        RefundedAt: null
      });

      db_billing.PaymentWebhook.push({
        PaymentWebhookId: uuid('70000000-0000-0000-0017-', billGlobalIndex),
        PaymentId: paymentId,
        Provider: 'PayOS',
        DedupeKey: `WH_DEDUPE_${paymentId}`,
        PayloadHash: crypto.createHash('sha256').update(`WEBHOOK_${paymentId}`).digest('hex'),
        SignatureValid: true,
        ReceivedAt: oInfo.closeTime,
        ProcessedAt: oInfo.closeTime,
        ProcessingStatus: 'Processed',
        RestaurantId: R1_ID
      });
    }

    // ReceiptPrint
    db_billing.ReceiptPrint.push({
      ReceiptPrintId: uuid('70000000-0000-0000-0018-', billGlobalIndex),
      BillId: billId,
      Type: 'Receipt',
      CopyNumber: 1,
      PrintedByRef: oInfo.cashier,
      PrintedAt: oInfo.closeTime,
      RestaurantId: R1_ID
    });

    // 4 Electronic Invoices across the month
    if (billGlobalIndex % 15 === 3) {
      db_billing.ElectronicInvoice.push({
        ElectronicInvoiceId: uuid('70000000-0000-0000-0019-', billGlobalIndex),
        BillId: billId,
        CompanyName: 'CÔNG TY CỔ PHẦN CÔNG NGHỆ VÀ TRUYỀN THÔNG SEN VÀNG',
        TaxCode: '0108998877',
        Address: 'Tầng 12, Tòa nhà Landmark 72, Nam Từ Liêm, Hà Nội',
        Email: 'ketoan@senvangtech.vn',
        ProviderReference: `HDDT-202611-${pad(billGlobalIndex)}`,
        Status: 'Issued',
        IssuedAt: oInfo.closeTime,
        RestaurantId: R1_ID
      });
    }

    // Customer Loyalty Points: 1000đ = 1 point
    const earnedPoints = Math.floor(totalAmount / 1000);
    db_customer.LoyaltyEntry.push({
      LoyaltyEntryId: uuid('80000000-0000-0000-0001-', billGlobalIndex),
      CustomerId: cust.id,
      BillRef: billId,
      RefundRef: null,
      PolicyId: LOYALTY_POLICY_ID,
      Type: 'Earn',
      SignedPoint: earnedPoints,
      EligibleSpend: totalAmount,
      SourceEventId: `BILL_PAID_${billId}`,
      PostedAt: oInfo.closeTime,
      RestaurantId: R1_ID
    });

    db_customer.PointReservation.push({
      PointReservationId: uuid('80000000-0000-0000-0002-', billGlobalIndex),
      CustomerId: cust.id,
      BillRef: billId,
      Point: earnedPoints,
      Status: 'Committed',
      ExpiredAt: oInfo.closeTime,
      RestaurantId: R1_ID
    });

    // Customer Feedback
    const fbAccessId = uuid('80000000-0000-0000-0003-', feedbackGlobalIndex);
    const fbId = uuid('80000000-0000-0000-0004-', feedbackGlobalIndex);

    db_customer.FeedbackAccess.push({
      FeedbackAccessId: fbAccessId,
      OrderRef: ordId,
      TokenHash: crypto.createHash('sha256').update(`FB_TOKEN_${ordId}`).digest('hex'),
      ExpiredAt: `${dayStr} 23:59:59`,
      UsedAt: oInfo.closeTime,
      RestaurantId: R1_ID
    });

    db_customer.Feedback.push({
      FeedbackId: fbId,
      AccessId: fbAccessId,
      OrderRef: ordId,
      CustomerId: cust.id,
      Star: 5,
      Comment: 'Món ăn rất tươi ngon đậm vị, không gian sang trọng và phục vụ chu đáo!',
      SubmittedAt: oInfo.closeTime,
      RestaurantId: R1_ID
    });

    db_customer.FeedbackItem.push({
      FeedbackItemId: uuid('80000000-0000-0000-0005-', feedbackGlobalIndex),
      FeedbackId: fbId,
      OrderItemRef: db_ordering.OrderItem[db_ordering.OrderItem.length - 1].OrderItemId,
      Star: 5,
      Comment: 'Món làm rất vừa miệng, trình bày bắt mắt.',
      RestaurantId: R1_ID
    });

    feedbackGlobalIndex++;
    orderGlobalIndex++;
    billGlobalIndex++;
    sessionGlobalIndex++;
  });

  // End of ShiftReports for the day
  db_billing.ShiftReport.push({
    ShiftReportId: uuid('60000000-0000-0000-0003-', dayOffset * 2 + 1),
    CashShiftId: mShiftId,
    ExpectedCash: morningCashTotal,
    ActualCash: morningCashTotal,
    CashHandleOver: morningCashTotal,
    DifferenceReason: 'Khớp tiền 100% không chênh lệch',
    ClosedAt: mCloseAt,
    RestaurantId: R1_ID
  });

  if (!isLastDay) {
    db_billing.ShiftReport.push({
      ShiftReportId: uuid('60000000-0000-0000-0003-', dayOffset * 2 + 2),
      CashShiftId: eShiftId,
      ExpectedCash: eveningCashTotal,
      ActualCash: eveningCashTotal,
      CashHandleOver: eveningCashTotal,
      DifferenceReason: 'Khớp tiền 100% không chênh lệch',
      ClosedAt: eCloseAt,
      RestaurantId: R1_ID
    });
  }

  // -------------------------
  // 4.3. AuditLogs, UserSessions, Notifications
  // -------------------------
  db_identity.UserSession.push(
    { UserSessionId: uuid('10000000-0000-0000-000B-', dayOffset * 2 + 1), UserId: U_C1, IpAddress: '192.168.1.101', UserAgent: 'SenViet POS Windows App v2.4', DeviceType: 'POS Terminal', RefreshTokenHash: crypto.createHash('sha256').update(`TOK_C1_${dayOffset}`).digest('hex'), LoggedInAt: mOpenAt, ExpiresAt: `${dayStr} 23:59:59`, RevokedAt: null },
    { UserSessionId: uuid('10000000-0000-0000-000B-', dayOffset * 2 + 2), UserId: U_C2, IpAddress: '192.168.1.102', UserAgent: 'SenViet POS Windows App v2.4', DeviceType: 'POS Terminal', RefreshTokenHash: crypto.createHash('sha256').update(`TOK_C2_${dayOffset}`).digest('hex'), LoggedInAt: eOpenAt, ExpiresAt: `${dayStr} 23:59:59`, RevokedAt: null }
  );

  db_identity.AuditLog.push(
    { AuditLogId: uuid('10000000-0000-0000-000C-', dayOffset * 2 + 1), UserId: U_C1, Action: 'OPEN_SHIFT', EntityName: 'CashShift', EntityId: mShiftId, OldValue: null, NewValue: JSON.stringify({ openingCash: 1000000 }), IpAddress: '192.168.1.101', CreatedAt: mOpenAt },
    { AuditLogId: uuid('10000000-0000-0000-000C-', dayOffset * 2 + 2), UserId: U_C1, Action: 'CLOSE_SHIFT', EntityName: 'CashShift', EntityId: mShiftId, OldValue: JSON.stringify({ status: 'Open' }), NewValue: JSON.stringify({ status: 'Closed', totalCash: morningCashTotal }), IpAddress: '192.168.1.101', CreatedAt: mCloseAt }
  );

  const notifId = uuid('10000000-0000-0000-000D-', dayOffset + 1);
  db_identity.Notification.push({
    NotificationId: notifId,
    RestaurantId: R1_ID,
    EventKey: `EVT_DAILY_SUMMARY_${dayStr}`,
    Type: 'DailySummary',
    Title: `Báo cáo ca ngày ${dayStr}`,
    Message: `Đã hoàn thành các ca vận hành và chốt sổ quỹ ngày ${dayStr}.`,
    EntityType: 'CashShift',
    EntityId: mShiftId,
    CreatedAt: mCloseAt,
    Suppressed: false
  });

  db_identity.NotificationRecipient.push({
    NotificationId: notifId,
    UserId: U_MGR,
    IsCustomer: false,
    ReadAt: mCloseAt
  });
}

// ----------------------------------------------------
// 5. SPECIAL FLOWS: CANCELLATION & REFUND, TABLE TRANSFERS, OUTBOXES
// ----------------------------------------------------
// Sample CancellationRequest & Refund
const cancelOrderItem = db_ordering.OrderItem[5];
const cancelBillLine = db_billing.BillLine[5];
const cancelPayment = db_billing.Payment.find(p => p.BillId === cancelBillLine.BillId);

const cancelReqId = uuid('70000000-0000-0000-001A-', 1);
db_ordering.CancellationRequest.push({
  CancellationRequestId: cancelReqId,
  OrderItemId: cancelOrderItem.OrderItemId,
  Quantity: 1,
  Reason: 'Khách đổi món sang phở bò tái',
  Source: 'Customer',
  RequestedByRef: U_W1,
  DecidedByRef: U_MGR,
  DecidedAt: '2026-10-27 12:30:00',
  DecisionReason: 'Đồng ý cho khách đổi món do bếp chưa chế biến xong',
  Status: 'Approved',
  RequestedAt: '2026-10-27 12:25:00',
  RestaurantId: R1_ID
});

const refundId = uuid('40000000-0000-0000-0020-', 1);
db_billing.Refund.push({
  RefundId: refundId,
  PaymentId: cancelPayment.PaymentId,
  Amount: cancelBillLine.UnitPriceSnapshot,
  Status: 'Completed',
  Reason: 'Hoàn tiền do khách hủy món theo yêu cầu',
  IdempotencyKey: `REFUND_${refundId}`,
  CompletedAt: '2026-10-27 12:35:00',
  RestaurantId: R1_ID
});

db_billing.RefundLine.push({
  RefundLineId: uuid('40000000-0000-0000-0021-', 1),
  BillLineId: cancelBillLine.BillLineId,
  RefundId: refundId,
  Amount: cancelBillLine.UnitPriceSnapshot,
  Quantity: 1,
  RestaurantId: R1_ID
});

// Table Transfers (Chuyển bàn)
const transferOpId = uuid('70000000-0000-0000-0030-', 1);
const sourceSess = db_restaurant.TableSession[0].TableSessionId;
const targetSess = db_restaurant.TableSession[1].TableSessionId;
const targetTable = TABLES[6].id; // VIP01

db_restaurant.TableTransferSession.push({
  OperationId: transferOpId,
  SourceSessionId: sourceSess,
  TargetSessionId: targetSess,
  TargetTableId: targetTable,
  ActorId: U_W1,
  RestaurantId: R1_ID,
  RequestHash: crypto.createHash('sha256').update('TRANSFER_1').digest('hex'),
  Mode: 'Transfer',
  Reason: 'Khách muốn chuyển sang phòng VIP để tiếp khách đối tác',
  Status: 'Completed',
  CreatedAt: '2026-10-25 12:00:00',
  TargetCreated: true
});

db_ordering.TableTransfer.push({
  OperationId: transferOpId,
  SourceSessionId: sourceSess,
  TargetSessionId: targetSess,
  TargetTableId: targetTable,
  ActorId: U_W1,
  RestaurantId: R1_ID,
  RequestHash: crypto.createHash('sha256').update('TRANSFER_1').digest('hex'),
  Mode: 'Transfer',
  Reason: 'Khách muốn chuyển sang phòng VIP để tiếp khách đối tác',
  Status: 'Completed',
  CreatedAt: '2026-10-25 12:00:00'
});

// Notification Outbox for RestaurantDb, OrderingDb, BillingDb
[
  { db: db_restaurant, key: 'EVT_TABLE_SESSION_OPENED', payload: JSON.stringify({ event: 'SessionOpened', table: 'T01' }) },
  { db: db_ordering, key: 'EVT_ORDER_CREATED_ORD001', payload: JSON.stringify({ event: 'OrderCreated', orderNumber: 'ORD-20261025-01' }) },
  { db: db_billing, key: 'EVT_BILL_PAID_HD001', payload: JSON.stringify({ event: 'BillPaid', billNumber: 'HD-20261025-01' }) }
].forEach((item, idx) => {
  item.db.NotificationOutbox.push({
    NotificationOutboxId: uuid('90000000-0000-0000-0001-', idx + 1),
    RestaurantId: R1_ID,
    EventKey: item.key,
    Payload: item.payload,
    CreatedAt: '2026-10-25 12:00:00',
    NextAttemptAt: '2026-10-25 12:05:00',
    ProcessedAt: '2026-10-25 12:01:00',
    Attempts: 1,
    LastError: null
  });
});

console.log('Writing seed files...');

// ==========================================
// 5. WRITE SEED SQL FILES
// ==========================================

function writeSeedFile(fileName, dbData) {
  const filePath = path.join(SEED_DIR, fileName);
  let sql = `-- Seed data for ${fileName.replace('.sql', '')}\n`;
  sql += `-- Auto-generated 1-month comprehensive operational dataset\n\n`;

  for (const [table, rows] of Object.entries(dbData)) {
    if (rows && rows.length > 0) {
      const cols = Object.keys(rows[0]);
      sql += `-- Table: dbo.[${table}] (${rows.length} rows)\n`;
      sql += buildInserts(table, cols, rows);
    }
  }

  fs.writeFileSync(filePath, sql, 'utf-8');
  console.log(`Generated: ${fileName} (${fs.statSync(filePath).size} bytes)`);
}

writeSeedFile('IdentityDb.sql', db_identity);
writeSeedFile('RestaurantDb.sql', db_restaurant);
writeSeedFile('CatalogDb.sql', db_catalog);
writeSeedFile('InventoryDb.sql', db_inventory);
writeSeedFile('OrderingDb.sql', db_ordering);
writeSeedFile('CustomerDb.sql', db_customer);
writeSeedFile('BillingDb.sql', db_billing);

console.log('ALL 7 DATABASE SEED FILES GENERATED SUCCESSFULLY!');
