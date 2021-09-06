using Neo.IO;
using Neo.Network.P2P.Payloads;
using Neo.Wallets;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using ECPoint = Neo.Cryptography.ECC.ECPoint;

namespace Neo.Cryptography
{
    /// <summary>
    /// A helper class for cryptography
    /// </summary>
    public static class Helper
    {
        /// <summary>
        /// Computes the hash value for the specified byte array using the ripemd160 algorithm.
        /// </summary>
        /// <param name="value">The input to compute the hash code for.</param>
        /// <returns>The computed hash code.</returns>
        public static byte[] RIPEMD160(this byte[] value)
        {
            using var ripemd160 = new RIPEMD160Managed();
            return ripemd160.ComputeHash(value);
        }

        /// <summary>
        /// Computes the hash value for the specified byte array using the ripemd160 algorithm.
        /// </summary>
        /// <param name="value">The input to compute the hash code for.</param>
        /// <returns>The computed hash code.</returns>
        public static byte[] RIPEMD160(this ReadOnlySpan<byte> value)
        {
            byte[] source = value.ToArray();
            return source.RIPEMD160();
        }

        /// <summary>
        /// Computes the hash value for the specified byte array using the murmur algorithm.
        /// </summary>
        /// <param name="value">The input to compute the hash code for.</param>
        /// <param name="seed">The seed used by the murmur algorithm.</param>
        /// <returns>The computed hash code.</returns>
        public static uint Murmur32(this byte[] value, uint seed)
        {
            using Murmur32 murmur = new(seed);
            return BinaryPrimitives.ReadUInt32LittleEndian(murmur.ComputeHash(value));
        }

        /// <summary>
        /// Computes the 128-bit hash value for the specified byte array using the murmur algorithm.
        /// </summary>
        /// <param name="value">The input to compute the hash code for.</param>
        /// <param name="seed">The seed used by the murmur algorithm.</param>
        /// <returns>The computed hash code.</returns>
        public static byte[] Murmur128(this byte[] value, uint seed)
        {
            using Murmur128 murmur = new(seed);
            return murmur.ComputeHash(value);
        }

        /// <summary>
        /// Computes the hash value for the specified byte array using the sha256 algorithm.
        /// </summary>
        /// <param name="value">The input to compute the hash code for.</param>
        /// <returns>The computed hash code.</returns>
        public static byte[] Sha256(this byte[] value)
        {
            using var sha256 = SHA256.Create();
            return sha256.ComputeHash(value);
        }

        /// <summary>
        /// Computes the hash value for the specified region of the specified byte array using the sha256 algorithm.
        /// </summary>
        /// <param name="value">The input to compute the hash code for.</param>
        /// <param name="offset">The offset into the byte array from which to begin using data.</param>
        /// <param name="count">The number of bytes in the array to use as data.</param>
        /// <returns>The computed hash code.</returns>
        public static byte[] Sha256(this byte[] value, int offset, int count)
        {
            using var sha256 = SHA256.Create();
            return sha256.ComputeHash(value, offset, count);
        }

        /// <summary>
        /// Computes the hash value for the specified byte array using the sha256 algorithm.
        /// </summary>
        /// <param name="value">The input to compute the hash code for.</param>
        /// <returns>The computed hash code.</returns>
        public static byte[] Sha256(this ReadOnlySpan<byte> value)
        {
            byte[] buffer = new byte[32];
            using var sha256 = SHA256.Create();
            sha256.TryComputeHash(value, buffer, out _);
            return buffer;
        }

        /// <summary>
        /// Computes the hash value for the specified byte array using the sha256 algorithm.
        /// </summary>
        /// <param name="value">The input to compute the hash code for.</param>
        /// <returns>The computed hash code.</returns>
        public static byte[] Sha256(this Span<byte> value)
        {
            return Sha256((ReadOnlySpan<byte>)value);
        }

        public static byte[] AES256Encrypt(this byte[] plainData, byte[] key, byte[] nonce, byte[] associatedData = null)
        {
            var keyLen = key is null ? 0 : key.Length;
            var nonceLen = nonce is null ? 0 : nonce.Length;

            if (keyLen != 32) throw new ArgumentException();
            if (nonceLen != 12) throw new ArgumentException();

            var msgLen = plainData is null ? 0 : plainData.Length;
            var tagLen = 16;

            var cipherBytes = new byte[msgLen];
            var tag = new byte[tagLen];
            using var cipher = new AesGcm(key);
            cipher.Encrypt(nonce, plainData, cipherBytes, tag, associatedData);
            var cipherWithTag = new byte[nonceLen + msgLen + tagLen];
            Buffer.BlockCopy(nonce, 0, cipherWithTag, 0, nonceLen);
            Buffer.BlockCopy(cipherBytes, 0, cipherWithTag, nonceLen, msgLen);
            Buffer.BlockCopy(tag, 0, cipherWithTag, nonceLen + msgLen, tagLen);
            return cipherWithTag;
        }

        public static byte[] AES256Decrypt(this byte[] encryptedData, byte[] key, byte[] associatedData = null)
        {
            if (key.Length != 32) throw new ArgumentException();
            var nonce = encryptedData.Take(12).ToArray();
            var cipherBytes = encryptedData.Skip(12).Take(encryptedData.Length - 28).ToArray();
            var tag = encryptedData[^16..];
            var decryptedData = new byte[cipherBytes.Length];
            using var cipher = new AesGcm(key);
            cipher.Decrypt(nonce, cipherBytes, tag, decryptedData, associatedData);
            return decryptedData;
        }

        public static byte[] ECEncrypt(byte[] message, ECPoint pubKey)
        {
            // P=kG,R=rG =>{R,M+rP}
            if (pubKey.IsInfinity) throw new ArgumentException();
            BigInteger r, rx;
            ECPoint R;
            var curve = pubKey.Curve;
            //r > N
            using (RandomNumberGenerator rng = RandomNumberGenerator.Create())
            {
                do
                {
                    do
                    {
                        r = rng.NextBigInteger((int)curve.N.GetBitLength());
                    } while (r.Sign == 0 || r.CompareTo(curve.N) >= 0);
                    R = ECPoint.Multiply(curve.G, r);
                    BigInteger x = R.X.Value;
                    rx = x.Mod(curve.N);
                } while (rx.Sign == 0);
            }
            byte[] RBar = R.EncodePoint(true);
            var EK = ECPoint.Multiply(pubKey, r).X.ToByteArray().Sha256(); // z = r * P = r* k * G
            Random random = new Random();
            byte[] Nonce = new byte[12];
            random.NextBytes(Nonce);
            return RBar.Concat(message.AES256Encrypt(EK, Nonce)).ToArray();
        }

        public static byte[] ECDecrypt(byte[] cypher, KeyPair key)
        {
            // {R,M+rP}={rG, M+rP}=> M + rP - kR = M + r(kG) - k(rG) = M
            if (cypher is null || cypher.Length < 33) throw new ArgumentException();
            if (cypher[0] != 0x02 && cypher[0] != 0x03) throw new ArgumentException();
            if (key.PublicKey.IsInfinity) throw new ArgumentException();
            var RBar = cypher.Take(33).ToArray();
            var EM = cypher.Skip(33).ToArray();
            var R = ECPoint.FromBytes(RBar, key.PublicKey.Curve);
            var k = new BigInteger(key.PrivateKey.Reverse().Concat(new byte[1]).ToArray());
            var EK = ECPoint.Multiply(R, k).X.ToByteArray().Sha256(); // z = k * R = k * r * G
            return EM.AES256Decrypt(EK);
        }

        internal static BigInteger NextBigInteger(this RandomNumberGenerator rng, int sizeInBits)
        {
            if (sizeInBits < 0)
                throw new ArgumentException("sizeInBits must be non-negative");
            if (sizeInBits == 0)
                return 0;
            byte[] b = new byte[sizeInBits / 8 + 1];
            rng.GetBytes(b);
            if (sizeInBits % 8 == 0)
                b[b.Length - 1] = 0;
            else
                b[b.Length - 1] &= (byte)((1 << sizeInBits % 8) - 1);
            return new BigInteger(b);
        }

        internal static bool Test(this BloomFilter filter, Transaction tx)
        {
            if (filter.Check(tx.Hash.ToArray())) return true;
            if (tx.Signers.Any(p => filter.Check(p.Account.ToArray())))
                return true;
            return false;
        }

        internal static byte[] ToAesKey(this string password)
        {
            using SHA256 sha256 = SHA256.Create();
            byte[] passwordBytes = Encoding.UTF8.GetBytes(password);
            byte[] passwordHash = sha256.ComputeHash(passwordBytes);
            byte[] passwordHash2 = sha256.ComputeHash(passwordHash);
            Array.Clear(passwordBytes, 0, passwordBytes.Length);
            Array.Clear(passwordHash, 0, passwordHash.Length);
            return passwordHash2;
        }

        internal static byte[] ToAesKey(this SecureString password)
        {
            using SHA256 sha256 = SHA256.Create();
            byte[] passwordBytes = password.ToArray();
            byte[] passwordHash = sha256.ComputeHash(passwordBytes);
            byte[] passwordHash2 = sha256.ComputeHash(passwordHash);
            Array.Clear(passwordBytes, 0, passwordBytes.Length);
            Array.Clear(passwordHash, 0, passwordHash.Length);
            return passwordHash2;
        }

        internal static byte[] ToArray(this SecureString s)
        {
            if (s == null)
                throw new NullReferenceException();
            if (s.Length == 0)
                return Array.Empty<byte>();
            List<byte> result = new();
            IntPtr ptr = SecureStringMarshal.SecureStringToGlobalAllocAnsi(s);
            try
            {
                int i = 0;
                do
                {
                    byte b = Marshal.ReadByte(ptr, i++);
                    if (b == 0)
                        break;
                    result.Add(b);
                } while (true);
            }
            finally
            {
                Marshal.ZeroFreeGlobalAllocAnsi(ptr);
            }
            return result.ToArray();
        }
    }
}
