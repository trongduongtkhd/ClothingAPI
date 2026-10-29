# Kiểm thử đặt hàng đồng thời và tồn kho — 28/10/2026

**Kết luận: đã tái hiện lỗi bán vượt kho và ghi đè số lượng tồn trên code hiện tại. Các lỗi dưới đây vẫn còn, chưa sửa code nghiệp vụ.**

Đã chạy 85 lượt kiểm tra HTTP + đối chiếu SQL: **14 đạt, 71 không đạt**, thuộc 33 nhóm tình huống. Đây là số lượt kiểm tra, không phải 71 lỗi độc lập. Trong 71 lượt không đạt có 3 lượt đánh giá yêu cầu chống tạo đơn lặp khi retry; yêu cầu này cần được xác định trong hợp đồng API. 68 lượt còn lại vi phạm tính nhất quán dữ liệu hoặc trả lỗi hệ thống ngoài dự kiến.

## Môi trường và bằng chứng

- Build Release từ source đang có trong workspace, bao gồm các thay đổi chưa commit của dự án. Không sửa các service/controller/model/migration để chạy thử.
- SQL Server 2022 CU27 trong container mới, database `StockAudit` do chính migration hiện tại tạo; `READ_COMMITTED_SNAPSHOT = ON`.
- API container riêng, 5 tài khoản khách và 1 admin riêng, JWT/mật khẩu riêng; không dùng database, tài khoản hay volume cửa hàng.
- Yêu cầu HTTP độc lập được phát qua cùng cổng chờ trong script. Độ lệch thời điểm bắt đầu lớn nhất khoảng **2,03 ms**. Không chèn delay hoặc khóa nhân tạo vào code nghiệp vụ.
- Mỗi tình huống đồng thời chạy 3 lượt với dữ liệu riêng. Có kiểm tra mã HTTP, số đơn/chi tiết đơn, tổng hàng bán, tồn cuối và dữ liệu coupon; không coi HTTP 201 hoặc tồn không âm là đủ để đạt.
- Đây là kiểm thử API; không phải hai trình duyệt thao tác UI và không phải benchmark tải lớn.
- Hai lượt chạy hoàn tất lúc **17:34:33 và 17:35:59, ngày 28/10/2026, giờ Việt Nam**.

Bằng chứng chi tiết, gồm kết quả từng request, thời gian, kỳ vọng và truy vấn trạng thái đã lưu:

- [69 lượt kiểm tra chính](../tests/manual/results/stockaudit-20261028103353-ce4870/result.json)
- [16 lượt kiểm tra bổ sung](../tests/manual/results/stockaudit-20261028103534-2b229c/result.json)
- [Script tái hiện](../tests/manual/order-stock-audit.cjs)

Log API nằm cạnh từng `result.json` trong file `api.log`, gồm stack trace cho các lỗi HTTP 500.

## Các lỗi còn tồn đọng

### L01 — Nghiêm trọng: nhiều khách cùng mua gây sai tồn và bán vượt kho

Tái hiện **30/30 lượt**, gồm cả giỏ hàng và “Mua ngay”.

| Tồn đầu / yêu cầu đồng thời | Kết quả đúng | Kết quả thực tế |
|---|---|---|
| Tồn 10, 2 khách mỗi người mua 1 | 2 đơn, bán 2, tồn 8 | 2 đơn, bán 2, tồn **9** |
| Tồn 10, 2 khách mỗi người mua 5 | 2 đơn, bán 10, tồn 0 | 2 đơn, bán 10, tồn **5** |
| Tồn 10, 2 khách mỗi người mua 6 | Chỉ 1 đơn, bán 6, tồn 4 | **2 đơn**, bán **12**, tồn 4 |
| Tồn 2, 5 khách mỗi người mua 1 | 2 đơn thành công, 3 từ chối, tồn 0 | Giỏ hàng tạo **3–5 đơn**; Mua ngay tạo **5 đơn**; tồn 0 hoặc 1 |
| Tồn 1, 2 khách mỗi người mua 1 | 1 đơn thành công, 1 từ chối | **2 đơn** thành công, tồn 0 |

Vị trí: `Services/Implementations/OrderService.cs:95,175,211` và `Data/AppDbContext.cs:397`.

Nguyên nhân: đọc `StockQuantity`, kiểm tra trong bộ nhớ, trừ trong entity rồi `SaveChanges`. Hai request có thể cùng đọc 10 rồi cùng ghi 9. Transaction hiện tại không biến thao tác này thành phép trừ có điều kiện nguyên tử; model chưa có concurrency token bảo vệ tồn. Khi mỗi người mua 6, cả hai cùng vượt qua kiểm tra trên giá trị 10.

**Tồn không âm vẫn có thể đã bán vượt kho.** Chỉ thêm ràng buộc `StockQuantity >= 0` không giải quyết được tình huống này.

### L02 — Cao: mất lượng hoàn kho khi hủy hoặc trả hai đơn khác nhau

- Hai đơn đã đặt tuần tự, lần lượt mua 2 và 3 từ tồn 10 → còn 5. Hủy đồng thời cả hai phải trả tồn về 10; thực tế chỉ về **7 hoặc 8**, dù cả hai trả HTTP 200. **3/3 lượt lỗi**.
- Hai đơn hoàn thành và được yêu cầu trả hàng hợp lệ, duyệt trả đồng thời phải về 10; thực tế **7 hoặc 8**. Hai đơn vẫn `Returned/Refunded`. **3/3 lượt lỗi**.
- Hủy đơn mua 2 đồng thời người khác mua 3: tồn đúng 7, thực tế **5**. **3/3 lượt lỗi**.

Vị trí: `OrderService.cs:317`; `AdminOrderService.cs:162,307`. Các phép cộng tồn cũng là đọc–sửa–ghi. `VnpayOrderLock` khóa theo **orderId**, nên bảo vệ một đơn nhưng không bảo vệ **cùng variantId thuộc hai đơn khác nhau**.

### L03 — Cao: nhập kho đồng thời và nhập trong lúc bán ghi đè tồn

- Tồn 10, tạo hai phiếu nhập +3 và +4 đồng thời: đúng 17, thực tế **13 hoặc 14**; cả hai phiếu tạo thành công. **3/3 lượt lỗi**.
- Tồn 10, nhập +5 đồng thời bán 2: đúng 13, thực tế **8 hoặc 15**. **3/3 lượt lỗi**.
- Tồn 10, nhập +5 trước, sau đó hủy phiếu nhập đồng thời bán 2: đúng 8; **2/3 lượt còn 13**, dù phiếu đã hủy và đơn bán vẫn thành công.

Vị trí: `PurchaseOrderService.cs:86,116,176,271,286` và `OrderService.cs:175`. Nhập, đảo phiếu nhập và bán không có cơ chế bảo vệ tồn chung. Cần bảo vệ cả tính lại giá vốn khi cập nhật kho; bộ test này chưa định lượng sai lệch giá vốn giữa các lô khác giá.

### L04 — Cao: coupon vượt giới hạn toàn hệ thống và từng khách

- Coupon giới hạn tổng 1 lượt, hai khách mua hai biến thể khác nhau đồng thời: **cả hai đơn được giảm giá**, `CouponUsages = 2` nhưng `UsedCount = 1`. **3/3 lượt lỗi**.
- Coupon giới hạn 1 lượt/khách, cùng khách mua hai biến thể khác nhau đồng thời: **2 lượt dùng**, `UsedCount = 1`. **3/3 lượt lỗi**.

Vị trí: `CouponService.cs:208–219` và `OrderService.cs:198`. Kiểm tra hạn mức rồi tăng bộ đếm không được bảo vệ chung; phép đếm theo khách cũng tranh chấp.

### L05 — Trung bình: hủy đồng thời làm sai bộ đếm coupon

Tạo tuần tự hai đơn dùng cùng coupon → `UsedCount = 2`. Hủy đồng thời hai đơn trên **hai biến thể khác nhau** → `CouponUsages = 0` nhưng `UsedCount = 1`. **3/3 lượt lỗi**.

Vị trí: `OrderService.cs:328–330`, tương tự nhánh hủy admin tại `AdminOrderService.cs:180–182`. Bộ đếm bị ghi đè, có thể làm mã hết lượt sớm dù các đơn đã hủy.

### L06 — Cao: đặt cùng giỏ đồng thời trả HTTP 500

Hai request tạo đơn cùng tài khoản/cùng giỏ: một trả 201, một trả **500**, cuối cùng có 1 đơn. **3/3 lượt lỗi**.

Log xác nhận `DbUpdateConcurrencyException`: thao tác xóa dòng giỏ mong đợi 1 dòng nhưng thực tế 0 do request kia đã xóa. Exception đi qua `ExceptionMiddleware` thành lỗi hệ thống chung.

Vị trí: `OrderService.cs:209–211`; `Middleware/ExceptionMiddleware.cs`. Cần xử lý tranh chấp/đặt lặp có chủ đích thay vì báo lỗi hệ thống.

### L07 — Trung bình: thêm giỏ hàng đồng thời lỗi 500 hoặc mất số lượng

- Giỏ chưa có biến thể: hai lần thêm +1 đồng thời → một HTTP 201, một **500**, còn 1 sản phẩm. Log: SQL unique index `IX_CartItems_UserId_VariantId` bị trùng. **3/3 lượt lỗi**.
- Giỏ đã có 1: hai lần thêm +1 đồng thời đều trả 201 nhưng cuối cùng chỉ có **2**, đúng phải **3**. **3/3 lượt lỗi**.

Vị trí: `CartService.cs:49–81`, `Data/AppDbContext.cs:513`. Có unique index nhưng chưa có xử lý tranh chấp tạo dòng mới; cập nhật dòng cũ cũng bị mất phép cộng.

### L08 — Cao: thanh toán QR mô phỏng và hủy đơn cùng thành công

Gọi đồng thời `PUT /api/orders/{id}/cancel` và `POST /api/orders/{id}/mock-qr/confirm`: **2/3 lượt** cả hai HTTP 200, đơn cuối `Cancelled` nhưng `PaymentStatus = Paid`, tồn đã hoàn đủ.

Vị trí: `OrderService.cs:559–598`. Nhánh xác nhận MockQR không tham gia khóa theo đơn mà nhánh hủy đang sử dụng. Đây là lỗi của **MockQR**, không phải kết luận về callback VNPAY thật.

### L09 — Cao: số lượng nhập hợp lệ theo DTO có thể làm tồn âm do tràn int

Tồn 10, tạo phiếu nhập `Quantity = 2147483647`, `UnitCost = 100000`: API trả **201**, tồn lưu thành **-2147483639**. **1/1 lượt lỗi**.

Vị trí: `DTOs/PurchaseOrders/CreatePurchaseOrderDto.cs:23`; `PurchaseOrderService.cs:273–280`; `ProductVariant.StockQuantity` là `int`.

DTO cho phép đến `int.MaxValue` nhưng không kiểm tra **tổng tồn sau nhập**. Phép cộng int không được kiểm tra tràn; database cũng không chặn tồn âm. Đây là ca biên qua API admin, không phải lượng nhập thường ngày, nhưng đã tạo dữ liệu sai thực tế.

### L10 — Trung bình, cần xác định yêu cầu: chưa chống tạo đơn “Mua ngay” lặp khi retry

Cùng khách gửi hai request “Mua ngay” giống nhau: **cả hai tạo đơn mới**, 3/3 lượt. UI có cờ `creatingOrder` ngăn bấm lặp trong cùng component, nhưng API không có khóa idempotency để nhận ra request gửi lại sau timeout hoặc từ tab khác.

Nếu đây là hai lần mua có chủ đích thì hai đơn là hợp lệ. Nếu là một thao tác người dùng được gửi lại, hệ thống hiện không bảo đảm chỉ tạo một đơn. Cần chốt và triển khai `Idempotency-Key`/checkout token; chỉ so nội dung hai request sẽ có thể chặn nhầm lần mua hợp lệ. Dù chọn chính sách nào, lỗi tồn ở L01 vẫn cần sửa độc lập.

## Các tình huống đã đạt trong lần kiểm tra này

- Hủy cùng một đơn đồng thời: **3/3**, chỉ một thành công, tồn không được cộng hai lần.
- Duyệt trả cùng một đơn đồng thời: **3/3**, chỉ một thành công, không hoàn tồn hai lần.
- Hai khách mua **hai biến thể khác nhau của cùng sản phẩm**: tồn từng biến thể chính xác.
- Mua ngay với số lượng -1, 0, 101: HTTP 400, không tạo đơn, không đổi tồn.
- Giỏ nhiều dòng có một dòng thiếu kho: HTTP 400, không tạo đơn hoặc trừ tồn dòng khác, giỏ được giữ nguyên.
- Biến thể đã hết hàng từ trước: từ chối đặt hàng.

Các kết quả đạt chỉ áp dụng cho mẫu đã chạy, không chứng minh mọi lịch xen kẽ đều an toàn.

## Hiệu năng và bộ test hiện có

182 response được ghi trong các ca của hai lượt chạy, không tính request chuẩn bị dữ liệu: p50 **16,50 ms**, p95 **70,02 ms**, chậm nhất **148,99 ms**. Có **6 HTTP 500** do tranh chấp giỏ hàng/đặt cùng giỏ.

Đây là độ trễ mẫu trên máy local, tải đồng thời chủ yếu 2–5 request, database nhỏ. **Không dùng các số này để kết luận năng lực tải production.** Vấn đề đã chứng minh ở đây là tính đúng đắn khi chạy đồng thời; API phản hồi nhanh vẫn có thể lưu sai tồn.

Sau khi restore dependency từ cache NuGet local:

- Unit tests hiện có: **301/301 đạt**.
- Integration tests hiện có được chọn bằng `FullyQualifiedName~IntegrationTests.IT00`: **9/9 đạt**. Không khẳng định đã chạy toàn bộ integration suite.
- Các test integration này dùng SQLite in-memory, một kết nối dùng chung. Kết quả đó không phủ hành vi tranh chấp của SQL Server trong bài kiểm tra này. Lỗi SkiaSharp lúc biên dịch test ban đầu hết sau restore; không coi là lỗi nghiệp vụ còn tồn đọng.

## Hướng sửa ưu tiên

1. Bảo vệ tồn kho **chung cho tất cả đường ghi**: tạo đơn, hủy, trả hàng, nhập/sửa/hủy phiếu nhập, hết hạn thanh toán. Có thể dùng cập nhật nguyên tử có điều kiện cho xuất kho, cộng/trừ nguyên tử cho hoàn kho; với giá vốn và phiếu nhập cần transaction/khóa thích hợp hoặc optimistic concurrency có retry toàn bộ thao tác. Khóa các biến thể theo thứ tự ổn định để giảm deadlock.
2. Giữ việc trừ kho, tạo đơn, coupon và xóa giỏ trong cùng transaction; thiếu kho ở bất kỳ dòng nào phải rollback tất cả. Đọc lại tồn/giá vốn sau khi lấy khóa hoặc xử lý concurrency phù hợp, không dùng snapshot entity cũ.
3. Bảo vệ hạn mức coupon tổng và theo khách; tăng/giảm `UsedCount` nguyên tử, kiểm tra đồng bộ với `CouponUsages`.
4. Cho mọi writer thanh toán/hủy tham gia cùng khóa đơn; xử lý lỗi concurrency/unique thành phản hồi nghiệp vụ thích hợp hoặc retry có giới hạn. Thêm cơ chế chống tạo đơn lặp theo khóa của request.
5. Dùng kiểu số đủ lớn hoặc kiểm tra tràn/tổng tồn trước khi nhập; thêm check constraint tồn không âm như lớp bảo vệ bổ sung. Constraint này không thay cho cơ chế chống bán vượt kho.
6. Thêm kiểm thử SQL Server đồng thời vào quy trình kiểm tra. Sau khi sửa, chạy lại cả 2 tập ca và đối chiếu ledger/số đơn với tồn; không chỉ kiểm tra mã HTTP.

## Giới hạn và các điểm cần kiểm tra tiếp

- Chưa kiểm thử cổng VNPAY thật, IPN/reconcile/expiry đang chạy đồng thời với nhập/bán. Qua đọc code, `VnpayService.cs:296–298` cũng cộng tồn và giảm coupon theo entity; khóa theo order không tự bảo vệ các order khác cùng variant/coupon. Đây là **nguy cơ từ đọc code**, chưa tính vào 85 lượt hay liệt kê là lỗi đã tái hiện của VNPAY.
- Chưa chạy benchmark hàng trăm/nghìn khách, test tải trên dữ liệu lớn, deadlock nhiều giỏ có thứ tự sản phẩm đảo nhau, hoặc mạng đứt giữa commit và trả response.
- Chưa sửa hoặc đối soát dữ liệu cửa hàng đang có. Nếu lỗi đã xảy ra trước đây, sửa code không tự khôi phục số tồn/bộ đếm đã sai; cần đối soát riêng dựa trên dữ liệu bán/nhập/trả thực tế.

## Dữ liệu thử và cách chạy lại

**Đã xóa toàn bộ database/dữ liệu thử** bằng cách xóa hai cặp container API/SQL Server riêng với `docker rm -f -v`, mạng và image tạm. Đã kiểm tra lại bằng Docker: không còn container, network hay image `stockaudit-*`. Không khởi động hay sửa các container cửa hàng vốn đang dừng. Chỉ giữ script và bằng chứng JSON/log để xem lại.

Chạy từ thư mục `ClothingAPI` (cần Docker, .NET 10, Node 14+ và SQL Server image hiện có):

```powershell
dotnet publish ClothingAPI.csproj -c Release --no-restore -o tests/manual/.publish /p:UseAppHost=false
node tests/manual/order-stock-audit.cjs
node tests/manual/order-stock-audit.cjs --extended-only
```

Mỗi lệnh Node tạo môi trường riêng và tự dọn trong `finally`, kể cả khi test thất bại. Script trả exit code 1 khi có ca không đạt, 2 khi lỗi thực thi; kết quả nằm trong `tests/manual/results/stockaudit-<UTC>-<random>/result.json`. Nếu tiến trình bị buộc dừng hoặc máy tắt, `finally` không bảo đảm chạy; chỉ dọn các tài nguyên có đúng tiền tố của lượt chạy bị ngắt.
