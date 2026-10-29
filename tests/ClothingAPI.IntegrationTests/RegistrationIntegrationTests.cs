using System.Net;
using System.Net.Http.Json;
using ClothingAPI.DTOs.Auth;
using ClothingAPI.Exceptions;
using ClothingAPI.Data;
using ClothingAPI.Models;
using ClothingAPI.Services.Implementations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using static ClothingAPI.IntegrationTests.TestHelpers;

namespace ClothingAPI.IntegrationTests;

public class RegistrationIntegrationTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    private static RegisterDto Valid(string email) => new()
    {
        FullName = "Nguyễn Văn An", Email = email, PhoneNumber = "0901234567",
        Password = DefaultPassword, ConfirmPassword = DefaultPassword, CaptchaToken = FakeTurnstileService.PassToken
    };

    [Theory]
    [InlineData("PhoneNumber", "letters")] [InlineData("PhoneNumber", "1")]
    [InlineData("PhoneNumber", "09012345678")] [InlineData("PhoneNumber", "0212345678")]
    [InlineData("FullName", "Nguyen123")] [InlineData("FullName", "   ")]
    [InlineData("FullName", "Nguyen\u200BAn")] [InlineData("Email", "12345")]
    [InlineData("Email", "an..test@example.com")] [InlineData("Email", "an@example.com\n")]
    [InlineData("Password", "123456")] [InlineData("Password", "test@1234")]
    [InlineData("ConfirmPassword", "Different@1234")]
    public async Task InvalidData_IsRejectedByApi_WithoutCreatingUser(string field, string value)
    {
        using var client = factory.CreateClient();
        var dto = Valid(UniqueEmail("registration"));
        typeof(RegisterDto).GetProperty(field)!.SetValue(dto, value);
        if (field == "Password") dto.ConfirmPassword = value;
        var response = await client.PostAsJsonAsync("/api/auth/register", dto);
        var body = await ReadJsonAsync(response);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(body.GetProperty("errors").TryGetProperty(field, out _));
        Assert.False(await WithDbAsync(factory, db => db.Users.AnyAsync(u => u.Email == dto.Email)));
    }

    [Fact]
    public async Task Registration_NormalizesContactData_AndAllowsSameNamesAndContactPhones()
    {
        using var client = factory.CreateClient();
        var firstEmail = UniqueEmail("normalized");
        var rawPayload = new { fullName = "  Nguye\u0302\u0303n   Văn An  ", phoneNumber = "+84 901 234 567",
            email = "  " + firstEmail.ToUpperInvariant() + "  ", password = DefaultPassword,
            confirmPassword = DefaultPassword, captchaToken = FakeTurnstileService.PassToken };
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/auth/register", rawPayload)).StatusCode);
        var saved = await WithDbAsync(factory, db => db.Users.AsNoTracking().SingleAsync(u => u.Email == firstEmail));
        Assert.Equal("Nguyễn Văn An", saved.FullName); Assert.Equal("0901234567", saved.PhoneNumber);
        var second = Valid(UniqueEmail("samecontact"));
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/auth/register", second)).StatusCode);
        var numeric = Valid(DateTime.UtcNow.Ticks + "@example.com");
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/auth/register", numeric)).StatusCode);
    }

    [Fact]
    public async Task EmailRace_AfterExistenceCheck_UniqueIndexRejectsSecondInsert_WithBusinessError()
    {
        using var client = factory.CreateClient(); // Initialize factory/schema.
        using var scope = factory.Services.CreateScope();
        var email = UniqueEmail("race");
        var interceptor = new BeforeSaveOnce(async () =>
        {
            // Simulate a competing request committing after RegisterAsync's AnyAsync check.
            await using var winnerDb = factory.CreateDbContext();
            var winner = new User { FullName = "Khách thắng", Email = email, PasswordHash = "unused-test-hash", IsActive = true };
            winner.UserRoles.Add(new UserRole { RoleId = 2 });
            winnerDb.Users.Add(winner); await winnerDb.SaveChangesAsync();
        });
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(factory.Connection).AddInterceptors(interceptor).Options);
        var service = ActivatorUtilities.CreateInstance<AuthService>(scope.ServiceProvider, db);
        var error = await Assert.ThrowsAsync<BadRequestException>(() => service.RegisterAsync(Valid(email)));
        Assert.Equal("Email này đã được sử dụng.", error.Message);
        Assert.Equal(1, await WithDbAsync(factory, query => query.Users.CountAsync(u => u.Email == email)));
    }

    [Fact]
    public async Task UnrelatedDatabaseFailure_IsNotMisreportedAsDuplicateEmail()
    {
        using var client = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        var interceptor = new BeforeSaveOnce(() => throw new DbUpdateException("Unrelated test failure"));
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(factory.Connection).AddInterceptors(interceptor).Options);
        var service = ActivatorUtilities.CreateInstance<AuthService>(scope.ServiceProvider, db);
        await Assert.ThrowsAsync<DbUpdateException>(() => service.RegisterAsync(Valid(UniqueEmail("dbfailure"))));
    }

    private sealed class BeforeSaveOnce(Func<Task> beforeSave) : SaveChangesInterceptor
    {
        private bool _ran;
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!_ran) { _ran = true; await beforeSave(); }
            return result;
        }
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task UsedEmail_CannotRegisterAgain_EvenWithDifferentCaseSpacesOrInactiveAccount(bool active)
    {
        using var client = factory.CreateClient();
        var dto = Valid(UniqueEmail("duplicate"));
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/auth/register", dto)).StatusCode);
        await using (var db = factory.CreateDbContext())
        {
            var user = await db.Users.SingleAsync(u => u.Email == dto.Email);
            user.IsActive = active; await db.SaveChangesAsync();
        }
        var duplicate = Valid("  " + dto.Email.ToUpperInvariant() + "  ");
        var response = await client.PostAsJsonAsync("/api/auth/register", duplicate);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Email này đã được sử dụng.", (await ReadJsonAsync(response)).GetProperty("message").GetString());
        Assert.Equal(1, await WithDbAsync(factory, db => db.Users.CountAsync(u => u.Email == dto.Email)));
    }

    [Theory]
    [InlineData("/api/auth/reset-password", "confirmPassword")]
    [InlineData("/api/users/change-password", "confirmNewPassword")]
    public async Task WeakNewPassword_IsRejectedAtPasswordEntryPoints(string endpoint, string confirmationField)
    {
        using var client = factory.CreateClient();
        var customer = await RegisterAndLoginCustomerAsync(client, "passwordpolicy");
        Authorize(client, customer.AccessToken);
        var payload = new Dictionary<string, string>
        {
            ["token"] = "invalid-token", ["currentPassword"] = DefaultPassword,
            ["newPassword"] = "123456", [confirmationField] = "123456"
        };
        var response = endpoint.StartsWith("/api/users/", StringComparison.Ordinal)
            ? await client.PutAsJsonAsync(endpoint, payload)
            : await client.PostAsJsonAsync(endpoint, payload);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await ReadJsonAsync(response)).GetProperty("errors").TryGetProperty("NewPassword", out _));
    }
}
