
using Microsoft.AspNetCore.Mvc.Filters;
using Services;

namespace Filters;


public sealed class SanitizeTextAttribute : ActionFilterAttribute
{
    public override void OnActionExecuting(ActionExecutingContext context)
    {
        foreach (var key in context.ActionArguments.Keys.ToList())
        {
            var value = context.ActionArguments[key];
            if (value is null)
            {
                continue;
            }

            if (value is string s)
            {
                context.ActionArguments[key] = TextSanitizer.SanitizePlainText(s);
                continue;
            }

            SanitizeTopLevelStringProperties(value);
        }

        base.OnActionExecuting(context);
    }

    private static void SanitizeTopLevelStringProperties(object value)
    {
        foreach (var prop in value.GetType().GetProperties())
        {
            if (prop.PropertyType != typeof(string) || !prop.CanRead || !prop.CanWrite)
            {
                continue;
            }

            if (prop.GetIndexParameters().Length > 0)
            {
                continue;
            }

            var current = prop.GetValue(value) as string;
            if (string.IsNullOrEmpty(current))
            {
                continue;
            }

            prop.SetValue(value, TextSanitizer.SanitizePlainText(current) ?? current);
        }
    }
}