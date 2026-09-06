using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ClothingAPI.Helpers
{
    public static class SlugHelper
    {
        public static string Generate(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var normalized = value.Trim().ToLowerInvariant();

            normalized = normalized.Replace("đ", "d");

            normalized = normalized.Normalize(NormalizationForm.FormD);

            var builder = new StringBuilder();

            foreach (var character in normalized)
            {
                var unicodeCategory = CharUnicodeInfo.GetUnicodeCategory(character);

                if (unicodeCategory != UnicodeCategory.NonSpacingMark)
                {
                    builder.Append(character);
                }
            }

            var slug = builder.ToString().Normalize(NormalizationForm.FormC);

            slug = Regex.Replace(slug, @"[^a-z0-9]+", "-");
            slug = Regex.Replace(slug, @"-+", "-");

            return slug.Trim('-');
        }
    }
}
