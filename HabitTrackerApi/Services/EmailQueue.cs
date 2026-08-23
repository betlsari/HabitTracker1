using System.Threading.Channels;

namespace Services;

public sealed class EmailQueue : IEmailQueue
{
    private readonly Channel<EmailMessage> _channel =
        Channel.CreateUnbounded<EmailMessage>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

    // YENİ (madde 9): EmailQueueHealthCheck'in kuyrukta biriken mesaj
    // sayısını okuyabilmesi için. .NET'in standart Unbounded channel
    // implementasyonu Reader.Count'u destekler (CanCount == true).
    public int PendingCount => _channel.Reader.CanCount ? _channel.Reader.Count : -1;

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