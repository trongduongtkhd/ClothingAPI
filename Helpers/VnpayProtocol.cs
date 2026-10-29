using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace ClothingAPI.Helpers;

public static class VnpayProtocol
{
    public static string Encode(string value) => WebUtility.UrlEncode(value);

    public static string QueryString(IEnumerable<KeyValuePair<string, string>> fields) => string.Join("&",
        fields.Where(x => x.Key.StartsWith("vnp_", StringComparison.Ordinal)
            && x.Key is not "vnp_SecureHash" and not "vnp_SecureHashType" && !string.IsNullOrEmpty(x.Value))
        .OrderBy(x => x.Key, StringComparer.Ordinal)
        .Select(x => Encode(x.Key) + "=" + Encode(x.Value)));

    public static string Hash(string secret, string data) =>
        Convert.ToHexString(HMACSHA512.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(data))).ToLowerInvariant();

    public static bool VerifyHash(string secret, string data, string? signature)
    {
        if (string.IsNullOrWhiteSpace(secret) || signature?.Length != 128) return false;
        try
        {
            return CryptographicOperations.FixedTimeEquals(Convert.FromHexString(Hash(secret, data)), Convert.FromHexString(signature));
        }
        catch (FormatException) { return false; }
    }

    public static bool Verify(string secret, IReadOnlyDictionary<string, string> fields) =>
        fields.TryGetValue("vnp_SecureHash", out var hash) && VerifyHash(secret, QueryString(fields), hash);

    public static string Amount(decimal amount)
    {
        if (amount < 5000 || amount != decimal.Truncate(amount) || amount > 9999999999m)
            throw new Exceptions.BadRequestException("VNPAY hỗ trợ đơn từ 5.000đ, số tiền phải là số nguyên VND và nằm trong hạn mức thanh toán.");
        return checked((long)(amount * 100)).ToString(CultureInfo.InvariantCulture);
    }

    public static string Date(DateTime utc) => utc.AddHours(7).ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
}
