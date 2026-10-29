# Tối ưu tải trang và ảnh

## Phạm vi

- Nginx nén JS/CSS/JSON/SVG, cache lâu cho bundle có content hash. index.html vẫn không cache để nhận bản phát hành mới.
- Ảnh sản phẩm/danh mục/logo dùng đúng độ rộng 160, 320, 640, 960, 1280 hoặc 1920 pixel. Giữ ảnh gốc; bản WebP được tạo và cache riêng.
- Ảnh tĩnh có tên theo hash nội dung, được cache một năm. Không ghi đè ảnh gốc.
- Ảnh upload dùng `?w=640` (hoặc kích thước cho phép). Backend xử lý trước StaticFiles, giữ tỉ lệ, chiều xoay EXIF và alpha. Tệp sinh nằm trong `wwwroot/uploads/optimized`, cùng volume uploads đã có. Ảnh không hợp lệ/động/ngoài giới hạn xử lý đi qua pipeline gốc.
- Giới hạn hai lượt encode đồng thời, tối đa 25 triệu pixel, không tải ảnh từ URL bên ngoài. Cache có ETag và hỗ trợ 304; client cache ảnh upload bảy ngày. Khóa cache chứa mtime/length nguồn để tránh dùng lại ảnh cũ nếu nguồn đổi.
- Card sản phẩm/ảnh thu nhỏ tải lazy và decode async; ảnh chính được ưu tiên. Carousel không tải cả bốn banner ngay: ưu tiên slide đầu và nạp slide kế tiếp sau đó.
- Không thêm cache cho dữ liệu thanh toán, tài khoản, tồn kho hoặc giá bán.

## Ảnh upload mới/cũ

Không cần sửa dữ liệu DB hay chuyển định dạng ảnh upload sẵn. ImageUrlPipe thêm kích thước theo nơi hiển thị; backend tự tạo bản nhỏ ở lần yêu cầu đầu rồi dùng lại cache. Ảnh gốc vẫn được phục vụ khi không có tham số kích thước.

Thư viện xử lý: SkiaSharp + SkiaSharp.NativeAssets.Linux.NoDependencies 4.148.0, chạy được trên Windows và container Linux hiện tại.

## Thêm hoặc thay ảnh tĩnh trong repository frontend

Sau khi thay ảnh trong src/assets/images, chạy từ thư mục backend:

```powershell
dotnet run --project tests/tools/OptimizeAssets/OptimizeAssets.csproj -- '<frontend>/src/assets/images' '<frontend>/src/assets/images/optimized' '<frontend>/src/app/core/utils/optimized-images.ts'
```

Thay `<frontend>` bằng đường dẫn thật. Công cụ giữ ảnh nguồn, sinh ảnh responsive và manifest hash. Nếu một ảnh mới chưa có trong manifest, pipe trả lại ảnh gốc để không gây ảnh lỗi.

Build lại hai container:

```powershell
docker compose up -d --build --no-deps api frontend
```

## Giới hạn Cloudflare Quick Tunnel

Đây vẫn là kết nối từ Internet về máy cá nhân, chịu độ trễ đường truyền và tốc độ upload của máy. Các phép đo HTTP ban đầu trong phiên 25/10/2026: API khoảng 4–5 ms ở localhost, khoảng 300 ms qua tunnel; kết nối HTML đầu khoảng 1,3 giây. Đây là các mẫu đo tại một thời điểm, không phải cam kết tốc độ.

Cloudflare đã tự nén JS/CSS trong mẫu ban đầu. Lợi ích chính của thay đổi là giảm byte ảnh, tránh tải đồng loạt ảnh chưa xem và tái sử dụng cache trình duyệt. Chuyển sang hosting/VPS gần người dùng và hostname ổn định là bước hạ tầng riêng nếu cần độ trễ ổn định hơn.
