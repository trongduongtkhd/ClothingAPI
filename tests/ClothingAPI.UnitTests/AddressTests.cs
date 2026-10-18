using ClothingAPI.DTOs.Addresses;
using ClothingAPI.Services.Implementations;
using Xunit.Abstractions;

namespace ClothingAPI.UnitTests;

/// UpsertAddressDto ([StringLength]) + AddressService.CreateAsync: biên độ dài các trường địa chỉ giao hàng.
public class AddressTests : UnitTestBase
{
    public AddressTests(ITestOutputHelper output) : base(output) { }

    private static UpsertAddressDto ValidDto() => new()
    {
        ReceiverName = "Nguyễn Văn A",
        ReceiverPhone = "0911111111",
        AddressDetail = "1 Đường A",
        Province = "TP. Hồ Chí Minh"
    };

    private List<string> ValidateWith(string field, int length, UpsertAddressDto dto)
    {
        var errors = ValidateDto(dto);
        Actual($"{field} dài {length} ký tự → validation DTO trả {errors.Count} lỗi: \"{string.Join("; ", errors)}\"");
        return errors;
    }

    [Fact]
    public void UT076_DiaChi_TenNguoiNhan151KyTu_BiTuChoi()
    {
        var dto = ValidDto();
        dto.ReceiverName = new string('A', 151);
        Step1("Tên người nhận 151 ký tự (giới hạn 150)");

        var errors = ValidateWith("Tên người nhận", 151, dto);

        Assert.Contains(errors, e => e.Contains("ReceiverName"));
    }

    [Fact]
    public void UT077_DiaChi_SoDienThoai21KyTu_BiTuChoi()
    {
        var dto = ValidDto();
        dto.ReceiverPhone = new string('1', 21);
        Step1("Số điện thoại 21 ký tự (giới hạn 20)");

        var errors = ValidateWith("Số điện thoại", 21, dto);

        Assert.Contains(errors, e => e.Contains("ReceiverPhone"));
    }

    [Fact]
    public void UT078_DiaChi_TinhThanh101KyTu_BiTuChoi()
    {
        var dto = ValidDto();
        dto.Province = new string('T', 101);
        Step1("Tỉnh/thành phố 101 ký tự (giới hạn 100)");

        var errors = ValidateWith("Tỉnh/thành phố", 101, dto);

        Assert.Contains(errors, e => e.Contains("Province"));
    }

    [Fact]
    public void UT079_DiaChi_DiaChiChiTiet501KyTu_BiTuChoi()
    {
        var dto = ValidDto();
        dto.AddressDetail = new string('D', 501);
        Step1("Địa chỉ chi tiết 501 ký tự (giới hạn 500)");

        var errors = ValidateWith("Địa chỉ chi tiết", 501, dto);

        Assert.Contains(errors, e => e.Contains("AddressDetail"));
    }

    [Fact]
    public async Task UT080_DiaChi_DungGioiHan150_20_100_500_LuuThanhCong()
    {
        var dto = new UpsertAddressDto
        {
            ReceiverName = new string('A', 150),
            ReceiverPhone = new string('1', 20),
            AddressDetail = new string('D', 500),
            Province = new string('T', 100)
        };
        Step1("Tên 150, SĐT 20, địa chỉ chi tiết 500, tỉnh/thành 100 ký tự (đúng giới hạn)");

        var errors = ValidateDto(dto);
        Exception? error = null;
        if (errors.Count == 0)
        {
            await using var context = Db.NewContext();
            error = await CaptureAsync(() => new AddressService(context).CreateAsync(Db.CustomerId, dto));
        }
        var saved = Db.Query(db => db.Addresses.Where(a => a.UserId == Db.CustomerId).ToList());
        Actual($"validation DTO: {errors.Count} lỗi; service: {Describe(error)}; đã lưu {saved.Count} địa chỉ");

        Assert.Empty(errors);
        Assert.Null(error);
        var address = Assert.Single(saved);
        Assert.Equal(150, address.ReceiverName.Length);
        Assert.Equal(20, address.ReceiverPhone.Length);
        Assert.Equal(500, address.AddressDetail.Length);
        Assert.Equal(100, address.Province.Length);
    }
}
