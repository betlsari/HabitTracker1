using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Services;
using System.Text.Json;
using Xunit;

namespace HabitTrackerApi.Tests;

public class GlobalExceptionHandlerTests
{
    private static (GlobalExceptionHandler Handler, DefaultHttpContext Context, MemoryStream Body) CreateContext()
    {
        var handler = new GlobalExceptionHandler(NullLogger<GlobalExceptionHandler>.Instance);
        var context = new DefaultHttpContext();
        var body = new MemoryStream();
        context.Response.Body = body;
        return (handler, context, body);
    }

    private static async Task<JsonDocument> ReadProblemDetailsAsync(MemoryStream body)
    {
        body.Position = 0;
        using var reader = new StreamReader(body, leaveOpen: true);
        var text = await reader.ReadToEndAsync();
        return JsonDocument.Parse(text);
    }

    

    [Fact]
    public async Task TimeoutException_Returns504()
    {
        var (handler, context, body) = CreateContext();

        var handled = await handler.TryHandleAsync(context, new TimeoutException(), CancellationToken.None);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status504GatewayTimeout, context.Response.StatusCode);
    }

    [Fact]
    public async Task UnknownException_Returns500WithGenericMessage()
    {
        var (handler, context, body) = CreateContext();

        var handled = await handler.TryHandleAsync(context, new InvalidOperationException("secret internal detail"), CancellationToken.None);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);

        var doc = await ReadProblemDetailsAsync(body);
        var detail = doc.RootElement.GetProperty("detail").GetString();
        Assert.DoesNotContain("secret internal detail", detail);
    }
}