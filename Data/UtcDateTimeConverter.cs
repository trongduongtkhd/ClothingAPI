using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace ClothingAPI.Data
{
    // DB luôn lưu giờ UTC. SQL Server không lưu DateTimeKind nên khi đọc ra phải gắn lại Kind=Utc,
    // để JSON trả về có hậu tố "Z" và trình duyệt tự đổi sang giờ địa phương.
    public class UtcDateTimeConverter : ValueConverter<DateTime, DateTime>
    {
        public UtcDateTimeConverter()
            : base(
                value => value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : value,
                value => DateTime.SpecifyKind(value, DateTimeKind.Utc))
        {
        }
    }
}
