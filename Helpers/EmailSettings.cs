namespace ClothingAPI.Helpers
{
    public class EmailSettings
    {
        public string SenderName { get; set; } = "Clothing Store";
        public string SenderEmail { get; set; } = string.Empty;
        public string AppPassword { get; set; } = string.Empty;
        public string SmtpHost { get; set; } = "smtp.gmail.com";
        public int SmtpPort { get; set; } = 587;
    }
}
