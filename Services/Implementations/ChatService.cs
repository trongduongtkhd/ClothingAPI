using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ClothingAPI.Data;
using ClothingAPI.DTOs.Chat;
using ClothingAPI.Helpers;
using ClothingAPI.Models;
using ClothingAPI.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace ClothingAPI.Services.Implementations;

public class ChatService(AppDbContext db, IChatIntentClient ai) : IChatService
{
    private static readonly CultureInfo Vietnamese = CultureInfo.GetCultureInfo("vi-VN");
    private static readonly string[] DefaultSuggestions = ["Tìm áo dưới 200 nghìn", "Phí vận chuyển", "Chính sách trả hàng"];

    public async Task<ChatResponseDto> ReplyAsync(ChatRequestDto request, CancellationToken cancellationToken)
    {
        request.Message = request.Message.Trim();
        if (request.Message.Length == 0)
            throw new Exceptions.BadRequestException("Bạn hãy nhập câu hỏi nhé.");

        var message = Normalize(request.Message);
        var quick = PolicyReply(message);
        if (quick is not null) return quick;

        // Chỉ nạp dữ liệu công khai. Không đưa tài khoản, đơn hàng hay thông tin nội bộ cho AI.
        var products = await db.Products.AsNoTracking()
            .Where(p => p.IsActive && p.Category.IsActive)
            .Include(p => p.Category).Include(p => p.Brand)
            .Include(p => p.ProductImages)
            .Include(p => p.ProductVariants.Where(v => v.IsActive)).ThenInclude(v => v.Color)
            .Include(p => p.ProductVariants.Where(v => v.IsActive)).ThenInclude(v => v.Size)
            .AsSplitQuery().OrderByDescending(p => p.CreatedAt).ToListAsync(cancellationToken);
        var categories = await db.Categories.AsNoTracking().ToDictionaryAsync(c => c.CategoryId, cancellationToken);

        string CategoryPath(Product product)
        {
            var names = new List<string>();
            var seen = new HashSet<int>();
            var current = product.Category;
            while (current is not null && seen.Add(current.CategoryId))
            {
                names.Insert(0, current.CategoryName);
                current = current.ParentCategoryId.HasValue
                    ? categories.GetValueOrDefault(current.ParentCategoryId.Value) : null;
            }
            return string.Join(" / ", names);
        }

        var catalog = products.Take(40).Select(p => new ChatCatalogEntry(p.ProductId,
            Clip(p.ProductName, 100), Clip(CategoryPath(p), 100), p.Brand?.BrandName)).ToList();
        var intent = await ai.InterpretAsync(request, catalog, cancellationToken);
        if (intent.Intent == "unsupported")
            return Answer("Mình hỗ trợ sản phẩm và chính sách của Clothing Store. Bạn muốn tìm món đồ nào hoặc cần kiểm tra giá, màu và size?", true);
        var intentPolicy = PolicyReply(intent.Intent switch
        {
            "shipping" => "phi ship", "returns" => "tra hang", "size_help" => "tu van size",
            "greeting" => "xin chao", _ => ""
        });
        if (intentPolicy is not null)
        {
            intentPolicy.UsedAi = true;
            return intentPolicy;
        }

        var publicIds = products.Select(p => p.ProductId).ToHashSet();
        var chosenIds = new List<int>();
        var remainingName = message;
        foreach (var product in products.OrderByDescending(p => p.ProductName.Length))
        {
            if (!ContainsPhrase(remainingName, product.ProductName)) continue;
            chosenIds.Add(product.ProductId);
            remainingName = remainingName.Replace(Normalize(product.ProductName), " ");
        }
        if (chosenIds.Count > 0 && intent.Intent == "search") intent.Intent = "details";
        // Tên như “Áo Thun Trắng” là tên sản phẩm, không phải bộ lọc màu cho mọi sản phẩm đang so sánh.
        ApplyExplicitFilters(intent, remainingName, products);
        if (intent.MinPrice.HasValue && intent.MaxPrice.HasValue && intent.MinPrice > intent.MaxPrice)
            return Answer("Khoảng giá bắt đầu đang lớn hơn khoảng giá kết thúc. Bạn nhập lại ngân sách giúp mình nhé.", true);
        var references = request.ProductIds.Where(publicIds.Contains).Distinct().Take(5).ToList();
        var referenceIndex = ExplicitReferenceIndex(message);
        if (referenceIndex.HasValue)
        {
            if (referenceIndex < 1 || referenceIndex > references.Count)
                return Answer("Bạn muốn hỏi sản phẩm nào? Hãy mở trang sản phẩm rồi chọn “Hỏi về sản phẩm này”, hoặc nhập tên sản phẩm.", true);
            chosenIds = [references[referenceIndex.Value - 1]];
            intent.Keywords = null;
        }
        else if (!string.IsNullOrEmpty(request.ProductSlug) && ContainsPhrase(message, "san pham nay"))
        {
            var current = products.FirstOrDefault(p => p.Slug == request.ProductSlug);
            if (current is not null) { chosenIds = [current.ProductId]; intent.Keywords = null; }
        }
        else if (intent.UseContext && chosenIds.Count == 0)
        {
            var current = products.FirstOrDefault(p => p.Slug == request.ProductSlug);
            chosenIds = current is not null ? [current.ProductId] : references;
            if (chosenIds.Count > 0) intent.Keywords = null;
        }
        else if (intent.Intent == "details" && chosenIds.Count == 0 && !string.IsNullOrEmpty(request.ProductSlug))
        {
            var current = products.FirstOrDefault(p => p.Slug == request.ProductSlug);
            if (current is not null) chosenIds = [current.ProductId];
        }

        if (Regex.IsMatch(message, @"\b(re hon|gia thap hon)\b") && references.Count > 0)
        {
            chosenIds.Clear();
            var previous = products.Where(p => references.Contains(p.ProductId)).ToList();
            intent.MaxPrice = Math.Max(0, previous.Min(MinPrice) - 1);
            intent.Sort = "priceAsc";
            intent.Intent = "search";
            intent.Category = previous[0].Category.CategoryName;
        }

        if (intent.Intent is "details" or "compare" && chosenIds.Count == 0 && SearchWords(intent.Keywords).Length == 0)
            return Answer("Bạn muốn kiểm tra sản phẩm nào? Hãy nhập tên sản phẩm, hoặc mở trang chi tiết và chọn “Hỏi về sản phẩm đang xem”.", true);

        IEnumerable<Product> matches = products;
        if (chosenIds.Count > 0)
        {
            matches = matches.Where(p => chosenIds.Contains(p.ProductId));
        }
        else
        {
            var words = SearchWords(intent.Keywords);
            if (words.Length > 0)
                matches = matches.Where(p => words.All(w => Normalize(p.ProductName + " " + p.Material + " " + p.ShortDescription).Contains(w)));
        }
        if (!string.IsNullOrWhiteSpace(intent.Category))
            matches = matches.Where(p => ContainsPhrase(CategoryPath(p), intent.Category));
        if (!string.IsNullOrWhiteSpace(intent.Brand))
            matches = matches.Where(p => ContainsPhrase(p.Brand?.BrandName ?? "", intent.Brand));
        if (Enum.TryParse<Enums.Gender>(intent.Gender, true, out var gender) && Enum.IsDefined(gender))
            matches = matches.Where(p => p.Gender == gender);

        // Màu + size + giá phải cùng thuộc MỘT biến thể, không ghép từ các biến thể khác nhau.
        bool VariantMatches(ProductVariant variant, Product product) =>
            (string.IsNullOrWhiteSpace(intent.Color) || ContainsPhrase(variant.Color.ColorName, intent.Color)) &&
            (string.IsNullOrWhiteSpace(intent.Size) || Normalize(variant.Size.SizeName) == Normalize(intent.Size)) &&
            (!intent.MinPrice.HasValue || UnitPrice(variant, product) >= intent.MinPrice.Value) &&
            (!intent.MaxPrice.HasValue || UnitPrice(variant, product) <= intent.MaxPrice.Value);

        var hasVariantFilter = !string.IsNullOrWhiteSpace(intent.Color) || !string.IsNullOrWhiteSpace(intent.Size)
            || intent.MinPrice.HasValue || intent.MaxPrice.HasValue;
        if (hasVariantFilter)
            matches = matches.Where(p => p.ProductVariants.Any(v => VariantMatches(v, p)));

        var matchedProducts = matches.ToList();
        if (intent.Sort == "priceAsc") matchedProducts = matchedProducts.OrderBy(MinPrice).ToList();
        else if (intent.Sort == "priceDesc") matchedProducts = matchedProducts.OrderByDescending(MinPrice).ToList();
        if (chosenIds.Count > 0 && intent.Sort is null)
            matchedProducts = matchedProducts.OrderBy(p => chosenIds.IndexOf(p.ProductId)).ToList();

        if (matchedProducts.Count == 0)
            return Answer("Shop chưa tìm thấy sản phẩm khớp tất cả tiêu chí bạn chọn. Bạn muốn đổi màu, size hoặc khoảng giá không?", true,
                ["Xem tất cả sản phẩm", "Tìm áo dưới 200 nghìn", "Tìm quần"]);

        var selected = matchedProducts.Take(5).ToList();
        var cards = selected.Select(p => MapProduct(p,
            hasVariantFilter ? p.ProductVariants.Where(v => VariantMatches(v, p)) : p.ProductVariants)).ToList();
        string reply;
        if (intent.Intent == "compare")
        {
            if (cards.Count < 2)
                reply = "Mình mới xác định được một sản phẩm. Bạn nhập thêm tên sản phẩm muốn so sánh nhé.\n\n" + Describe(cards[0]);
            else
                reply = "Thông tin so sánh từ dữ liệu hiện tại của shop:\n\n" + string.Join("\n\n", cards.Take(3).Select(Describe));
        }
        else if (intent.Intent == "details" || chosenIds.Count == 1 || (cards.Count == 1 && hasVariantFilter))
        {
            reply = string.Join("\n\n", cards.Select(Describe));
        }
        else
        {
            reply = $"Mình tìm thấy {matchedProducts.Count} sản phẩm phù hợp" +
                (matchedProducts.Count > 5 ? ", đang hiển thị 5 sản phẩm đầu tiên." : ".") +
                " Bạn xem các sản phẩm bên dưới nhé. Giá hiển thị theo các biến thể đang được liệt kê.";
        }

        return new ChatResponseDto
        {
            Reply = reply, Products = cards, UsedAi = true,
            Suggestions = cards.Count > 1 ? ["Cái đầu tiên còn màu và size nào?", "So sánh các sản phẩm này", "Phí vận chuyển"]
                : ["Sản phẩm này có màu và size nào?", "Chính sách trả hàng", "Tìm sản phẩm khác"]
        };
    }

    private static ChatResponseDto? PolicyReply(string message)
    {
        if (Regex.IsMatch(message, @"\b(tra hang|doi tra|doi hang|hoan tra|hoan tien)\b"))
            return Answer($"Shop tiếp nhận yêu cầu trả hàng trong {ReturnPolicy.WindowDays} ngày kể từ khi đơn hàng hoàn thành. " +
                "Đơn phải đã thanh toán và chưa có yêu cầu trả hàng trước đó. Bạn đăng nhập, mở “Đơn hàng của tôi”, chọn đơn rồi gửi yêu cầu; shop sẽ xem xét và phản hồi.", false);
        if (Regex.IsMatch(message, @"\b(phi ship|phi van chuyen|mien phi van chuyen|freeship|giao hang|van chuyen)\b"))
            return Answer($"Phí vận chuyển tiêu chuẩn là {Money(ShippingHelper.StandardShippingFee)}. " +
                $"Đơn có tạm tính từ {Money(ShippingHelper.FreeShippingThreshold)} được miễn phí vận chuyển; ngưỡng này tính trước giảm giá. " +
                "Shop chưa có thông tin thời gian giao hàng cụ thể trong dữ liệu hiện tại.", false);
        if (Regex.IsMatch(message, @"\b(tu van size|chon size phu hop|cao \d+|can nang|nang \d+|vua nguoi)\b"))
            return Answer("Shop hiện có tên size và tồn kho, nhưng chưa có bảng số đo riêng đủ để xác định size theo cơ thể của bạn. " +
                "Bạn có thể xem bảng size tham khảo ở trang chi tiết áo. Hãy cho mình tên sản phẩm để kiểm tra các size đang bán nhé.", false);
        if (Regex.IsMatch(message, @"\b(don hang|mat khau|tai khoan|thanh toan ho|huy don|ngoai pham vi)\b"))
            return Answer("Mình hỗ trợ tìm và so sánh sản phẩm, kiểm tra giá, màu, size, tồn kho và chính sách của shop. " +
                "Với đơn hàng cá nhân, bạn vui lòng đăng nhập và mở “Đơn hàng của tôi” để xem hoặc thao tác.", false);
        if (Regex.IsMatch(message, @"^(xin chao|chao|hello|hi|cam on|thanks)[!.\s]*$"))
            return Answer("Chào bạn! Mình là trợ lý mua sắm của Clothing Store. Bạn đang tìm áo, quần hay muốn kiểm tra màu và size của sản phẩm nào?", false);
        return null;
    }

    public static string Normalize(string value)
    {
        var text = new string(value.Normalize(NormalizationForm.FormD)
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray());
        return Regex.Replace(text.Replace('đ', 'd').Replace('Đ', 'D').ToLowerInvariant(), @"\s+", " ").Trim();
    }

    private static bool ContainsPhrase(string source, string value) =>
        (" " + Regex.Replace(Normalize(source), @"[^a-z0-9]+", " ") + " ")
            .Contains(" " + Regex.Replace(Normalize(value), @"[^a-z0-9]+", " ") + " ");

    private static string[] SearchWords(string? value) => Normalize(value ?? "")
        .Split(' ', StringSplitOptions.RemoveEmptyEntries)
        .Where(w => !new[] { "tim", "mua", "san", "pham", "cho", "toi", "co", "khong", "gia", "duoi", "tren", "size", "mau", "nao", "con", "hang", "cai", "nay" }.Contains(w))
        .Take(6).ToArray();

    private static void ApplyExplicitFilters(ChatIntent intent, string message, List<Product> products)
    {
        intent.MinPrice = null; intent.MaxPrice = null; intent.Color = null; intent.Size = null;
        intent.Brand = null; intent.Gender = null; intent.Category = null; intent.Sort = null;
        intent.UseContext = Regex.IsMatch(message, @"\b(cai|chiec|san pham|ao|quan) (nay|do|dau|thu|so)\b|\b(con mau khac|cac san pham nay|hai san pham nay)\b");
        if (Regex.IsMatch(message, @"\b(tat ca san pham|san pham khac|xem tat ca)\b"))
        {
            intent.Keywords = null; intent.Category = null; intent.Brand = null;
            intent.Gender = null; intent.Color = null; intent.Size = null; intent.MinPrice = null;
            intent.MaxPrice = null; intent.UseContext = false; intent.Intent = "search";
        }
        if (Regex.IsMatch(message, @"\b(nam|nu)\b") && !Regex.IsMatch(message, @"\bgioi tinh\b"))
        {
            intent.Category = Regex.IsMatch(message, @"\bnu\b") ? "Nữ" : "Nam";
            intent.Gender = null;
            if (intent.Keywords is not null)
                intent.Keywords = Regex.Replace(Normalize(intent.Keywords), @"\b(nam|nu)\b", "").Trim();
        }
        if (Regex.IsMatch(message, @"\bunisex\b")) intent.Gender = "Unisex";
        if (Regex.IsMatch(message, @"\bgioi tinh nam\b")) intent.Gender = "Male";
        if (Regex.IsMatch(message, @"\bgioi tinh nu\b")) intent.Gender = "Female";
        foreach (var brand in products.Select(p => p.Brand?.BrandName).Where(b => b is not null).Distinct())
            if (ContainsPhrase(message, brand!)) intent.Brand = brand;
        foreach (var color in products.SelectMany(p => p.ProductVariants).Select(v => v.Color.ColorName).Distinct().OrderByDescending(c => c.Length))
            if (ContainsPhrase(message, color)) { intent.Color = color; break; }
        // Giữ cả màu/size chưa bán để trả kết quả rỗng, thay vì âm thầm bỏ điều kiện của khách.
        const string stopWords = "va|size|kich|gia|duoi|tren|tu|khong|con|co|nao|gi|khac|nhung|cua|thuong|phu|nay|voi";
        var explicitColor = Regex.Match(message, @"\bmau\s+((?!(?:va|size|nao|gi|khac|phu)\b)[a-z]+(?:\s+(?!(?:" + stopWords + @")\b)[a-z]+){0,2})");
        if (explicitColor.Success) intent.Color = explicitColor.Groups[1].Value;
        var explicitBrand = Regex.Match(message, @"\bthuong hieu\s+([a-z0-9]+(?:\s+(?!(?:" + stopWords + @")\b)[a-z0-9]+){0,2})");
        if (explicitBrand.Success) intent.Brand = explicitBrand.Groups[1].Value;
        var size = Regex.Match(message, @"\b(?:size|kich thuoc)\s*([a-z0-9]+)\b");
        if (size.Success && !new[] { "nao", "gi", "khac", "va", "cua", "phu", "vua", "cho", "tu" }.Contains(size.Groups[1].Value))
            intent.Size = size.Groups[1].Value.ToUpperInvariant();
        if (intent.Keywords is not null)
        {
            var keywords = Normalize(intent.Keywords);
            foreach (var term in new[] { intent.Category, intent.Brand, intent.Color, intent.Size, "unisex" }.Where(t => !string.IsNullOrWhiteSpace(t)))
                keywords = Regex.Replace(keywords, @"\b" + Regex.Escape(Normalize(term!)) + @"\b", " ");
            intent.Keywords = Regex.Replace(keywords, @"\d+|\b(nam|nu|nghin|ngan|trieu)\b", " ").Trim();
        }

        const string amount = @"(\d+(?:[.,]\d+)*)(?:\s*)(trieu|tr|nghin|ngan|k|vnd|dong|d|₫)?";
        static decimal? ReadAmount(Match match, int numberGroup, int unitGroup, string? fallbackUnit = null)
        {
            var raw = match.Groups[numberGroup].Value;
            var unit = match.Groups[unitGroup].Value;
            if (unit.Length == 0 && fallbackUnit is not null && raw.Length <= 3) unit = fallbackUnit;
            var multiplier = unit is "trieu" or "tr" ? 1000000m : unit is "k" or "nghin" or "ngan" ? 1000m : 1m;
            var numeric = multiplier > 1 ? raw.Replace(',', '.') : raw.Replace(".", "").Replace(",", "");
            return decimal.TryParse(numeric, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value)
                && value * multiplier <= 1000000000 ? value * multiplier : null;
        }
        var range = Regex.Match(message, @"\btu\s+" + amount + @"\s*(?:den|toi|-)\s*" + amount);
        if (range.Success)
        {
            intent.MinPrice = ReadAmount(range, 1, 2, range.Groups[4].Value); intent.MaxPrice = ReadAmount(range, 3, 4);
        }
        else
        {
            var maximum = Regex.Match(message, @"\b(?:duoi|khong qua|toi da|ngan sach|tam)\s+" + amount);
            var minimum = Regex.Match(message, @"\b(?:tren|it nhat|tu)\s+" + amount);
            if (maximum.Success) intent.MaxPrice = ReadAmount(maximum, 1, 2);
            if (minimum.Success) intent.MinPrice = ReadAmount(minimum, 1, 2);
        }
        if (Regex.IsMatch(message, @"\b(re nhat|gia tang|gia thap)\b")) intent.Sort = "priceAsc";
        if (Regex.IsMatch(message, @"\b(dat nhat|gia giam|gia cao)\b")) intent.Sort = "priceDesc";
    }

    private static int? ExplicitReferenceIndex(string message)
    {
        if (Regex.IsMatch(message, @"\b(cai|ao|san pham|chiec) (dau tien|dau|thu nhat|thu 1|so 1)\b")) return 1;
        var match = Regex.Match(message, @"\b(?:cai|ao|san pham|chiec) (?:thu|so) ([1-5])\b");
        if (match.Success) return int.Parse(match.Groups[1].Value);
        if (Regex.IsMatch(message, @"\b(cai|ao|san pham|chiec) thu hai\b")) return 2;
        if (Regex.IsMatch(message, @"\b(cai|ao|san pham|chiec) thu ba\b")) return 3;
        return null;
    }

    private static decimal UnitPrice(ProductVariant variant, Product product) =>
        PriceHelper.GetEffectiveSalePrice(variant, product) ?? PriceHelper.GetEffectivePrice(variant, product);
    private static decimal MinPrice(Product product) => product.ProductVariants.Count > 0
        ? product.ProductVariants.Min(v => UnitPrice(v, product)) : product.SalePrice ?? product.BasePrice;
    private static string Money(decimal value) => value.ToString("N0", Vietnamese) + "đ";
    private static string Clip(string value, int max) => value.Length <= max ? value : value[..max];

    private static ChatProductDto MapProduct(Product product, IEnumerable<ProductVariant> variants)
    {
        var rows = variants.OrderBy(v => v.Color.ColorName).ThenBy(v => v.Size.DisplayOrder).Select(v => new ChatVariantDto
        {
            Color = v.Color.ColorName, Size = v.Size.SizeName, StockQuantity = v.StockQuantity,
            Price = UnitPrice(v, product)
        }).ToList();
        var prices = rows.Select(v => v.Price).DefaultIfEmpty(product.SalePrice ?? product.BasePrice).ToList();
        return new ChatProductDto
        {
            ProductId = product.ProductId, ProductName = product.ProductName, Slug = product.Slug,
            BrandName = product.Brand?.BrandName, CategoryName = product.Category.CategoryName,
            Material = product.Material, Description = Clip(product.ShortDescription ?? product.Description, 300),
            ThumbnailUrl = product.ProductImages.OrderByDescending(i => i.IsThumbnail).ThenBy(i => i.DisplayOrder).FirstOrDefault()?.ImageUrl,
            MinPrice = prices.Min(), MaxPrice = prices.Max(), Variants = rows
        };
    }

    private static string Describe(ChatProductDto product)
    {
        var price = product.MinPrice == product.MaxPrice ? Money(product.MinPrice) : $"{Money(product.MinPrice)} – {Money(product.MaxPrice)}";
        var text = $"{product.ProductName}" + (product.BrandName is null ? "" : $" · {product.BrandName}") +
            $"\nGiá: {price}.\nChất liệu: {product.Material ?? "Shop chưa có thông tin"}.";
        if (!string.IsNullOrWhiteSpace(product.Description)) text += $"\nMô tả: {product.Description}";
        if (product.Variants.Count == 0) return text + "\nHiện chưa có biến thể đang bán.";
        var groups = product.Variants.GroupBy(v => v.Color).Take(6).Select(g =>
            $"{g.Key}: " + string.Join(", ", g.Select(v => $"{v.Size} ({(v.StockQuantity > 0 ? $"còn {v.StockQuantity}" : "hết hàng")})")));
        return text + "\nMàu / size: " + string.Join("; ", groups) + ".";
    }

    private static ChatResponseDto Answer(string reply, bool usedAi, string[]? suggestions = null) => new()
    {
        Reply = reply, UsedAi = usedAi, Suggestions = suggestions ?? DefaultSuggestions
    };
}
