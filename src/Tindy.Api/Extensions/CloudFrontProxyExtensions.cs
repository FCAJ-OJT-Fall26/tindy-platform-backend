using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace Tindy.Api.Extensions;

internal static class CloudFrontProxyExtensions
{
    private const string ViewerProtocolHeader = "CloudFront-Forwarded-Proto";
    private const string ViewerAddressHeader = "CloudFront-Viewer-Address";

    internal static void UseCloudFrontProxy(this WebApplication app)
    {
        // The default hosting middleware trusts every peer when this switch is enabled.
        if (app.Configuration.GetValue<bool>("ForwardedHeaders_Enabled"))
            throw new InvalidOperationException("Disable automatic forwarded headers when CloudFrontProxy is enabled.");

        var settings = app.Configuration.GetSection("CloudFrontProxy");
        var publicHost = settings["PublicHost"];
        if (Uri.CheckHostName(publicHost) != UriHostNameType.Dns)
            throw new InvalidOperationException("CloudFrontProxy:PublicHost must be a DNS hostname without a scheme or port.");

        var networks = settings.GetSection("TrustedProxyNetworks").Get<string[]>() ?? [];
        if (networks.Length == 0)
            throw new InvalidOperationException("CloudFrontProxy:TrustedProxyNetworks must contain the ALB subnet CIDRs.");

        var forwarding = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
            ForwardedForHeaderName = ViewerAddressHeader,
            ForwardedProtoHeaderName = ViewerProtocolHeader,
            ForwardLimit = 1,
            RequireHeaderSymmetry = true
        };
        forwarding.KnownProxies.Clear();
        forwarding.KnownIPNetworks.Clear();
        foreach (var cidr in networks)
        {
            if (!System.Net.IPNetwork.TryParse(cidr, out var network) || network.PrefixLength == 0)
                throw new InvalidOperationException("CloudFrontProxy:TrustedProxyNetworks requires specific, valid subnet CIDRs.");

            forwarding.KnownIPNetworks.Add(network);
        }

        // ALB target checks reach the task directly over HTTP with an internal Host.
        app.UseWhen(context => !IsHealthPath(context.Request.Path), branch =>
        {
            branch.Use(async (context, next) =>
            {
                var peer = context.Connection.RemoteIpAddress;
                if (peer?.IsIPv4MappedToIPv6 == true)
                    peer = peer.MapToIPv4();

                var viewerAddress = context.Request.Headers[ViewerAddressHeader];
                if (peer is null || !forwarding.KnownIPNetworks.Any(network => network.Contains(peer)) ||
                    !string.Equals(context.Request.Host.Value, publicHost, StringComparison.OrdinalIgnoreCase) ||
                    context.Request.Headers[ViewerProtocolHeader] != "https" ||
                    viewerAddress.Count != 1 || !IPEndPoint.TryParse(viewerAddress.ToString(), out var endpoint) ||
                    endpoint.Port == 0)
                {
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                    return;
                }

                await next(context);
            });
            branch.UseForwardedHeaders(forwarding);
        });
    }

    private static bool IsHealthPath(PathString path) =>
        path == "/api/health" || path == "/api/health/live" || path == "/api/health/ready";
}
