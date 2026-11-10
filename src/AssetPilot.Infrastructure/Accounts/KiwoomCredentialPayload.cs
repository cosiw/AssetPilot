using System.Text.Json;
using AssetPilot.Core.Accounts;

namespace AssetPilot.Infrastructure.Accounts;

public sealed record KiwoomCredentialPayload(string SecretKey, KiwoomEnvironment Environment, Guid? AccountId = null)
{
    public static byte[] Serialize(string secretKey, KiwoomEnvironment environment, Guid? accountId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secretKey);
        if (!Enum.IsDefined(environment))
        {
            throw new ArgumentOutOfRangeException(nameof(environment));
        }

        return JsonSerializer.SerializeToUtf8Bytes(new KiwoomCredentialPayload(secretKey, environment, accountId));
    }

    public static KiwoomCredentialPayload Deserialize(byte[] value)
    {
        try
        {
            var payload = JsonSerializer.Deserialize<KiwoomCredentialPayload>(value);
            if (payload is null || string.IsNullOrWhiteSpace(payload.SecretKey) || !Enum.IsDefined(payload.Environment))
            {
                throw InvalidCredentialFormat();
            }

            return payload;
        }
        catch (JsonException exception)
        {
            throw InvalidCredentialFormat(exception);
        }
    }

    private static InvalidOperationException InvalidCredentialFormat(Exception? innerException = null) =>
        new("Kiwoom credential format is invalid.", innerException);
}
