using System;
using System.IO;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using NUnit.Framework;
using UnityEngine;

namespace MTD.Tests
{
    // The decision both transports ask for when a broker shows its TLS certificate. The certificates are in
    // Samples/tls: certificates only, no private keys.
    //
    //   test-ca.pem                 a site's own certificate authority
    //   issued-by-test-ca.pem       a broker certificate that authority issued, for "localhost"
    //   self-signed.pem             a broker certificate nobody issued
    //   impostor-ca.pem             a different authority using the same name as test-ca
    //   forged-test-ca-issuer.pem   a certificate issued by the impostor, so it names test-ca as its issuer
    //   expired-self-signed.pem     a broker certificate nobody issued, whose validity period ended in 2025
    //   expired-issued-by-test-ca.pem   one that test-ca issued, for the same period
    public class TlsTrustTests
    {
        const SslPolicyErrors notTrustedByWindows = SslPolicyErrors.RemoteCertificateChainErrors;

        string folder;

        [SetUp]
        public void SetUp()
        {
            folder = Path.Combine(Path.GetTempPath(), "mtd-tls-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
        }

        static string SamplePath(string fileName)
        {
            return Path.Combine(Application.dataPath, "MTD", "Tests", "Editor", "Samples", "tls", fileName);
        }

        static X509Certificate2 Sample(string fileName)
        {
            return new X509Certificate2(SamplePath(fileName));
        }

        void Trust(string fileName)
        {
            File.Copy(SamplePath(fileName), Path.Combine(folder, fileName));
        }

        bool Accepts(string certificate, SslPolicyErrors errors, out string problem, bool allowUntrusted = false)
        {
            return MtdTlsTrust.Accepts(Sample(certificate), errors, "localhost", allowUntrusted, folder, out problem);
        }

        // The same decision as it would be made at another moment, for certificates that have years left to run.
        bool AcceptsAt(DateTime utcNow, string certificate, out string problem)
        {
            return MtdTlsTrust.Accepts(Sample(certificate), notTrustedByWindows, "localhost", false, folder, utcNow, out problem);
        }

        // The reported fault: a broker's own certificate, copied into the trusted folder, was accepted as soon as it
        // was found there, before anything looked at its dates.
        [Test]
        public void BrokersOwnCertificateInTheTrustedFolder_IsRefused_OnceItHasExpired()
        {
            Trust("expired-self-signed.pem");

            Assert.IsFalse(Accepts("expired-self-signed.pem", notTrustedByWindows, out string problem));
            StringAssert.Contains("expired on", problem);
            StringAssert.DoesNotContain("Put its issuer's certificate", problem, "Copying another file into the folder would not help.");
        }

        [Test]
        public void BrokersOwnCertificateInTheTrustedFolder_IsAcceptedOnlyInsideItsValidityPeriod()
        {
            Trust("self-signed.pem");
            DateTime validFrom = Sample("self-signed.pem").NotBefore.ToUniversalTime();
            DateTime validTo = Sample("self-signed.pem").NotAfter.ToUniversalTime();

            Assert.IsTrue(AcceptsAt(validFrom.AddSeconds(1), "self-signed.pem", out string problem), problem);
            Assert.IsTrue(AcceptsAt(validTo.AddSeconds(-1), "self-signed.pem", out problem), problem);

            Assert.IsFalse(AcceptsAt(validTo.AddSeconds(1), "self-signed.pem", out problem));
            StringAssert.Contains("expired on", problem);

            Assert.IsFalse(AcceptsAt(validFrom.AddSeconds(-1), "self-signed.pem", out problem));
            StringAssert.Contains("not valid until", problem);
        }

        [Test]
        public void CertificateIssuedByATrustedAuthority_IsRefused_OnceItHasExpired()
        {
            Trust("test-ca.pem");

            Assert.IsFalse(Accepts("expired-issued-by-test-ca.pem", notTrustedByWindows, out string problem));
            StringAssert.Contains("expired on", problem);

            DateTime validTo = Sample("issued-by-test-ca.pem").NotAfter.ToUniversalTime();
            Assert.IsFalse(AcceptsAt(validTo.AddSeconds(1), "issued-by-test-ca.pem", out problem));
            StringAssert.Contains("expired on", problem);
        }

        // The authority's certificate runs out one second before the certificate it issued, which leaves a moment
        // when only the authority is out of date.
        [Test]
        public void CertificateIssuedByAnAuthorityThatHasExpired_IsRefused_ThoughItsOwnDatesAreInOrder()
        {
            Trust("test-ca.pem");
            DateTime authorityValidTo = Sample("test-ca.pem").NotAfter.ToUniversalTime();
            DateTime certificateValidTo = Sample("issued-by-test-ca.pem").NotAfter.ToUniversalTime();
            Assert.Greater(certificateValidTo, authorityValidTo, "The samples no longer leave a gap to test in.");
            DateTime inTheGap = authorityValidTo.AddTicks((certificateValidTo - authorityValidTo).Ticks / 2);

            Assert.IsTrue(AcceptsAt(authorityValidTo.AddSeconds(-1), "issued-by-test-ca.pem", out string problem), problem);
            Assert.IsFalse(AcceptsAt(inTheGap, "issued-by-test-ca.pem", out problem));
            StringAssert.Contains("not trusted", problem);
        }

        [Test]
        public void CertificateWindowsTrusts_IsAccepted()
        {
            Assert.IsTrue(Accepts("self-signed.pem", SslPolicyErrors.None, out string problem));
            Assert.IsNull(problem);
        }

        [Test]
        public void CertificateNothingTrusts_IsRefused_AndTheReasonSaysWhereToPutItsIssuer()
        {
            Assert.IsFalse(Accepts("issued-by-test-ca.pem", notTrustedByWindows, out string problem));
            StringAssert.Contains("not trusted", problem);
            StringAssert.Contains(folder, problem);
        }

        [Test]
        public void CertificateIssuedByAnAuthorityInTheTrustedFolder_IsAccepted()
        {
            Trust("test-ca.pem");

            Assert.IsTrue(Accepts("issued-by-test-ca.pem", notTrustedByWindows, out string problem), problem);
            Assert.IsNull(problem);
        }

        [Test]
        public void BrokersOwnCertificateInTheTrustedFolder_IsAccepted()
        {
            Trust("self-signed.pem");

            Assert.IsTrue(Accepts("self-signed.pem", notTrustedByWindows, out string problem), problem);
        }

        [Test]
        public void SomeOtherCertificate_IsRefused_WhateverIsInTheTrustedFolder()
        {
            Trust("test-ca.pem");

            Assert.IsFalse(Accepts("self-signed.pem", notTrustedByWindows, out _));
        }

        // The certificate says "issued by MTD Test CA", and a chain to the trusted authority can even be put
        // together from the names. Only the signature shows it was issued by someone else.
        [Test]
        public void CertificateThatOnlyNamesATrustedAuthority_IsRefused()
        {
            Trust("test-ca.pem");

            Assert.IsFalse(Accepts("forged-test-ca-issuer.pem", notTrustedByWindows, out string problem));
            StringAssert.Contains("not trusted", problem);
        }

        [Test]
        public void CertificateForAnotherHost_IsRefused_EvenFromATrustedAuthority()
        {
            Trust("test-ca.pem");

            Assert.IsFalse(Accepts("issued-by-test-ca.pem", notTrustedByWindows | SslPolicyErrors.RemoteCertificateNameMismatch, out string problem));
            StringAssert.Contains("different host", problem);

            Assert.IsFalse(Accepts("issued-by-test-ca.pem", SslPolicyErrors.RemoteCertificateNameMismatch, out _));
        }

        [Test]
        public void NoCertificate_IsRefused()
        {
            Assert.IsFalse(MtdTlsTrust.Accepts(null, SslPolicyErrors.RemoteCertificateNotAvailable, "localhost", false, folder, out string problem));
            Assert.IsNotEmpty(problem);
        }

        [Test]
        public void AllowUntrustedCertificate_AcceptsAnything()
        {
            Assert.IsTrue(Accepts("forged-test-ca-issuer.pem", notTrustedByWindows | SslPolicyErrors.RemoteCertificateNameMismatch, out string problem, allowUntrusted: true));
            Assert.IsNull(problem);

            Assert.IsTrue(Accepts("expired-self-signed.pem", notTrustedByWindows, out problem, allowUntrusted: true));
            Assert.IsNull(problem);
        }

        [Test]
        public void TrustedFolder_ReadsBundles_AndSkipsFilesThatAreNotCertificates()
        {
            File.WriteAllText(Path.Combine(folder, "bundle.pem"), File.ReadAllText(SamplePath("impostor-ca.pem")) + "\n" + File.ReadAllText(SamplePath("test-ca.pem")));
            File.WriteAllText(Path.Combine(folder, "notes.crt"), "this is not a certificate");
            File.WriteAllText(Path.Combine(folder, "readme.txt"), "ignored");
            File.WriteAllBytes(Path.Combine(folder, "broker.der"), Sample("self-signed.pem").RawData);

            Assert.AreEqual(3, MtdTlsTrust.LoadCertificates(folder).Count);
            Assert.IsTrue(Accepts("issued-by-test-ca.pem", notTrustedByWindows, out string problem), problem);
            Assert.IsTrue(Accepts("self-signed.pem", notTrustedByWindows, out problem), problem);
        }

        [Test]
        public void MissingTrustedFolder_IsTheSameAsAnEmptyOne()
        {
            Directory.Delete(folder, true);

            Assert.AreEqual(0, MtdTlsTrust.LoadCertificates(folder).Count);
            Assert.IsFalse(Accepts("issued-by-test-ca.pem", notTrustedByWindows, out _));
        }
    }
}
