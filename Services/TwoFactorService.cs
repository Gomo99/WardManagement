using OtpNet;
using QRCoder;
using System.Text.Json;

namespace WARDMANAGEMENTSYSTEM.Services
{
    public class TwoFactorService : ITwoFactorService
    {
        public string GenerateSecretKey()
        {
            var key = KeyGeneration.GenerateRandomKey(20);
            return Base32Encoding.ToString(key);
        }

        public string GetQrCodeUri(string secretKey, string email, string issuer)
        {
            // otpauth://totp/{issuer}:{email}?secret={key}&issuer={issuer}
            var encodedIssuer = Uri.EscapeDataString(issuer);
            var encodedEmail = Uri.EscapeDataString(email);
            return $"otpauth://totp/{encodedIssuer}:{encodedEmail}" +
                   $"?secret={secretKey}&issuer={encodedIssuer}&digits=6&period=30";
        }

        public byte[] GenerateQrCodePng(string uri)
        {
            using var qrGenerator = new QRCodeGenerator();
            using var qrCodeData = qrGenerator.CreateQrCode(uri, QRCodeGenerator.ECCLevel.Q);
            using var qrCode = new PngByteQRCode(qrCodeData);
            return qrCode.GetGraphic(6);
        }

        public bool VerifyCode(string secretKey, string code)
        {
            try
            {
                var keyBytes = Base32Encoding.ToBytes(secretKey);
                var totp = new Totp(keyBytes);

                // Allow 1 step of clock drift in each direction
                return totp.VerifyTotp(
                    code.Trim(),
                    out _,
                    new VerificationWindow(previous: 1, future: 1));
            }
            catch
            {
                return false;
            }
        }

        public List<string> GenerateRecoveryCodes()
        {
            var rng = new Random();
            var codes = new List<string>();

            for (int i = 0; i < 8; i++)
            {
                // Format: XXXX-XXXX  (8 hex chars)
                var part1 = rng.Next(0x1000, 0xFFFF).ToString("X4");
                var part2 = rng.Next(0x1000, 0xFFFF).ToString("X4");
                codes.Add($"{part1}-{part2}");
            }

            return codes;
        }

        public bool VerifyRecoveryCode(string storedJson, string inputCode,
                                        out string updatedJson)
        {
            updatedJson = storedJson;

            var codes = JsonSerializer.Deserialize<List<string>>(storedJson)
                        ?? new List<string>();

            // Recovery codes are stored as BCrypt hashes
            var matched = codes.FirstOrDefault(c =>
                BCrypt.Net.BCrypt.Verify(inputCode.Trim().ToUpper(), c));

            if (matched == null) return false;

            // Remove the used code (one-time use)
            codes.Remove(matched);
            updatedJson = JsonSerializer.Serialize(codes);
            return true;
        }
    }
}
