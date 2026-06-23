namespace MartialHeroes.Client.Application.Login;

public interface ILoginHandshakeDriver
{
    int OnKeyExchange(ReadOnlySpan<byte> keyExchangePayload);
}