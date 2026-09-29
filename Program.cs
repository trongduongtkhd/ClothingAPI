using ClothingAPI.Data;
using ClothingAPI.Helpers;
using ClothingAPI.Middleware;
using ClothingAPI.Models;
using ClothingAPI.Services.Implementations;
using ClothingAPI.Services.Interfaces;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Serilog;
using System.Text;
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
                          .AllowAnyMethod();
                });
            });
            builder.Services.AddScoped<IAuthService, AuthService>();
            builder.Services.AddScoped<ITokenService, TokenService>();
            builder.Services.AddScoped<IEmailService, EmailService>();
            builder.Services.AddScoped<ICategoryService, CategoryService>();
            builder.Services.AddScoped<IBrandService, BrandService>();
            builder.Services.AddScoped<IColorService, ColorService>();
            builder.Services.AddScoped<ISizeService, SizeService>();
            builder.Services.AddScoped<IProductService, ProductService>();
            builder.Services.AddScoped<IStorefrontService, StorefrontService>();
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
            app.UseStaticFiles();
            app.UseCors("AngularApp");
            app.UseAuthentication();
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
