# PT009 / P8 — FAILED

Thời gian: 14:40:46–14:40:55, ngày 22/10/2026 (Asia/Saigon).

Test source hiện tại của ClothingAPI, publish bằng .NET SDK 10.0.400, chạy trong container ASP.NET Core 10.0 và dùng SQL Server 2022 của dự án. Database kiểm thử riêng: `PT009_20261022074046_ff8f5b`. Image kiểm thử: `sha256:74f64e7f29a5988a64c6172b047b40904186bb92dd4e05b26bf5c16b24ff2799`.

Hai tài khoản riêng đăng ký và đăng nhập qua API, mỗi người có địa chỉ riêng và giỏ hàng chứa một sản phẩm. Mỗi lượt dùng một biến thể Áo Polo riêng, màu Be, size M, tồn kho ban đầu bằng 1. Hai request POST `/api/orders` được phát đồng thời từ cổng chờ chung. Không dùng coupon; thanh toán COD. CAPTCHA tắt trong API kiểm thử riêng.

Kỳ vọng mỗi lượt: một khách tạo đơn thành công, khách còn lại nhận thông báo không đủ tồn kho; SQL có đúng một đơn, tổng số lượng bán bằng 1 và tồn kho bằng 0.

| Lượt | VariantId | Độ lệch lúc phát request | HTTP khách 1 / khách 2 | Số đơn lưu SQL | Số lượng bán | Tồn kho cuối | Kết quả |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 1 | 1 | 0,743 ms | 201 / 201 | 2 | 2 | 0 | FAILED |
| 2 | 2 | 0,252 ms | 201 / 201 | 2 | 2 | 0 | FAILED |
| 3 | 3 | 0,344 ms | 201 / 201 | 2 | 2 | 0 | FAILED |

Cả sáu response đều có `success=true` và thông báo `Đặt hàng thành công.`. Không có response báo không đủ tồn kho. SQL Server lưu OrderId 1, 2 cho lượt 1; 3, 4 cho lượt 2; 5, 6 cho lượt 3. Độ lệch trên là thời điểm client bắt đầu phát request, không phải thời điểm request đến server.

**Lỗi mức High: bán vượt tồn kho.** Mỗi biến thể chỉ có 1 sản phẩm nhưng hệ thống bán 2. Tồn kho bằng 0 không đủ chứng minh testcase đạt.

Trong `Services/Implementations/OrderService.cs`, phương thức `CreateAsync` đọc và kiểm tra `variant.StockQuantity` rồi thực hiện `variant.StockQuantity -= quantity`, sau đó lưu bằng EF. Hai transaction có thể cùng đọc 1, cùng tính ra 0 và cùng tạo đơn; lần cập nhật sau ghi đè giá trị 0 của lần trước (lost update). Transaction hiện tại không bảo đảm chỉ một khách lấy được sản phẩm cuối.

Hướng khắc phục: trừ tồn kho bằng thao tác SQL có điều kiện `StockQuantity >= quantity` trong transaction và kiểm tra số dòng cập nhật; khi không lấy được tồn kho thì trả lỗi có kiểm soát và rollback toàn bộ đơn. Cần kiểm thử lại sau khi sửa.

Phạm vi lần chạy: kiểm thử API đồng thời và dữ liệu SQL Server; chưa kiểm thử hiển thị bằng hai trình duyệt. Sản phẩm dùng dữ liệu riêng tương đương điều kiện testcase. Không sửa code nghiệp vụ hoặc dữ liệu trong database chính. Container kiểm thử đã xóa; database riêng được giữ để xem trong SSMS bằng `localhost,14333`. Xem `verify.sql` trong thư mục này.

Bằng chứng đầy đủ: `result.json` gồm response HTTP, thời gian, số đơn và số lượng bán truy vấn trực tiếp từ SQL Server; `api.log` gồm log API của lần chạy. Script tái hiện và hướng dẫn nằm trong `tests/manual`.
