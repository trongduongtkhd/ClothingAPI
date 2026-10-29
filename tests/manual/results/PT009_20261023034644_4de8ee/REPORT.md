# PT009 / P8 — FAILED

Thời gian chạy: 10:46:44–10:46:53 ngày 23/10/2026 (Asia/Saigon).

## Môi trường và cách chạy

Publish mã nguồn hiện tại bằng .NET SDK 10.0.400, build image ASP.NET Core 10.0 riêng rồi chạy với SQL Server của dự án. Image: `sha256:ffc031fb233c603b96678d9089249b2e18603efc02efe6f0b20ec5728c9304dd`.

Database riêng: `PT009_20261023034644_4de8ee`. API kiểm thử dùng cổng local 18081, JWT secret riêng, CAPTCHA tắt. Không sửa dữ liệu database chính hoặc code nghiệp vụ. Container API kiểm thử đã được xóa khi kết thúc; database kiểm thử được giữ để đối chiếu.

Hai tài khoản riêng đăng ký, đăng nhập, tạo địa chỉ và thêm cùng một biến thể vào giỏ qua API. Mỗi lượt dùng một biến thể riêng (màu Be, size M), tồn kho ban đầu 1; mỗi khách đặt số lượng 1. Hai yêu cầu POST `/api/orders` được phát từ cùng một cổng chờ phía client; thanh toán COD, không coupon. Phạm vi là API và dữ liệu SQL Server, không phải thao tác qua hai trình duyệt.

## Kỳ vọng

Mỗi lượt chỉ một khách tạo đơn thành công; khách còn lại nhận HTTP 400/409 với thông báo không đủ tồn kho. Database phải có đúng một đơn, tổng số lượng đặt 1 và tồn kho cuối 0.

## Kết quả thực tế

| Lượt | Tồn đầu | Độ lệch phát request | HTTP khách A / B | Số đơn lưu | Tổng số lượng đặt | Tồn cuối | Kết quả |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 1 | 1 | 0,571 ms | 201 / 201 | 2 | 2 | 0 | FAILED |
| 2 | 1 | 0,343 ms | 201 / 201 | 2 | 2 | 0 | FAILED |
| 3 | 1 | 0,307 ms | 201 / 201 | 2 | 2 | 0 | FAILED |

Cả sáu phản hồi có `success=true` và thông báo `Đặt hàng thành công.`. SQL ghi nhận hai đơn cho mỗi biến thể: OrderId 1–2, 3–4 và 5–6. Độ lệch là thời điểm client bắt đầu phát request, không phải thời điểm request đến server.

Kết luận: tái hiện lỗi bán vượt tồn kho trong **3/3 lượt của lần chạy này**. Mỗi lượt hệ thống nhận đặt 2 sản phẩm trong khi chỉ có 1 sản phẩm. Tồn cuối bằng 0 không đủ chứng minh hệ thống xử lý đúng. Không suy rộng 3/3 thành xác suất lỗi 100% cho mọi lần chạy.

## Nguyên nhân từ mã nguồn

`Services/Implementations/OrderService.cs` kiểm tra `variant.StockQuantity < quantity` tại dòng 88, trừ trên entity đã đọc tại dòng 167, rồi `SaveChangesAsync` tại dòng 203. Entity không có concurrency token cho tồn kho. Cách xử lý này cho phép hai yêu cầu cùng đọc 1, cùng tính 1 - 1 = 0 rồi cùng lưu 0 và tạo hai đơn (lost update). Đây là diễn giải cơ chế dựa trên mã nguồn và kết quả; phép thử không ghi thời điểm đọc dữ liệu nội bộ của từng request. Transaction hiện tại không đủ bảo vệ thao tác đọc–kiểm tra–ghi này khỏi xung đột đồng thời.

## Hướng khắc phục đề xuất — chưa triển khai

Gộp kiểm tra và trừ tồn kho trong một lệnh SQL có điều kiện `StockQuantity >= quantity`; kiểm tra số dòng cập nhật. Nếu không cập nhật được, trả lỗi không đủ tồn kho và rollback toàn bộ đơn. Thực hiện trừ kho và tạo đơn trong cùng transaction. Nếu dùng SQL trực tiếp/ExecuteUpdate, thay thao tác trừ cũ và quản lý trạng thái entity để SaveChanges không ghi đè kết quả mới. Một lựa chọn khác là cấu hình rowversion/concurrency token và xử lý xung đột, đọc lại tồn kho trước khi quyết định thử lại. Cần chạy lại PT009 sau khi sửa.

## Nội dung đề xuất cho slide

**PT009 · Hai khách mua sản phẩm cuối cùng**

**3/3 lượt tái hiện lỗi**

Chỉ còn 1 sản phẩm nhưng cả hai khách đều đặt thành công; mỗi lượt lưu 2 đơn, tồn cuối 0.

**Nguyên nhân:** Hai yêu cầu cùng đọc tồn kho cũ và cùng ghi kết quả sau khi trừ, làm mất một lần cập nhật.

**Hướng khắc phục:** Kiểm tra và trừ tồn kho bằng cùng một lệnh UPDATE có điều kiện; chỉ hoàn tất đơn khi trừ kho thành công. Dùng transaction để hoàn tác nếu xử lý thất bại.

## Lời thuyết trình gợi ý

“Ở PT009, em kiểm tra hai khách cùng mua sản phẩm cuối cùng. Mỗi khách mua một sản phẩm, nhưng kho chỉ còn một. Kết quả đúng phải là một người đặt thành công, người còn lại được báo không đủ tồn kho. Tuy nhiên, lần chạy ngày 23/10 tái hiện lỗi ở cả ba lượt: cả hai khách đều nhận thông báo thành công và SQL lưu hai đơn. Dù tồn kho cuối bằng không, hệ thống vẫn nhận bán vượt một sản phẩm. Nguyên nhân là hai yêu cầu có thể cùng đọc tồn kho bằng một, cùng tính còn không và ghi đè kết quả lên nhau. Em đề xuất gộp kiểm tra và trừ kho vào một lệnh tại database, chỉ hoàn tất đơn khi trừ kho thành công, đồng thời dùng transaction để hoàn tác nếu có lỗi.”

## Bằng chứng

- `result.json`: phản hồi HTTP và truy vấn SQL ngay sau mỗi lượt.
- `api.log`: log của API kiểm thử.
- `verify.sql`: truy vấn kiểm tra lại trên database riêng bằng SSMS (`localhost,14333`).
- Script tái hiện: `tests/manual/pt009-concurrent-last-item.cjs`.
