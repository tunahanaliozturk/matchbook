namespace Matchbook.Payables.Application.Features.PaymentRuns;

/// <summary>
/// A released run's bank file, ready to be streamed. The checks that can refuse the download (the run exists and was
/// released) run before this is returned, so an endpoint can still answer with an error; after that the file is
/// written straight from the database to <see cref="WriteToAsync"/>'s stream.
/// </summary>
public sealed class PaymentFile
{
    public const string ContentType = "application/xml";

    private readonly Func<Stream, CancellationToken, Task> _write;

    internal PaymentFile(string fileName, Func<Stream, CancellationToken, Task> write)
    {
        FileName = fileName;
        _write = write;
    }

    public string FileName { get; }

    public Task WriteToAsync(Stream destination, CancellationToken cancellationToken) => _write(destination, cancellationToken);
}
