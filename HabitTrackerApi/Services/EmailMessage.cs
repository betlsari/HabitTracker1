namespace Services;

public sealed record EmailMessage(string ToEmail, string Subject, string Body);