using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Filters;
using Services;

namespace Filters;

public sealed class SanitizeTextAttribute : ActionFilterAttribute
{
    private static readonly Regex HtmlTagRegex = new("<.*?>", RegexOptions.Compiled | RegexOptions.CultureInvariant);

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

            SanitizeObject(value, new HashSet<object>(ReferenceEqualityComparer.Instance));
        }

        base.OnActionExecuting(context);
    }

    
    private static void SanitizeObject(object value, HashSet<object> visited)
    {
        if (value is string)
        {
            // Üst seviyede zaten ayrı ele alınıyor; iç içe string
            // property'ler de aşağıdaki property döngüsünde ayrı ele alınır.
            return;
        }

        
        if (!value.GetType().IsValueType && !visited.Add(value))
        {
            // Zaten ziyaret edilmiş (cycle) — tekrar işleme.
            return;
        }

        if (value is System.Collections.IDictionary dictionary)
        {
            foreach (var item in dictionary.Values)
            {
                SanitizeValueOrRecurse(item, visited);
            }
            return;
        }

        if (value is System.Collections.IEnumerable enumerable)
        {
            foreach (var item in enumerable)
            {
                SanitizeValueOrRecurse(item, visited);
            }
            return;
        }

        var props = value.GetType().GetProperties();
        foreach (var prop in props)
        {
            if (!prop.CanRead || prop.GetIndexParameters().Length > 0)
            {
                continue;
            }

            if (prop.PropertyType == typeof(string))
            {
                if (!prop.CanWrite)
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
                continue;
            }

            if (IsSimpleNonRecursableType(prop.PropertyType))
            {
                continue;
            }

            var nested = prop.GetValue(value);
            if (nested != null)
            {
                SanitizeObject(nested, visited);
            }
        }
    }

    private static void SanitizeValueOrRecurse(object? item, HashSet<object> visited)
    {
        if (item is null)
        {
            return;
        }

        if (item is string)
        {
            
            return;
        }

        SanitizeObject(item, visited);
    }

    private static bool IsSimpleNonRecursableType(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;

        return underlying.IsPrimitive
            || underlying.IsEnum
            || underlying == typeof(decimal)
            || underlying == typeof(DateTime)
            || underlying == typeof(DateTimeOffset)
            || underlying == typeof(DateOnly)
            || underlying == typeof(TimeOnly)
            || underlying == typeof(TimeSpan)
            || underlying == typeof(Guid);
    }
}