using System;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;

public static class AgoraCrlImport
{
    [StructLayout(LayoutKind.Sequential)]
    public struct Blob
    {
        public uint count;
        public IntPtr data;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Algorithm
    {
        public IntPtr oid;
        public Blob parameters;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Time
    {
        public uint low;
        public uint high;

        /// <summary>
        /// Преобразует время CryptoAPI в дату и время UTC.
        /// </summary>
        public DateTime Utc()
        {
            return DateTime.FromFileTimeUtc(unchecked((long)(((ulong)high << 32) | low)));
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Info
    {
        public uint version;
        public Algorithm signature;
        public Blob issuer;
        public Time thisUpdate;
        public Time nextUpdate;
        public uint entryCount;
        public IntPtr entries;
        public uint extensionCount;
        public IntPtr extensions;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Context
    {
        public uint encoding;
        public IntPtr encoded;
        public uint encodedLength;
        public IntPtr info;
        public IntPtr store;
    }

    /// <summary>
    /// Создаёт контекст списка отозванных сертификатов.
    /// </summary>
    [DllImport("crypt32.dll", SetLastError = true)]
    private static extern IntPtr CertCreateCRLContext(uint encoding, byte[] data, uint length);

    /// <summary>
    /// Освобождает контекст списка отозванных сертификатов.
    /// </summary>
    [DllImport("crypt32.dll")]
    private static extern bool CertFreeCRLContext(IntPtr context);

    /// <summary>
    /// Проверяет криптографическую подпись списка отзыва.
    /// </summary>
    [DllImport("crypt32.dll", SetLastError = true)]
    private static extern bool CryptVerifyCertificateSignatureEx(IntPtr provider, uint encoding, uint subjectType, IntPtr subject, uint issuerType, IntPtr issuer, uint flags, IntPtr extra);

    /// <summary>
    /// Проверяет подпись и срок действия списка отзыва сертификатов.
    /// </summary>
    public static Info Verify(byte[] data, X509Certificate2 issuer)
    {
        IntPtr crl = CertCreateCRLContext(1, data, (uint)data.Length);
        if (crl == IntPtr.Zero)
        {
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }

        try
        {
            if (!CryptVerifyCertificateSignatureEx(IntPtr.Zero, 1, 3, crl, 2, issuer.Handle, 0, IntPtr.Zero))
            {
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            }

            var context = (Context)Marshal.PtrToStructure(crl, typeof(Context));
            var info = (Info)Marshal.PtrToStructure(context.info, typeof(Info));
            if (info.thisUpdate.Utc() > DateTime.UtcNow.AddMinutes(5) || info.nextUpdate.Utc() <= DateTime.UtcNow)
            {
                throw new InvalidOperationException("CRL is not current");
            }

            return info;
        }
        finally
        {
            CertFreeCRLContext(crl);
        }
    }
}
