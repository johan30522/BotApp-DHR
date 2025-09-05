using System.Text.RegularExpressions;

namespace BotApp.Infrastructure.Logging
{
    public class PiiRedactor
    {
        static readonly Regex Cedula = new(@"(\d{1,2}-)\d{3,6}-(\d{3,4})", RegexOptions.Compiled);
        static readonly Regex Phone8 = new(@"\b\d{8}\b", RegexOptions.Compiled);

        public static string? Safe(string? input, int max = 1000)
        {
            if (string.IsNullOrEmpty(input)) return input;
            var s = Cedula.Replace(input, m => $"{m.Groups[1].Value}****-{m.Groups[2].Value}");
            s = Phone8.Replace(s, "xx**xx**");
            return s.Length > max ? s[..max] + "…" : s;
        }
    }
}
