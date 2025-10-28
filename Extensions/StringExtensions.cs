namespace BotApp.Extensions
{
    public static class StringExtensions
    {
        public static string MascararEmail(this string email)
        {
            var parts = email.Split('@');
            if (parts.Length != 2) return email;
            var visible = parts[0].Length <= 3 ? parts[0] : parts[0][..3] + new string('*', 3);
            return $"{visible}@{parts[1]}";
        }
    }
}
