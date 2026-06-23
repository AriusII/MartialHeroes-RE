using System.Text;
using MartialHeroes.Network.Crypto;

namespace MartialHeroes.Client.Application.UseCases;

public sealed class LoginCredentialStore
{
    private const int MaxPinLengthExclusive = 5;

    private static readonly Encoding Cp949 = CreateCp949();

    private byte[] _accountBytes = [];

    private byte[] _pinBytes = [];

    private byte[] _stagedPasswordM = [];

    public string Username { get; private set; } = string.Empty;

    public bool HasStagedCredential => _stagedPasswordM.Length > 0;

    public bool IncludePin { get; private set; }

    public ReadOnlySpan<byte> AccountBytes => _accountBytes;

    public ReadOnlySpan<byte> StagedPasswordM => _stagedPasswordM;

    public ReadOnlySpan<byte> PinBytes => _pinBytes;

    public void Stage(string username, string password, string? pin = null)
    {
        ArgumentNullException.ThrowIfNull(username);
        ArgumentNullException.ThrowIfNull(password);

        Clear();
        Username = username;

        _accountBytes = Cp949.GetBytes(username);

        var passwordBytes = Cp949.GetBytes(password);
        _stagedPasswordM = CredentialPlaintext.StagePassword(passwordBytes);
        Array.Clear(passwordBytes);

        if (!string.IsNullOrEmpty(pin))
        {
            _pinBytes = Cp949.GetBytes(pin);
            var pinLength = _pinBytes.Length;
            if (pinLength >= MaxPinLengthExclusive)
            {
                Array.Clear(_pinBytes);
                Clear();
                throw new ArgumentException(
                    $"PIN length {pinLength} is out of range: must be < {MaxPinLengthExclusive} bytes (≤ 4 digits).",
                    nameof(pin));
            }

            IncludePin = true;
        }
        else
        {
            _pinBytes = [];
            IncludePin = false;
        }
    }

    public void Clear()
    {
        if (_stagedPasswordM.Length > 0)
        {
            Array.Clear(_stagedPasswordM);
            _stagedPasswordM = [];
        }

        if (_accountBytes.Length > 0)
        {
            Array.Clear(_accountBytes);
            _accountBytes = [];
        }

        if (_pinBytes.Length > 0)
        {
            Array.Clear(_pinBytes);
            _pinBytes = [];
        }

        IncludePin = false;
        Username = string.Empty;
    }

    private static Encoding CreateCp949()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(949);
    }
}