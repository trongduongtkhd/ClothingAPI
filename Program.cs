using ClothingAPI.Data;
using ClothingAPI.Helpers;
using ClothingAPI.Middleware;
using ClothingAPI.Services.Implementations;
using ClothingAPI.Services.Interfaces;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using System.Text;
namespace ClothingAPI
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

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

          

            builder.Services.AddCors(options =>
            {
                options.AddPolicy("AngularApp", policy =>
                {
                    policy.AllowAnyOrigin()
                          .AllowAnyHeader()
                          .AllowAnyMethod();
                });
            });
            builder.Services.AddScoped<IAuthService, AuthService>();
            builder.Services.AddScoped<ITokenService, TokenService>();
            builder.Services.AddScoped<ICategoryService, CategoryService>();
            builder.Services.AddScoped<IBrandService, BrandService>();
            builder.Services.AddScoped<IColorService, ColorService>();
            builder.Services.AddScoped<ISizeService, SizeService>();
            builder.Services.AddScoped<IProductService, ProductService>();
            builder.Services.AddScoped<IStorefrontService, StorefrontService>();
            builder.Services.AddScoped<IUserProfileService, UserProfileService>();
            builder.Services.AddScoped<IAddressService, AddressService>();
            builder.Services.AddScoped<ICartService, CartService>();
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

            app.UseHttpsRedirection();
            app.UseStaticFiles();
            app.UseCors("AngularApp");
            app.UseAuthentication();
            app.UseAuthorization();


            app.MapControllers();

            app.Run();
        }
    }
}
