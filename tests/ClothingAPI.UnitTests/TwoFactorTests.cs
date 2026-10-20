using System.Text.RegularExpressions;
using ClothingAPI.Data;
using ClothingAPI.DTOs.Auth;
using ClothingAPI.DTOs.Users;
using ClothingAPI.Exceptions;
using ClothingAPI.Helpers;
using ClothingAPI.Models;
using ClothingAPI.Services.Implementations;
using ClothingAPI.Services.Interfaces;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using OtpNet;
using Xunit.Abstractions;

namespace ClothingAPI.UnitTests;

/// Xác thực hai lớp (TOTP + mã khôi phục). Thời gian được điều khiển bằng ManualTimeProvider,
/// mã TOTP đúng được tự tính bằng Otp.NET từ secret trả về lúc thiết lập.
public class TwoFactorTests : UnitTestBase
{
    private const string Email = "twofactor@test.local";
    private const string Password = "Dung@1234";
    private const string Pepper = "unit-test-recovery-pepper-0123456789-abcdefgh";
    private const string Ip = "127.0.0.1";

    // Bắt đầu từ thời điểm thật làm tròn xuống đầu bước 30 giây: AuthService (bước 1) kiểm tra khóa bằng DateTime.UtcNow,
    // nên đồng hồ giả phải xấp xỉ giờ thật để hai nơi cùng thấy trạng thái khóa.
    private readonly ManualTimeProvider _clock = new(DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30 * 30));

    private readonly Mock<ITokenService> _tokenService = new();

    // Dùng chung một provider cho mọi DbContext/service trong test để giải mã được secret đã lưu.
    private readonly IDataProtectionProvider _dataProtection = new EphemeralDataProtectionProvider();

    private readonly int _userId;

    private int _maxChallengeAttempts = 5;

    public TwoFactorTests(ITestOutputHelper output) : base(output)
    {
        _userId = Db.AddUser(Email, Password);
        _tokenService.Setup(x => x.CreateToken(It.IsAny<User>(), It.IsAny<IEnumerable<string>>()))
            .Returns(("fake-access-token", DateTime.UtcNow.AddMinutes(15)));
    }

    private TwoFactorService NewService(AppDbContext context) =>
        new(context,
            _dataProtection,
            Options.Create(new TwoFactorSettings { RecoveryCodePepper = Pepper, MaxChallengeAttempts = _maxChallengeAttempts }),
            _clock,
            new RefreshTokenService(context, Options.Create(new RefreshTokenSettings()), NullLogger<RefreshTokenService>.Instance),
            NullLogger<TwoFactorService>.Instance);

    private AuthService NewAuthService(AppDbContext context) =>
        new(context,
            _tokenService.Object,
            Mock.Of<IEmailService>(),
            Options.Create(new FrontendSettings { BaseUrl = "http://localhost:4200" }),
            NullLogger<AuthService>.Instance,
            new RefreshTokenService(context, Options.Create(new RefreshTokenSettings()), NullLogger<RefreshTokenService>.Instance),
            NewService(context));

    private async Task<LoginResult> LoginAsync(string password = Password)
    {
        await using var context = Db.NewContext();
        return await NewAuthService(context).LoginAsync(new LoginDto { Email = Email, Password = password }, Ip);
    }

    private async Task<string> StartChallengeAsync() => (await LoginAsync()).Challenge!.TwoFactorToken;

    private Task<TwoFactorLoginResult> VerifyAsync(string challenge, string code) =>
        WithServiceAsync(s => s.VerifyLoginCodeAsync(challenge, code, Ip));

    private Task<TwoFactorLoginResult> VerifyRecoveryAsync(string challenge, string recoveryCode) =>
        WithServiceAsync(s => s.VerifyLoginRecoveryCodeAsync(challenge, recoveryCode, Ip));

    private TwoFactorChallenge LoadChallenge(string rawToken)
    {
        var hash = RefreshTokenService.HashToken(rawToken);
        return Db.Query(db => db.TwoFactorChallenges.AsNoTracking().Single(x => x.TokenHash == hash));
    }

    private void SetFailedAttempts(int value)
    {
        using var db = Db.NewContext();
        db.Users.Single(u => u.UserId == _userId).FailedLoginAttempts = value;
        db.SaveChanges();
    }

    private async Task<T> WithServiceAsync<T>(Func<TwoFactorService, Task<T>> action)
    {
        await using var context = Db.NewContext();
        return await action(NewService(context));
    }

    private async Task<Exception?> CaptureWithServiceAsync(Func<TwoFactorService, Task> action)
    {
        await using var context = Db.NewContext();
        var service = NewService(context);
        return await CaptureAsync(() => action(service));
    }

    private User LoadUser() => Db.Query(db => db.Users.AsNoTracking().Single(u => u.UserId == _userId));

    private static string Code(string base32Secret, DateTime at) =>
        new Totp(Base32Encoding.ToBytes(base32Secret.Replace(" ", ""))).ComputeTotp(at);

    private static string WrongCode(string correct) => correct == "000000" ? "111111" : "000000";

    /// Thiết lập + bật 2FA, rồi tiến đồng hồ sang bước kế tiếp (bước hiện tại đã bị "dùng" khi xác nhận).
    private async Task<(string Secret, List<string> RecoveryCodes)> EnableTwoFactorAsync()
    {
        var setup = await WithServiceAsync(s => s.GetSetupAsync(_userId));
        var result = await WithServiceAsync(s => s.EnableAsync(_userId, Code(setup.Secret, _clock.UtcNow), Ip));
        _clock.Advance(TimeSpan.FromSeconds(30));
        return (setup.Secret, result.RecoveryCodes);
    }

    private void ReportUser(string outcome)
    {
        var user = LoadUser();
        Actual($"{outcome}; TwoFactorEnabled = {user.TwoFactorEnabled}; FailedLoginAttempts = {user.FailedLoginAttempts}; " +
               $"LockoutEndUtc = {(user.LockoutEndUtc.HasValue ? user.LockoutEndUtc.Value.ToString("HH:mm:ss") : "null")}");
    }

    // ===================== Thiết lập / bật =====================

    [Fact]
    public async Task UT102_2FA_ThietLap_SecretLuuDaMaHoa_KhongPhaiBase32()
    {
        Step1("Tài khoản chưa bật 2FA; gọi bắt đầu thiết lập");

        var setup = await WithServiceAsync(s => s.GetSetupAsync(_userId));
        var user = LoadUser();
        var plainSecret = setup.Secret.Replace(" ", "");
        Actual($"secret trả về dài {plainSecret.Length} ký tự Base32; DB lưu chuỗi dài {user.TwoFactorSecretProtected!.Length} ký tự; TwoFactorEnabled = {user.TwoFactorEnabled}");

        Assert.Matches("^[A-Z2-7]{32}$", plainSecret);
        Assert.False(user.TwoFactorEnabled);
        Assert.NotEqual(plainSecret, user.TwoFactorSecretProtected);
        Assert.DoesNotContain(plainSecret, user.TwoFactorSecretProtected);
        Assert.Equal(plainSecret, _dataProtection.CreateProtector("ClothingAPI.TwoFactor.Secret.v1").Unprotect(user.TwoFactorSecretProtected));
        Assert.StartsWith("otpauth://totp/", setup.OtpauthUri);
        Assert.Contains("issuer=ClothingStore", setup.OtpauthUri);
        Assert.StartsWith("data:image/png;base64,", setup.QrCodeDataUri);
    }

    [Fact]
    public async Task UT103_2FA_Bat_MaSai_BiTuChoi_ChuaBat()
    {
        var setup = await WithServiceAsync(s => s.GetSetupAsync(_userId));
        Step1("Đã bắt đầu thiết lập; xác nhận bằng mã SAI");

        var error = await CaptureWithServiceAsync(s => s.EnableAsync(_userId, WrongCode(Code(setup.Secret, _clock.UtcNow)), Ip));
        ReportUser(Describe(error));

        Assert.IsType<BadRequestException>(error);
        Assert.False(LoadUser().TwoFactorEnabled);
        Assert.Equal(0, LoadUser().FailedLoginAttempts);
    }

    [Fact]
    public async Task UT104_2FA_Bat_MaDung_BatVaSinhMaKhoiPhuc_ThuHoiPhienCu()
    {
        await using (var context = Db.NewContext())
        {
            await new RefreshTokenService(context, Options.Create(new RefreshTokenSettings()), NullLogger<RefreshTokenService>.Instance)
                .IssueAsync(_userId, Ip, "UnitTest");
        }

        var setup = await WithServiceAsync(s => s.GetSetupAsync(_userId));
        Step1("Đã có một phiên đăng nhập (refresh token) và đã bắt đầu thiết lập; xác nhận bằng mã ĐÚNG");

        var result = await WithServiceAsync(s => s.EnableAsync(_userId, Code(setup.Secret, _clock.UtcNow), Ip));
        var tokens = Db.Query(db => db.RefreshTokens.AsNoTracking().Where(x => x.UserId == _userId).ToList());
        ReportUser($"nhận {result.RecoveryCodes.Count} mã khôi phục; refresh token cũ: {tokens.Single().RevokedReason}");

        var user = LoadUser();
        Assert.True(user.TwoFactorEnabled);
        Assert.NotNull(user.TwoFactorLastUsedStep);
        Assert.Equal(8, result.RecoveryCodes.Count);
        Assert.Equal("TwoFactorEnabled", tokens.Single().RevokedReason);
    }

    [Fact]
    public async Task UT105_2FA_DaBat_ThietLapLai_BiTuChoi_KhongTraSecret()
    {
        await EnableTwoFactorAsync();
        var protectedBefore = LoadUser().TwoFactorSecretProtected;
        Step1("2FA đã bật; gọi lại bắt đầu thiết lập");

        TwoFactorSetupDto? setup = null;
        var error = await CaptureWithServiceAsync(async s => setup = await s.GetSetupAsync(_userId));
        ReportUser(Describe(error));

        Assert.IsType<BadRequestException>(error);
        Assert.Null(setup);
        Assert.Equal(protectedBefore, LoadUser().TwoFactorSecretProtected);
    }

    // ===================== Mã khôi phục =====================

    [Fact]
    public async Task UT106_2FA_MaKhoiPhuc_DungDinhDang_DBChiLuuHash()
    {
        Step1("Bật 2FA thành công; kiểm tra định dạng mã khôi phục và nội dung bảng TwoFactorRecoveryCodes");

        var (_, codes) = await EnableTwoFactorAsync();
        var rows = Db.Query(db => db.TwoFactorRecoveryCodes.AsNoTracking().Where(x => x.UserId == _userId).ToList());
        Actual($"{codes.Count} mã dạng {Regex.Replace(codes[0], "[A-Z0-9]", "X")}; DB có {rows.Count} dòng, CodeHash dài {rows[0].CodeHash.Length}");

        Assert.Equal(8, codes.Distinct().Count());
        Assert.All(codes, c => Assert.Matches("^[2-9A-HJKMNP-Z]{4}-[2-9A-HJKMNP-Z]{4}-[2-9A-HJKMNP-Z]{4}$", c));
        Assert.Equal(8, rows.Count);
        Assert.All(rows, r => Assert.Matches("^[0-9A-F]{64}$", r.CodeHash));

        // Không dòng nào chứa mã gốc; hash là HMAC với pepper (không phải SHA-256 trần).
        var normalized = codes.Select(TwoFactorService.NormalizeRecoveryCode).ToList();
        Assert.DoesNotContain(rows, r => normalized.Any(n => r.CodeHash.Contains(n)));
        Assert.Contains(rows, r => r.CodeHash == TwoFactorService.HashRecoveryCode(normalized[0], Pepper));
        Assert.DoesNotContain(rows, r => r.CodeHash == RefreshTokenService.HashToken(normalized[0]));
    }

    // ===================== Tắt =====================

    [Fact]
    public async Task UT107_2FA_Tat_SaiMatKhau_BiTuChoi_TangBoDem()
    {
        var (secret, _) = await EnableTwoFactorAsync();
        Step1("2FA đang bật; tắt với mật khẩu SAI và mã TOTP đúng");

        var error = await CaptureWithServiceAsync(s => s.DisableAsync(_userId,
            new DisableTwoFactorDto { Password = "Sai@0000", Code = Code(secret, _clock.UtcNow) }, Ip));
        ReportUser(Describe(error));

        Assert.IsType<BadRequestException>(error);
        Assert.Equal("Mật khẩu hoặc mã xác thực không đúng.", error!.Message);
        Assert.True(LoadUser().TwoFactorEnabled);
        Assert.Equal(1, LoadUser().FailedLoginAttempts);
    }

    [Fact]
    public async Task UT108_2FA_Tat_SaiMa_BiTuChoi_TangBoDem()
    {
        var (secret, _) = await EnableTwoFactorAsync();
        Step1("2FA đang bật; tắt với mật khẩu đúng và mã TOTP SAI");

        var error = await CaptureWithServiceAsync(s => s.DisableAsync(_userId,
            new DisableTwoFactorDto { Password = Password, Code = WrongCode(Code(secret, _clock.UtcNow)) }, Ip));
        ReportUser(Describe(error));

        Assert.IsType<BadRequestException>(error);
        Assert.True(LoadUser().TwoFactorEnabled);
        Assert.Equal(1, LoadUser().FailedLoginAttempts);
    }

    [Fact]
    public async Task UT109_2FA_Tat_ThieuMa_BiTuChoi()
    {
        await EnableTwoFactorAsync();
        Step1("2FA đang bật; tắt chỉ với mật khẩu đúng, không nhập mã");

        var error = await CaptureWithServiceAsync(s => s.DisableAsync(_userId, new DisableTwoFactorDto { Password = Password }, Ip));
        ReportUser(Describe(error));

        Assert.IsType<BadRequestException>(error);
        Assert.True(LoadUser().TwoFactorEnabled);
    }

    [Fact]
    public async Task UT110_2FA_Tat_MatKhauVaMaDung_XoaSecretVaMaKhoiPhuc()
    {
        var (secret, _) = await EnableTwoFactorAsync();
        Step1("2FA đang bật; tắt với mật khẩu đúng và mã TOTP đúng");

        var error = await CaptureWithServiceAsync(s => s.DisableAsync(_userId,
            new DisableTwoFactorDto { Password = Password, Code = Code(secret, _clock.UtcNow) }, Ip));
        ReportUser(Describe(error));

        var user = LoadUser();
        Assert.Null(error);
        Assert.False(user.TwoFactorEnabled);
        Assert.Null(user.TwoFactorSecretProtected);
        Assert.Null(user.TwoFactorLastUsedStep);
        Assert.Equal(0, Db.Query(db => db.TwoFactorRecoveryCodes.Count(x => x.UserId == _userId)));
    }

    [Fact]
    public async Task UT111_2FA_Tat_BangMaKhoiPhuc_ThanhCong()
    {
        var (_, codes) = await EnableTwoFactorAsync();
        Step1("2FA đang bật; tắt với mật khẩu đúng và một mã khôi phục (viết thường, bỏ dấu gạch)");

        var input = codes[3].Replace("-", "").ToLowerInvariant();
        var error = await CaptureWithServiceAsync(s => s.DisableAsync(_userId,
            new DisableTwoFactorDto { Password = Password, RecoveryCode = input }, Ip));
        ReportUser(Describe(error));

        Assert.Null(error);
        Assert.False(LoadUser().TwoFactorEnabled);
    }

    // ===================== Đăng nhập hai bước =====================

    [Fact]
    public async Task UT112_DangNhap_KhongBat2FA_HanhViGiuNguyen_CoToken()
    {
        SetFailedAttempts(2);
        Step1("Tài khoản KHÔNG bật 2FA, FailedLoginAttempts = 2; đăng nhập với mật khẩu đúng");

        var result = await LoginAsync();
        ReportUser($"Auth = {(result.Auth is null ? "null" : $"\"{result.Auth.AccessToken}\"")}, Challenge = {(result.Challenge is null ? "null" : "có")}");

        Assert.NotNull(result.Auth);
        Assert.Null(result.Challenge);
        Assert.Equal("fake-access-token", result.Auth!.AccessToken);
        Assert.False(result.Auth.RequiresTwoFactor);
        Assert.Equal(0, LoadUser().FailedLoginAttempts);
    }

    [Fact]
    public async Task UT113_DangNhap_Bat2FA_KhongCapToken_TraChallenge_KhongResetBoDem()
    {
        await EnableTwoFactorAsync();
        SetFailedAttempts(3);
        Step1("Tài khoản bật 2FA, FailedLoginAttempts = 3; đăng nhập với mật khẩu đúng");

        var result = await LoginAsync();
        var challenge = LoadChallenge(result.Challenge!.TwoFactorToken);
        ReportUser($"Auth = {(result.Auth is null ? "null" : "có token")}, challenge hết hạn sau {result.Challenge.ExpiresInSeconds} giây");

        Assert.Null(result.Auth);
        Assert.True(result.Challenge.RequiresTwoFactor);
        Assert.Matches("^[0-9A-F]{64}$", result.Challenge.TwoFactorToken);
        Assert.Equal(300, result.Challenge.ExpiresInSeconds);
        Assert.NotEqual(result.Challenge.TwoFactorToken, challenge.TokenHash); // DB chỉ lưu hash
        Assert.Equal(3, LoadUser().FailedLoginAttempts);
        _tokenService.Verify(x => x.CreateToken(It.IsAny<User>(), It.IsAny<IEnumerable<string>>()), Times.Never);
    }

    [Fact]
    public async Task UT114_DangNhap2FA_MaDung_ThanhCong_ChallengeBiTieuThu_ResetBoDem()
    {
        var (secret, _) = await EnableTwoFactorAsync();
        SetFailedAttempts(2);
        var token = await StartChallengeAsync();
        Step1("Mật khẩu đúng, FailedLoginAttempts = 2; nhập mã TOTP đúng ở bước 2");

        var result = await VerifyAsync(token, Code(secret, _clock.UtcNow));
        var challenge = LoadChallenge(token);
        ReportUser($"Status = {result.Status}, UserId = {result.UserId}, ConsumedAt = {challenge.ConsumedAt?.ToString("HH:mm:ss") ?? "null"}, FailedAttempts (challenge) = {challenge.FailedAttempts}");

        Assert.Equal(TwoFactorLoginStatus.Success, result.Status);
        Assert.Equal(_userId, result.UserId);
        Assert.NotNull(challenge.ConsumedAt);
        Assert.Equal(0, challenge.FailedAttempts);
        Assert.Equal(0, LoadUser().FailedLoginAttempts);
    }

    [Fact]
    public async Task UT115_DangNhap2FA_MaSai_TinhVaoKhoaTaiKhoan_Sai5LanQuaNhieuChallenge_BiKhoa()
    {
        var (secret, _) = await EnableTwoFactorAsync();
        var pendingChallenge = await StartChallengeAsync(); // giữ lại để thử mã ĐÚNG sau khi bị khóa
        Step1("Bật 2FA; đăng nhập lại 5 lần (mỗi lần một challenge mới), mỗi lần nhập sai mã; sau đó nhập mã đúng");

        var statuses = new List<TwoFactorLoginStatus>();
        for (var i = 0; i < 5; i++)
        {
            var token = await StartChallengeAsync();
            statuses.Add((await VerifyAsync(token, WrongCode(Code(secret, _clock.UtcNow)))).Status);
            if (i < 4)
            {
                Assert.Equal(i + 1, LoadUser().FailedLoginAttempts);
            }
        }

        var afterLock = await VerifyAsync(pendingChallenge, Code(secret, _clock.UtcNow));
        var loginError = await CaptureAsync(() => LoginAsync());
        ReportUser($"5 lần sai: {string.Join(", ", statuses)}; mã đúng sau khi khóa: {afterLock.Status}; đăng nhập lại: {Describe(loginError)}");

        Assert.Equal(TwoFactorLoginStatus.LockedOut, statuses[^1]);
        Assert.All(statuses.Take(4), s => Assert.Equal(TwoFactorLoginStatus.InvalidCode, s));
        Assert.NotNull(LoadUser().LockoutEndUtc);
        Assert.Equal(TwoFactorLoginStatus.LockedOut, afterLock.Status);
        Assert.IsType<UnauthorizedException>(loginError);
    }

    [Fact]
    public async Task UT116_DangNhap2FA_ChallengeHetHan_BiTuChoi()
    {
        var (secret, _) = await EnableTwoFactorAsync();
        var token = await StartChallengeAsync();
        Step1("Mật khẩu đúng; chờ 5 phút 1 giây (đồng hồ giả) rồi nhập mã TOTP đúng");

        _clock.Advance(TimeSpan.FromMinutes(5).Add(TimeSpan.FromSeconds(1)));
        var result = await VerifyAsync(token, Code(secret, _clock.UtcNow));
        ReportUser($"Status = {result.Status}");

        Assert.Equal(TwoFactorLoginStatus.ChallengeExpired, result.Status);
        Assert.Equal(0, LoadUser().FailedLoginAttempts);
    }

    [Fact]
    public async Task UT117_DangNhap2FA_ChallengeChiDungMotLan()
    {
        var (secret, _) = await EnableTwoFactorAsync();
        var token = await StartChallengeAsync();
        Step1("Đăng nhập 2 bước thành công; dùng lại CÙNG challenge với mã mới (30 giây sau)");

        var first = await VerifyAsync(token, Code(secret, _clock.UtcNow));
        _clock.Advance(TimeSpan.FromSeconds(30));
        var second = await VerifyAsync(token, Code(secret, _clock.UtcNow));
        ReportUser($"lần 1: {first.Status}; lần 2: {second.Status}");

        Assert.Equal(TwoFactorLoginStatus.Success, first.Status);
        Assert.Equal(TwoFactorLoginStatus.ChallengeExpired, second.Status);
    }

    [Fact]
    public async Task UT118_DangNhap2FA_ChallengeHetLuotThu_VoHieu()
    {
        _maxChallengeAttempts = 3; // nhỏ hơn ngưỡng khóa tài khoản (5) để thấy riêng giới hạn theo challenge
        var (secret, _) = await EnableTwoFactorAsync();
        var token = await StartChallengeAsync();
        Step1("MaxChallengeAttempts = 3; nhập sai 3 lần trên cùng một challenge, lần 4 nhập mã đúng");

        var statuses = new List<TwoFactorLoginStatus>();
        for (var i = 0; i < 3; i++)
        {
            statuses.Add((await VerifyAsync(token, WrongCode(Code(secret, _clock.UtcNow)))).Status);
        }
        var correct = await VerifyAsync(token, Code(secret, _clock.UtcNow));
        ReportUser($"3 lần sai: {string.Join(", ", statuses)}; lần 4 (mã đúng): {correct.Status}; FailedAttempts (challenge) = {LoadChallenge(token).FailedAttempts}");

        Assert.Equal(TwoFactorLoginStatus.ChallengeExpired, statuses[^1]);
        Assert.Equal(TwoFactorLoginStatus.ChallengeExpired, correct.Status);
        Assert.Equal(3, LoadChallenge(token).FailedAttempts);
        Assert.Equal(3, LoadUser().FailedLoginAttempts);
    }

    [Fact]
    public async Task UT119_DangNhap2FA_MaDaDungBiPhatLai_BiTuChoi()
    {
        var (secret, _) = await EnableTwoFactorAsync();
        var code = Code(secret, _clock.UtcNow);
        var first = await VerifyAsync(await StartChallengeAsync(), code);
        Step1("Vừa đăng nhập 2 bước thành công bằng một mã; trong cùng 30 giây đăng nhập lại và nhập ĐÚNG mã đó");

        var replay = await VerifyAsync(await StartChallengeAsync(), code);
        ReportUser($"lần 1: {first.Status}; lần 2 (phát lại): {replay.Status}");

        Assert.Equal(TwoFactorLoginStatus.Success, first.Status);
        Assert.Equal(TwoFactorLoginStatus.Replayed, replay.Status);
        Assert.Equal(0, LoadUser().FailedLoginAttempts); // phát lại không tính là đoán mã
    }

    [Fact]
    public async Task UT120_DangNhap2FA_MaBuocTruocDuocChapNhan_CuHonBiTuChoi()
    {
        var (secret, _) = await EnableTwoFactorAsync();
        _clock.Advance(TimeSpan.FromSeconds(90)); // để các bước -1, -2 đều mới hơn bước đã dùng khi bật
        Step1("Nhập mã của 60 giây trước (lệch 2 bước), sau đó mã của 30 giây trước (lệch 1 bước)");

        var twoStepsOld = await VerifyAsync(await StartChallengeAsync(), Code(secret, _clock.UtcNow.AddSeconds(-60)));
        var oneStepOld = await VerifyAsync(await StartChallengeAsync(), Code(secret, _clock.UtcNow.AddSeconds(-30)));
        ReportUser($"mã −60s: {twoStepsOld.Status}; mã −30s: {oneStepOld.Status}");

        Assert.Equal(TwoFactorLoginStatus.InvalidCode, twoStepsOld.Status);
        Assert.Equal(TwoFactorLoginStatus.Success, oneStepOld.Status);
    }

    [Fact]
    public async Task UT121_DangNhap2FA_MaKhoiPhuc_DungMotLan()
    {
        var (_, codes) = await EnableTwoFactorAsync();
        Step1("Đăng nhập bằng mã khôi phục (viết thường, bỏ dấu gạch); sau đó dùng lại đúng mã đó");

        var input = codes[0].Replace("-", "").ToLowerInvariant();
        var first = await VerifyRecoveryAsync(await StartChallengeAsync(), input);
        var second = await VerifyRecoveryAsync(await StartChallengeAsync(), codes[0]);
        var rows = Db.Query(db => db.TwoFactorRecoveryCodes.AsNoTracking().Where(x => x.UserId == _userId).ToList());
        ReportUser($"lần 1: {first.Status} (còn {first.RemainingRecoveryCodes} mã); lần 2: {second.Status}; số mã đã dùng trong DB: {rows.Count(r => r.UsedAt != null)}");

        Assert.Equal(TwoFactorLoginStatus.Success, first.Status);
        Assert.Equal(7, first.RemainingRecoveryCodes);
        Assert.Equal(TwoFactorLoginStatus.InvalidCode, second.Status);
        Assert.Equal(1, rows.Count(r => r.UsedAt != null));
        Assert.Equal(1, LoadUser().FailedLoginAttempts); // mã khôi phục sai cũng tính vào khóa
    }

    [Fact]
    public async Task UT122_DatLaiMatKhau_KhongTat2FA_VanPhaiNhapMa()
    {
        await EnableTwoFactorAsync();
        using (var db = Db.NewContext())
        {
            var user = db.Users.Single(u => u.UserId == _userId);
            user.ResetPasswordToken = "reset-token-2fa";
            user.ResetPasswordTokenExpiry = DateTime.UtcNow.AddMinutes(30);
            db.SaveChanges();
        }
        Step1("2FA đang bật; đặt lại mật khẩu qua link quên mật khẩu, rồi đăng nhập bằng mật khẩu mới");

        await using (var context = Db.NewContext())
        {
            await NewAuthService(context).ResetPasswordAsync(new ResetPasswordDto
            {
                Token = "reset-token-2fa",
                NewPassword = "MatKhauMoi@123",
                ConfirmPassword = "MatKhauMoi@123"
            });
        }

        var result = await LoginAsync("MatKhauMoi@123");
        ReportUser($"sau khi đặt lại mật khẩu: Auth = {(result.Auth is null ? "null" : "có token")}, Challenge = {(result.Challenge is null ? "null" : "có")}");

        Assert.True(LoadUser().TwoFactorEnabled);
        Assert.NotNull(LoadUser().TwoFactorSecretProtected);
        Assert.Null(result.Auth);
        Assert.NotNull(result.Challenge);
    }
}
