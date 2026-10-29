using ClothingAPI.Data;
using ClothingAPI.Helpers;
using ClothingAPI.Middleware;
using ClothingAPI.Models;
using ClothingAPI.Services.Implementations;
using ClothingAPI.Services.Interfaces;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Serilog;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
namespace ClothingAPI
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Ghi log ra cả Console (xem qua `docker logs`) và ra file thật
            // (xoay vòng theo ngày) để demo/kiểm tra sau này - đặc biệt cho
            // các sự kiện bảo mật như đăng nhập sai / khoá tài khoản.
        builder.Host.UseSerilog((context, configuration) =>
{
    configuration
        .MinimumLevel.Information()
        // Code của mình giữ Information, nhưng tắt bớt log hạ tầng:
        // SQL của EF Core và vòng đời request của ASP.NET làm chìm sự kiện bảo mật.
        .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
        .MinimumLevel.Override("System", Serilog.Events.LogEventLevel.Warning)
        .WriteTo.Console()
        .WriteTo.File(
            "Logs/log-.txt",
            rollingInterval: RollingInterval.Day,
            outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level:u3}] {Message:lj}{NewLine}{Exception}"
        );
});

            // Add services to the container.

            builder.Services.AddControllers()
          .AddJsonOptions(options =>
          {
           options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()
          );
    });
            // Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi

            builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlServer
            (
            builder.Configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Không tìm thấy ConnectionStrings:DefaultConnection trong appsettings.json.")
            )
            );
            builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection("JwtSettings"));
            var jwtSettings = builder.Configuration.GetSection("JwtSettings").Get<JwtSettings>()
           ?? throw new InvalidOperationException("Không tìm thấy JwtSettings trong appsettings.json.");

            if (string.IsNullOrWhiteSpace(jwtSettings.SecretKey) || jwtSettings.SecretKey.Length < 32)
            {
                throw new InvalidOperationException(
                    "JwtSettings:SecretKey chưa được cấu hình hoặc quá ngắn (cần >= 32 ký tự). " +
                    "Ở local dev, đặt qua User Secrets: dotnet user-secrets set \"JwtSettings:SecretKey\" \"<secret>\". " +
                    "Ở Docker, đặt qua biến môi trường JWT_SECRET_KEY trong file .env."
                );
            }

            builder.Services.Configure<EmailSettings>(builder.Configuration.GetSection("EmailSettings"));
            builder.Services.Configure<FrontendSettings>(builder.Configuration.GetSection("FrontendSettings"));
            builder.Services.AddOptions<VnpaySettings>()
                .Bind(builder.Configuration.GetSection("VnpaySettings"))
                .Validate(x => !x.Enabled || (x.IsConfigured && x.PaymentMinutes is >= 1 and <= 60
                    && x.OrderMinutes >= x.PaymentMinutes
                    && x.PaymentUrl == "https://sandbox.vnpayment.vn/paymentv2/vpcpay.html"
                    && x.TransactionApiUrl == "https://sandbox.vnpayment.vn/merchant_webapi/api/transaction"),
                    "Cấu hình VNPAY Sandbox không hợp lệ hoặc thiếu thông tin kết nối.")
                .ValidateOnStart();
            builder.Services.AddSingleton(TimeProvider.System);
            builder.Services.AddHttpClient("vnpay", client => client.Timeout = TimeSpan.FromSeconds(15));
            builder.Services.AddScoped<IVnpayService, VnpayService>();
            builder.Services.AddHostedService<VnpayExpiryWorker>();
            builder.Services.Configure<RefreshTokenSettings>(builder.Configuration.GetSection("RefreshTokenSettings"));

            builder.Services.Configure<TwoFactorSettings>(builder.Configuration.GetSection("TwoFactorSettings"));
            var twoFactorSettings = builder.Configuration.GetSection("TwoFactorSettings").Get<TwoFactorSettings>()
                ?? new TwoFactorSettings();

            if (string.IsNullOrWhiteSpace(twoFactorSettings.RecoveryCodePepper) || twoFactorSettings.RecoveryCodePepper.Length < 32)
            {
                throw new InvalidOperationException(
                    "TwoFactorSettings:RecoveryCodePepper chưa được cấu hình hoặc quá ngắn (cần >= 32 ký tự). " +
                    "Ở local dev, đặt qua User Secrets: dotnet user-secrets set \"TwoFactorSettings:RecoveryCodePepper\" \"<pepper>\". " +
                    "Ở Docker, đặt qua biến môi trường TWO_FACTOR_RECOVERY_PEPPER trong file .env. " +
                    "Lưu ý: đổi pepper sẽ làm mọi mã khôi phục 2FA đã phát mất hiệu lực."
                );
            }

            builder.Services.Configure<TurnstileSettings>(builder.Configuration.GetSection("TurnstileSettings"));
            var turnstileSettings = builder.Configuration.GetSection("TurnstileSettings").Get<TurnstileSettings>()
                ?? new TurnstileSettings();

            if (turnstileSettings.Enabled && string.IsNullOrWhiteSpace(turnstileSettings.SecretKey))
            {
                throw new InvalidOperationException(
                    "TurnstileSettings:SecretKey chưa được cấu hình (CAPTCHA đang bật). " +
                    "Ở local dev, đặt qua User Secrets: dotnet user-secrets set \"TurnstileSettings:SecretKey\" \"<secret>\". " +
                    "Ở Docker, đặt qua biến môi trường TURNSTILE_SECRET_KEY trong file .env. " +
                    "Chỉ khi dev/test mới tắt bằng TurnstileSettings:Enabled = false (TURNSTILE_ENABLED=false)."
                );
            }

            // HttpClient riêng cho Cloudflare Turnstile: quá TimeoutSeconds coi như không gọi được.
            builder.Services.AddHttpClient(TurnstileService.HttpClientName, client =>
            {
                client.Timeout = TimeSpan.FromSeconds(turnstileSettings.TimeoutSeconds);
            });

            // Mã hóa secret TOTP trong DB. Cố định ApplicationName để key ring không phụ thuộc đường dẫn cài đặt;
            // thư mục key giữ mặc định (/root/.aspnet/DataProtection-Keys trong Docker, đã gắn volume dataprotection_data).
            builder.Services.AddDataProtection().SetApplicationName("ClothingAPI");

            // Nguồn thời gian dùng chung (test thay bằng đồng hồ giả để kiểm soát bước TOTP, hạn challenge).
            builder.Services.AddSingleton(TimeProvider.System);

            builder.Services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme =
                    JwtBearerDefaults.AuthenticationScheme;

                options.DefaultChallengeScheme =
                    JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,

                    ValidIssuer = jwtSettings.Issuer,
                    ValidAudience = jwtSettings.Audience,

                    IssuerSigningKey = new SymmetricSecurityKey(
                        Encoding.UTF8.GetBytes(jwtSettings.SecretKey)
                    ),

                    ClockSkew = TimeSpan.Zero
                };
            });



            builder.Services.AddAuthorization();

          

            var allowedOrigins = (builder.Configuration["Cors:AllowedOrigins"] ?? "http://localhost:4200")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            builder.Services.AddCors(options =>
            {
                options.AddPolicy("AngularApp", policy =>
                {
                    policy.WithOrigins(allowedOrigins)
                          .AllowAnyHeader()
                          .AllowAnyMethod()
                          // Cho phép trình duyệt gửi/nhận cookie refresh token khi frontend khác origin (localhost:4200 -> :8080).
                          // Hợp lệ vì đã whitelist origin cụ thể (không dùng AllowAnyOrigin).
                          .AllowCredentials();
                });
            });
            builder.Services.AddScoped<IAuthService, AuthService>();
            builder.Services.AddScoped<ITokenService, TokenService>();
            builder.Services.AddScoped<IRefreshTokenService, RefreshTokenService>();
            builder.Services.AddScoped<ITwoFactorService, TwoFactorService>();
            builder.Services.AddScoped<ITurnstileService, TurnstileService>();
            builder.Services.AddScoped<IEmailService, EmailService>();
            builder.Services.AddScoped<ICategoryService, CategoryService>();
            builder.Services.AddScoped<IBrandService, BrandService>();
            builder.Services.AddScoped<IColorService, ColorService>();
            builder.Services.AddScoped<ISizeService, SizeService>();
            builder.Services.AddScoped<IProductService, ProductService>();
            builder.Services.AddScoped<IStorefrontService, StorefrontService>();
            builder.Services.Configure<ChatbotOptions>(builder.Configuration.GetSection("Chatbot"));
            builder.Services.AddSingleton<ChatInferenceGate>();
            builder.Services.AddHttpClient<IChatIntentClient, OllamaChatIntentClient>((services, client) =>
            {
                var options = services.GetRequiredService<Microsoft.Extensions.Options.IOptions<ChatbotOptions>>().Value;
                client.BaseAddress = new Uri(options.OllamaUrl.TrimEnd('/') + "/");
                client.Timeout = Timeout.InfiniteTimeSpan;
            });
            builder.Services.AddScoped<IChatService, ChatService>();
            builder.Services.AddRateLimiter(options =>
            {
                options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
                options.AddPolicy("chat", context => RateLimitPartition.GetSlidingWindowLimiter(
                    context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new SlidingWindowRateLimiterOptions
                    {
                        PermitLimit = 30, Window = TimeSpan.FromMinutes(1), SegmentsPerWindow = 6,
                        QueueLimit = 0, AutoReplenishment = true
                    }));
                options.OnRejected = async (context, token) =>
                {
                    context.HttpContext.Response.Headers.RetryAfter = "60";
                    await context.HttpContext.Response.WriteAsJsonAsync(new ApiResponse<object>(false,
                        "Có quá nhiều câu hỏi trong một phút. Bạn chờ một chút rồi thử lại nhé."), token);
                };
            });
            builder.Services.AddScoped<IUserProfileService, UserProfileService>();
            builder.Services.AddScoped<IAddressService, AddressService>();
            builder.Services.AddScoped<ICartService, CartService>();
            builder.Services.AddScoped<IWishlistService, WishlistService>();
            builder.Services.AddScoped<ISupplierService, SupplierService>();
            builder.Services.AddScoped<IPurchaseOrderService, PurchaseOrderService>();
            builder.Services.AddScoped<ICouponService, CouponService>();
            builder.Services.AddScoped<IOrderService, OrderService>();
            builder.Services.AddScoped<IAdminOrderService, AdminOrderService>();
            builder.Services.AddScoped<IReviewService, ReviewService>();
            builder.Services.AddScoped<IAdminUserService, AdminUserService>();
            builder.Services.AddScoped<IAdminDashboardService, AdminDashboardService>();

            builder.Services.AddSwaggerGen(options =>
            {
                options.SwaggerDoc("v1", new OpenApiInfo
                {
                    Title = "Clothing Store API",
                    Version = "v1",
                    Description = "API cho website bán quần áo"
                });

                options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
                {
                    Name = "Authorization",
                    Type = SecuritySchemeType.Http,
                    Scheme = "bearer",
                    BearerFormat = "JWT",
                    In = ParameterLocation.Header,
                    Description = "Nhập JWT token vào ô bên dưới. Không cần gõ chữ Bearer."
                });

                options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
                {
                    [new OpenApiSecuritySchemeReference("Bearer", document)] = []
                });
            });

            var app = builder.Build();

            if (!turnstileSettings.Enabled)
            {
                app.Logger.LogWarning(
                    "CAPTCHA (Cloudflare Turnstile) đang TẮT (TurnstileSettings:Enabled = false): " +
                    "đăng nhập và đăng ký KHÔNG được bảo vệ khỏi bot. Chỉ dùng khi dev/test.");
            }

            using (var scope = app.Services.CreateScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                dbContext.Database.Migrate();

                await SeedAdminAsync(dbContext, app.Configuration);
            }
            app.UseMiddleware<ExceptionMiddleware>();

            // Configure the HTTP request pipeline.
            app.UseSwagger();

            app.UseSwaggerUI(options =>
            {
                options.SwaggerEndpoint(
                    "/swagger/v1/swagger.json",
                    "Clothing Store API v1"
                );

                options.RoutePrefix = "swagger";
            });

            //app.UseHttpsRedirection();
            if (!app.Environment.IsEnvironment("Docker"))
            {
                app.UseHttpsRedirection();
            }
            app.UseMiddleware<OptimizedImageMiddleware>();
            app.UseStaticFiles(new StaticFileOptions
            {
                OnPrepareResponse = context =>
                {
                    if (context.Context.Request.Path.StartsWithSegments("/uploads"))
                        context.Context.Response.Headers.CacheControl = "public, max-age=604800";
                }
            });
            app.UseCors("AngularApp");
            app.UseAuthentication();
            app.UseRateLimiter();
            app.UseAuthorization();


            app.MapControllers();

            await app.RunAsync();
        }

        private static async Task SeedAdminAsync(AppDbContext context, IConfiguration configuration)
        {
            var hasAdmin = await context.Users
                .AnyAsync(u => u.UserRoles.Any(ur => ur.Role.RoleName == "Admin"));

            if (hasAdmin)
            {
                return;
            }

            var adminEmail = configuration["AdminSeedSettings:Email"]?.Trim().ToLower();
            var adminPassword = configuration["AdminSeedSettings:Password"];

            if (string.IsNullOrWhiteSpace(adminEmail) || string.IsNullOrWhiteSpace(adminPassword))
            {
                return;
            }

            var adminRole = await context.Roles.FirstOrDefaultAsync(r => r.RoleName == "Admin");

            if (adminRole is null)
            {
                return;
            }

            var existingUser = await context.Users
                .Include(u => u.UserRoles)
                .FirstOrDefaultAsync(u => u.Email == adminEmail);

            if (existingUser is not null)
            {
                if (!existingUser.UserRoles.Any(ur => ur.RoleId == adminRole.RoleId))
                {
                    existingUser.UserRoles.Add(new UserRole { RoleId = adminRole.RoleId });
                    await context.SaveChangesAsync();
                }

                return;
            }

            var admin = new User
            {
                FullName = "Administrator",
                Email = adminEmail,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(adminPassword),
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
            };

            admin.UserRoles.Add(new UserRole { RoleId = adminRole.RoleId });

            context.Users.Add(admin);
            await context.SaveChangesAsync();
        }
    }
}
