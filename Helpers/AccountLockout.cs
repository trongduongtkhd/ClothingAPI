using ClothingAPI.Models;

namespace ClothingAPI.Helpers
{
    /// Chống brute-force dùng chung cho mật khẩu VÀ mã 2FA: cùng một bộ đếm FailedLoginAttempts / LockoutEndUtc.
    /// Các hàm chỉ thay đổi entity User, nơi gọi tự SaveChanges.
    public static class AccountLockout
    {
        public const int MaxFailedAttempts = 5;
        public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

        public static bool IsLockedOut(User user, DateTime now, out int remainingMinutes)
        {
            if (user.LockoutEndUtc.HasValue && user.LockoutEndUtc.Value > now)
            {
                remainingMinutes = (int)Math.Ceiling((user.LockoutEndUtc.Value - now).TotalMinutes);
                return true;
            }

            remainingMinutes = 0;
            return false;
        }

        /// Ghi nhận một lần sai (mật khẩu hoặc mã 2FA). Trả về true nếu lần sai này làm tài khoản bị khóa.
        public static bool RegisterFailure(User user, DateTime now, ILogger logger, string attemptKind)
        {
            user.FailedLoginAttempts += 1;

            logger.LogWarning(
                "{AttemptKind} sai lần {Attempt}/{MaxAttempts} cho tài khoản {Email}.",
                attemptKind, user.FailedLoginAttempts, MaxFailedAttempts, user.Email
            );

            if (user.FailedLoginAttempts < MaxFailedAttempts)
            {
                return false;
            }

            user.LockoutEndUtc = now.Add(LockoutDuration);
            user.FailedLoginAttempts = 0;

            logger.LogWarning(
                "Tài khoản {Email} bị khóa đến {LockoutEnd} UTC do nhập sai {MaxAttempts} lần liên tiếp.",
                user.Email, user.LockoutEndUtc.Value, MaxFailedAttempts
            );

            return true;
        }

        public static void Reset(User user)
        {
            user.FailedLoginAttempts = 0;
            user.LockoutEndUtc = null;
        }
    }
}
