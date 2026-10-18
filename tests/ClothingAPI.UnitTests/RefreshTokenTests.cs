using ClothingAPI.DTOs.Auth;
using ClothingAPI.Helpers;
using ClothingAPI.Models;
using ClothingAPI.Services.Implementations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit.Abstractions;

namespace ClothingAPI.UnitTests;

/// RefreshTokenService: cấp token (chỉ lưu hash), xoay vòng (rotation), phát hiện dùng lại (reuse detection), thu hồi.
/// "Thời gian" được giả lập bằng cách sửa trực tiếp ExpiresAt / RevokedAt trong dữ liệu.
public class RefreshTokenTests : UnitTestBase
{
    private const string Ip = "127.0.0.1";
    private const string UserAgent = "UnitTest/1.0";

    public RefreshTokenTests(ITestOutputHelper output) : base(output) { }

    private RefreshTokenService NewService(AppDbContextHolder holder, int reuseGraceSeconds = 10) =>
        new(holder.Context,
            Options.Create(new RefreshTokenSettings { ExpirationDays = 7, ReuseGraceSeconds = reuseGraceSeconds }),
            NullLogger<RefreshTokenService>.Instance);

    private async Task<IssuedRefreshToken> IssueAsync(int userId)
    {
        await using var holder = new AppDbContextHolder(Db);
        return await NewService(holder).IssueAsync(userId, Ip, UserAgent);
    }

    private async Task<RefreshRotationResult> RotateAsync(string? rawToken, int reuseGraceSeconds = 10)
    {
        await using var holder = new AppDbContextHolder(Db);
        return await NewService(holder, reuseGraceSeconds).RotateAsync(rawToken, Ip, UserAgent);
    }

    private RefreshToken Load(string rawToken)
    {
        var hash = RefreshTokenService.HashToken(rawToken);
        return Db.Query(db => db.RefreshTokens.AsNoTracking().Single(x => x.TokenHash == hash));
    }

    private List<RefreshToken> LoadUserTokens(int userId) =>
        Db.Query(db => db.RefreshTokens.AsNoTracking().Where(x => x.UserId == userId).OrderBy(x => x.RefreshTokenId).ToList());

    private void Update(string rawToken, Action<RefreshToken> change)
    {
        var hash = RefreshTokenService.HashToken(rawToken);
        using var db = Db.NewContext();
        change(db.RefreshTokens.Single(x => x.TokenHash == hash));
        db.SaveChanges();
    }

    private static string DescribeToken(RefreshToken t) =>
        $"#{t.RefreshTokenId} family {t.FamilyId.ToString()[..8]} " +
        (t.RevokedAt.HasValue ? $"đã thu hồi ({t.RevokedReason})" : "còn hiệu lực") +
        (t.ReplacedByTokenId.HasValue ? $", thay bởi #{t.ReplacedByTokenId}" : "");

    private void ReportTokens(string outcome, int userId) =>
        Actual($"{outcome}; token trong DB: {string.Join("; ", LoadUserTokens(userId).Select(DescribeToken))}");

    [Fact]
    public async Task UT089_CapRefreshToken_ChiLuuHash()
    {
        Step1("Khách hàng đăng nhập thành công; hệ thống cấp refresh token cho phiên mới");

        var before = DateTime.UtcNow;
        var issued = await IssueAsync(Db.CustomerId);
        var tokens = LoadUserTokens(Db.CustomerId);
        var saved = tokens.Single();
        Actual($"token gốc dài {issued.RawToken.Length} ký tự; DB lưu TokenHash = {saved.TokenHash[..12]}... (dài {saved.TokenHash.Length}); " +
               $"có lưu bản gốc: {tokens.Any(t => t.TokenHash == issued.RawToken)}; hết hạn sau {(saved.ExpiresAt - before).TotalDays:0.0} ngày");

        Assert.Equal(64, issued.RawToken.Length);
        Assert.Equal(RefreshTokenService.HashToken(issued.RawToken), saved.TokenHash);
        Assert.NotEqual(issued.RawToken, saved.TokenHash);
        Assert.Null(saved.RevokedAt);
        Assert.Equal(Ip, saved.CreatedByIp);
        Assert.InRange(saved.ExpiresAt, before.AddDays(7), DateTime.UtcNow.AddDays(7));
    }

    [Fact]
    public async Task UT090_Refresh_ThanhCong_XoayVongToken()
    {
        var oldToken = await IssueAsync(Db.CustomerId);
        Step1("Phiên đăng nhập có refresh token còn hiệu lực; gọi refresh bằng token này");

        var result = await RotateAsync(oldToken.RawToken);
        ReportTokens($"kết quả {result.Status}", Db.CustomerId);

        var oldRow = Load(oldToken.RawToken);
        var newRow = Load(result.NewToken!.RawToken);
        Assert.Equal(RefreshRotationStatus.Success, result.Status);
        Assert.Equal(Db.CustomerId, result.UserId);
        Assert.NotEqual(oldToken.RawToken, result.NewToken.RawToken);
        Assert.NotNull(oldRow.RevokedAt);
        Assert.Equal(RefreshTokenRevokeReasons.Rotated, oldRow.RevokedReason);
        Assert.Equal(newRow.RefreshTokenId, oldRow.ReplacedByTokenId);
        Assert.Equal(oldRow.FamilyId, newRow.FamilyId);
        Assert.Equal(oldRow.ExpiresAt, newRow.ExpiresAt); // hạn tuyệt đối, không gia hạn
        Assert.Null(newRow.RevokedAt);
    }

    [Fact]
    public async Task UT091_DungLaiTokenCu_ThuHoiCaFamily()
    {
        var first = await IssueAsync(Db.CustomerId);
        var rotated = await RotateAsync(first.RawToken);
        var otherSession = await IssueAsync(Db.CustomerId); // phiên trên thiết bị khác, không được bị ảnh hưởng
        Step1("Token cũ đã bị xoay vòng (đã có token mới cùng family); kẻ tấn công gửi lại token cũ (grace = 0 giây)");

        var result = await RotateAsync(first.RawToken, reuseGraceSeconds: 0);
        ReportTokens($"kết quả {result.Status}", Db.CustomerId);

        var newRow = Load(rotated.NewToken!.RawToken);
        Assert.Equal(RefreshRotationStatus.ReuseDetected, result.Status);
        Assert.Equal(RefreshTokenRevokeReasons.ReuseDetected, newRow.RevokedReason);
        Assert.Null(Load(otherSession.RawToken).RevokedAt);

        // Người dùng hợp lệ (đang giữ token mới) cũng bị buộc đăng nhập lại.
        var legitimate = await RotateAsync(rotated.NewToken.RawToken, reuseGraceSeconds: 0);
        Assert.NotEqual(RefreshRotationStatus.Success, legitimate.Status);
    }

    [Fact]
    public async Task UT092_DungLaiTrongGraceWindow_KhongThuHoiFamily()
    {
        var first = await IssueAsync(Db.CustomerId);
        var rotated = await RotateAsync(first.RawToken);
        Step1("Token vừa được xoay vòng < 10 giây; request đồng thời (tab khác) gửi lại token cũ");

        var result = await RotateAsync(first.RawToken, reuseGraceSeconds: 10);
        ReportTokens($"kết quả {result.Status}", Db.CustomerId);

        Assert.Equal(RefreshRotationStatus.ConcurrentReuse, result.Status);
        Assert.Null(Load(rotated.NewToken!.RawToken).RevokedAt);
    }

    [Fact]
    public async Task UT093_DungLaiSauGraceWindow_ThuHoiFamily()
    {
        var first = await IssueAsync(Db.CustomerId);
        var rotated = await RotateAsync(first.RawToken);
        Update(first.RawToken, t => t.RevokedAt = DateTime.UtcNow.AddSeconds(-30));
        Step1("Token cũ bị xoay vòng cách đây 30 giây (quá grace 10 giây); gửi lại token cũ");

        var result = await RotateAsync(first.RawToken, reuseGraceSeconds: 10);
        ReportTokens($"kết quả {result.Status}", Db.CustomerId);

        Assert.Equal(RefreshRotationStatus.ReuseDetected, result.Status);
        Assert.NotNull(Load(rotated.NewToken!.RawToken).RevokedAt);
    }

    [Fact]
    public async Task UT094_TokenHetHan_BiTuChoi()
    {
        var token = await IssueAsync(Db.CustomerId);
        Update(token.RawToken, t => t.ExpiresAt = DateTime.UtcNow.AddMinutes(-1));
        Step1("Refresh token đã hết hạn 1 phút trước; gọi refresh");

        var result = await RotateAsync(token.RawToken);
        ReportTokens($"kết quả {result.Status}", Db.CustomerId);

        Assert.Equal(RefreshRotationStatus.Expired, result.Status);
        Assert.Single(LoadUserTokens(Db.CustomerId)); // không cấp token mới
    }

    [Fact]
    public async Task UT095_TaiKhoanBiKhoa_RefreshThatBai()
    {
        var userId = Db.AddUser("bikhoa@test.local", "Khoa@1234");
        var token = await IssueAsync(userId);
        using (var db = Db.NewContext())
        {
            db.Users.Single(u => u.UserId == userId).IsActive = false;
            db.SaveChanges();
        }
        Step1("Tài khoản có refresh token còn hạn nhưng đã bị vô hiệu hoá (IsActive = false); gọi refresh");

        var result = await RotateAsync(token.RawToken);
        ReportTokens($"kết quả {result.Status}", userId);

        Assert.Equal(RefreshRotationStatus.Inactive, result.Status);
        Assert.Equal(RefreshTokenRevokeReasons.UserInactive, Load(token.RawToken).RevokedReason);
    }

    [Fact]
    public async Task UT096_KhongCoCookie_HoacTokenLa_BiTuChoi()
    {
        Step1("Gọi refresh khi không có cookie, và khi gửi một chuỗi token bịa ra");

        var missing = await RotateAsync(null);
        var invalid = await RotateAsync(new string('A', 64));
        Actual($"không có cookie: {missing.Status}; token lạ: {invalid.Status}");

        Assert.Equal(RefreshRotationStatus.Missing, missing.Status);
        Assert.Equal(RefreshRotationStatus.Invalid, invalid.Status);
    }

    [Fact]
    public async Task UT097_DangXuat_ThuHoiToken()
    {
        var token = await IssueAsync(Db.CustomerId);
        var otherDevice = await IssueAsync(Db.CustomerId);
        Step1("Khách hàng đăng nhập trên 2 thiết bị; đăng xuất ở thiết bị thứ nhất rồi dùng lại token của thiết bị đó");

        await using (var holder = new AppDbContextHolder(Db))
        {
            await NewService(holder).RevokeFamilyAsync(token.RawToken, RefreshTokenRevokeReasons.Logout);
        }
        var result = await RotateAsync(token.RawToken, reuseGraceSeconds: 0);
        ReportTokens($"refresh sau đăng xuất: {result.Status}", Db.CustomerId);

        Assert.NotEqual(RefreshRotationStatus.Success, result.Status);
        Assert.Null(Load(otherDevice.RawToken).RevokedAt);
    }

    [Fact]
    public async Task UT098_DangBiLockout_VanRefreshDuoc()
    {
        var userId = Db.AddUser("lockout@test.local", "Lock@1234", lockoutEndUtc: DateTime.UtcNow.AddMinutes(10));
        var token = await IssueAsync(userId);
        Step1("Tài khoản đang hoạt động nhưng bị khoá đăng nhập tạm thời (ai đó nhập sai mật khẩu 5 lần); phiên cũ gọi refresh");

        var result = await RotateAsync(token.RawToken);
        ReportTokens($"kết quả {result.Status}", userId);

        Assert.Equal(RefreshRotationStatus.Success, result.Status);
    }

    private void AssertAllRevoked(int userId, string reason)
    {
        var tokens = LoadUserTokens(userId);
        Assert.NotEmpty(tokens);
        Assert.All(tokens, t => Assert.NotNull(t.RevokedAt));
        Assert.Contains(tokens, t => t.RevokedReason == reason);
    }

    [Fact]
    public async Task UT099_DoiMatKhau_ThuHoiTatCaToken()
    {
        var userId = Db.AddUser("doimk@test.local", "Cu@123456");
        await IssueAsync(userId);
        await IssueAsync(userId);
        Step1("Tài khoản đăng nhập trên 2 thiết bị; đổi mật khẩu thành công ở một thiết bị");

        await using (var holder = new AppDbContextHolder(Db))
        {
            await new UserProfileService(holder.Context, NewService(holder)).ChangePasswordAsync(userId, new DTOs.Users.ChangePasswordDto
            {
                CurrentPassword = "Cu@123456",
                NewPassword = "Moi@123456",
                ConfirmNewPassword = "Moi@123456"
            });
        }
        ReportTokens("đổi mật khẩu thành công", userId);

        AssertAllRevoked(userId, RefreshTokenRevokeReasons.PasswordChanged);
    }

    [Fact]
    public async Task UT100_DatLaiMatKhau_ThuHoiTatCaToken()
    {
        var userId = Db.AddUser("resetmk@test.local", "Cu@123456");
        await IssueAsync(userId);
        using (var db = Db.NewContext())
        {
            var user = db.Users.Single(u => u.UserId == userId);
            user.ResetPasswordToken = "RESET-TOKEN-TEST";
            user.ResetPasswordTokenExpiry = DateTime.UtcNow.AddMinutes(30);
            db.SaveChanges();
        }
        Step1("Tài khoản có phiên đăng nhập và yêu cầu quên mật khẩu còn hạn; đặt lại mật khẩu bằng link trong email");

        await using (var holder = new AppDbContextHolder(Db))
        {
            var authService = new AuthService(
                holder.Context,
                Moq.Mock.Of<Services.Interfaces.ITokenService>(),
                Moq.Mock.Of<Services.Interfaces.IEmailService>(),
                Options.Create(new FrontendSettings { BaseUrl = "http://localhost:4200" }),
                NullLogger<AuthService>.Instance,
                NewService(holder));

            await authService.ResetPasswordAsync(new ResetPasswordDto
            {
                Token = "RESET-TOKEN-TEST",
                NewPassword = "Moi@123456",
                ConfirmPassword = "Moi@123456"
            });
        }
        ReportTokens("đặt lại mật khẩu thành công", userId);

        AssertAllRevoked(userId, RefreshTokenRevokeReasons.PasswordReset);
    }

    [Fact]
    public async Task UT101_AdminKhoaTaiKhoan_ThuHoiTatCaToken()
    {
        var token = await IssueAsync(Db.CustomerId);
        Step1("Khách hàng đang đăng nhập; quản trị viên khoá tài khoản (IsActive = false)");

        await using (var holder = new AppDbContextHolder(Db))
        {
            await new AdminUserService(holder.Context, NewService(holder)).UpdateStatusAsync(Db.AdminId, Db.CustomerId, isActive: false);
        }
        var result = await RotateAsync(token.RawToken);
        ReportTokens($"refresh sau khi bị khoá: {result.Status}", Db.CustomerId);

        AssertAllRevoked(Db.CustomerId, RefreshTokenRevokeReasons.AccountDisabled);
        Assert.NotEqual(RefreshRotationStatus.Success, result.Status);
    }
}

/// Gói một AppDbContext để dùng với "await using" trong test.
public sealed class AppDbContextHolder : IAsyncDisposable
{
    public AppDbContextHolder(TestDb db) => Context = db.NewContext();

    public ClothingAPI.Data.AppDbContext Context { get; }

    public ValueTask DisposeAsync() => Context.DisposeAsync();
}
