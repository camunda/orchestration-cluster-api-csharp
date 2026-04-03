using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Camunda.Orchestration.Sdk.Tests;

/// <summary>
/// Tests for TLS / mTLS configuration validation and HttpClientHandler construction.
/// </summary>
public class TlsTests : IDisposable
{
    private readonly string _tmpDir;
    private readonly string _caCertPath;
    private readonly string _clientCertPath;
    private readonly string _clientKeyPath;
    private readonly string _caCertPem;
    private readonly string _clientCertPem;
    private readonly string _clientKeyPem;

    public TlsTests()
    {
        _tmpDir = Path.Combine(Path.GetTempPath(), $"tls-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tmpDir);

        // Generate a self-signed CA cert
        using var caKey = RSA.Create(2048);
        var caReq = new CertificateRequest("CN=test-ca", caKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        caReq.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        var notBefore = DateTimeOffset.UtcNow;
        var notAfter = notBefore.AddYears(10);
        using var caCert = caReq.CreateSelfSigned(notBefore, notAfter);
        _caCertPem = caCert.ExportCertificatePem();

        // Generate a client cert signed by the CA
        using var clientKey = RSA.Create(2048);
        var clientReq = new CertificateRequest("CN=test-client", clientKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var clientCert = clientReq.Create(caCert, notBefore, notAfter, new byte[] { 1, 2, 3, 4 });
        _clientCertPem = clientCert.ExportCertificatePem();
        _clientKeyPem = clientKey.ExportPkcs8PrivateKeyPem();

        // Write to temp files
        _caCertPath = Path.Combine(_tmpDir, "ca.crt");
        _clientCertPath = Path.Combine(_tmpDir, "client.crt");
        _clientKeyPath = Path.Combine(_tmpDir, "client.key");
        File.WriteAllText(_caCertPath, _caCertPem);
        File.WriteAllText(_clientCertPath, _clientCertPem);
        File.WriteAllText(_clientKeyPath, _clientKeyPem);
    }

    public void Dispose()
    {
        try
        { Directory.Delete(_tmpDir, true); }
        catch { /* ignore */ }
        GC.SuppressFinalize(this);
    }

    // -----------------------------------------------------------------------
    // Config validation tests
    // -----------------------------------------------------------------------

    [Fact]
    public void CertWithoutKey_Throws()
    {
        var act = () => ConfigurationHydrator.Hydrate(
            env: new Dictionary<string, string?> { ["CAMUNDA_MTLS_CERT"] = _clientCertPem });
        var ex = Assert.Throws<CamundaConfigurationException>(act);
        Assert.Contains("Incomplete mTLS", ex.Message);
    }

    [Fact]
    public void KeyWithoutCert_Throws()
    {
        var act = () => ConfigurationHydrator.Hydrate(
            env: new Dictionary<string, string?> { ["CAMUNDA_MTLS_KEY"] = _clientKeyPem });
        var ex = Assert.Throws<CamundaConfigurationException>(act);
        Assert.Contains("Incomplete mTLS", ex.Message);
    }

    [Fact]
    public void CertPathWithoutKeyPath_Throws()
    {
        var act = () => ConfigurationHydrator.Hydrate(
            env: new Dictionary<string, string?> { ["CAMUNDA_MTLS_CERT_PATH"] = _clientCertPath });
        var ex = Assert.Throws<CamundaConfigurationException>(act);
        Assert.Contains("Incomplete mTLS", ex.Message);
    }

    [Fact]
    public void KeyPathWithoutCertPath_Throws()
    {
        var act = () => ConfigurationHydrator.Hydrate(
            env: new Dictionary<string, string?> { ["CAMUNDA_MTLS_KEY_PATH"] = _clientKeyPath });
        var ex = Assert.Throws<CamundaConfigurationException>(act);
        Assert.Contains("Incomplete mTLS", ex.Message);
    }

    [Fact]
    public void PassphraseWithoutKey_Throws()
    {
        var act = () => ConfigurationHydrator.Hydrate(
            env: new Dictionary<string, string?> { ["CAMUNDA_MTLS_KEY_PASSPHRASE"] = "secret" });
        var ex = Assert.Throws<CamundaConfigurationException>(act);
        Assert.Contains("no client key", ex.Message);
    }

    [Fact]
    public void CaOnly_Accepted()
    {
        var config = ConfigurationHydrator.Hydrate(
            env: new Dictionary<string, string?> { ["CAMUNDA_MTLS_CA"] = _caCertPem });
        Assert.NotNull(config.Tls);
        Assert.Equal(_caCertPem, config.Tls!.Ca);
    }

    [Fact]
    public void CaPathOnly_Accepted()
    {
        var config = ConfigurationHydrator.Hydrate(
            env: new Dictionary<string, string?> { ["CAMUNDA_MTLS_CA_PATH"] = _caCertPath });
        Assert.NotNull(config.Tls);
        Assert.Equal(_caCertPath, config.Tls!.CaPath);
    }

    [Fact]
    public void CertAndKey_Accepted()
    {
        var config = ConfigurationHydrator.Hydrate(
            env: new Dictionary<string, string?>
            {
                ["CAMUNDA_MTLS_CERT"] = _clientCertPem,
                ["CAMUNDA_MTLS_KEY"] = _clientKeyPem,
            });
        Assert.NotNull(config.Tls);
        Assert.Equal(_clientCertPem, config.Tls!.Cert);
        Assert.Equal(_clientKeyPem, config.Tls!.Key);
    }

    [Fact]
    public void CertPathAndKeyPath_Accepted()
    {
        var config = ConfigurationHydrator.Hydrate(
            env: new Dictionary<string, string?>
            {
                ["CAMUNDA_MTLS_CERT_PATH"] = _clientCertPath,
                ["CAMUNDA_MTLS_KEY_PATH"] = _clientKeyPath,
            });
        Assert.NotNull(config.Tls);
        Assert.Equal(_clientCertPath, config.Tls!.CertPath);
        Assert.Equal(_clientKeyPath, config.Tls!.KeyPath);
    }

    [Fact]
    public void NoTlsFields_TlsIsNull()
    {
        var config = ConfigurationHydrator.Hydrate(
            env: new Dictionary<string, string?>());
        Assert.Null(config.Tls);
    }

    // -----------------------------------------------------------------------
    // TlsHelper.BuildHandler tests
    // -----------------------------------------------------------------------

    [Fact]
    public void BuildHandler_ReturnsNull_WhenNoTls()
    {
        Assert.Null(TlsHelper.BuildHandler(null));
    }

    [Fact]
    public void BuildHandler_CaOnly_ReturnsHandler()
    {
        var tls = new TlsConfig { Ca = _caCertPem };
        var handler = TlsHelper.BuildHandler(tls);
        Assert.NotNull(handler);
        Assert.NotNull(handler!.ServerCertificateCustomValidationCallback);
    }

    [Fact]
    public void BuildHandler_CaPathOnly_ReturnsHandler()
    {
        var tls = new TlsConfig { CaPath = _caCertPath };
        var handler = TlsHelper.BuildHandler(tls);
        Assert.NotNull(handler);
        Assert.NotNull(handler!.ServerCertificateCustomValidationCallback);
    }

    [Fact]
    public void BuildHandler_CertAndKey_ReturnsHandlerWithClientCert()
    {
        var tls = new TlsConfig { Cert = _clientCertPem, Key = _clientKeyPem };
        var handler = TlsHelper.BuildHandler(tls);
        Assert.NotNull(handler);
        Assert.Single(handler!.ClientCertificates);
    }

    [Fact]
    public void BuildHandler_Paths_ReturnsHandlerWithClientCert()
    {
        var tls = new TlsConfig { CertPath = _clientCertPath, KeyPath = _clientKeyPath };
        var handler = TlsHelper.BuildHandler(tls);
        Assert.NotNull(handler);
        Assert.Single(handler!.ClientCertificates);
    }

    [Fact]
    public void BuildHandler_FullMtls_ReturnsHandlerWithBoth()
    {
        var tls = new TlsConfig
        {
            Cert = _clientCertPem,
            Key = _clientKeyPem,
            Ca = _caCertPem,
        };
        var handler = TlsHelper.BuildHandler(tls);
        Assert.NotNull(handler);
        Assert.Single(handler!.ClientCertificates);
        Assert.NotNull(handler.ServerCertificateCustomValidationCallback);
    }

    [Fact]
    public void BuildHandler_MissingFile_ThrowsFileNotFound()
    {
        var tls = new TlsConfig { CertPath = "/nonexistent/cert.pem", KeyPath = "/nonexistent/key.pem" };
        var act = () => TlsHelper.BuildHandler(tls);
        var ex = Assert.Throws<FileNotFoundException>(act);
        Assert.Contains("cert.pem", ex.Message);
    }

    [Fact]
    public void BuildHandler_InlineOverridesPath()
    {
        var tls = new TlsConfig
        {
            Cert = _clientCertPem,
            Key = _clientKeyPem,
            CertPath = "/nonexistent/cert.pem",
            KeyPath = "/nonexistent/key.pem",
        };
        // Should not throw because inline overrides the invalid paths
        var handler = TlsHelper.BuildHandler(tls);
        Assert.NotNull(handler);
        Assert.Single(handler!.ClientCertificates);
    }
}
