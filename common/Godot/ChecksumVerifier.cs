using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

static class ChecksumVerifier {
    public static void VerifySha512(string archive, string sums) {
        Verify(archive, sums, "SHA512", SHA512.Create);
    }

    public static void VerifySha256(string archive, string sums) {
        Verify(archive, sums, "SHA256", SHA256.Create);
    }

    static void Verify(string archive, string sums, string algorithm, Func<HashAlgorithm> createHasher) {
        string filename = Path.GetFileName(archive);
        string pattern = "^([0-9a-fA-F]+)\\s+\\*?" + Regex.Escape(filename) + "\\r?$";
        var match = Regex.Match(File.ReadAllText(sums), pattern, RegexOptions.Multiline);
        if (!match.Success) {
            throw new InvalidDataException("missing " + algorithm + " checksum for " + filename);
        }

        using var hasher = createHasher();
        using var input = File.OpenRead(archive);
        string actual = Convert.ToHexString(hasher.ComputeHash(input));
        if (!actual.Equals(match.Groups[1].Value, StringComparison.OrdinalIgnoreCase)) {
            throw new InvalidDataException(algorithm + " checksum mismatch for " + filename);
        }
    }
}
