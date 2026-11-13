using System.Buffers.Binary;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace Velador.Core;

public static class P256
{
	public static readonly BigInteger P = Hex("FFFFFFFF00000001000000000000000000000000FFFFFFFFFFFFFFFFFFFFFFFF");
	public static readonly BigInteger N = Hex("FFFFFFFF00000000FFFFFFFFFFFFFFFFBCE6FAADA7179E84F3B9CAC2FC632551");
	static readonly BigInteger A = P - 3;
	static readonly BigInteger B = Hex("5AC635D8AA3A93E7B3EBBD55769886BC651D06B0CC53B0F63BCE3C3E27D2604B");

	public static readonly (BigInteger X, BigInteger Y) G = (Hex("6B17D1F2E12C4247F8BCE6E563A440F277037D812DEB33A0F4A13945D898C296"), Hex("4FE342E2FE1A7F9B8EE7EB4A7C0F9E162BCE33576B315ECECBB6406837BF51F5"));
	public static readonly (BigInteger X, BigInteger Y) M = Decode(Convert.FromHexString("02886e2f97ace46e55ba9dd7242579f2993b64e16ef3dcab95afd497333d8fa12f"))!.Value;
	public static readonly (BigInteger X, BigInteger Y) Npoint = Decode(Convert.FromHexString("03d8bbd6c639c62937b04d997f38c3770719c629d7014d49a24b4f98baa1292b49"))!.Value;

	static BigInteger Hex(string hex) => BigInteger.Parse("0" + hex, System.Globalization.NumberStyles.HexNumber);

	static BigInteger Mod(BigInteger value) => ((value % P) + P) % P;

	static BigInteger Inverse(BigInteger value) => BigInteger.ModPow(Mod(value), P - 2, P);

	public static (BigInteger X, BigInteger Y)? Add((BigInteger X, BigInteger Y)? first, (BigInteger X, BigInteger Y)? second)
	{
		if (first is not { } p1) return second;
		if (second is not { } p2) return first;
		if (p1.X == p2.X && Mod(p1.Y + p2.Y) == 0) return null;
		var slope = p1 == p2 ? Mod((3 * p1.X * p1.X + A) * Inverse(2 * p1.Y)) : Mod((p2.Y - p1.Y) * Inverse(p2.X - p1.X));
		var x = Mod(slope * slope - p1.X - p2.X);
		return (x, Mod(slope * (p1.X - x) - p1.Y));
	}

	public static (BigInteger X, BigInteger Y)? Multiply(BigInteger scalar, (BigInteger X, BigInteger Y)? point)
	{
		scalar = ((scalar % N) + N) % N;
		(BigInteger X, BigInteger Y)? result = null;
		var addend = point;
		while (scalar > 0)
		{
			if (!scalar.IsEven) result = Add(result, addend);
			addend = Add(addend, addend);
			scalar >>= 1;
		}
		return result;
	}

	public static (BigInteger X, BigInteger Y)? Negate((BigInteger X, BigInteger Y)? point) => point is { } p ? (p.X, Mod(-p.Y)) : null;

	public static (BigInteger X, BigInteger Y)? Decode(byte[] bytes)
	{
		if (bytes.Length == 65 && bytes[0] == 4) return (Unsigned(bytes.AsSpan(1, 32)), Unsigned(bytes.AsSpan(33, 32)));
		if (bytes.Length != 33 || bytes[0] is not (2 or 3)) return null;
		var x = Unsigned(bytes.AsSpan(1, 32));
		var y = BigInteger.ModPow(Mod(BigInteger.ModPow(x, 3, P) + A * x + B), (P + 1) / 4, P);
		if (y.IsEven != (bytes[0] == 2)) y = P - y;
		return (x, y);
	}

	public static byte[] Encode((BigInteger X, BigInteger Y) point) => [4, .. Fixed(point.X), .. Fixed(point.Y)];

	public static BigInteger Unsigned(ReadOnlySpan<byte> bytes) => new(bytes, isUnsigned: true, isBigEndian: true);

	public static byte[] Fixed(BigInteger value)
	{
		var raw = value.ToByteArray(isUnsigned: true, isBigEndian: true);
		var result = new byte[32];
		raw.CopyTo(result, 32 - raw.Length);
		return result;
	}
}

public sealed class Spake2PlusClient
{
	readonly byte[] _confirmKey;
	readonly byte[] _sharedKey;

	public Spake2PlusClient(byte[] deviceSalt, int iterations, byte[] deviceShare, byte[] deviceRandom, byte[] userRandom, string credential, BigInteger randomScalar)
	{
		var derived = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(credential), deviceSalt, iterations, HashAlgorithmName.SHA256, 80);
		var w0 = P256.Unsigned(derived.AsSpan(0, 40)) % P256.N;
		var w1 = P256.Unsigned(derived.AsSpan(40, 40)) % P256.N;
		var y = P256.Decode(deviceShare) ?? throw new CryptographicException("Bad device share");
		var x = randomScalar % (P256.N - 1) + 1;
		var bigX = P256.Add(P256.Multiply(x, P256.G), P256.Multiply(w0, P256.M))!.Value;
		var h = P256.Add(y, P256.Negate(P256.Multiply(w0, P256.Npoint)));
		UserShare = P256.Encode(bigX);
		var deviceShareFull = P256.Encode(y);
		var context = SHA256.HashData([.. "PAKE V1"u8, .. userRandom, .. deviceRandom]);
		var transcript = Prefixed(context, [], [], P256.Encode(P256.M), P256.Encode(P256.Npoint), UserShare, deviceShareFull, P256.Encode(P256.Multiply(x, h)!.Value), P256.Encode(P256.Multiply(w1, h)!.Value), P256.Fixed(w0));
		var ke = SHA256.HashData(transcript);
		var confirmation = HKDF.DeriveKey(HashAlgorithmName.SHA256, ke, 64, info: "ConfirmationKeys"u8.ToArray());
		_confirmKey = confirmation[32..];
		UserConfirm = HMACSHA256.HashData(confirmation[..32], deviceShareFull);
		_sharedKey = HKDF.DeriveKey(HashAlgorithmName.SHA256, ke, 32, info: "SharedKey"u8.ToArray());
	}

	public byte[] UserShare { get; }
	public byte[] UserConfirm { get; }

	public bool DeviceConfirms(byte[] deviceConfirm) => CryptographicOperations.FixedTimeEquals(deviceConfirm, HMACSHA256.HashData(_confirmKey, UserShare));

	public (byte[] Key, byte[] Nonce) Session() =>
		(HKDF.DeriveKey(HashAlgorithmName.SHA256, _sharedKey, 32, "tp-kdf-salt-aes128-key"u8.ToArray(), "tp-kdf-info-aes128-key"u8.ToArray())[..16],
		 HKDF.DeriveKey(HashAlgorithmName.SHA256, _sharedKey, 32, "tp-kdf-salt-aes128-iv"u8.ToArray(), "tp-kdf-info-aes128-iv"u8.ToArray())[..12]);

	static byte[] Prefixed(params byte[][] chunks)
	{
		using var stream = new MemoryStream();
		Span<byte> length = stackalloc byte[8];
		foreach (var chunk in chunks)
		{
			BinaryPrimitives.WriteUInt64LittleEndian(length, (ulong)chunk.Length);
			stream.Write(length);
			stream.Write(chunk);
		}
		return stream.ToArray();
	}
}

public static class AesCcm128
{
	const int TagLength = 16;
	const int NonceLength = 12;

	public static byte[] Seal(byte[] key, byte[] nonce, byte[] plain)
	{
		using var aes = Aes.Create();
		aes.Key = key;
		var tag = Mac(aes, nonce, plain);
		return [.. Counter(aes, nonce, plain, 1), .. Counter(aes, nonce, tag, 0)];
	}

	public static byte[]? Open(byte[] key, byte[] nonce, byte[] sealedBytes)
	{
		if (sealedBytes.Length < TagLength) return null;
		using var aes = Aes.Create();
		aes.Key = key;
		var plain = Counter(aes, nonce, sealedBytes[..^TagLength], 1);
		var tag = Counter(aes, nonce, Mac(aes, nonce, plain), 0);
		return CryptographicOperations.FixedTimeEquals(tag, sealedBytes.AsSpan(sealedBytes.Length - TagLength)) ? plain : null;
	}

	static byte[] Block(byte flags, byte[] nonce, int number)
	{
		var block = new byte[16];
		block[0] = flags;
		nonce.CopyTo(block, 1);
		block[13] = (byte)(number >> 16);
		block[14] = (byte)(number >> 8);
		block[15] = (byte)number;
		return block;
	}

	static byte[] Mac(Aes aes, byte[] nonce, byte[] plain)
	{
		if (nonce.Length != NonceLength) throw new CryptographicException("Bad nonce length");
		var state = aes.EncryptEcb(Block(((TagLength - 2) / 2) << 3 | 2, nonce, plain.Length), PaddingMode.None);
		for (var offset = 0; offset < plain.Length; offset += 16)
		{
			var count = Math.Min(16, plain.Length - offset);
			for (var i = 0; i < count; i++) state[i] ^= plain[offset + i];
			state = aes.EncryptEcb(state, PaddingMode.None);
		}
		return state;
	}

	static byte[] Counter(Aes aes, byte[] nonce, byte[] data, int start)
	{
		var output = new byte[data.Length];
		for (int offset = 0, number = start; offset < data.Length; offset += 16, number++)
		{
			var stream = aes.EncryptEcb(Block(2, nonce, number), PaddingMode.None);
			var count = Math.Min(16, data.Length - offset);
			for (var i = 0; i < count; i++) output[offset + i] = (byte)(data[offset + i] ^ stream[i]);
		}
		return output;
	}
}

public static class Sha256Crypt
{
	const string Alphabet = "./0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";
	static readonly (int, int, int)[] Order = [(0, 10, 20), (21, 1, 11), (12, 22, 2), (3, 13, 23), (24, 4, 14), (15, 25, 5), (6, 16, 26), (27, 7, 17), (18, 28, 8), (9, 19, 29)];

	public static string Hash(string key, string prefix)
	{
		var rest = prefix.StartsWith("$5$", StringComparison.Ordinal) ? prefix[3..] : prefix;
		var rounds = 5000;
		var explicitRounds = false;
		if (rest.StartsWith("rounds=", StringComparison.Ordinal) && rest.IndexOf('$') is > 0 and var cut && cut + 1 < rest.Length)
		{
			rounds = (int)Math.Clamp(long.Parse(rest[7..cut]), 1000, 999_999_999);
			rest = rest[(cut + 1)..];
			explicitRounds = true;
		}
		var end = rest.IndexOf('$');
		var salt = rest[..Math.Min(end > 0 ? end : rest.Length, 16)];
		var k = Encoding.UTF8.GetBytes(key);
		var s = Encoding.UTF8.GetBytes(salt);
		var b = SHA256.HashData([.. k, .. s, .. k]);
		using var first = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
		first.AppendData(k);
		first.AppendData(s);
		first.AppendData(Repeat(b, k.Length));
		for (var n = k.Length; n > 0; n >>= 1) first.AppendData((n & 1) == 1 ? b : k);
		var a = first.GetHashAndReset();
		var p = Repeat(SHA256.HashData(Enumerable.Repeat(k, k.Length).SelectMany(x => x).ToArray()), k.Length);
		var sb = Repeat(SHA256.HashData(Enumerable.Repeat(s, 16 + a[0]).SelectMany(x => x).ToArray()), s.Length);
		var c = a;
		for (var i = 0; i < rounds; i++)
		{
			first.AppendData((i & 1) == 1 ? p : c);
			if (i % 3 != 0) first.AppendData(sb);
			if (i % 7 != 0) first.AppendData(p);
			first.AppendData((i & 1) == 1 ? c : p);
			c = first.GetHashAndReset();
		}
		var encoded = new StringBuilder();
		foreach (var (x, y, z) in Order) To64(encoded, (c[x] << 16) | (c[y] << 8) | c[z], 4);
		To64(encoded, (c[31] << 8) | c[30], 3);
		return $"$5${(explicitRounds ? $"rounds={rounds}$" : "")}{salt}${encoded}";
	}

	static byte[] Repeat(byte[] digest, int length)
	{
		var output = new byte[length];
		for (var i = 0; i < length; i++) output[i] = digest[i % digest.Length];
		return output;
	}

	static void To64(StringBuilder output, int value, int count)
	{
		for (var i = 0; i < count; i++, value >>= 6) output.Append(Alphabet[value & 0x3F]);
	}
}
