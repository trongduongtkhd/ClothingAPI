# PT009 / P8 — Hai khách mua sản phẩm cuối cùng

Chạy từ thư mục gốc ClothingAPI:

```powershell
dotnet publish ClothingAPI.csproj -c Release --no-restore -o tests/manual/.publish /p:UseAppHost=false
docker build -f tests/manual/Dockerfile -t clothing-api-pt009:local .
node tests/manual/pt009-concurrent-last-item.cjs
```

Cần Docker, Node.js >= 14 và hai container `clothing-api`, `clothing-db` của dự án đang chạy. Không cần cài thêm package.

Script tạo API kiểm thử tại `127.0.0.1:18081` từ image build riêng và database SQL Server mới có tên `PT009_<thời gian UTC>_<mã ngẫu nhiên>`. Chỉ lấy kết nối SQL và tên mạng từ cấu hình container hiện tại; không ghi mật khẩu, secret hay access token vào báo cáo. API kiểm thử dùng JWT secret riêng, tắt CAPTCHA và không dùng volume của API chính.

Hai khách đăng ký, đăng nhập, tạo địa chỉ qua API. Mỗi lượt tạo biến thể màu Be, size M, tồn kho 1 và thêm vào giỏ của cả hai người. Hai yêu cầu POST `/api/orders` được phát từ cùng một cổng chờ trong Node.js, ghi lại độ lệch thời điểm bắt đầu. Đây là kiểm thử API đồng thời; không kiểm tra giao diện bằng hai trình duyệt.

Chạy 3 lượt, mỗi lượt dùng biến thể riêng. Chỉ đạt khi đồng thời thỏa mãn:

- Một request trả HTTP 201 và `success=true`.
- Một request trả HTTP 400/409 với thông báo `không đủ tồn kho`.
- SQL Server có đúng một đơn chứa biến thể đó, tổng số lượng bán bằng 1, tồn kho bằng 0.

Không chấp nhận mọi lỗi 400 làm kết quả đúng. Tồn kho bằng 0 nhưng có hai đơn cũng là FAILED.

Kết quả HTTP và truy vấn SQL nằm trong `results/<database>/result.json`, log API trong cùng thư mục. Container kiểm thử được xóa khi kết thúc; database kiểm thử được giữ để đối chiếu bằng SSMS. Chọn database đó và chạy:

```sql
SELECT v.VariantId, v.SKU, v.StockQuantity,
       COUNT(DISTINCT i.OrderId) AS OrderCount,
       COALESCE(SUM(i.Quantity), 0) AS SoldQuantity
FROM ProductVariants v
LEFT JOIN OrderItems i ON i.VariantId = v.VariantId
WHERE v.SKU LIKE 'PT009-ROUND-%'
GROUP BY v.VariantId, v.SKU, v.StockQuantity
ORDER BY v.VariantId;

SELECT o.OrderId, o.OrderCode, o.UserId, o.Note, i.VariantId, i.Quantity
FROM Orders o
JOIN OrderItems i ON i.OrderId = o.OrderId
WHERE o.Note LIKE 'PT009 round %'
ORDER BY i.VariantId, o.OrderId;
```

Script không sửa code nghiệp vụ, tồn kho hoặc tài khoản trong database chính.
