using System;
using System.IO;
using System.Security.Cryptography;
using Godot;
using NUnit.Framework;

namespace Godot.Tests;

public sealed class ChecksumVerifierTests {
    [TestCase(false)]
    [TestCase(true)]
    public void AcceptsMatchingChecksum(bool binaryMarker) {
        using var directory = new TemporaryDirectory();
        string archive = directory.Write("archive.zip", "verified contents");
        string checksum = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(archive))).ToLowerInvariant();
        string sums = directory.Write("sums.txt", checksum + (binaryMarker ? " *" : "  ") + "archive.zip\n");

        ChecksumVerifier.VerifySha256(archive, sums);
    }

    [Test]
    public void RejectsMismatchedChecksum() {
        using var directory = new TemporaryDirectory();
        string archive = directory.Write("archive.zip", "contents");
        string sums = directory.Write("sums.txt", new string('0', 64) + "  archive.zip\n");

        var exception = Assert.Throws<InvalidDataException>(() => ChecksumVerifier.VerifySha256(archive, sums))!;

        Assert.That(exception.Message, Does.Contain("mismatch"));
    }

    [Test]
    public void RejectsMissingChecksumEntry() {
        using var directory = new TemporaryDirectory();
        string archive = directory.Write("archive.zip", "contents");
        string sums = directory.Write("sums.txt", new string('0', 64) + "  another.zip\n");

        var exception = Assert.Throws<InvalidDataException>(() => ChecksumVerifier.VerifySha256(archive, sums))!;

        Assert.That(exception.Message, Does.Contain("missing"));
    }
}