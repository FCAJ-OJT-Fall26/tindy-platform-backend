using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Amazon.Runtime;
using Amazon.Runtime.Credentials;

namespace Tindy.Infrastructure.Ai;

internal static class BedrockMantleSigning
{
    private const string Algorithm = "AWS4-HMAC-SHA256";

    // The Responses endpoint uses a fixed path and has no query parameters.
    public static async Task SignAsync(HttpRequestMessage request, string region, AWSCredentials? credentials,
        CancellationToken cancellationToken)
    {
        credentials ??= await DefaultAWSCredentialsIdentityResolver.GetCredentialsAsync().WaitAsync(cancellationToken);
        var identity = await credentials.GetCredentialsAsync().WaitAsync(cancellationToken);
        var content = request.Content!;
        var payload = await content.ReadAsByteArrayAsync(cancellationToken);
        var date = DateTimeOffset.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        var day = date[..8];
        var payloadHash = Convert.ToHexStringLower(SHA256.HashData(payload));

        var signedHeaders = "content-type;host;x-amz-content-sha256;x-amz-date";
        var headers = $"content-type:{content.Headers.ContentType}\nhost:{request.RequestUri!.Host}\n"
            + $"x-amz-content-sha256:{payloadHash}\nx-amz-date:{date}\n";
        request.Headers.Add("x-amz-content-sha256", payloadHash);
        request.Headers.Add("x-amz-date", date);
        if (identity.UseToken)
        {
            signedHeaders += ";x-amz-security-token";
            headers += $"x-amz-security-token:{identity.Token}\n";
            request.Headers.Add("x-amz-security-token", identity.Token);
        }

        var canonicalRequest = $"{request.Method}\n{request.RequestUri.AbsolutePath}\n\n{headers}\n{signedHeaders}\n{payloadHash}";
        var scope = $"{day}/{region}/bedrock-mantle/aws4_request";
        var requestHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalRequest)));
        var stringToSign = $"{Algorithm}\n{date}\n{scope}\n{requestHash}";
        var dateKey = Hmac(Encoding.UTF8.GetBytes("AWS4" + identity.SecretKey), day);
        var regionKey = Hmac(dateKey, region);
        var serviceKey = Hmac(regionKey, "bedrock-mantle");
        var signingKey = Hmac(serviceKey, "aws4_request");
        var signature = Convert.ToHexStringLower(Hmac(signingKey, stringToSign));
        request.Headers.Authorization = new AuthenticationHeaderValue(Algorithm,
            $"Credential={identity.AccessKey}/{scope}, SignedHeaders={signedHeaders}, Signature={signature}");
    }

    private static byte[] Hmac(byte[] key, string value) => HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(value));
}
