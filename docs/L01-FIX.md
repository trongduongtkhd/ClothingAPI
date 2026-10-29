# L01 — Chống bán vượt kho khi đặt hàng đồng thời

Ngày sửa và kiểm chứng: 29/10/2026. Phạm vi: nhiều request **tạo đơn** cùng mua một biến thể, qua giỏ hàng hoặc Mua ngay.

## Nguyên nhân trước sửa

`OrderService.CreateAsync` đọc `StockQuantity` vào entity, kiểm tra đủ hàng, trừ trong bộ nhớ rồi `SaveChanges`. Hai request có thể cùng đọc tồn 10, cùng tính 9 và cùng ghi 9. Với tồn 2, nhiều request cũng có thể cùng vượt qua kiểm tra trước khi bất kỳ request nào lưu đơn.

Transaction bảo đảm một đơn được lưu trọn vẹn, nhưng không tự biến chuỗi đọc–kiểm tra–ghi giá trị tuyệt đối này thành phép trừ nguyên tử.

## Thay đổi

Trong [OrderService.cs](../Services/Implementations/OrderService.cs):

- Dùng `ExecuteUpdateAsync` để trừ từ giá trị tồn hiện tại ngay trong database, với điều kiện `StockQuantity >= quantity` và sản phẩm/biến thể vẫn đang bán.
- Chỉ coi giữ hàng thành công khi cập nhật đúng một dòng. Nếu không, trả `BadRequestException` → HTTP 400 với thông báo thiếu kho hoặc ngừng bán.
- Giữ toàn bộ phép cập nhật tồn trong transaction tạo đơn đang có. Một dòng giữ hàng thất bại hoặc lưu đơn thất bại sẽ rollback các dòng đã trừ, coupon và giỏ hàng.
- Xử lý biến thể theo thứ tự `VariantId` tăng dần để giảm nguy cơ deadlock giữa các giỏ nhiều dòng.
- Bỏ phép `variant.StockQuantity -= quantity` cũ để tránh trừ hai lần hoặc ghi đè bằng snapshot.
- Đồng bộ riêng giá trị tồn trong EF change tracker sau cập nhật trực tiếp, giữ nguyên snapshot giá bán/giá vốn của đơn và không để `SaveChanges` ghi lại tồn cũ.
- Kiểm tra số lượng 1–100 ở tầng service trước khi trừ; API vẫn dùng validation DTO hiện có.

Không cần migration hoặc thay đổi frontend cho bản sửa này.

## Kiểm chứng sau sửa

[Kết quả HTTP và SQL Server đầy đủ](../tests/manual/results/stockaudit-20261029044915-b80dde/result.json): **36/36 đạt**, không có HTTP 500.

Mỗi tình huống đồng thời dưới đây chạy ba lượt ở cả chế độ giỏ và Mua ngay: 5 tình huống × 3 lượt × 2 chế độ = **30 lượt**.

| Tồn đầu | Yêu cầu đồng thời | Sau sửa | Số lượt đạt |
|---|---|---|---|
| 10 | 2 khách, mỗi khách mua 1 | 2 đơn thành công, bán 2, tồn 8 | 6/6 |
| 10 | 2 khách, mỗi khách mua 5 | 2 đơn thành công, bán 10, tồn 0 | 6/6 |
| 10 | 2 khách, mỗi khách mua 6 | 1 đơn thành công, 1 HTTP 400 thiếu kho, bán 6, tồn 4 | 6/6 |
| 2 | 5 khách, mỗi khách mua 1 | 2 đơn thành công, 3 HTTP 400 thiếu kho, bán 2, tồn 0 | 6/6 |
| 1 | 2 khách, mỗi khách mua 1 | 1 đơn thành công, 1 HTTP 400 thiếu kho, bán 1, tồn 0 | 6/6 |

Sáu ca còn lại đều đạt: số lượng -1/0/101 bị từ chối; mua hai biến thể khác nhau đúng tồn riêng; giỏ nhiều dòng có dòng thiếu kho không tạo đơn/không trừ dòng khác và giữ giỏ; biến thể hết hàng bị từ chối.

Hai bài test mới trong [OrderStockReservationIntegrationTests.cs](../tests/ClothingAPI.IntegrationTests/OrderStockReservationIntegrationTests.cs) kiểm tra transaction bằng SQLite in-memory:

1. Snapshot hai dòng đều đủ hàng. Trigger kiểm thử cho cập nhật giữ dòng sau trả 0 dòng, sau khi dòng trước đã trừ. Kết quả phải HTTP 400, tồn cả hai dòng được khôi phục, không có đơn và giữ nguyên giỏ.
2. Sau khi cả hai dòng đã giữ hàng, trigger kiểm thử từ chối INSERT đơn. Xác nhận đúng lỗi được chủ động tạo ra, sau đó kiểm tra tồn, bộ đếm/lượt dùng coupon và giỏ đều rollback.

Trigger chỉ được tạo trong database in-memory dùng cho test và được xóa trong `finally`. Hai bài này kiểm tra rollback; hành vi nhiều request thực được kiểm chứng riêng bằng SQL Server ở trên.

Các kiểm tra khác:

- Unit tests: **301/301 đạt**.
- Integration được chọn: **11/11 đạt**, gồm 9 test IT00 hiện có và 2 bài mới.
- Build Release/publish thành công; `git diff --check` đạt.
- Đã xóa container API/SQL Server, database thử, network và image tạm. Chỉ giữ bằng chứng JSON/log.

## Chạy lại

Từ thư mục gốc backend, cần Docker engine đang chạy và .NET 10:

```powershell
dotnet publish ClothingAPI.csproj -c Release --no-restore -o tests/manual/.publish /p:UseAppHost=false
node tests/manual/order-stock-audit.cjs --l01-only

dotnet test tests/ClothingAPI.UnitTests/ClothingAPI.UnitTests.csproj --no-restore
dotnet test tests/ClothingAPI.IntegrationTests/ClothingAPI.IntegrationTests.csproj --no-restore --filter "FullyQualifiedName~OrderStockReservationIntegrationTests|FullyQualifiedName~IntegrationTests.IT00"
```

## Các lỗi tiếp theo

Bản sửa L01 không bảo đảm tính nhất quán khi một request tạo đơn chạy đồng thời với đường **hủy/trả/nhập/sửa/hủy phiếu nhập** còn dùng snapshot tồn cũ. Những đường ghi đó phải được sửa tiếp theo L02/L03 bằng cơ chế cập nhật tồn phù hợp. Các lỗi coupon, đặt lặp, giỏ hàng và MockQR cũng chưa được xử lý trong bước này.

Số tồn sai đã phát sinh trong dữ liệu cửa hàng trước đây chưa được tự động sửa. Báo cáo Excel ngày 29/10 phản ánh lần kiểm tra trước sửa; bằng chứng trong tài liệu này ghi nhận riêng kết quả sau sửa L01.
