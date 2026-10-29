# Chatbot mua sắm cục bộ

Chatbot dùng Ollama và `qwen3:1.7b` chạy trên máy chủ Docker. Không dùng API AI trả phí,
không cần API key, không tự chuyển sang cloud. Dữ liệu công khai được đọc từ database
hiện có; không có migration hoặc dữ liệu sản phẩm mẫu mới.

## Khởi động

Chạy các lệnh sau trong thư mục backend, cùng file `docker-compose.yml` và `.env` hiện tại:

```powershell
docker compose up -d ollama
docker compose exec ollama ollama pull qwen3:1.7b
docker compose up -d --build api frontend
```

Lệnh pull chỉ cần ở lần đầu hoặc khi đổi/cập nhật model. Model khoảng 1,4 GB, được lưu
trong volume `ollama_data`, không cần tải lại khi recreate container. Image Ollama cũng
chiếm thêm dung lượng đĩa. Máy triển khai hiện tại đã kiểm tra hỗ trợ GPU NVIDIA qua Docker.
Compose có `gpus: all`; nếu chuyển máy không có GPU tương thích, bỏ dòng này để chạy CPU
và đo lại tốc độ. RAM Ollama bị giới hạn 3 GB, một lượt suy luận tại một thời điểm.

Mở http://localhost:4200, nhấn **Hỏi shop** ở góc dưới bên phải. Không cần đăng nhập.

```powershell
docker compose exec ollama ollama ps
docker compose logs --tail 50 ollama
docker compose stop ollama
docker compose start ollama
```

`ollama ps` hiển thị model và mức sử dụng CPU/GPU khi model còn được nạp. Model tự nhả
bộ nhớ sau khoảng 2 phút không dùng; câu hỏi đầu tiên sau đó có thể chậm hơn.
Không dùng `docker compose down -v` nếu muốn giữ dữ liệu SQL, ảnh và model đã tải.

## Hoạt động

- Angular gọi `POST /api/chat` qua cùng origin với website trong bản Docker.
- Ollama chỉ phân loại ý định và trích xuất tên/loại sản phẩm. Giá, thương hiệu, màu, size,
  tồn kho, thẻ sản phẩm và câu trả lời dữ kiện được backend đọc/kiểm tra rồi dựng từ database.
- Màu, size và khoảng giá phải khớp cùng một biến thể đang hoạt động. Giá dùng chung
  `PriceHelper`, gồm giá riêng/giảm giá biến thể. Có thể hiển thị size hết hàng kèm nhãn rõ ràng.
- Tìm danh mục Nam/Nữ theo cây danh mục, bao gồm sản phẩm Unisex nằm trong nhánh đó.
  Chỉ lọc trường giới tính riêng khi khách hỏi rõ giới tính hoặc Unisex.
- So sánh các sản phẩm được nêu tên; câu hỏi “cái đầu tiên” dùng thứ tự thẻ hiển thị.
  Nút “Hỏi về sản phẩm đang xem” dùng slug của trang chi tiết hiện tại.
- Chính sách lấy từ `ReturnPolicy` và `ShippingHelper`; FAQ vẫn trả lời khi Ollama dừng.
- Không đọc tài khoản/đơn hàng, không thực hiện thao tác đặt/hủy/thanh toán. Không suy ra
  size theo cơ thể nếu chưa có bảng số đo riêng phù hợp.
- Lịch sử tối đa 20 tin lưu trong sessionStorage của tab. Chỉ gửi 8 tin ngắn gần nhất,
  tối đa 5 ID sản phẩm tham chiếu. Model nhận tối đa 6 tin đã rút gọn và 40 tên sản phẩm
  công khai gần nhất để giữ ngữ cảnh gọn; truy vấn và kiểm tra sản phẩm chạy trên dữ liệu
  công khai hiện tại. Chưa tối ưu cho catalog lớn hoặc tải đông khách.
- Ollama không publish cổng ra máy host/Internet. Chỉ backend gọi `http://ollama:11434`.
  `OLLAMA_NO_CLOUD=1` tắt tính năng cloud.

## Giới hạn và Cloudflare Tunnel

Tin nhắn tối đa 1.000 ký tự; request tối đa 32 KB. Endpoint chat giới hạn 30 yêu cầu/phút
theo địa chỉ kết nối mà API nhìn thấy. Khi đi qua Nginx/tunnel, nhiều khách có thể chia sẻ
hạn mức này. Không tin trực tiếp header IP do client gửi. AI xử lý một câu tại một thời điểm;
khi bận, API trả 503 để khách thử lại. Thời gian suy luận tối đa 45 giây, Angular 55 giây,
Nginx 60 giây. Không có fallback dịch vụ trả phí.

Chat trả JSON hoàn chỉnh, không dùng SSE, phù hợp với Quick Tunnel. Tunnel trỏ đến frontend
ở cổng 4200; `/api` được Nginx chuyển tới backend. Không đưa Ollama ra tunnel.
Máy chủ, Docker, Ollama và tunnel cần hoạt động để khách bên ngoài dùng chatbot.
Repo không tự tạo tunnel hoặc thay đổi cấu hình Cloudflare.

## Kiểm thử

```powershell
dotnet test tests/ClothingAPI.UnitTests/ClothingAPI.UnitTests.csproj --filter 'FullyQualifiedName~ChatTests|FullyQualifiedName~OllamaChatClientTests'
dotnet test tests/ClothingAPI.IntegrationTests/ClothingAPI.IntegrationTests.csproj --filter 'FullyQualifiedName~ChatIntegrationTests'
```

Frontend: chạy `npm test -- --watch=false --browsers=ChromeHeadless --include=src/app/layouts/storefront-layout/components/shopping-chat/shopping-chat.component.spec.ts`.
Trên máy chưa có Chrome, đặt `CHROME_BIN` thành đường dẫn Edge trước khi chạy.

`tests/manual/chatbot-smoke.cjs` kiểm thử bản production bằng Edge headless, chụp màn hình
desktop/mobile; dùng Node >=20 và biến `PLAYWRIGHT_MODULE_PATH`, `CHATBOT_TEST_OUTPUT`.
Script chỉ đọc catalog và gửi tin nhắn, không sửa sản phẩm, tài khoản hoặc đơn hàng.

Các câu hỏi mẫu:

- Tìm áo nam dưới 150 nghìn.
- Tìm áo màu trắng size M.
- Áo Polo có màu và size nào?
- So sánh Áo thun Basic với Áo Thun Trắng.
- Cái đầu tiên còn màu và size nào?
- Có cái nào rẻ hơn không?
- Phí vận chuyển / Chính sách trả hàng.

Các ca cần giữ: số tiền kiểu `150k`, `150 nghìn`, `150.000đ`; màu và size khác biến thể;
tên sản phẩm chứa từ chỉ màu; dữ liệu sản phẩm/biến thể bị tắt; không có kết quả; thiếu
bảng size; role lịch sử giả mạo; giới hạn lượt gửi; model tắt và khởi động lại.
