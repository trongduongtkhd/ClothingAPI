# VNPAY Sandbox

Website hỗ trợ ba phương thức: COD, QR mô phỏng và VNPAY Sandbox. VNPAY được bật qua cấu hình backend. Khóa bí mật không được trả về frontend hoặc đưa vào Git.

## Cấu hình Docker

Đặt trong `.env` của backend:

```dotenv
VNPAY_ENABLED=true
VNPAY_TMN_CODE=<Terminal ID do VNPAY cấp>
VNPAY_HASH_SECRET=<Secret Key do VNPAY cấp>
VNPAY_PUBLIC_BASE_URL=https://<hostname-public-cua-website>
VNPAY_PAYMENT_MINUTES=15
VNPAY_ORDER_MINUTES=30
```

Frontend Docker/Nginx đã proxy `/api/`. IPN dùng URL public trỏ vào cổng frontend 4200. Khi tạo phiên thanh toán, backend lấy Origin của frontend, đối chiếu danh sách cho phép và ký Return URL quay lại chính origin đó. Vì vậy bắt đầu từ `http://localhost:4200` thì trở về localhost, bắt đầu từ hostname public thì trở về hostname public; không đổi miền làm mất phiên đăng nhập. API client không có Origin dùng PublicBaseUrl mặc định.

Địa chỉ của phiên thiết lập ban đầu: `https://hardcover-trend-uploaded-open.trycloudflare.com`.

- IPN cần đăng ký với VNPAY: `https://hardcover-trend-uploaded-open.trycloudflare.com/api/payments/vnpay/ipn`.
- Return URL được backend tự truyền: `<origin-bat-dau>/api/payments/vnpay/return`. Origin được phép gồm PublicBaseUrl và AllowedFrontendOrigins (mặc định localhost:4200, 127.0.0.1:4200).
- Trang giao diện kết quả: `/checkout/vnpay-result?orderId=<id>`.

Quick Tunnel đổi hostname khi tạo phiên mới. Khi đó sửa `VNPAY_PUBLIC_BASE_URL`, cập nhật IPN tại VNPAY và tạo lại container API. Không cần sửa code Angular.

Lệnh cập nhật sau khi đổi cấu hình:

```powershell
docker compose up -d --build --no-deps api frontend
```

Chạy `dotnet run` trực tiếp: đặt các khóa tương ứng trong User Secrets, dưới section `VnpaySettings`. JSON mặc định tắt VNPAY. Khi bật mà thiếu cấu hình, API báo lỗi lúc khởi động.

## Luồng và nguyên tắc

- Tạo đơn bằng backend hiện có: tính lại giá/coupon/phí vận chuyển, trừ kho một lần. Lấy URL VNPAY sau khi commit đơn.
- Mỗi lần thử có TxnRef riêng trong `PaymentAttempts`. Lặp request khi phiên còn hiệu lực trả lại cùng URL.
- IPN xác minh HMAC-SHA512, merchant, mã tham chiếu, số tiền và trạng thái. Transaction + SQL Server application lock bảo vệ trước hai callback/retry/hủy đơn chạy đồng thời.
- Return URL và IPN dùng chung bộ kiểm tra chữ ký, merchant, TxnRef, số tiền, trạng thái và khóa transaction. Return có chữ ký hợp lệ ghi nhận được thành công/hủy ngay cả khi chưa đăng ký IPN hoặc QUERYDR không khả dụng. Callback lặp không ghi nhận hai lần. Không xác nhận thanh toán chỉ dựa vào query string từ trình duyệt thiếu chữ ký hợp lệ.
- Địa chỉ trang kết quả được lấy từ Return URL trong PaymentUrl đã lưu khi tạo phiên và được đối chiếu danh sách cho phép. Không nhận URL chuyển hướng tùy ý từ callback.
- Trang kết quả đọc trạng thái DB ngay, hiển thị rõ hủy/thành công/thất bại và chỉ poll khi đang chờ. Nút Kiểm tra kết quả gọi QUERYDR khi cần; kết quả query cũng phải có chữ ký hợp lệ.
- Query một giao dịch tối đa một lần mỗi 5 phút để tránh giới hạn cổng; kiểm tra trạng thái DB không gọi mạng VNPAY.
- Phiên mặc định 15 phút, giữ đơn tối đa 30 phút. Worker xử lý đơn hết hạn mỗi phút: đối soát trước, chỉ hoàn kho/coupon khi không còn phiên có khả năng trả tiền hoặc cần kiểm tra. Giao dịch chưa rõ kết quả giữ nguyên để đối soát lại.
- QUERYDR trả trạng thái chưa hoàn tất chỉ được coi là hết hạn sau thời hạn phiên cộng 5 phút. Không tự kết luận thất bại từ việc đóng tab hoặc lỗi mạng.
- Khi cổng trả thiếu/chữ ký sai/kết quả chưa rõ hoặc mã yêu cầu không thành công, không tự hủy phiên và không tự mở phiên mới. Lần Return có chữ ký hợp lệ hủy giao dịch sẽ đóng phiên để lần thử tiếp theo có TxnRef mới.
- Callback thành công bất thường đến sau hủy hoặc trả tiền hai lần được lưu `RequiresReview`; không tự khôi phục đơn đã hoàn kho. Shop có thể xem thông báo cần đối soát tại chi tiết đơn VNPAY.
- Admin không thể sửa Paid thủ công cho VNPAY và chỉ xử lý giao hàng sau Paid.

## Giới hạn bản đầu

Chưa tích hợp API hoàn tiền VNPAY. Hủy đơn VNPAY đã Paid và duyệt trả hàng dẫn tới hoàn tiền bị chặn ở backend và giao diện; không gắn nhãn Refunded khi chưa thực hiện hoàn tiền. Khách vẫn gửi được yêu cầu trả hàng. COD và MockQR giữ luồng cũ.

## Thử nghiệm

Thẻ test NCB: `9704198526191432198`, chủ thẻ `NGUYEN VAN A`, ngày phát hành `07/15`, OTP `123456`. Không dùng thông tin thẻ thật.

Các endpoint khách yêu cầu đăng nhập và kiểm tra chủ đơn:

- POST `/api/orders/{id}/vnpay/payment-url`
- GET `/api/orders/{id}/vnpay/status`
- POST `/api/orders/{id}/vnpay/reconcile`

IPN/Return không yêu cầu JWT nhưng phải kiểm tra chữ ký và dữ liệu giao dịch. IPN trả đúng `RspCode`/`Message`, không bọc ApiResponse. Có các endpoint GET status/POST reconcile tương ứng dưới `/api/admin/orders/{id}/vnpay` dành cho Admin.

## Sửa lỗi ngày 25/10/2026

- Checkout dùng phương thức đã gửi lúc đặt hàng, tách màn hình đang chuyển VNPAY khỏi kết quả COD/MockQR; không hiện thoáng qua thông báo hoặc QR sai phương thức.
- Return URL giữ nguyên origin của frontend để tránh chuyển localhost sang Cloudflare và yêu cầu đăng nhập lại.
- Bản trước chỉ xác minh Return rồi bỏ qua kết quả. Bản sửa xử lý Return hợp lệ qua cùng đường cập nhật có khóa của IPN.
- Đã xác định QUERYDR trả HTTP 403 từ chính container API ở thời điểm kiểm tra. Không bỏ qua TLS, không giả lập thành công. Luồng trả kết quả có chữ ký vẫn hoạt động khi API này bị từ chối.

Kiểm thử tự động dùng merchant/secret giả; không gọi tài khoản thật:

```powershell
dotnet test tests/ClothingAPI.UnitTests/ClothingAPI.UnitTests.csproj --filter FullyQualifiedName~VnpayTests
dotnet test ClothingAPI.slnx
```

Kiểm thử concurrency phải chạy trên SQL Server để kiểm tra `sp_getapplock`; SQLite unit test không chứng minh được hành vi khóa SQL Server.

Nguồn: [PAY/IPN](https://sandbox.vnpayment.vn/apis/docs/thanh-toan-pay/pay.html), [QUERYDR/REFUND](https://sandbox.vnpayment.vn/apis/docs/truy-van-hoan-tien/querydr%26refund.html), [Thẻ thử](https://sandbox.vnpayment.vn/apis/vnpay-demo/).
