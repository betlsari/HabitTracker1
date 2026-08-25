namespace Services;

public static class TextSanitizer
{
    public static string? SanitizePlainText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        var result = value.Trim();

        result = System.Text.RegularExpressions.Regex.Replace(
            result,
            "<(script|style|iframe|object|embed|svg|img|form|input)\\b[^>]*>.*?</\\1\\s*>",
            " ",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase |
            System.Text.RegularExpressions.RegexOptions.CultureInvariant |
            System.Text.RegularExpressions.RegexOptions.Singleline);

        result = System.Text.RegularExpressions.Regex.Replace(
            result,
            "<[^>]+>",
            " ",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant | System.Text.RegularExpressions.RegexOptions.Singleline);

        result = System.Text.RegularExpressions.Regex.Replace(
            result,
            "\\s+",
            " ",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant);

        return result.Trim();
    }
}
