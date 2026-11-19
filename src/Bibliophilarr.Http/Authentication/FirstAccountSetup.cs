using System;
using System.Net;
using Microsoft.AspNetCore.Http;

namespace Bibliophilarr.Http.Authentication
{
    public static class FirstAccountSetup
    {
        private static readonly object DirectLocalRequest = new object();

        // Evaluate the original socket and host before forwarded headers are applied.
        public static void CaptureRequest(HttpContext context)
        {
            var address = context.Connection.RemoteIpAddress;
            var host = context.Request.Host.Host.Trim('[', ']');
            var headers = context.Request.Headers;

            if (address != null && IsLoopback(address) &&
                (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
                 (IPAddress.TryParse(host, out var hostAddress) && IsLoopback(hostAddress))) &&
                !headers.ContainsKey("Forwarded") &&
                !headers.ContainsKey("X-Forwarded-For") &&
                !headers.ContainsKey("X-Forwarded-Host") &&
                !headers.ContainsKey("X-Forwarded-Proto"))
            {
                context.Items[DirectLocalRequest] = true;
            }
        }

        public static bool IsDirectLocalRequest(HttpContext context)
        {
            return context?.Items.ContainsKey(DirectLocalRequest) == true;
        }

        private static bool IsLoopback(IPAddress address)
        {
            return IPAddress.IsLoopback(address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address);
        }
    }
}
