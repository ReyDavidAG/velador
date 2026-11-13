using System.Security.Cryptography;
using System.Text;

namespace Velador.Core;

public sealed record TapoKeys(string HashedPassword, byte[] Lsk, byte[] Ivb);

public static class TapoCrypto
{
	public static string Sha256Upper(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

	public static string Md5Upper(string text) => Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(text)));

	public static string? HashThatMatches(string password, string cnonce, string nonce, string deviceConfirm)
	{
		foreach (var hashed in new[] { Sha256Upper(password), Md5Upper(password) })
			if (deviceConfirm == Sha256Upper(cnonce + hashed + nonce) + nonce + cnonce) return hashed;
		return null;
	}

	public static string Digest(string hashedPassword, string cnonce, string nonce) => Sha256Upper(hashedPassword + cnonce + nonce) + cnonce + nonce;

	public static TapoKeys Keys(string hashedPassword, string cnonce, string nonce)
	{
		var hashedKey = Sha256Upper(cnonce + hashedPassword + nonce);
		byte[] Token(string kind) => SHA256.HashData(Encoding.UTF8.GetBytes(kind + cnonce + nonce + hashedKey))[..16];
		return new TapoKeys(hashedPassword, Token("lsk"), Token("ivb"));
	}

	public static string Encrypt(TapoKeys keys, string json)
	{
		using var aes = Aes.Create();
		aes.Key = keys.Lsk;
		return Convert.ToBase64String(aes.EncryptCbc(Encoding.UTF8.GetBytes(json), keys.Ivb));
	}

	public static string Decrypt(TapoKeys keys, string base64)
	{
		using var aes = Aes.Create();
		aes.Key = keys.Lsk;
		return Encoding.UTF8.GetString(aes.DecryptCbc(Convert.FromBase64String(base64), keys.Ivb));
	}

	public static string Tag(TapoKeys keys, string cnonce, string body, long seq) => Sha256Upper(Sha256Upper(keys.HashedPassword + cnonce) + body + seq);

	public static string EncryptForCamera(string publicKey, string secret)
	{
		using var rsa = RSA.Create();
		if (publicKey.Contains("BEGIN", StringComparison.Ordinal)) rsa.ImportFromPem(publicKey);
		else rsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKey), out _);
		return Convert.ToBase64String(rsa.Encrypt(Encoding.UTF8.GetBytes(secret), RSAEncryptionPadding.Pkcs1));
	}
}
