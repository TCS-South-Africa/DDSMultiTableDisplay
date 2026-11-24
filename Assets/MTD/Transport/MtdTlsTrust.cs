using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace MTD
{
    // Decides whether MTD goes on talking to a broker once it has shown its TLS certificate. Both transports ask
    // here, so they follow one policy:
    //
    //  1. A certificate that Windows trusts, issued for the host MTD connected to, is accepted.
    //  2. So is a certificate for that host that is in the TrustedCertificates folder beside the configuration
    //     file, or was issued by a root certificate in that folder. This is for a site whose broker uses the
    //     site's own certificate authority: copying one file there is enough, and Windows is left alone.
    //  3. Anything else is refused, unless T-Connect|Allow Untrusted Certificate is ticked. With that ticked
    //     the connection is still encrypted, but MTD cannot tell which broker it reached.
    public static class MtdTlsTrust
    {
        public const string TrustedFolderName = "TrustedCertificates";

        static readonly string[] certificateFilePatterns = { "*.pem", "*.crt", "*.cer", "*.der" };

        public static string DefaultTrustedFolder => Path.Combine(MtdConfigurationStore.DefaultFolder, TrustedFolderName);

        // errors is what the TLS layer found when it checked the certificate against Windows' own trust store and
        // the host name. problem is text for the status display, null when the certificate is accepted.
        public static bool Accepts(X509Certificate certificate, SslPolicyErrors errors, string host, bool allowUntrusted, string trustedFolder, out string problem)
        {
            problem = null;
            if (allowUntrusted) return true;

            if (certificate == null || (errors & SslPolicyErrors.RemoteCertificateNotAvailable) != 0)
            {
                problem = "the broker did not send a TLS certificate";
                return false;
            }

            if (errors == SslPolicyErrors.None) return true;

            if ((errors & SslPolicyErrors.RemoteCertificateNameMismatch) != 0)
            {
                problem = $"the broker's TLS certificate is for a different host ({certificate.Subject}), not {host}";
                return false;
            }

            // What is left is a certificate for the right host that Windows does not trust.
            string folder = string.IsNullOrEmpty(trustedFolder) ? DefaultTrustedFolder : trustedFolder;
            try
            {
                if (IsTrustedByFolder(new X509Certificate2(certificate), LoadCertificates(folder))) return true;
            }
            catch (Exception exception)
            {
                problem = $"the broker's TLS certificate could not be checked ({exception.Message})";
                return false;
            }

            problem = $"the broker's TLS certificate ({certificate.Subject}) is not trusted. Put its issuer's certificate in {folder}";
            return false;
        }

        // True when the certificate is one of the trusted ones, or chains, with every signature and date in order,
        // to a root that is one of them.
        internal static bool IsTrustedByFolder(X509Certificate2 certificate, List<X509Certificate2> trusted)
        {
            if (trusted.Count == 0) return false;
            if (Contains(trusted, certificate)) return true;

            using (X509Chain chain = new X509Chain())
            {
                foreach (X509Certificate2 authority in trusted) chain.ChainPolicy.ExtraStore.Add(authority);
                chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck; // A site's own authority rarely publishes revocations.
                chain.ChainPolicy.VerificationFlags = X509VerificationFlags.AllowUnknownCertificateAuthority;

                // "Unknown authority" is the one fault allowed, because the root is checked against the folder just
                // below. Anything else (a bad signature, an expired certificate, a broken chain) is a refusal. This
                // matters: a certificate that only names a trusted root as its issuer still builds a chain ending
                // at that root, and the bad signature is all that gives it away.
                if (!chain.Build(certificate)) return false;
                foreach (X509ChainStatus status in chain.ChainStatus)
                {
                    if (status.Status != X509ChainStatusFlags.NoError && status.Status != X509ChainStatusFlags.UntrustedRoot) return false;
                }

                int count = chain.ChainElements.Count;
                return count > 1 && Contains(trusted, chain.ChainElements[count - 1].Certificate);
            }
        }

        // Every certificate in the folder. A file may be DER, or PEM holding one certificate or several. Files that
        // are not certificates are skipped.
        public static List<X509Certificate2> LoadCertificates(string folder)
        {
            List<X509Certificate2> certificates = new List<X509Certificate2>();
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return certificates;

            foreach (string pattern in certificateFilePatterns)
            {
                foreach (string path in Directory.GetFiles(folder, pattern))
                {
                    try
                    {
                        AddCertificates(File.ReadAllBytes(path), certificates);
                    }
                    catch (Exception)
                    {
                        // Not a certificate, or not readable. The others still count.
                    }
                }
            }

            return certificates;
        }

        static void AddCertificates(byte[] file, List<X509Certificate2> certificates)
        {
            const string begin = "-----BEGIN CERTIFICATE-----";
            const string end = "-----END CERTIFICATE-----";

            string text = System.Text.Encoding.ASCII.GetString(file);
            int from = text.IndexOf(begin, StringComparison.Ordinal);
            if (from < 0)
            {
                certificates.Add(new X509Certificate2(file)); // DER.
                return;
            }

            while (from >= 0)
            {
                int to = text.IndexOf(end, from, StringComparison.Ordinal);
                if (to < 0) break;

                string base64 = text.Substring(from + begin.Length, to - from - begin.Length);
                certificates.Add(new X509Certificate2(Convert.FromBase64String(base64)));
                from = text.IndexOf(begin, to, StringComparison.Ordinal);
            }
        }

        static bool Contains(List<X509Certificate2> certificates, X509Certificate2 wanted)
        {
            byte[] wantedBytes = wanted.RawData;
            foreach (X509Certificate2 candidate in certificates)
            {
                if (SameBytes(candidate.RawData, wantedBytes)) return true;
            }

            return false;
        }

        static bool SameBytes(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i]) return false;
            }

            return true;
        }
    }
}
