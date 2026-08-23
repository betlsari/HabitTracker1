using System.Threading.Channels;

namespace Services;

/// <summary>
/// Sınırsız kapasiteli, thread-safe in-memory kuyruk. E-posta gönderimini
/// istek işleme akışından ayırır: enqueue anlık döner, gerçek SMTP çağrısı
/// EmailSenderBackgroundService tarafından arka planda yapılır.
/// Not: In-memory olduğu için process restart'ında bekleyen mesajlar kaybolur;
/// production'da bu kritikse Postgres tablosu / Redis / gerçek bir message
/// broker (RabbitMQ, Azure Service Bus) tercih edilmeli.
/// </summary>
public sealed class EmailQueue : IEmailQueue
{
    private readonly Channel<EmailMessage> _channel =
        Channel.CreateUnbounded<EmailMessage>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

    public void Enqueue(EmailMessage message)
    {
        if (!_channel.Writer.TryWrite(message))
        {
            throw new InvalidOperationException("Email kuyruğuna yazılamadı.");
        }
    }

    public async IAsyncEnumerable<EmailMessage> DequeueAllAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var message in _channel.Reader.ReadAllAsync(cancellationToken))
        {
            yield return message;
        }
    }
}