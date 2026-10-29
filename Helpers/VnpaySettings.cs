namespace ClothingAPI.Helpers;

public sealed class VnpaySettings
{
    public bool Enabled { get; set; }
    public string TmnCode { get; set; } = "";
    public string HashSecret { get; set; } = "";
    public string PublicBaseUrl { get; set; } = "http://localhost:4200";
    public string PaymentUrl { get; set; } = "https://sandbox.vnpayment.vn/paymentv2/vpcpay.html";
    public string TransactionApiUrl { get; set; } = "https://sandbox.vnpayment.vn/merchant_webapi/api/transaction";
    public int PaymentMinutes { get; set; } = 15;
    public int OrderMinutes { get; set; } = 30;
    public bool WorkerEnabled { get; set; } = true;
    // Only these frontend origins and PublicBaseUrl can be used for browser redirects.
    public string[] AllowedFrontendOrigins { get; set; } = ["http://localhost:4200", "http://127.0.0.1:4200"];

    public bool IsConfigured => Enabled && !string.IsNullOrWhiteSpace(TmnCode)
        && !string.IsNullOrWhiteSpace(HashSecret)
        && Uri.TryCreate(PublicBaseUrl, UriKind.Absolute, out var uri)
        && (uri.Scheme == "https" || (uri.Scheme == "http" && uri.IsLoopback));

    public string ReturnUrl => PublicBaseUrl.TrimEnd('/') + "/api/payments/vnpay/return";
    public string ResultUrl => PublicBaseUrl.TrimEnd('/') + "/checkout/vnpay-result";

    public string ResolveFrontendOrigin(string? requestedOrigin)
    {
        if (string.IsNullOrWhiteSpace(requestedOrigin)) return PublicBaseUrl.TrimEnd('/');
        if (!Uri.TryCreate(requestedOrigin, UriKind.Absolute, out var uri)
            || !string.IsNullOrEmpty(uri.UserInfo) || uri.AbsolutePath != "/"
            || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new Exceptions.BadRequestException("Địa chỉ quay lại thanh toán không hợp lệ.");
        var origin = uri.GetLeftPart(UriPartial.Authority);
        if (!origin.Equals(PublicBaseUrl.TrimEnd('/'), StringComparison.OrdinalIgnoreCase)
            && !AllowedFrontendOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase))
            throw new Exceptions.BadRequestException("Địa chỉ quay lại thanh toán chưa được cho phép.");
        return origin;
    }

    public string ResultUrlForAttempt(string paymentUrl)
    {
        // Use only the return URL saved when we signed this attempt; never a redirect target from callback parameters.
        if (Uri.TryCreate(paymentUrl, UriKind.Absolute, out var payment))
        {
            var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(payment.Query);
            if (query.TryGetValue("vnp_ReturnUrl", out var returnValue)
                && Uri.TryCreate(returnValue.ToString(), UriKind.Absolute, out var callback)
                && callback.AbsolutePath == "/api/payments/vnpay/return")
            {
                try { return ResolveFrontendOrigin(callback.GetLeftPart(UriPartial.Authority)) + "/checkout/vnpay-result"; }
                catch (Exceptions.BadRequestException) { }
            }
        }
        return ResultUrl;
    }
}
