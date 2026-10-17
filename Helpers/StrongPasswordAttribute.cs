using System.ComponentModel.DataAnnotations;

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

            if (password.Length < MinLength)
            {
                return new ValidationResult($"Mật khẩu phải có ít nhất {MinLength} ký tự.", memberNames);
            }

            if (!char.IsUpper(password[0]))
            {
                return new ValidationResult("Ký tự đầu tiên của mật khẩu phải là chữ in hoa.", memberNames);
            }

            if (!password.Any(char.IsDigit))
            {
                return new ValidationResult("Mật khẩu phải chứa ít nhất 1 chữ số.", memberNames);
            }

            if (!password.Any(c => !char.IsLetterOrDigit(c) && !char.IsWhiteSpace(c)))
            {
                return new ValidationResult("Mật khẩu phải chứa ít nhất 1 ký tự đặc biệt (ví dụ: @, !, #, $).", memberNames);
            }

            return ValidationResult.Success;
        }
    }
}
