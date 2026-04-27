namespace WARDMANAGEMENTSYSTEM.Services
{
    public interface ITwoFactorService
    {
        string GenerateSecretKey();
        string GetQrCodeUri(string secretKey, string email, string issuer);
        byte[] GenerateQrCodePng(string uri);
        bool VerifyCode(string secretKey, string code);
        List<string> GenerateRecoveryCodes();
        bool VerifyRecoveryCode(string storedJson, string inputCode, out string updatedJson);
    }
}
