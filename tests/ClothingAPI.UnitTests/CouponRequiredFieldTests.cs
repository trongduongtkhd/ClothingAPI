using ClothingAPI.DTOs.Coupons;
using Xunit.Abstractions;

namespace ClothingAPI.UnitTests;

/// UpsertCouponDto: mã và tên chương trình là bắt buộc ([Required]) — kiểm tra bằng validation DTO như [ApiController].
public class CouponRequiredFieldTests : UnitTestBase
{
    public CouponRequiredFieldTests(ITestOutputHelper output) : base(output) { }

    private static UpsertCouponDto Dto(string code, string name) => new()
    {
        Code = code,
        Name = name,
        DiscountValue = 10,
        StartDate = DateTime.UtcNow,
        EndDate = DateTime.UtcNow.AddDays(10)
    };

    [Fact]
    public void UT081_Coupon_ThieuMa_BiTuChoi()
    {
        Step1("Tạo coupon với mã trống, tên \"Khuyến mãi hè\"");

        var errors = ValidateDto(Dto("", "Khuyến mãi hè"));
        Actual($"validation DTO: \"{string.Join("; ", errors)}\"");

        Assert.Contains("Mã giảm giá là bắt buộc.", errors);
    }

    [Fact]
    public void UT082_Coupon_ThieuTenChuongTrinh_BiTuChoi()
    {
        Step1("Tạo coupon với mã \"HE2026\", tên chương trình trống");

        var errors = ValidateDto(Dto("HE2026", ""));
        Actual($"validation DTO: \"{string.Join("; ", errors)}\"");

        Assert.Contains("Tên chương trình là bắt buộc.", errors);
    }

    [Fact]
    public void UT083_Coupon_DuMaVaTen_KhongCoLoiBatBuoc()
    {
        Step1("Tạo coupon với mã \"HE2026\" và tên \"Khuyến mãi hè\"");

        var errors = ValidateDto(Dto("HE2026", "Khuyến mãi hè"));
        Actual($"validation DTO: {errors.Count} lỗi");

        Assert.DoesNotContain("Mã giảm giá là bắt buộc.", errors);
        Assert.DoesNotContain("Tên chương trình là bắt buộc.", errors);
    }
}
