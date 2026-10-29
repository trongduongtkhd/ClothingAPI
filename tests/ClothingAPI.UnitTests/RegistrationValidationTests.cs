using System.ComponentModel.DataAnnotations;
using ClothingAPI.DTOs.Auth;
using ClothingAPI.DTOs.Users;
using ClothingAPI.Helpers;

namespace ClothingAPI.UnitTests;

public class RegistrationValidationTests
{
    private static RegisterDto Valid() => new()
    {
        FullName = "Nguyễn Văn An", Email = "customer@example.com", PhoneNumber = "0901234567",
        Password = "Test@1234", ConfirmPassword = "Test@1234", CaptchaToken = "test-token"
    };

    private static List<ValidationResult> Errors(object dto)
    {
        var errors = new List<ValidationResult>();
        Validator.TryValidateObject(dto, new ValidationContext(dto), errors, true);
        return errors;
    }

    [Theory]
    [InlineData("", false)] [InlineData("   ", false)] [InlineData("A", false)]
    [InlineData("Nguyễn Văn An", true)] [InlineData("An", true)] [InlineData("O'Connor", true)]
    [InlineData("Anne-Marie", true)] [InlineData("D’Arcy", true)] [InlineData("李明", true)]
    [InlineData("𐐀𐐁", true)] [InlineData("Nguyễn 123", false)] [InlineData("12345", false)]
    [InlineData("<script>alert</script>", false)] [InlineData("Nguyễn🙂", false)]
    [InlineData("Nguyễn\nAn", false)] [InlineData("Nguyễn\tAn", false)] [InlineData("Nguyễn\u200BAn", false)]
    [InlineData("-Nguyen", false)] [InlineData("Nguyen-", false)] [InlineData("O''Connor", false)]
    public void FullName_UsesSameRulesForRegistrationAndProfile(string name, bool valid)
    {
        var registration = Valid(); registration.FullName = name;
        var profile = new UpdateProfileDto { FullName = name };
        Assert.Equal(valid, Errors(registration).Count == 0);
        Assert.Equal(valid, Errors(profile).Count == 0);
    }

    [Fact]
    public void FullName_NormalizesUnicodeAndSpaces_AndChecksLengthAfterNormalization()
    {
        var dto = Valid(); dto.FullName = "  Nguye\u0302\u0303n   Va\u0306n An  ";
        Assert.Equal("Nguyễn Văn An", dto.FullName);
        Assert.Empty(Errors(dto));
        dto.FullName = new string('A', 150); Assert.Empty(Errors(dto));
        dto.FullName = new string('A', 151); Assert.NotEmpty(Errors(dto));
    }

    [Theory]
    [InlineData(null, null)] [InlineData("", null)] [InlineData("   ", null)]
    [InlineData("0901234567", "0901234567")] [InlineData("0312345678", "0312345678")]
    [InlineData("0512345678", "0512345678")] [InlineData("0712345678", "0712345678")]
    [InlineData("0812345678", "0812345678")] [InlineData("+84901234567", "0901234567")]
    [InlineData(" +84 901 234 567 ", "0901234567")] [InlineData("090.123.4567", "0901234567")]
    [InlineData("090-123-4567", "0901234567")]
    public void Phone_AcceptsSupportedFormats_AndNormalizes(string? input, string? expected)
    {
        var dto = Valid(); dto.PhoneNumber = input;
        var profile = new UpdateProfileDto { FullName = "Nguyễn An", PhoneNumber = input };
        Assert.Equal(expected, dto.PhoneNumber); Assert.Equal(expected, profile.PhoneNumber);
        Assert.Empty(Errors(dto)); Assert.Empty(Errors(profile));
    }

    [Theory]
    [InlineData("jfjewbjbfv")] [InlineData("1")] [InlineData("090123456")] [InlineData("09012345678")]
    [InlineData("0212345678")] [InlineData("0000000000")] [InlineData("090abc4567")]
    [InlineData("-0901234567")] [InlineData("0901234567-")] [InlineData("+840901234567")]
    [InlineData("84901234567")] [InlineData("０９０１２３４５６７")] [InlineData("0901234567\n")]
    [InlineData("090\t1234567")] [InlineData("\t")] [InlineData("090\u200B1234567")]
    public void Phone_RejectsInvalidInput(string input)
    {
        var dto = Valid(); dto.PhoneNumber = input;
        Assert.Contains(Errors(dto), e => e.MemberNames.Contains("PhoneNumber"));
    }

    [Theory]
    [InlineData("238420@gmail.com", true)] [InlineData("an+shop@example.com", true)]
    [InlineData("  AN@EXAMPLE.COM  ", true)] [InlineData("an.o'connor@example.com", true)]
    [InlineData("123456", false)] [InlineData("an@gmail", false)] [InlineData("an@@gmail.com", false)]
    [InlineData(".an@example.com", false)] [InlineData("an.@example.com", false)]
    [InlineData("an..test@example.com", false)] [InlineData("an@-example.com", false)]
    [InlineData("an@example-.com", false)] [InlineData("an@example..com", false)]
    [InlineData("an test@example.com", false)] [InlineData("an@example.com\n", false)]
    public void Email_AcceptsNumericLocalPart_ButRejectsMalformedAddresses(string email, bool valid)
    {
        var dto = Valid(); dto.Email = email;
        Assert.Equal(valid, Errors(dto).Count == 0);
    }

    [Fact]
    public void Email_ChecksLocalPartDomainLabelsAndTotalLength()
    {
        var dto = Valid();
        dto.Email = new string('a', 64) + "@example.com"; Assert.Empty(Errors(dto));
        dto.Email = new string('a', 65) + "@example.com"; Assert.NotEmpty(Errors(dto));
        dto.Email = "an@" + new string('a', 64) + ".com"; Assert.NotEmpty(Errors(dto));
        dto.Email = new string('a', 64) + "@" + new string('b', 63) + "." + new string('c', 63) + "." + new string('d', 61);
        Assert.Equal(254, dto.Email.Length); Assert.Empty(Errors(dto));
        dto.Email += "a"; Assert.NotEmpty(Errors(dto));
    }

    [Theory]
    [InlineData("Test@1234", true)] [InlineData("Abc@1234", true)] [InlineData("Ab1@    ", true)]
    [InlineData("test@1234", false)] [InlineData("Test1234", false)] [InlineData("Test@@@@", false)]
    [InlineData("A1@abcd", false)] [InlineData("Abc1234\u0301", false)]
    [InlineData("Test@1234\n", false)] [InlineData("Test@1234\0", false)] [InlineData("Test@1234\u200B", false)]
    public void Password_PolicyIsIdenticalForRegisterResetAndChange(string password, bool valid)
    {
        var dto = Valid(); dto.Password = password; dto.ConfirmPassword = password;
        var reset = new ResetPasswordDto { Token = "token", NewPassword = password, ConfirmPassword = password };
        var change = new ChangePasswordDto { CurrentPassword = "Old@1234", NewPassword = password, ConfirmNewPassword = password };
        Assert.Equal(valid, Errors(dto).Count == 0);
        Assert.Equal(valid, Errors(reset).Count == 0);
        Assert.Equal(valid, Errors(change).Count == 0);
    }

    [Fact]
    public void Password_BcryptLimitCountsUtf8Bytes_AndNeverTruncates()
    {
        var validator = new StrongPasswordAttribute();
        bool Valid(string value) => validator.GetValidationResult(value, new ValidationContext(new object())) == ValidationResult.Success;
        Assert.True(Valid("A1@" + new string('a', 69)));
        Assert.False(Valid("A1@" + new string('a', 70)));
        Assert.True(Valid("A1@" + new string('é', 34) + "x"));
        Assert.False(Valid("A1@" + new string('é', 35)));
    }

    [Fact]
    public void ConfirmPassword_MustMatchExactly_IncludingTrailingSpaces()
    {
        var dto = Valid(); dto.ConfirmPassword += " ";
        Assert.Contains(Errors(dto), e => e.MemberNames.Contains("ConfirmPassword"));
    }
}
