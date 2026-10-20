using ClothingAPI.Data;
using ClothingAPI.DTOs.Auth;
using ClothingAPI.DTOs.Users;
using ClothingAPI.Exceptions;
using ClothingAPI.Helpers;
using ClothingAPI.Models;
using ClothingAPI.Services.Interfaces;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OtpNet;
using QRCoder;
using System.Security.Cryptography;
using System.Text;

namespace ClothingAPI.Services.Implementations
{
    /// Xác thực hai lớp bằng TOTP (RFC 6238: SHA-1, 6 chữ số, chu kỳ 30 giây – tương thích Google Authenticator).
    /// TUYỆT ĐỐI không ghi secret, mã TOTP hay mã khôi phục ra log.
    public class TwoFactorService : ITwoFactorService
    {
        private const string SecretProtectorPurpose = "ClothingAPI.TwoFactor.Secret.v1";
        private const int SecretSizeBytes = 20;
        private const int TotpDigits = 6;
        private const int TotpPeriodSeconds = 30;
        private const int ChallengeSizeBytes = 32;
        private const int MaxIpLength = 45;

        // Bỏ các ký tự dễ nhầm 0/O, 1/I/L. 31 ký tự, 12 ký tự/mã => ~59 bit.
        private const string RecoveryCodeAlphabet = "23456789ABCDEFGHJKMNPQRSTUVWXYZ";
        private const int RecoveryCodeLength = 12;
        private const int RecoveryCodeGroupSize = 4;

        private readonly AppDbContext _context;
        private readonly IDataProtector _secretProtector;
        private readonly TwoFactorSettings _settings;
        private readonly TimeProvider _timeProvider;
        private readonly IRefreshTokenService _refreshTokenService;
        private readonly ILogger<TwoFactorService> _logger;

        public TwoFactorService(
            AppDbContext context,
            IDataProtectionProvider dataProtectionProvider,
            IOptions<TwoFactorSettings> settings,
            TimeProvider timeProvider,
            IRefreshTokenService refreshTokenService,
            ILogger<TwoFactorService> logger)
        {
            _context = context;
            _secretProtector = dataProtectionProvider.CreateProtector(SecretProtectorPurpose);
            _settings = settings.Value;
            _timeProvider = timeProvider;
            _refreshTokenService = refreshTokenService;
            _logger = logger;
        }

        private DateTime UtcNow => _timeProvider.GetUtcNow().UtcDateTime;

        // ===================== Quản lý (đã đăng nhập) =====================

        public async Task<TwoFactorSetupDto> GetSetupAsync(int userId)
        {
            var user = await GetUserAsync(userId);

            if (user.TwoFactorEnabled)
            {
                throw new BadRequestException("Xác thực hai lớp đã được bật.");
            }

            // Mỗi lần bắt đầu thiết lập sinh secret mới (ghi đè secret đang thiết lập dở). Chưa bật cho tới khi xác nhận mã.
            var secret = Base32Encoding.ToString(RandomNumberGenerator.GetBytes(SecretSizeBytes));

            user.TwoFactorSecretProtected = _secretProtector.Protect(secret);
            user.TwoFactorLastUsedStep = null;
            user.UpdatedAt = UtcNow;
            await _context.SaveChangesAsync();

            var otpauthUri = new OtpUri(
                OtpType.Totp, secret, user.Email, _settings.Issuer,
                OtpHashMode.Sha1, TotpDigits, TotpPeriodSeconds, 0).ToString();

            _logger.LogInformation("UserId {UserId} bắt đầu thiết lập xác thực hai lớp.", userId);

            return new TwoFactorSetupDto
            {
                Secret = FormatInGroups(secret, 4, " "),
                OtpauthUri = otpauthUri,
                QrCodeDataUri = CreateQrCodeDataUri(otpauthUri)
            };
        }

        public async Task<EnableTwoFactorResultDto> EnableAsync(int userId, string code, string? ip)
        {
            var user = await GetUserAsync(userId);

            if (user.TwoFactorEnabled)
            {
                throw new BadRequestException("Xác thực hai lớp đã được bật.");
            }

            if (string.IsNullOrEmpty(user.TwoFactorSecretProtected))
            {
                throw new BadRequestException("Vui lòng bắt đầu thiết lập xác thực hai lớp trước.");
            }

            // Sai mã ở đây KHÔNG tính vào khóa tài khoản: người gọi đã đăng nhập và đã thấy secret, đoán mã không có lợi gì.
            if (!TryMatchTotp(user, code, UtcNow, out var step))
            {
                _logger.LogInformation("UserId {UserId} nhập sai mã xác nhận khi bật 2FA (IP {Ip}).", userId, ip);
                throw new BadRequestException("Mã xác thực không đúng. Vui lòng kiểm tra lại thời gian trên điện thoại và thử lại.");
            }

            var (entities, rawCodes) = CreateRecoveryCodes(userId);

            await using var transaction = await _context.Database.BeginTransactionAsync();

            await _context.TwoFactorRecoveryCodes.Where(x => x.UserId == userId).ExecuteDeleteAsync();

            user.TwoFactorEnabled = true;
            user.TwoFactorEnabledAt = UtcNow;
            user.TwoFactorLastUsedStep = step;
            user.UpdatedAt = UtcNow;
            _context.TwoFactorRecoveryCodes.AddRange(entities);
            await _context.SaveChangesAsync();

            // Các phiên tạo trước khi bật chưa từng qua 2FA (có thể là phiên của kẻ đã biết mật khẩu) => thu hồi hết.
            await _refreshTokenService.RevokeAllForUserAsync(userId, RefreshTokenRevokeReasons.TwoFactorEnabled);

            await transaction.CommitAsync();

            _logger.LogInformation(
                "UserId {UserId} đã BẬT xác thực hai lớp (IP {Ip}), sinh {Count} mã khôi phục, thu hồi mọi phiên cũ.",
                userId, ip, rawCodes.Count);

            return new EnableTwoFactorResultDto { RecoveryCodes = rawCodes };
        }

        public async Task DisableAsync(int userId, DisableTwoFactorDto dto, string? ip)
        {
            var user = await GetUserAsync(userId);

            if (!user.TwoFactorEnabled)
            {
                throw new BadRequestException("Xác thực hai lớp chưa được bật.");
            }

            var hasCode = !string.IsNullOrWhiteSpace(dto.Code);
            var hasRecoveryCode = !string.IsNullOrWhiteSpace(dto.RecoveryCode);

            // Thiếu dữ liệu thì từ chối trước khi kiểm tra bất cứ điều gì (không lộ mật khẩu đúng/sai, không tính lần sai).
            if (hasCode == hasRecoveryCode)
            {
                throw new BadRequestException("Vui lòng nhập mã xác thực hoặc mã khôi phục.");
            }

            var now = UtcNow;

            // Endpoint này có thể bị dùng để dò mật khẩu bằng access token bị đánh cắp => áp dụng chung bộ đếm khóa.
            if (AccountLockout.IsLockedOut(user, now, out var remainingMinutes))
            {
                _logger.LogWarning("Từ chối tắt 2FA: UserId {UserId} đang bị khóa (IP {Ip}).", userId, ip);
                throw new BadRequestException(
                    $"Tài khoản tạm khóa do nhập sai quá nhiều lần. Vui lòng thử lại sau {remainingMinutes} phút.");
            }

            var passwordOk = BCrypt.Net.BCrypt.Verify(dto.Password, user.PasswordHash);
            var secondFactorOk = false;
            TwoFactorRecoveryCode? usedRecoveryCode = null;

            if (passwordOk)
            {
                if (hasCode)
                {
                    secondFactorOk = TryMatchTotp(user, dto.Code, now, out var step) &&
                                     (user.TwoFactorLastUsedStep is null || step > user.TwoFactorLastUsedStep);
                }
                else
                {
                    usedRecoveryCode = await FindUnusedRecoveryCodeAsync(userId, dto.RecoveryCode);
                    secondFactorOk = usedRecoveryCode is not null;
                }
            }

            if (!passwordOk || !secondFactorOk)
            {
                AccountLockout.RegisterFailure(user, now, _logger, "Tắt 2FA");
                await _context.SaveChangesAsync();

                _logger.LogWarning(
                    "Tắt 2FA thất bại cho UserId {UserId} (IP {Ip}): sai mật khẩu hoặc sai mã.",
                    userId, ip);

                // Thông báo chung, không tiết lộ phần nào sai.
                throw new BadRequestException("Mật khẩu hoặc mã xác thực không đúng.");
            }

            await using var transaction = await _context.Database.BeginTransactionAsync();

            await _context.TwoFactorRecoveryCodes.Where(x => x.UserId == userId).ExecuteDeleteAsync();
            await _context.TwoFactorChallenges.Where(x => x.UserId == userId).ExecuteDeleteAsync();

            user.TwoFactorEnabled = false;
            user.TwoFactorEnabledAt = null;
            user.TwoFactorSecretProtected = null;
            user.TwoFactorLastUsedStep = null;
            user.UpdatedAt = now;
            AccountLockout.Reset(user);
            await _context.SaveChangesAsync();

            await transaction.CommitAsync();

            // Không thu hồi phiên: mọi phiên đang sống đều đã qua 2FA (bật 2FA đã thu hồi các phiên cũ).
            _logger.LogWarning(
                "UserId {UserId} đã TẮT xác thực hai lớp (IP {Ip}, xác minh bằng {Method}).",
                userId, ip, usedRecoveryCode is null ? "mã TOTP" : "mã khôi phục");
        }

        // ===================== Đăng nhập hai bước =====================

        public async Task<TwoFactorChallengeDto> CreateLoginChallengeAsync(int userId, string? ip)
        {
            var now = UtcNow;
            var rawToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(ChallengeSizeBytes));
            var expiresAt = now.AddMinutes(_settings.ChallengeMinutes);

            _context.TwoFactorChallenges.Add(new TwoFactorChallenge
            {
                UserId = userId,
                TokenHash = RefreshTokenService.HashToken(rawToken),
                CreatedAt = now,
                ExpiresAt = expiresAt,
                CreatedByIp = Truncate(ip, MaxIpLength)
            });
            await _context.SaveChangesAsync();

            _logger.LogInformation(
                "Mật khẩu đúng cho UserId {UserId} (IP {Ip}), chờ mã xác thực hai lớp.",
                userId, ip);

            return new TwoFactorChallengeDto
            {
                RequiresTwoFactor = true,
                TwoFactorToken = rawToken,
                ExpiresAt = expiresAt,
                ExpiresInSeconds = _settings.ChallengeMinutes * 60
            };
        }

        public Task<TwoFactorLoginResult> VerifyLoginCodeAsync(string? rawChallenge, string? code, string? ip)
        {
            return VerifyLoginAsync(rawChallenge, code, ip, useRecoveryCode: false);
        }

        public Task<TwoFactorLoginResult> VerifyLoginRecoveryCodeAsync(string? rawChallenge, string? recoveryCode, string? ip)
        {
            return VerifyLoginAsync(rawChallenge, recoveryCode, ip, useRecoveryCode: true);
        }

        private async Task<TwoFactorLoginResult> VerifyLoginAsync(string? rawChallenge, string? input, string? ip, bool useRecoveryCode)
        {
            var now = UtcNow;

            if (string.IsNullOrWhiteSpace(rawChallenge))
            {
                return new TwoFactorLoginResult(TwoFactorLoginStatus.ChallengeExpired);
            }

            var tokenHash = RefreshTokenService.HashToken(rawChallenge);

            var challenge = await _context.TwoFactorChallenges
                .Include(x => x.User)
                .FirstOrDefaultAsync(x => x.TokenHash == tokenHash);

            if (challenge is null)
            {
                _logger.LogInformation("Từ chối mã 2FA: challenge không tồn tại (IP {Ip}).", ip);
                return new TwoFactorLoginResult(TwoFactorLoginStatus.ChallengeExpired);
            }

            var user = challenge.User;

            if (challenge.ConsumedAt.HasValue)
            {
                _logger.LogWarning(
                    "Challenge 2FA đã dùng bị gửi lại cho UserId {UserId} (IP {Ip}).", user.UserId, ip);
                return new TwoFactorLoginResult(TwoFactorLoginStatus.ChallengeExpired);
            }

            if (challenge.ExpiresAt <= now)
            {
                _logger.LogInformation(
                    "Challenge 2FA của UserId {UserId} đã hết hạn (IP {Ip}).", user.UserId, ip);
                return new TwoFactorLoginResult(TwoFactorLoginStatus.ChallengeExpired);
            }

            if (!user.IsActive || !user.TwoFactorEnabled)
            {
                return new TwoFactorLoginResult(TwoFactorLoginStatus.ChallengeExpired);
            }

            // Kiểm tra khóa TRƯỚC khi verify mã: đang khóa thì mã đúng cũng bị từ chối.
            if (AccountLockout.IsLockedOut(user, now, out var remainingMinutes))
            {
                _logger.LogWarning(
                    "Từ chối mã 2FA: UserId {UserId} đang bị khóa đến {LockoutEnd} UTC (IP {Ip}).",
                    user.UserId, user.LockoutEndUtc, ip);
                return new TwoFactorLoginResult(TwoFactorLoginStatus.LockedOut, RemainingLockoutMinutes: remainingMinutes);
            }

            // Giữ chỗ một lượt thử NGUYÊN TỬ trước khi verify: nhiều request song song trên cùng challenge
            // cũng không vượt quá MaxChallengeAttempts lượt (UPDATE có điều kiện, chỉ request còn lượt mới cập nhật được).
            var reserved = await _context.TwoFactorChallenges
                .Where(x => x.TwoFactorChallengeId == challenge.TwoFactorChallengeId &&
                            x.ConsumedAt == null &&
                            x.FailedAttempts < _settings.MaxChallengeAttempts)
                .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.FailedAttempts, x => x.FailedAttempts + 1));

            if (reserved == 0)
            {
                _logger.LogWarning(
                    "Challenge 2FA của UserId {UserId} đã hết lượt thử ({MaxAttempts}) (IP {Ip}).",
                    user.UserId, _settings.MaxChallengeAttempts, ip);
                return new TwoFactorLoginResult(TwoFactorLoginStatus.ChallengeExpired);
            }

            var attempt = challenge.FailedAttempts + 1;

            long step = 0;
            TwoFactorRecoveryCode? recoveryCode = null;

            var matched = useRecoveryCode
                ? (recoveryCode = await FindUnusedRecoveryCodeAsync(user.UserId, input)) is not null
                : TryMatchTotp(user, input, now, out step);

            if (!matched)
            {
                var lockedNow = AccountLockout.RegisterFailure(user, now, _logger, useRecoveryCode ? "Mã khôi phục 2FA" : "Mã 2FA");
                await _context.SaveChangesAsync();

                _logger.LogWarning(
                    "{Kind} sai cho UserId {UserId} (IP {Ip}), lượt {Attempt}/{MaxAttempts} của challenge.",
                    useRecoveryCode ? "Mã khôi phục" : "Mã 2FA", user.UserId, ip, attempt, _settings.MaxChallengeAttempts);

                if (lockedNow)
                {
                    return new TwoFactorLoginResult(
                        TwoFactorLoginStatus.LockedOut,
                        RemainingLockoutMinutes: (int)AccountLockout.LockoutDuration.TotalMinutes);
                }

                if (attempt >= _settings.MaxChallengeAttempts)
                {
                    _logger.LogWarning(
                        "Challenge 2FA của UserId {UserId} đã hết lượt thử ({MaxAttempts}) (IP {Ip}).",
                        user.UserId, _settings.MaxChallengeAttempts, ip);
                    return new TwoFactorLoginResult(TwoFactorLoginStatus.ChallengeExpired);
                }

                return new TwoFactorLoginResult(TwoFactorLoginStatus.InvalidCode);
            }

            // Mã đúng nhưng thuộc bước thời gian đã dùng => phát lại. Không tính vào khóa tài khoản (không phải lần đoán),
            // nhưng đã tốn một lượt của challenge.
            if (!useRecoveryCode && user.TwoFactorLastUsedStep.HasValue && step <= user.TwoFactorLastUsedStep.Value)
            {
                _logger.LogWarning(
                    "Mã 2FA bị phát lại cho UserId {UserId} (IP {Ip}): bước {Step} <= bước đã dùng {LastUsedStep}.",
                    user.UserId, ip, step, user.TwoFactorLastUsedStep.Value);
                return new TwoFactorLoginResult(TwoFactorLoginStatus.Replayed);
            }

            await using var transaction = await _context.Database.BeginTransactionAsync();

            // Các UPDATE có điều kiện: 2 request đúng mã gửi cùng lúc thì chỉ một request thắng.
            int factorUpdated;

            if (useRecoveryCode)
            {
                factorUpdated = await _context.TwoFactorRecoveryCodes
                    .Where(x => x.TwoFactorRecoveryCodeId == recoveryCode!.TwoFactorRecoveryCodeId && x.UsedAt == null)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.UsedAt, now));
            }
            else
            {
                factorUpdated = await _context.Users
                    .Where(x => x.UserId == user.UserId &&
                                (x.TwoFactorLastUsedStep == null || x.TwoFactorLastUsedStep < step))
                    .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.TwoFactorLastUsedStep, step));
            }

            // Lượt thử thành công không tính là "sai" => trả lại lượt đã giữ chỗ.
            var challengeConsumed = await _context.TwoFactorChallenges
                .Where(x => x.TwoFactorChallengeId == challenge.TwoFactorChallengeId && x.ConsumedAt == null)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.ConsumedAt, now)
                    .SetProperty(x => x.FailedAttempts, x => x.FailedAttempts - 1));

            if (factorUpdated == 0 || challengeConsumed == 0)
            {
                await transaction.RollbackAsync();

                _logger.LogWarning(
                    "Mã 2FA / challenge của UserId {UserId} vừa được một request đồng thời sử dụng (IP {Ip}).",
                    user.UserId, ip);

                return factorUpdated == 0
                    ? new TwoFactorLoginResult(TwoFactorLoginStatus.Replayed)
                    : new TwoFactorLoginResult(TwoFactorLoginStatus.ChallengeExpired);
            }

            // Chỉ reset bộ đếm khi đã qua ĐỦ hai lớp (bước 1 không reset với user bật 2FA).
            await _context.Users
                .Where(x => x.UserId == user.UserId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.FailedLoginAttempts, 0)
                    .SetProperty(x => x.LockoutEndUtc, (DateTime?)null));

            await transaction.CommitAsync();

            if (useRecoveryCode)
            {
                var remaining = await _context.TwoFactorRecoveryCodes
                    .CountAsync(x => x.UserId == user.UserId && x.UsedAt == null);

                _logger.LogWarning(
                    "UserId {UserId} đăng nhập bằng MÃ KHÔI PHỤC (IP {Ip}), còn {Remaining} mã chưa dùng.",
                    user.UserId, ip, remaining);

                return new TwoFactorLoginResult(TwoFactorLoginStatus.Success, user.UserId, RemainingRecoveryCodes: remaining);
            }

            _logger.LogInformation("UserId {UserId} đăng nhập hai lớp thành công (IP {Ip}).", user.UserId, ip);

            return new TwoFactorLoginResult(TwoFactorLoginStatus.Success, user.UserId);
        }

        // ===================== Tiện ích =====================

        /// Chuẩn hóa mã khôi phục trước khi băm: bỏ dấu gạch/khoảng trắng, viết hoa.
        public static string NormalizeRecoveryCode(string? input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(input.Length);

            foreach (var ch in input)
            {
                if (ch != '-' && !char.IsWhiteSpace(ch))
                {
                    builder.Append(char.ToUpperInvariant(ch));
                }
            }

            return builder.ToString();
        }

        /// HMAC-SHA256 (khóa = pepper) dạng hex 64 ký tự. Mã có ~59 bit nên không cần hàm băm chậm.
        public static string HashRecoveryCode(string normalizedCode, string pepper)
        {
            return Convert.ToHexString(HMACSHA256.HashData(
                Encoding.UTF8.GetBytes(pepper),
                Encoding.UTF8.GetBytes(normalizedCode)));
        }

        private async Task<User> GetUserAsync(int userId)
        {
            var user = await _context.Users.FirstOrDefaultAsync(x => x.UserId == userId);

            if (user is null)
            {
                throw new NotFoundException("Không tìm thấy người dùng.");
            }

            return user;
        }

        private bool TryMatchTotp(User user, string? code, DateTime now, out long step)
        {
            step = 0;

            var normalized = (code ?? string.Empty).Replace(" ", string.Empty).Trim();

            if (normalized.Length != TotpDigits || !normalized.All(char.IsAsciiDigit) ||
                string.IsNullOrEmpty(user.TwoFactorSecretProtected))
            {
                return false;
            }

            var secret = _secretProtector.Unprotect(user.TwoFactorSecretProtected);
            var totp = new Totp(Base32Encoding.ToBytes(secret), TotpPeriodSeconds, OtpHashMode.Sha1, TotpDigits);

            return totp.VerifyTotp(
                now, normalized, out step,
                new VerificationWindow(_settings.VerificationWindow, _settings.VerificationWindow));
        }

        private async Task<TwoFactorRecoveryCode?> FindUnusedRecoveryCodeAsync(int userId, string? input)
        {
            var normalized = NormalizeRecoveryCode(input);

            if (normalized.Length != RecoveryCodeLength)
            {
                return null;
            }

            var codeHash = HashRecoveryCode(normalized, _settings.RecoveryCodePepper);

            return await _context.TwoFactorRecoveryCodes
                .FirstOrDefaultAsync(x => x.UserId == userId && x.CodeHash == codeHash && x.UsedAt == null);
        }

        private (List<TwoFactorRecoveryCode> Entities, List<string> RawCodes) CreateRecoveryCodes(int userId)
        {
            var entities = new List<TwoFactorRecoveryCode>();
            var rawCodes = new List<string>();

            while (rawCodes.Count < _settings.RecoveryCodeCount)
            {
                var chars = new char[RecoveryCodeLength];

                for (var i = 0; i < chars.Length; i++)
                {
                    // GetInt32 lấy mẫu không thiên lệch (khác với byte % 31).
                    chars[i] = RecoveryCodeAlphabet[RandomNumberGenerator.GetInt32(RecoveryCodeAlphabet.Length)];
                }

                var normalized = new string(chars);

                if (rawCodes.Contains(normalized))
                {
                    continue;
                }

                rawCodes.Add(normalized);
                entities.Add(new TwoFactorRecoveryCode
                {
                    UserId = userId,
                    CodeHash = HashRecoveryCode(normalized, _settings.RecoveryCodePepper),
                    CreatedAt = UtcNow
                });
            }

            return (entities, rawCodes.Select(x => FormatInGroups(x, RecoveryCodeGroupSize, "-")).ToList());
        }

        private static string CreateQrCodeDataUri(string content)
        {
            using var generator = new QRCodeGenerator();
            using var data = generator.CreateQrCode(content, QRCodeGenerator.ECCLevel.Q);
            var png = new PngByteQRCode(data).GetGraphic(6);

            return $"data:image/png;base64,{Convert.ToBase64String(png)}";
        }

        private static string FormatInGroups(string value, int groupSize, string separator)
        {
            return string.Join(separator, value.Chunk(groupSize).Select(x => new string(x)));
        }

        private static string? Truncate(string? value, int maxLength)
        {
            if (string.IsNullOrEmpty(value))
            {
                return null;
            }

            return value.Length <= maxLength ? value : value[..maxLength];
        }
    }
}
