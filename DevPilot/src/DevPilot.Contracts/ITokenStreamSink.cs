namespace DevPilot.Contracts;

public interface ITokenStreamSink
{
    ValueTask OnTokenAsync(string token, CancellationToken cancellationToken = default);
}
