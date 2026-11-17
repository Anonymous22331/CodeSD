using System;
using System.IO;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Runtime.InteropServices;

namespace Agora.Outgoing.Workflow
{
    public static partial class Runtime
    {
        /// <summary>
        /// Открывает машинное хранилище сертификатов CryptoAPI.
        /// </summary>
        [DllImport("crypt32.dll", SetLastError = true)]
        private static extern IntPtr CertOpenStore(IntPtr provider, uint encoding, IntPtr p, uint flags, [MarshalAs(UnmanagedType.LPWStr)] string name);

        /// <summary>
        /// Добавляет проверенный список отзыва в хранилище.
        /// </summary>
        [DllImport("crypt32.dll", SetLastError = true)]
        private static extern bool CertAddEncodedCRLToStore(IntPtr store, uint encoding, byte[] bytes, uint size, uint disposition, IntPtr context);

        /// <summary>
        /// Закрывает хранилище сертификатов CryptoAPI.
        /// </summary>
        [DllImport("crypt32.dll")]
        private static extern bool CertCloseStore(IntPtr store, uint flags);

        /// <summary>
        /// Обновляет CRL и импортирует их после проверки подписи и срока.
        /// </summary>
        private static void RefreshCrl()
        {
            string dir = Path.Combine(Root, "crypto-runtime");
            int failed = 0;
            foreach (var spec in new[]
            {
                new[]
                {
                    "fns-2025-current.crl",
                    "http://uc.nalog.ru/cdp/aeec619ebfcd5254b7ebb5c5b2842e161bb21f4c.crl",
                    "CA",
                    "2FEF177002F2B1B95CA1A30E10A50CD38A9866F6"
                },
                new[]
                {
                    "guc2022-current.crl",
                    "http://crl.gosuslugi.ru/cdp/guc2022.crl",
                    "Root",
                    "2F0CB09BE3550EF17EC4F29C90ABD18BFCAAD63A"
                }
            }

            )
            {
                try
                {
                    using (var store = new X509Store(spec[2], StoreLocation.LocalMachine))
                    {
                        store.Open(OpenFlags.ReadOnly);
                        var issuer = store.Certificates.Find(X509FindType.FindByThumbprint, spec[3], false);
                        if (issuer.Count != 1)
                        {
                            throw new InvalidOperationException("Missing CRL issuer");
                        }

                        string target = Path.Combine(dir, spec[0]);
                        string relay = Path.Combine(dir, @"relay-inbox\" + spec[0]);
                        byte[] bytes;
                        if (File.Exists(relay) && File.GetLastWriteTimeUtc(relay) > DateTime.UtcNow.AddHours(-3))
                        {
                            bytes = File.ReadAllBytes(relay);
                        }
                        else
                        {
                            var request = (HttpWebRequest)WebRequest.Create(spec[1]);
                            request.Timeout = 25000;
                            request.ReadWriteTimeout = 25000;
                            using (var response = request.GetResponse())
                            using (var stream = response.GetResponseStream())
                            using (var memory = new MemoryStream())
                            {
                                stream.CopyTo(memory);
                                bytes = memory.ToArray();
                            }
                        }

                        var info = AgoraCrlImport.Verify(bytes, issuer[0]);
                        if (File.Exists(target))
                        {
                            try
                            {
                                var old = AgoraCrlImport.Verify(File.ReadAllBytes(target), issuer[0]);
                                if (info.thisUpdate.Utc() < old.thisUpdate.Utc())
                                {
                                    throw new InvalidOperationException("Downloaded CRL older than verified cache");
                                }
                            }
                            catch (InvalidOperationException ex)
                            {
                                if (ex.Message.Contains("older"))
                                {
                                    throw;
                                }
                            }
                        }

                        IntPtr handle = CertOpenStore(new IntPtr(10), 0, IntPtr.Zero, 0x20000, "CA");
                        if (handle == IntPtr.Zero)
                        {
                            throw new System.ComponentModel.Win32Exception();
                        }

                        try
                        {
                            if (!CertAddEncodedCRLToStore(handle, 1, bytes, (uint)bytes.Length, 3, IntPtr.Zero))
                            {
                                throw new System.ComponentModel.Win32Exception();
                            }
                        }
                        finally
                        {
                            CertCloseStore(handle, 0);
                        }

                        File.WriteAllBytes(target, bytes);
                        Log("crl", new { file = spec[0], status = "refreshed", nextUpdate = info.nextUpdate.Utc().ToString("o") });
                    }
                }
                catch (Exception ex)
                {
                    failed++;
                    Log("crl", new { file = spec[0], status = "refresh_failed", error = ex.ToString(), signatureChecksDisabled = false });
                }
            }

            if (failed > 0)
            {
                throw new InvalidOperationException("CRL refresh failures: " + failed);
            }
        }
    }
}
