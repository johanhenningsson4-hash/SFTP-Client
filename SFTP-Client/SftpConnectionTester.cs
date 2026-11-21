using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace SFTP_Client
{
    public sealed class SftpConnectionSettings
    {
        public string Host { get; set; }
        public int Port { get; set; } = 22;
        public string Username { get; set; }
        public string PrivateKeyPath { get; set; }
        public string PrivateKeyPassphrase { get; set; }
        public string ExpectedFingerprint { get; set; }
        public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(15);
    }

    public sealed class SftpConnectionTestResult
    {
        public bool Success { get; internal set; }
        public string Message { get; internal set; }
        public string ServerSha256Fingerprint { get; internal set; }
        public string ServerMd5Fingerprint { get; internal set; }
        public string HostKeyAlgorithm { get; internal set; }
        public string ServerVersion { get; internal set; }
        public string WorkingDirectory { get; internal set; }
        public int? DirectoryEntryCount { get; internal set; }
    }

    public static class SftpConnectionTester
    {
        public static Task<SftpConnectionTestResult> TestAsync(SftpConnectionSettings settings, Action<string> log, CancellationToken cancellationToken = default(CancellationToken))
        {
            return Task.Run(() => Test(settings, log ?? (_ => { }), cancellationToken), cancellationToken);
        }

        public static IList<string> Validate(SftpConnectionSettings settings)
        {
            var errors = new List<string>();
            if (settings == null)
            {
                errors.Add("Settings are missing.");
                return errors;
            }

            if (string.IsNullOrWhiteSpace(settings.Host))
                errors.Add("Host is required.");
            if (settings.Port < 1 || settings.Port > 65535)
                errors.Add("Port must be between 1 and 65535.");
            if (string.IsNullOrWhiteSpace(settings.Username))
                errors.Add("Username is required.");
            if (string.IsNullOrWhiteSpace(settings.PrivateKeyPath))
                errors.Add("Private key (.pem) file is required.");
            else if (!File.Exists(settings.PrivateKeyPath))
                errors.Add("Private key file not found: " + settings.PrivateKeyPath);
            if (!string.IsNullOrWhiteSpace(settings.ExpectedFingerprint) && !TryParseFingerprint(settings.ExpectedFingerprint, out _, out _))
                errors.Add("Fingerprint format not recognized. Use 'SHA256:<base64>' or an MD5 hex value like 'aa:bb:cc:...'.");
            return errors;
        }

        private static SftpConnectionTestResult Test(SftpConnectionSettings settings, Action<string> log, CancellationToken cancellationToken)
        {
            var result = new SftpConnectionTestResult();

            var errors = Validate(settings);
            if (errors.Count > 0)
            {
                result.Message = string.Join(Environment.NewLine, errors);
                return result;
            }

            bool hasExpectedFingerprint = TryParseFingerprint(settings.ExpectedFingerprint, out var expectedKind, out var expectedValue);
            bool hostKeyRejected = false;

            PrivateKeyFile keyFile;
            try
            {
                log("Loading private key: " + settings.PrivateKeyPath);
                keyFile = string.IsNullOrEmpty(settings.PrivateKeyPassphrase)
                    ? new PrivateKeyFile(settings.PrivateKeyPath)
                    : new PrivateKeyFile(settings.PrivateKeyPath, settings.PrivateKeyPassphrase);
            }
            catch (SshPassPhraseNullOrEmptyException)
            {
                result.Message = "The private key is encrypted. Enter its passphrase.";
                return result;
            }
            catch (Exception ex)
            {
                result.Message = "Could not load private key: " + ex.Message;
                return result;
            }

            using (keyFile)
            {
                var connectionInfo = new PrivateKeyConnectionInfo(settings.Host.Trim(), settings.Port, settings.Username.Trim(), keyFile)
                {
                    Timeout = settings.Timeout
                };

                using (var client = new SftpClient(connectionInfo))
                {
                    client.OperationTimeout = settings.Timeout;
                    client.HostKeyReceived += (sender, e) =>
                    {
                        result.HostKeyAlgorithm = e.HostKeyName;
                        result.ServerSha256Fingerprint = "SHA256:" + e.FingerPrintSHA256;
                        result.ServerMd5Fingerprint = string.Join(":", e.FingerPrint.Select(b => b.ToString("x2")));

                        log("Server host key: " + e.HostKeyName);
                        log("  " + result.ServerSha256Fingerprint);
                        log("  MD5:" + result.ServerMd5Fingerprint);

                        if (!hasExpectedFingerprint)
                        {
                            log("No fingerprint provided - host key NOT trusted. Verify the fingerprint above and enter it to connect.");
                            e.CanTrust = false;
                        }
                        else if (expectedKind == FingerprintKind.Sha256)
                        {
                            e.CanTrust = string.Equals(expectedValue, e.FingerPrintSHA256.TrimEnd('='), StringComparison.Ordinal);
                        }
                        else
                        {
                            e.CanTrust = string.Equals(expectedValue, result.ServerMd5Fingerprint.Replace(":", string.Empty), StringComparison.OrdinalIgnoreCase);
                        }

                        hostKeyRejected = !e.CanTrust;
                        if (hasExpectedFingerprint)
                            log(e.CanTrust ? "Host key fingerprint matches." : "Host key fingerprint MISMATCH!");
                    };

                    try
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        log(string.Format("Connecting to {0}:{1} as '{2}'...", connectionInfo.Host, connectionInfo.Port, connectionInfo.Username));
                        client.Connect();

                        result.ServerVersion = client.ConnectionInfo.ServerVersion;
                        log("Connected. Server version: " + result.ServerVersion);

                        cancellationToken.ThrowIfCancellationRequested();
                        result.WorkingDirectory = client.WorkingDirectory;
                        log("Working directory: " + result.WorkingDirectory);

                        result.DirectoryEntryCount = client.ListDirectory(result.WorkingDirectory)
                            .Count(f => f.Name != "." && f.Name != "..");
                        log("Directory listing OK (" + result.DirectoryEntryCount + " entries).");

                        result.Success = true;
                        result.Message = "SFTP connection test succeeded.";
                    }
                    catch (OperationCanceledException)
                    {
                        result.Message = "Connection test was cancelled.";
                    }
                    catch (SshConnectionException ex) when (hostKeyRejected)
                    {
                        result.Message = hasExpectedFingerprint
                            ? "Host key fingerprint does not match the expected value. Connection aborted."
                            : "Host key not trusted because no fingerprint was provided.";
                        log(ex.Message);
                    }
                    catch (SshAuthenticationException ex)
                    {
                        result.Message = "Authentication failed: " + ex.Message;
                    }
                    catch (SshOperationTimeoutException ex)
                    {
                        result.Message = "Operation timed out: " + ex.Message;
                    }
                    catch (System.Net.Sockets.SocketException ex)
                    {
                        result.Message = "Network error: " + ex.Message;
                    }
                    catch (SftpPermissionDeniedException ex)
                    {
                        result.Message = "Connected, but permission denied listing directory: " + ex.Message;
                    }
                    catch (Exception ex)
                    {
                        result.Message = ex.GetType().Name + ": " + ex.Message;
                    }
                    finally
                    {
                        if (client.IsConnected)
                        {
                            client.Disconnect();
                            log("Disconnected.");
                        }
                    }
                }
            }

            return result;
        }

        private enum FingerprintKind
        {
            Sha256,
            Md5
        }

        /// <summary>
        /// Accepts "SHA256:base64", raw base64 SHA256, "MD5:aa:bb:..", or "aa:bb:.." hex,
        /// optionally surrounded by the bit size / key type text printed by ssh-keygen -l.
        /// </summary>
        private static bool TryParseFingerprint(string input, out FingerprintKind kind, out string value)
        {
            kind = FingerprintKind.Sha256;
            value = null;
            if (string.IsNullOrWhiteSpace(input))
                return false;

            var token = input.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault(t => t.StartsWith("SHA256:", StringComparison.OrdinalIgnoreCase)
                                  || t.StartsWith("MD5:", StringComparison.OrdinalIgnoreCase))
                ?? input.Trim();

            if (token.StartsWith("MD5:", StringComparison.OrdinalIgnoreCase))
                return TryParseMd5(token.Substring(4), out kind, out value);

            if (token.StartsWith("SHA256:", StringComparison.OrdinalIgnoreCase))
                token = token.Substring(7);

            if (TryParseMd5(token, out kind, out value))
                return true;

            var base64 = token.TrimEnd('=');
            if (base64.Length == 43 && base64.All(c => (c < 128 && char.IsLetterOrDigit(c)) || c == '+' || c == '/'))
            {
                kind = FingerprintKind.Sha256;
                value = base64;
                return true;
            }

            return false;
        }

        private static bool TryParseMd5(string token, out FingerprintKind kind, out string value)
        {
            kind = FingerprintKind.Md5;
            value = token.Replace(":", string.Empty).Replace("-", string.Empty);
            if (value.Length == 32 && value.All(Uri.IsHexDigit))
                return true;
            value = null;
            return false;
        }
    }
}
