using ClothingAPI.Data;
using ClothingAPI.DTOs.Auth;
using ClothingAPI.Helpers;
using ClothingAPI.Models;
using ClothingAPI.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;

namespace ClothingAPI.Services.Implementations
{
    public class RefreshTokenService : IRefreshTokenService
    {
        private const int MaxIpLength = 45;
        private const int MaxUserAgentLength = 512;

        private readonly AppDbContext _context;
        private readonly RefreshTokenSettings _settings;
        private readonly ILogger<RefreshTokenService> _logger;

        public RefreshTokenService(
            AppDbContext context,
            IOptions<RefreshTokenSettings> settings,
            ILogger<RefreshTokenService> logger)
        {
            _context = context;
            _settings = settings.Value;
            _logger = logger;
        }

        public async Task<IssuedRefreshToken> IssueAsync(int userId, string? ip, string? userAgent)
        {
            var expiresAt = DateTime.UtcNow.AddDays(_settings.ExpirationDays);

            var (entity, issued) = CreateToken(userId, Guid.NewGuid(), expiresAt, ip, userAgent);

            _context.RefreshTokens.Add(entity);
            await _context.SaveChangesAsync();

            return issued;
        }

        public async Task<RefreshRotationResult> RotateAsync(string? rawToken, string? ip, string? userAgent)
        {
            // Không có cookie là chuyện bình thường (khách chưa đăng nhập) => từ chối im lặng, không ghi log.
            if (string.IsNullOrWhiteSpace(rawToken))
            {
                return new RefreshRotationResult(RefreshRotationStatus.Missing);
            }

            var tokenHash = HashToken(rawToken);

            // Xác định người dùng CHỈ qua DB (hash cookie -> RefreshTokens -> UserId), không tin dữ liệu nào từ client.
            var token = await _context.RefreshTokens
                .AsNoTracking()
                .Include(x => x.User)
                .FirstOrDefaultAsync(x => x.TokenHash == tokenHash);

            if (token is null)
            {
                _logger.LogInformation("Từ chối refresh: token không tồn tại (IP {Ip}).", ip);
                return new RefreshRotationResult(RefreshRotationStatus.Invalid);
            }

            var now = DateTime.UtcNow;

            if (token.RevokedAt.HasValue)
            {
                var withinGrace = token.RevokedReason == RefreshTokenRevokeReasons.Rotated &&
                                  now - token.RevokedAt.Value <= TimeSpan.FromSeconds(_settings.ReuseGraceSeconds);

                if (withinGrace)
                {
                    _logger.LogInformation(
                        "Từ chối refresh: token của UserId {UserId} vừa được xoay vòng {Seconds:0.0} giây trước (request đồng thời), không thu hồi family {FamilyId}.",
                        token.UserId, (now - token.RevokedAt.Value).TotalSeconds, token.FamilyId);

                    return new RefreshRotationResult(RefreshRotationStatus.ConcurrentReuse);
                }

                // Token đã bị thu hồi mà vẫn được gửi lại => có thể đã bị đánh cắp. Thu hồi cả phiên (family).
                var revokedCount = await RevokeActiveInFamilyAsync(token.FamilyId, RefreshTokenRevokeReasons.ReuseDetected, now);

                _logger.LogWarning(
                    "Phát hiện dùng lại refresh token đã thu hồi (lý do trước đó: {PreviousReason}) của UserId {UserId}, FamilyId {FamilyId}, IP {Ip}. Đã thu hồi {RevokedCount} token còn hiệu lực trong family.",
                    token.RevokedReason, token.UserId, token.FamilyId, ip, revokedCount);

                return new RefreshRotationResult(RefreshRotationStatus.ReuseDetected);
            }

            if (token.ExpiresAt <= now)
            {
                return new RefreshRotationResult(RefreshRotationStatus.Expired);
            }

            if (!token.User.IsActive)
            {
                await RevokeActiveInFamilyAsync(token.FamilyId, RefreshTokenRevokeReasons.UserInactive, now);
                return new RefreshRotationResult(RefreshRotationStatus.Inactive);
            }

            // Lưu ý: KHÔNG chặn theo LockoutEndUtc. Lockout chống dò mật khẩu; nếu chặn cả refresh thì kẻ tấn công
            // chỉ cần cố tình nhập sai 5 lần là đá được người dùng thật ra khỏi mọi phiên.

            await using var transaction = await _context.Database.BeginTransactionAsync();

            // Token mới kế thừa ExpiresAt của token cũ => phiên có hạn tuyệt đối, không gia hạn vô tận.
            var (newEntity, issued) = CreateToken(token.UserId, token.FamilyId, token.ExpiresAt, ip, userAgent);

            _context.RefreshTokens.Add(newEntity);
            await _context.SaveChangesAsync();

            // UPDATE có điều kiện "RevokedAt IS NULL": trong 2 request refresh đồng thời chỉ một request cập nhật được dòng này.
            var updated = await _context.RefreshTokens
                .Where(x => x.RefreshTokenId == token.RefreshTokenId && x.RevokedAt == null)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.RevokedAt, now)
                    .SetProperty(x => x.RevokedReason, RefreshTokenRevokeReasons.Rotated)
                    .SetProperty(x => x.ReplacedByTokenId, newEntity.RefreshTokenId));

            if (updated == 0)
            {
                // Request khác đã xoay token này trước => huỷ token vừa tạo.
                await transaction.RollbackAsync();
                _context.Entry(newEntity).State = EntityState.Detached;

                _logger.LogInformation(
                    "Từ chối refresh: token của UserId {UserId} đã được một request đồng thời xoay vòng trước.",
                    token.UserId);

                return new RefreshRotationResult(RefreshRotationStatus.ConcurrentReuse);
            }

            await transaction.CommitAsync();

            return new RefreshRotationResult(RefreshRotationStatus.Success, token.UserId, issued);
        }

        public async Task RevokeFamilyAsync(string? rawToken, string reason)
        {
            if (string.IsNullOrWhiteSpace(rawToken))
            {
                return;
            }

            var tokenHash = HashToken(rawToken);

            var familyId = await _context.RefreshTokens
                .Where(x => x.TokenHash == tokenHash)
                .Select(x => (Guid?)x.FamilyId)
                .FirstOrDefaultAsync();

            if (familyId is null)
            {
                return;
            }

            await RevokeActiveInFamilyAsync(familyId.Value, reason, DateTime.UtcNow);
        }

        public async Task RevokeAllForUserAsync(int userId, string reason)
        {
            var revokedCount = await _context.RefreshTokens
                .Where(x => x.UserId == userId && x.RevokedAt == null)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.RevokedAt, DateTime.UtcNow)
                    .SetProperty(x => x.RevokedReason, reason));

            _logger.LogInformation(
                "Đã thu hồi {RevokedCount} refresh token của UserId {UserId} (lý do: {Reason}).",
                revokedCount, userId, reason);
        }

        /// Token có 256 bit ngẫu nhiên nên SHA-256 là đủ (không cần hàm băm chậm như BCrypt).
        public static string HashToken(string rawToken)
        {
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));
        }

        private Task<int> RevokeActiveInFamilyAsync(Guid familyId, string reason, DateTime now)
        {
            return _context.RefreshTokens
                .Where(x => x.FamilyId == familyId && x.RevokedAt == null)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.RevokedAt, now)
                    .SetProperty(x => x.RevokedReason, reason));
        }

        private static (RefreshToken Entity, IssuedRefreshToken Issued) CreateToken(
            int userId, Guid familyId, DateTime expiresAt, string? ip, string? userAgent)
        {
            var rawToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

            var entity = new RefreshToken
            {
                UserId = userId,
                TokenHash = HashToken(rawToken),
                FamilyId = familyId,
                ExpiresAt = expiresAt,
                CreatedAt = DateTime.UtcNow,
                CreatedByIp = Truncate(ip, MaxIpLength),
                UserAgent = Truncate(userAgent, MaxUserAgentLength)
            };

            return (entity, new IssuedRefreshToken(rawToken, expiresAt));
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
