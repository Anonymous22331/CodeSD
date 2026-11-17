using System;
using System.IO;
using System.IO.Compression;
using System.Xml;
using System.Linq;
using System.Drawing;
using System.Drawing.Imaging;
using System.Reflection;
using System.Globalization;

namespace Agora.Outgoing.Workflow
{
    public static class RegisteredPdf
    {
        /// <summary>
        /// Создаёт производный PDF по подтверждённой подписи и реквизитам.
        /// </summary>
        public static void Create(string source, string output, string number, DateTime date, XmlElement proof)
        {
            if (File.Exists(output) || Path.GetFullPath(source) == Path.GetFullPath(output))
            {
                throw new InvalidOperationException("Output must be a new derivative");
            }

            if (proof.Name != "SignatureConfirmationResult" || proof.GetAttribute("status") != "verified" || String.IsNullOrWhiteSpace(proof.GetAttribute("signerName")) || String.IsNullOrWhiteSpace(proof.GetAttribute("signatureTime")) || String.IsNullOrWhiteSpace(number))
            {
                throw new InvalidOperationException("Cryptographic signature confirmation and number required");
            }

            string dir = Path.Combine(Path.GetTempPath(), "AgoraPdf-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                const string components = @"C:\Program Files (x86)\Docsvision\5.5\Client\Components";
                Assembly.LoadFrom(Path.Combine(components, "DevExpress.Printing.v17.2.Core.dll"));
                var editors = Assembly.LoadFrom(Path.Combine(components, "DevExpress.XtraEditors.v17.2.dll"));
                var printing = Assembly.LoadFrom(Path.Combine(components, "DevExpress.Printing.v17.2.Core.dll"));
                dynamic barcode = Activator.CreateInstance(editors.GetType("DevExpress.XtraEditors.BarCodeControl", true));
                string qr = Path.Combine(dir, "qr.png");
                try
                {
                    dynamic generator = Activator.CreateInstance(printing.GetType("DevExpress.XtraPrinting.BarCode.QRCodeGenerator", true));
                    var property = generator.GetType().GetProperty("CompactionMode");
                    property.SetValue(generator, Enum.Parse(property.PropertyType, "Byte"), null);
                    property = generator.GetType().GetProperty("ErrorCorrectionLevel");
                    property.SetValue(generator, Enum.Parse(property.PropertyType, "M"), null);
                    barcode.Symbology = generator;
                    barcode.AutoModule = true;
                    barcode.ShowText = false;
                    barcode.BackColor = Color.White;
                    barcode.ForeColor = Color.Black;
                    barcode.Size = new Size(600, 600);
                    barcode.Text = proof.GetAttribute("signerName") + "\n" + proof.GetAttribute("signatureTime");
                    using (Image image = barcode.ExportToImage(ImageFormat.Png, 600))
                        image.Save(qr, ImageFormat.Png);
                }
                finally
                {
                    barcode.Dispose();
                }

                string basePdf = Path.Combine(dir, "base.pdf");
                string header = "№ " + number + " от " + date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
                bool isPdf;
                using (var stream = File.OpenRead(source))
                {
                    byte[] bytes = new byte[5];
                    isPdf = stream.Read(bytes, 0, 5) == 5 && System.Text.Encoding.ASCII.GetString(bytes) == "%PDF-";
                }

                if (isPdf)
                {
                    File.Copy(source, basePdf);
                }
                else
                {
                    string docx = Path.Combine(dir, "derivative.docx");
                    File.Copy(source, docx);
                    using (var zip = ZipFile.Open(docx, ZipArchiveMode.Update))
                    {
                        var entry = zip.GetEntry("word/document.xml");
                        if (entry == null)
                        {
                            throw new InvalidOperationException("Supported formats: DOCX/PDF");
                        }

                        var xml = new XmlDocument
                        {
                            PreserveWhitespace = true,
                            XmlResolver = null
                        };
                        using (var stream = entry.Open())
                            xml.Load(stream);
                        var ns = new XmlNamespaceManager(xml.NameTable);
                        const string w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
                        ns.AddNamespace("w", w);
                        int filled = 0;
                        foreach (var pair in new[]
                        {
                            new[]
                            {
                                "РЕГ_НОМЕР",
                                number
                            },
                            new[]
                            {
                                "РЕГ_ДАТА",
                                date.ToString("dd MMMM yyyy", CultureInfo.GetCultureInfo("ru-RU")) + " г."
                            }
                        }

                        )
                        {
                            var start = (XmlElement)xml.SelectSingleNode("//w:bookmarkStart[@w:name='" + pair[0] + "']", ns);
                            if (start == null)
                            {
                                continue;
                            }

                            var end = xml.SelectSingleNode("//w:bookmarkEnd[@w:id='" + start.GetAttribute("id", w) + "']", ns);
                            if (end == null)
                            {
                                throw new InvalidOperationException("Unclosed registration bookmark");
                            }

                            bool inside = false;
                            var texts = new System.Collections.Generic.List<XmlNode>();
                            foreach (XmlNode node in xml.SelectNodes("//*"))
                            {
                                if (Object.ReferenceEquals(node, end))
                                {
                                    break;
                                }

                                if (inside && node.LocalName == "t" && node.NamespaceURI == w)
                                {
                                    texts.Add(node);
                                }

                                if (Object.ReferenceEquals(node, start))
                                {
                                    inside = true;
                                }
                            }

                            if (texts.Count == 0)
                            {
                                throw new InvalidOperationException("Bookmark without text run");
                            }

                            texts[0].InnerText = pair[1];
                            foreach (var text in texts.Skip(1))
                            {
                                text.InnerText = "";
                            }

                            filled++;
                        }

                        if (filled == 2)
                        {
                            header = "";
                        }

                        entry.Delete();
                        entry = zip.CreateEntry("word/document.xml");
                        using (var writer = XmlWriter.Create(entry.Open(), new XmlWriterSettings { Encoding = new System.Text.UTF8Encoding(false) }))
                            xml.Save(writer);
                    }

                    var rich = Assembly.LoadFrom(Path.Combine(components, "DevExpress.RichEdit.v17.2.Core.dll"));
                    var type = rich.GetType("DevExpress.XtraRichEdit.RichEditDocumentServer", false) ?? Assembly.LoadFrom(Path.Combine(components, "DevExpress.XtraRichEdit.v17.2.dll")).GetType("DevExpress.XtraRichEdit.RichEditDocumentServer", true);
                    dynamic server = Activator.CreateInstance(type);
                    try
                    {
                        server.LoadDocument(docx);
                        server.ExportToPdf(basePdf);
                    }
                    finally
                    {
                        server.Dispose();
                    }
                }

                var overlay = Assembly.LoadFrom(@"C:\AGORA-OUT\pdf-runtime\pdfsharp\AgoraPdfOverlay.dll");
                overlay.GetType("AgoraPdfOverlay", true).GetMethod("Create").Invoke(null, new object[] { basePdf, qr, output, header });
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }
    }
}
