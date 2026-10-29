using System.ComponentModel.DataAnnotations;

using System.Text;

namespace ClothingAPI.Helpers
{
    /// <summary>
    /// Mật khẩu mạnh: tối thiểu 8 ký tự, ký tự đầu viết hoa, có ít nhất 1 chữ số và 1 ký tự đặc biệt.
    /// Trả về đúng lỗi đầu tiên vi phạm để người dùng biết mình sai ở đâu.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property)]
    public class StrongPasswordAttribute : ValidationAttribute
    {
        public const int MinLength = 8;
        public const int MaxUtf8Bytes = 72;

        protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
        {
            // Để [Required] xử lý trường hợp bỏ trống.
            if (value is not string password || password.Length == 0)
            {
                return ValidationResult.Success;
            }

            var memberNames = validationContext.MemberName is null
                ? null
                : new[] { validationContext.MemberName };

            // BCrypt considers at most 72 bytes. Reject, never silently truncate.
            if (Encoding.UTF8.GetByteCount(password) > MaxUtf8Bytes)
            {
                return new ValidationResult("Mật khẩu quá dài. Vui lòng dùng mật khẩu ngắn hơn.", memberNames);
            }

            if (password.EnumerateRunes().Any(r => Rune.GetUnicodeCategory(r) is System.Globalization.UnicodeCategory.Control or System.Globalization.UnicodeCategory.Format))
            {
                return new ValidationResult("Mật khẩu không được chứa ký tự điều khiển hoặc ký tự ẩn.", memberNames);
            }

            if (password.Length < MinLength)
            {
                return new ValidationResult($"Mật khẩu phải có ít nhất {MinLength} ký tự.", memberNames);
            }

            if (!Rune.IsUpper(password.EnumerateRunes().First()))
            {
                return new ValidationResult("Ký tự đầu tiên của mật khẩu phải là chữ in hoa.", memberNames);
            }

            if (!password.Any(c => c is >= '0' and <= '9'))
            {
                return new ValidationResult("Mật khẩu phải chứa ít nhất 1 chữ số.", memberNames);
            }

            if (!password.EnumerateRunes().Any(r => Rune.IsPunctuation(r) || Rune.IsSymbol(r)))
            {
                return new ValidationResult("Mật khẩu phải chứa ít nhất 1 ký tự đặc biệt (ví dụ: @, !, #, $).", memberNames);
            }

            return ValidationResult.Success;
        }
    }
}
