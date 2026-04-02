using System.Security.Cryptography.X509Certificates;

namespace Camunda.Orchestration.Sdk;

/// <summary>
/// Builds an <see cref="HttpClientHandler"/> configured for custom TLS (self-signed CA and/or mTLS).
///
/// Supports three modes:
/// <list type="number">
///   <item><description><b>CA-only</b> — trust a self-signed server certificate without presenting a client identity.</description></item>
///   <item><description><b>Client cert + key</b> — present a client identity using system CAs.</description></item>
///   <item><description><b>Full mTLS</b> — both a custom CA and a client cert/key pair.</description></item>
/// </list>
/// </summary>
internal static class TlsHelper
{
    /// <summary>
    /// Create an <see cref="HttpClientHandler"/> from the TLS configuration, or <c>null</c> if no TLS config is set.
    /// </summary>
    public static HttpClientHandler? BuildHandler(TlsConfig? tls)
    {
        if (tls == null)
            return null;

        var handler = new HttpClientHandler();

        // Load client certificate + key (for mTLS).
        var certPem = tls.Cert ?? ReadPath(tls.CertPath);
        var keyPem = tls.Key ?? ReadPath(tls.KeyPath);

        if (certPem != null && keyPem != null)
        {
            var clientCert = tls.KeyPassphrase != null
                ? X509Certificate2.CreateFromPem(certPem, keyPem)
                : X509Certificate2.CreateFromPem(certPem, keyPem);

            // On some platforms (Windows), the ephemeral key needs to be exported to a new cert.
            // CreateFromPem produces an ephemeral key that HttpClientHandler may not accept.
            var exportable = new X509Certificate2(
                clientCert.Export(X509ContentType.Pfx, tls.KeyPassphrase),
                tls.KeyPassphrase,
                X509KeyStorageFlags.Exportable);
            clientCert.Dispose();

            handler.ClientCertificateOptions = ClientCertificateOption.Manual;
            handler.ClientCertificates.Add(exportable);
        }

        // Load custom CA (for trusting self-signed server certs).
        var caPem = tls.Ca ?? ReadPath(tls.CaPath);
        if (caPem != null)
        {
            var caCert = new X509Certificate2(System.Text.Encoding.UTF8.GetBytes(caPem));
            handler.ServerCertificateCustomValidationCallback = (message, cert, chain, errors) =>
            {
                if (errors == System.Net.Security.SslPolicyErrors.None)
                    return true;

                // Build a chain with the custom CA to validate the server cert.
                if (cert == null || chain == null)
                    return false;

                chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                chain.ChainPolicy.CustomTrustStore.Add(caCert);
                chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                return chain.Build(new X509Certificate2(cert));
            };
        }

        return handler;
    }

    private static string? ReadPath(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return null;
        if (!File.Exists(path))
            throw new FileNotFoundException($"TLS certificate file not found: {path}", path);
        return File.ReadAllText(path);
    }
}
