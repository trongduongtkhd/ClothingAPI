using ClothingAPI.DTOs.Users;
using ClothingAPI.Services.Implementations;
using Xunit.Abstractions;

namespace ClothingAPI.UnitTests;

/// UpdateProfileDto + UserProfileService.UpdateProfileAsync: họ tên tối đa 150 và số di động Việt Nam hợp lệ.
public class ProfileTests : UnitTestBase
{
    public ProfileTests(ITestOutputHelper output) : base(output) { }

    private static UpdateProfileDto Dto(string fullName, string? phone = null) => new()
    {
        FullName = fullName,
        PhoneNumber = phone
    };

    private async Task<(List<string> DtoErrors, Exception? Error)> SaveAsync(UpdateProfileDto dto)
    {
        var dtoErrors = ValidateDto(dto);
        if (dtoErrors.Count > 0)
        {
            return (dtoErrors, null);
        }

        await using var context = Db.NewContext();
        return (dtoErrors, await CaptureAsync(() => new UserProfileService(context, Moq.Mock.Of<ClothingAPI.Services.Interfaces.IRefreshTokenService>()).UpdateProfileAsync(Db.CustomerId, dto)));
    }

    private (string FullName, string? Phone) Saved() =>
        Db.Query(db => db.Users.Where(u => u.UserId == Db.CustomerId).Select(u => new { u.FullName, u.PhoneNumber }).Single()) is { } u
            ? (u.FullName, u.PhoneNumber)
            : default;

    [Fact]
    public async Task UT084_HoSo_HoTenTrong_BiTuChoi()
    {
        Step1("Xoá họ tên (để trống) rồi lưu hồ sơ");

        var (errors, error) = await SaveAsync(Dto(""));
        Actual($"validation DTO: \"{string.Join("; ", errors)}\"; service: {Describe(error)}; họ tên giữ = {Saved().FullName}");

        Assert.Contains("Họ tên là bắt buộc.", errors);
        Assert.Equal("Khách hàng", Saved().FullName);
    }

    [Fact]
    public async Task UT085_HoSo_HoTen151KyTu_BiTuChoi()
    {
        Step1("Nhập họ tên 151 ký tự rồi lưu");

        var (errors, error) = await SaveAsync(Dto(new string('A', 151)));
        Actual($"validation DTO: \"{string.Join("; ", errors)}\"; service: {Describe(error)}");

        Assert.Contains(errors, e => e.Contains("FullName"));
        Assert.Equal("Khách hàng", Saved().FullName);
    }

    [Fact]
    public async Task UT086_HoSo_HoTen150KyTu_LuuThanhCong()
    {
        Step1("Nhập họ tên đúng 150 ký tự rồi lưu");

        var (errors, error) = await SaveAsync(Dto(new string('A', 150)));
        Actual($"validation DTO: {errors.Count} lỗi; service: {Describe(error)}; họ tên đã lưu dài {Saved().FullName.Length}");

        Assert.Empty(errors);
        Assert.Null(error);
        Assert.Equal(150, Saved().FullName.Length);
    }

    [Fact]
    public async Task UT087_HoSo_SoDienThoai10ChuSo_LuuThanhCong()
    {
        Step1("Nhập số di động Việt Nam hợp lệ có 10 chữ số rồi lưu");

        var (errors, error) = await SaveAsync(Dto("Khách hàng", "0901234567"));
        Actual($"validation DTO: {errors.Count} lỗi; service: {Describe(error)}; SĐT đã lưu dài {Saved().Phone?.Length}");

        Assert.Empty(errors);
        Assert.Null(error);
        Assert.Equal("0901234567", Saved().Phone);
    }

    [Fact]
    public async Task UT088_HoSo_SoDienThoai11ChuSo_BiTuChoi()
    {
        Step1("Nhập số di động dư một chữ số rồi lưu");

        var (errors, error) = await SaveAsync(Dto("Khách hàng", "09012345678"));
        Actual($"validation DTO: \"{string.Join("; ", errors)}\"; service: {Describe(error)}; SĐT giữ = {Saved().Phone ?? "null"}");

        Assert.Contains(errors, e => e.Contains("10 chữ số"));
        Assert.Null(Saved().Phone);
    }
}
