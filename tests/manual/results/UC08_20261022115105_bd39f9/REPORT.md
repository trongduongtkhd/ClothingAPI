# UC08 — FUNC 058.1 đến 058.40

Kết quả: 39 Passed, 1 Failed. Kiểm thử ngày 22/10/2026 trên Edge headless, frontend/API đúng image đang chạy, SQL Server thật trong database cô lập. Đăng nhập và thay đổi tồn/giá là fixture; lỗi mạng được mô phỏng ở request của trình duyệt test.

## Lỗi phát hiện

FUNC 058.39 — DEF-UC08-039: ảnh thay thế assets/images/product-placeholder.png không có trong bản build. Nginx trả HTML (HTTP 200, Content-Type text/html), ảnh có naturalWidth=0 và không hiển thị. Đã tái hiện hai lượt. Tăng số lượng và xóa dòng vẫn hoạt động.

## Dọn dữ liệu

Cả hai database UC08_20261022115105_bd39f9 và UC08_20261022115459_04532e cùng 4 container test đã được xóa. Không thay đổi database/container chính. Chỉ giữ báo cáo và ảnh làm bằng chứng.

| Testcase | Kết quả | Kết quả thực tế |
|---|---|---|
| FUNC 058.1 | Passed | Xóa Y khỏi 3 dòng: giữ X=1, Z=2; tạm tính 400.000đ, badge=3; F5 vẫn đúng. |
| FUNC 058.2 | Passed | Cả Z hết hàng và Z ngừng bán đều được giữ sau xóa X; xóa tiếp Z được, badge về 0. |
| FUNC 058.3 | Passed | Xóa dòng cuối: hiện giỏ trống, badge=0; không còn nút thanh toán; F5 giữ giỏ trống. |
| FUNC 058.4 | Passed | Chặn DELETE: có lỗi, giữ X=2/Y=1, tạm tính 350.000đ, badge=3; bỏ chặn xóa lại đúng X. |
| FUNC 058.5 | Passed | Hủy xóa toàn bộ: giữ 3 dòng, tổng lượng 6, tạm tính 850.000đ; không gửi DELETE; F5 vẫn đúng. |
| FUNC 058.6 | Passed | Token B sửa/xóa cartItemId của A đều trả 404; hai giỏ riêng giữ nguyên. |
| FUNC 058.7 | Passed | Tồn từ 10 về 2, giỏ có 5: nút − đưa về 2; tổng 200.000đ, badge=2; F5 giữ đúng. |
| FUNC 058.8 | Passed | Tồn còn 1, giỏ có 5: giảm về 1; khóa tăng/giảm tại biên 1; F5 giữ đúng. |
| FUNC 058.9 | Passed | Tồn bằng 0: cảnh báo hết hàng, khóa +/−; vẫn xóa được X và giữ Y. |
| FUNC 058.10 | Passed | Tồn đổi sau tải trang: PUT quantity=4 trả 400, tự GET tồn 2; bấm − lại lưu 2 thành công. |
| FUNC 058.11 | Passed | Ẩn sản phẩm cha: cả hai biến thể báo ngừng bán, khóa +/−; xóa một dòng giữ dòng còn lại. |
| FUNC 058.12 | Passed | Nhập kho từ 2 lên 10: hết cảnh báo; giỏ vẫn 5, tăng lên 6 rồi giảm về 5 được. |
| FUNC 058.13 | Passed | Giỏ 99 thêm 2: POST 400 với lỗi giới hạn 1–100; sau F5 vẫn 99, không sinh dòng trùng. |
| FUNC 058.14 | Passed | Giỏ 99 thêm 1: POST 201, thành 100; tiền/badge đúng; F5 vẫn 100. |
| FUNC 058.15 | Passed | Giỏ 100 thêm 1: POST 400; giữ nguyên 100 và tổng tiền. |
| FUNC 058.16 | Passed | Chạy 9 lần thêm 11 đạt 99; lần thứ 10 bị từ chối 400, giỏ giữ 99. |
| FUNC 058.17 | Passed | X=100 và Y=100 được chấp nhận; tổng lượng 200, tạm tính 25.000.000đ. |
| FUNC 058.18 | Passed | Ở 100 nút + bị khóa; giảm về 99 rồi tăng lại 100 được; API lưu 100. |
| FUNC 058.19 | Passed | Dữ liệu cũ 150 có cảnh báo; nút − sửa trực tiếp về 100; F5 đúng 100. |
| FUNC 058.20 | Passed | Hai tab ở mức 99: tab 1 thêm 1 được, tab 2 thêm tiếp bị 400; tải lại đều 100. |
| FUNC 058.21 | Passed | POST và PUT với 0, -1, 101, 2147483647: cả 8 yêu cầu trả 400; giữ số lượng 2. |
| FUNC 058.22 | Passed | POST/PUT với 1.5, "abc", số nguyên quá lớn: cả 6 yêu cầu trả 400; không làm tròn hoặc lưu sai; giỏ giữ 2. |
| FUNC 058.23 | Passed | variantId 0/-1 trả 400, ID dương không tồn tại trả 404; giỏ cũ giữ nguyên. |
| FUNC 058.24 | Passed | Cùng sản phẩm Đen–M và Be–L thành 2 dòng; sửa/xóa Đen–M không đổi Be–L=3; F5 đúng. |
| FUNC 058.25 | Passed | Tăng X: tổng 650.000→750.000đ, badge 5→6; giảm Y: tổng 600.000đ, badge=5; F5 đúng. |
| FUNC 058.26 | Passed | Đổi giá 100.000→120.000đ: tải lại X×2=240.000đ; tăng X×3=360.000đ. |
| FUNC 058.27 | Passed | Sau sửa X=3/xóa Y, điều hướng, F5 và tab mới đều chỉ có X=3. |
| FUNC 058.28 | Passed | Đăng xuất bằng UI, đăng nhập B rồi A bằng API/cookie thật trong cùng browser: giỏ và badge tách đúng. Không kiểm thử form đăng nhập/CAPTCHA. |
| FUNC 058.29 | Passed | Thiếu token và token hết hạn: 10 yêu cầu GET/POST/PUT/DELETE đều 401; giỏ X=2 giữ nguyên. |
| FUNC 058.30 | Passed | Chặn GET: hiện lỗi tải, không spinner vô hạn/giỏ trống giả; bỏ chặn F5 khôi phục X=2/Y=1. |
| FUNC 058.31 | Passed | Chặn PUT: báo lỗi, giữ X=2/tổng 200.000đ/badge=2; bỏ chặn tăng lên 3 và F5 đúng. |
| FUNC 058.32 | Passed | Giữ PUT Pending rồi bấm + 5 lần: chỉ 1 PUT, số lượng 2→3; hoàn tất nút dùng lại được. |
| FUNC 058.33 | Passed | Giữ DELETE Pending rồi bấm lặp: chỉ 1 DELETE; chỉ xóa Y, giữ X/Z; tổng 400.000đ, badge=3. |
| FUNC 058.34 | Passed | Xóa X ở tab 1, sửa X cũ ở tab 2 trả 404; F5 cả hai chỉ còn Y=1, không tạo lại X. |
| FUNC 058.35 | Passed | DELETE giỏ rỗng hai lần đều thành công; A vẫn rỗng; đã kiểm tra B vẫn giữ Y=3. |
| FUNC 058.36 | Passed | Tồn ban đầu 10; sau thêm 2, sửa lên 3, xóa dòng vẫn đúng 10, không trừ/cộng tồn. |
| FUNC 058.37 | Passed | Tồn 3: tăng 2→3 thì khóa +; giảm về 2 rồi tăng lại 3 được; dữ liệu lưu đúng. |
| FUNC 058.38 | Passed | Sang /checkout nhận X=3/Y=1, tạm tính 450.000đ; quay lại giữ giỏ; SQL xác nhận chưa tạo đơn. |
| FUNC 058.39 | Failed | DEF-UC08-039: ảnh thay thế /assets/images/product-placeholder.png không tồn tại trong bundle; URL trả 200 text/html, naturalWidth=0 nên ảnh trống. Tăng số lượng và xóa đúng dòng vẫn đạt. Đã tái hiện 2 lượt. |
| FUNC 058.40 | Passed | Giỏ rỗng: thông báo đúng, badge=0, không nút xóa/thanh toán; Khám phá sản phẩm mở /products; thêm 1 và mở giỏ mới đúng. |
