using ClothingAPI.Exceptions;
using ClothingAPI.Helpers;
using System.Net;
using System.Text.Json;

namespace ClothingAPI.Middleware
{
    public class ExceptionMiddleware
    {
        // Phải khớp camelCase với JSON mà controller trả về (mặc định của ASP.NET Core),
        // nếu không frontend (đang đọc "message" chữ thường) sẽ không thấy nội dung lỗi thật.
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        private readonly RequestDelegate _next;
        private readonly ILogger<ExceptionMiddleware> _logger;

        public ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                await _next(context);
            }
            catch (Exception exception)
            {
             

                var (statusCode, message) = exception switch
                {
                    BadRequestException => (HttpStatusCode.BadRequest, exception.Message),
                    ConflictException => (HttpStatusCode.Conflict, exception.Message),
                    
                    UnauthorizedException => (HttpStatusCode.Unauthorized, exception.Message),
                    
                    NotFoundException => (HttpStatusCode.NotFound, exception.Message),

                    _ => (
                        HttpStatusCode.InternalServerError,
                        "Đã xảy ra lỗi hệ thống. Vui lòng thử lại sau."
                    )
                };
                  if (statusCode == HttpStatusCode.InternalServerError)
            {
                   // Lỗi ngoài dự kiến: cần stack trace để điều tra.
                  _logger.LogError(exception, "Đã xảy ra lỗi: {Message}", exception.Message);
            }
           else
        {
        // Lỗi nghiệp vụ dự kiến (sai mật khẩu, không tìm thấy...): ghi gọn, không kèm stack trace.
        // Sự kiện bảo mật đã được ghi ở tầng service rồi.
        _logger.LogInformation("Trả {StatusCode}: {Message}", (int)statusCode, exception.Message);
         }

                context.Response.ContentType = "application/json";
                context.Response.StatusCode = (int)statusCode;

                var response = new ApiResponse<object>(
                    false,
                    message
                );

                await context.Response.WriteAsync(
                    JsonSerializer.Serialize(response, JsonOptions)
                );
            }
        }
    }
}
