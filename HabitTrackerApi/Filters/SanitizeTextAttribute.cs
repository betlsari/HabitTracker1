using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Filters;
using Services;

namespace Filters;

public sealed class SanitizeTextAttribute : ActionFilterAttribute
{
    private static readonly Regex HtmlTagRegex = new("<.*?>", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public override void OnActionExecuting(ActionExecutingContext context)
    {
        foreach (var key in context.ActionArguments.Keys)
        {
            var value = context.ActionArguments[key];
            if (value is null)
            {
                continue;
            }

            SanitizeObject(value);
        }

        base.OnActionExecuting(context);
    }

    private static void SanitizeObject(object value)
    {
        if (value is string s)
        {
            _ = TextSanitizer.SanitizePlainText(s);
            return;
        }

        if (value is not System.Collections.IDictionary and not System.Collections.IEnumerable)
        {
            var props = value.GetType().GetProperties();
            foreach (var prop in props)
            {
                if (prop.PropertyType != typeof(string) || !prop.CanRead || !prop.CanWrite)
                {
                    continue;
                }

                var current = prop.GetValue(value) as string;
                if (string.IsNullOrEmpty(current))
                {
                    continue;
                }

                var sanitized = TextSanitizer.SanitizePlainText(current) ?? current;
                prop.SetValue(value, sanitized);
            }
        }
    }
}
