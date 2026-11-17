using System;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using PdfSharp.Drawing;

public static class AgoraPdfOverlay
{
    /// <summary>
    /// Добавляет QR-код и регистрационные реквизиты в производный PDF.
    /// </summary>
    public static void Create(string source, string qr, string output, string registrationText)
    {
        using (var input = PdfReader.Open(source, PdfDocumentOpenMode.Import))
        using (var document = new PdfDocument())
        {
            if (input.PageCount == 0)
            {
                throw new InvalidOperationException("Исходный PDF не содержит страниц.");
            }

            foreach (PdfPage page in input.Pages)
            {
                document.AddPage(page);
            }

            document.Info.Title = "Подписанный вариант";
            var last = document.Pages[document.PageCount - 1];
            double size = 10.0 * 72.0 / 25.4;
            double margin = 10.0 * 72.0 / 25.4;
            using (var graphics = XGraphics.FromPdfPage(last))
            using (var image = XImage.FromFile(qr))
            {
                graphics.DrawImage(image, last.Width.Point - margin - size, last.Height.Point - margin - size, size, size);
                if (!String.IsNullOrEmpty(registrationText))
                {
                    graphics.DrawString(registrationText, new XFont("Arial", 8, XFontStyleEx.Regular), XBrushes.Black, new XRect(margin, last.Height.Point - margin - 12, last.Width.Point - 2 * margin - size - 8, 12), XStringFormats.TopLeft);
                }
            }

            document.Save(output);
        }
    }
}
