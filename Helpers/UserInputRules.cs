using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.RegularExpressions;

namespace ClothingAPI.Helpers;

public static class UserInputRules
{
    public const string FullNameMessage = "Họ tên phải có từ 2 đến 150 ký tự, chỉ gồm chữ cái, khoảng trắng, dấu nháy hoặc gạch nối giữa các phần tên.";
    public const string PhoneMessage = "Số điện thoại di động Việt Nam phải có 10 chữ số, bắt đầu bằng 03, 05, 07, 08 hoặc 09 (hoặc dùng +84 thay số 0).";
    public const string EmailMessage = "Email không đúng định dạng hoặc vượt quá 254 ký tự.";

    private static readonly Regex PhoneInputPattern = new(@"\A\+?[0-9]+(?:[ .-][0-9]+)*\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly Regex PhonePattern = new(@"\A0[35789][0-9]{8}\z", RegexOptions.CultureInvariant);
    // Angular's conventional email syntax plus a public-domain TLD. Keep numeric local parts and plus tags.
    private static readonly Regex EmailPattern = new(@"\A(?=.{1,254}\z)(?=.{1,64}@)[a-zA-Z0-9!#$%&'*+/=?^_`{|}~-]+(?:\.[a-zA-Z0-9!#$%&'*+/=?^_`{|}~-]+)*@[a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?(?:\.[a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?)*\.[a-zA-Z]{2,63}\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    public static string NormalizeFullName(string? value)
    {
        if (value is null) return string.Empty;
        if (value.Length > 1000) return value;
        try { return Regex.Replace(value.Normalize(NormalizationForm.FormC).Trim(' '), " +", " "); }
        catch (ArgumentException) { return value; }
    }

    public static string NormalizeEmail(string? value) => (value ?? string.Empty).Trim(' ').ToLowerInvariant();

    public static string? NormalizePhone(string? value)
    {
        if (value is null) return null;
        var phone = value.Trim(' ');
        if (phone.Length == 0) return null;
        if (phone.Length > 32 || !PhoneInputPattern.IsMatch(phone)) return phone;
        phone = phone.Replace(" ", "").Replace(".", "").Replace("-", "");
        return phone.StartsWith("+84", StringComparison.Ordinal) ? "0" + phone[3..] : phone;
    }

    public static bool IsFullName(string value)
    {
        if (value.Length is < 2 or > 150) return false;
        var letters = 0;
        var needsLetter = true;
        foreach (var rune in value.EnumerateRunes())
        {
            if (Rune.IsLetter(rune)) { letters++; needsLetter = false; }
            else if (!needsLetter && Rune.GetUnicodeCategory(rune) is System.Globalization.UnicodeCategory.NonSpacingMark or System.Globalization.UnicodeCategory.SpacingCombiningMark or System.Globalization.UnicodeCategory.EnclosingMark) { }
            else if (!needsLetter && rune.Value is ' ' or '\'' or '\u2019' or '-') { needsLetter = true; }
            else { return false; }
        }
        return letters >= 2 && !needsLetter;
    }
    public static bool IsPhone(string? value) => value is null || PhonePattern.IsMatch(value);
    public static bool IsEmail(string value) => value.Length <= 254 && EmailPattern.IsMatch(value);
}

[AttributeUsage(AttributeTargets.Property)]
public sealed class PersonNameAttribute : ValidationAttribute
{
    public PersonNameAttribute() : base(UserInputRules.FullNameMessage) { }
    public override bool IsValid(object? value) => value is null || value is string name && (name.Length == 0 || UserInputRules.IsFullName(name));
}

[AttributeUsage(AttributeTargets.Property)]
public sealed class VietnameseMobilePhoneAttribute : ValidationAttribute
{
    public VietnameseMobilePhoneAttribute() : base(UserInputRules.PhoneMessage) { }
    public override bool IsValid(object? value) => value is null || value is string phone && UserInputRules.IsPhone(phone);
}

[AttributeUsage(AttributeTargets.Property)]
public sealed class AccountEmailAttribute : ValidationAttribute
{
    public AccountEmailAttribute() : base(UserInputRules.EmailMessage) { }
    public override bool IsValid(object? value) => value is null || value is string email && (email.Length == 0 || UserInputRules.IsEmail(email));
}
