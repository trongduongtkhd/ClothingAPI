# Clothing Store — Website bán quần áo

Đồ án cơ sở: website bán quần áo gồm trang khách hàng và trang quản trị (sản phẩm, đơn hàng, mã giảm giá, nhập kho, thống kê).

| Phần | Công nghệ | Repo |
|---|---|---|
| Backend (repo này) | ASP.NET Core (.NET 10), Entity Framework Core, SQL Server | https://github.com/trongduongtkhd/ClothingAPI |
| Frontend | Angular 12, Bootstrap 5, Nginx | https://github.com/trongduongtkhd/WebBanQuanAo |

Toàn bộ hệ thống (database, API, frontend) chạy bằng **Docker Compose** — máy chạy chỉ cần cài Docker Desktop, không cần cài .NET, Node.js hay SQL Server.

## Chạy dự án bằng Docker

### 1. Yêu cầu

- [Docker Desktop](https://www.docker.com/products/docker-desktop/) (đã bật và đang chạy)
- Git
- Các cổng còn trống: `4200` (web), `8080` (API), `14333` (SQL Server)

### 2. Tải mã nguồn

Clone **cả 2 repo vào cùng một thư mục cha**, repo frontend đặt tên là `shop-clothing-ui`:

```bash
mkdir ClothingStore
cd ClothingStore
git clone https://github.com/trongduongtkhd/ClothingAPI.git
git clone https://github.com/trongduongtkhd/WebBanQuanAo.git shop-clothing-ui
```

Cấu trúc thư mục sau khi clone:

```
ClothingStore/
├── ClothingAPI/        ← backend + docker-compose.yml
└── shop-clothing-ui/   ← frontend
```

### 3. Tạo file cấu hình `.env`

```bash
cd ClothingAPI
cp .env.example .env              # macOS / Linux / Git Bash
# Copy-Item .env.example .env     # Windows PowerShell
```

Có thể giữ nguyên giá trị mẫu để chạy thử. Mở `.env` để đổi mật khẩu nếu cần — ý nghĩa từng biến được ghi chú ngay trong file.

### 4. Build và chạy

```bash
docker compose up -d --build
```

Lần đầu mất vài phút (tải image SQL Server, build API và frontend). API chỉ khởi động sau khi SQL Server sẵn sàng, database và các bảng được **tạo tự động**.

Kiểm tra trạng thái — cả 3 container phải là `running` (db là `healthy`):

```bash
docker compose ps
```

### 5. Truy cập

| Địa chỉ | Nội dung |
|---|---|
| http://localhost:4200 | Website (trang khách hàng) |
| http://localhost:4200/admin | Trang quản trị |
| http://localhost:8080/swagger | Tài liệu API (Swagger) |
| `localhost,14333` (SSMS / Azure Data Studio) | SQL Server — user `sa`, mật khẩu là `MSSQL_SA_PASSWORD` trong `.env` |

**Tài khoản admin**: email và mật khẩu là `ADMIN_EMAIL` / `ADMIN_PASSWORD` trong `.env`
(mặc định `admin@clothingstore.local` / `Admin@123`).

> Database ban đầu **trống** (chưa có sản phẩm). Đăng nhập admin rồi tạo lần lượt: danh mục, thương hiệu, màu, size, nhà cung ứng → sản phẩm và biến thể → phiếu nhập kho để có hàng bán.

## Các lệnh thường dùng

```bash
docker compose ps                 # xem trạng thái container
docker compose logs -f api        # xem log API (thay bằng db / frontend)
docker compose up -d --build      # build lại sau khi sửa code
docker compose down               # dừng hệ thống (dữ liệu vẫn được giữ)
docker compose down -v            # dừng và XÓA toàn bộ dữ liệu (DB, ảnh upload)
```

## Xử lý sự cố

| Hiện tượng | Cách xử lý |
|---|---|
| `clothing-db` không lên `healthy` | `MSSQL_SA_PASSWORD` chưa đủ mạnh (cần >= 8 ký tự, có chữ hoa, chữ thường, số, ký tự đặc biệt). Sửa `.env` rồi chạy `docker compose down -v` và `docker compose up -d --build` |
| `clothing-api` dừng ngay sau khi chạy | Xem `docker compose logs api`. Thường do `JWT_SECRET_KEY` ngắn hơn 32 ký tự |
| Lỗi `unable to prepare context ... shop-clothing-ui` | Repo frontend chưa nằm cạnh `ClothingAPI` hoặc sai tên thư mục — xem bước 2, hoặc đặt `FRONTEND_PATH` trong `.env` |
| Lỗi `port is already allocated` | Cổng 4200 / 8080 / 14333 đang bị chương trình khác dùng — tắt chương trình đó |
| Không đăng nhập được admin | `ADMIN_EMAIL` / `ADMIN_PASSWORD` chỉ được dùng ở **lần chạy đầu** khi DB chưa có admin. Nếu đã chạy trước khi điền, dùng `docker compose down -v` để tạo lại DB |
| Giao diện không cập nhật sau khi build lại | Nhấn `Ctrl + F5` trên trình duyệt |
