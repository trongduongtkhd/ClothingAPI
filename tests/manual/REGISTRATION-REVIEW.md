# Rà soát và sửa validation đăng ký

Phạm vi: Angular + ASP.NET Core API; đăng ký, sửa hồ sơ, đổi và đặt lại mật khẩu. Giữ chính sách mật khẩu hiện có theo lựa chọn của chủ dự án.

| Trường | Quy tắc áp dụng |
| --- | --- |
| Họ tên | Bắt buộc; 2–150 đơn vị UTF-16 sau chuẩn hóa NFC và khoảng trắng; có ít nhất 2 chữ cái Unicode; cho phép dấu tiếng Việt, khoảng trắng thông thường, dấu nháy `'`/`’` hoặc gạch nối giữa các phần tên; không số, emoji, HTML, ký tự ẩn/điều khiển. Không bắt buộc có hai từ. |
| Điện thoại | Không bắt buộc theo thiết kế hiện tại; khi có thì là số di động Việt Nam 10 chữ số, đầu 03/05/07/08/09; nhận +84 và dấu cách/chấm/gạch nối giữa nhóm số; lưu về dạng 0xxxxxxxxx. Không nhận chữ, số âm, dư/thiếu chữ số hay định dạng số quốc tế khác. |
| Email | Bắt buộc; địa chỉ ASCII thông dụng, tối đa 254 ký tự, phần trước @ tối đa 64, nhãn domain tối đa 63, có đuôi domain chữ cái ít nhất 2 ký tự; không khoảng trắng nội bộ, chấm liên tiếp/đầu/cuối local part hoặc domain sai. Trim khoảng trắng thông thường ở hai đầu, lowercase invariant. |
| Mật khẩu | Ít nhất 8 đơn vị UTF-16; ký tự đầu là chữ hoa Unicode, ít nhất 1 chữ số ASCII 0–9 và 1 dấu câu/ký hiệu. Không ký tự điều khiển/ẩn. Không quá 72 byte UTF-8 vì đang dùng BCrypt. Không trim, cắt hay chuẩn hóa mật khẩu ngầm. |
| Xác nhận mật khẩu | Bắt buộc, phải khớp chính xác, kể cả khoảng trắng. |
| CAPTCHA | Vẫn bắt buộc trên đăng ký thật; kiểm tra API, không chỉ giao diện. |

Độ dài tên/mật khẩu được tính thống nhất với JavaScript/.NET hiện tại; ký tự ngoài BMP dùng hai đơn vị UTF-16. Giới hạn mật khẩu UTF-8 được kiểm tra bằng byte thực tế ở cả hai bên, nên 72 ký tự có dấu có thể vượt giới hạn dù 72 ký tự ASCII thì không.

## Trùng tài khoản

Dự án dùng **email để đăng nhập**, không có trường username riêng. `FullName` là tên hiển thị, không phải username. Người trùng họ tên hoặc dùng chung số liên hệ vẫn được đăng ký; không đặt unique index lên họ tên hay số điện thoại chưa xác minh.

Backend đã có truy vấn kiểm tra email và unique index `IX_Users_Email`. Giữ index đó làm ràng buộc cuối cùng. Bổ sung xử lý trường hợp hai request vượt qua bước kiểm tra trước khi lưu: khi EF báo lỗi lưu, tra lại email đã tồn tại và trả HTTP 400 `Email này đã được sử dụng.`; không biến lỗi database khác thành lỗi trùng email. Email khác hoa/thường hoặc có khoảng trắng thông thường hai đầu là cùng tài khoản. Tài khoản bị khóa/ngừng hoạt động vẫn chiếm email; phải khôi phục tài khoản cũ thay vì tạo tài khoản mới bằng email đó.

Không tự xóa dấu chấm hoặc phần `+tag` của email: đây là quy tắc riêng từng nhà cung cấp, không an toàn nếu áp dụng cho mọi domain.

## Những dữ liệu không phải bug

- `238420@gmail.com` có thể hợp lệ: local part toàn số được phép. `123456` không có @ và domain thì bị từ chối.
- `An`, `O'Connor`, `Anne-Marie`, `Nguyễn Văn An` là tên phù hợp chính sách. Không ép tên phải có dấu hoặc có ít nhất hai từ.
- Một chuỗi toàn chữ như `vbvbdfvbdfv` có thể vượt kiểm tra định dạng tên. Regex không xác minh được đó có phải tên thật; muốn xác minh danh tính cần quy trình riêng.
- Số điện thoại/email đúng định dạng không chứng minh nó tồn tại, đang hoạt động hoặc thuộc về người đăng ký. Muốn kiểm tra quyền sở hữu cần SMS OTP/email xác minh. Lần sửa này không thêm dịch vụ gửi OTP/email xác minh.

## Mật khẩu và giới hạn phạm vi

Giữ quy tắc 8 ký tự/chữ đầu hoa/số/ký tự đặc biệt theo lựa chọn của chủ dự án; đây là chính sách nghiệp vụ hiện tại, không tuyên bố đạt toàn bộ khuyến nghị OWASP/NIST mới. Khuyến nghị hiện nay ưu tiên mật khẩu dài và sàng lọc mật khẩu phổ biến hơn là ép vị trí chữ hoa. Đổi và quên mật khẩu trước đây chỉ yêu cầu 6 ký tự; nay dùng cùng bộ quy tắc với đăng ký để không thể đi đường vòng.

Nguồn đối chiếu:

- [OWASP Authentication](https://cheatsheetseries.owasp.org/cheatsheets/Authentication_Cheat_Sheet.html)
- [OWASP Password Storage — giới hạn BCrypt](https://cheatsheetseries.owasp.org/cheatsheets/Password_Storage_Cheat_Sheet.html#input-limits-of-bcrypt)
- [OWASP Email Validation and Verification](https://cheatsheetseries.owasp.org/cheatsheets/Email_Validation_and_Verification_Cheat_Sheet.html)
- [ITU — Vietnam numbering plan](https://www.itu.int/oth/T02020000E4/en)

## Bộ kiểm thử hồi quy

- `RegistrationValidationTests`: dữ liệu sai, chuẩn hóa Unicode/khoảng trắng, biên độ dài tên/email/byte mật khẩu, đối chiếu chính sách đăng ký/sửa hồ sơ/đổi/đặt lại mật khẩu.
- `RegistrationIntegrationTests`: gọi HTTP trực tiếp để tránh chỉ kiểm tra phía trình duyệt; dữ liệu sai không tạo user; email trùng kể cả tài khoản inactive; lỗi unique index do request khác thắng sau kiểm tra; lỗi DB khác phải được giữ nguyên.
- Angular `account-validation.util.spec.ts`: các nhóm dữ liệu tương ứng phía trình duyệt.
- Angular `register.component.spec.ts`: hiển thị lỗi tại trường, chặn request sai, chuẩn hóa payload, chặn submit lặp, xác nhận mật khẩu/CAPTCHA và thông báo email trùng.

## Kết quả kiểm chứng ngày 22/10/2026

- Backend unit tests: 227/227 đạt (toàn bộ project unit test).
- Backend integration tests: 45/45 đạt (toàn bộ project integration test, chạy bằng tài khoản Windows hiện tại để các ca Data Protection/2FA truy cập được kho khóa).
- Angular: 81/81 ca liên quan validation tài khoản và form đăng ký đạt trong Edge headless.
- Angular build development và production đều thành công. Vẫn có cảnh báo budget/CSS sẵn có và cảnh báo package backend; chưa xử lý trong phạm vi này.
- Docker local đã cập nhật sang `clothing-store-api:registration` và `clothing-store-frontend:registration` qua override `docker-compose.registration.yml`; môi trường API khớp container trước khi cập nhật. Không migration hoặc sửa dữ liệu cũ.
- Kiểm tra sau cập nhật tại `http://localhost:8080/api/auth/register` và qua Nginx `http://localhost:4200/api/auth/register`: payload sai họ tên/điện thoại/email/mật khẩu đều trả 400 với lỗi đúng từng trường. Không tạo user. Xem `registration-smoke-result.json`.

Trang kiểm tra: `http://localhost:4200/auth/register`; tải lại trang để nhận bundle mới. Không thay đổi hay tự động làm sạch các tài khoản/dữ liệu cũ đã lưu.

Lưu ý dữ liệu JMeter hiện tại: tên như `Khach Kiem Thu 01` trong `D:\KiemThuTai\accounts.csv` không phù hợp quy tắc tên mới. Đổi dữ liệu kiểm thử sang tên không có chữ số trước khi dùng để đăng ký. File CSV ngoài repo chưa bị sửa.
