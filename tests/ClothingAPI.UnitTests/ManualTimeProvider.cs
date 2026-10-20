namespace ClothingAPI.UnitTests;

/// Đồng hồ giả: test tự đặt/tiến thời gian (bước TOTP 30 giây, hạn challenge) thay vì chờ thật.
public sealed class ManualTimeProvider : TimeProvider
{
    private DateTimeOffset _utcNow;

    public ManualTimeProvider(DateTimeOffset utcNow) => _utcNow = utcNow;

    public override DateTimeOffset GetUtcNow() => _utcNow;

    public DateTime UtcNow => _utcNow.UtcDateTime;

    public void Advance(TimeSpan delta) => _utcNow = _utcNow.Add(delta);
}
