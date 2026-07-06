using DinkToPdf;

namespace WARDMANAGEMENTSYSTEM.Components
{
    public static class HtmlToPdfConverter
    {
        public static byte[] Convert(string html)
        {
            var converter = new SynchronizedConverter(new PdfTools());
            var doc = new HtmlToPdfDocument()
            {
                GlobalSettings = {
                ColorMode = ColorMode.Color,
                Orientation = Orientation.Portrait,
                PaperSize = PaperKind.A4,
                Margins = new MarginSettings { Top = 10, Bottom = 10 }
            },
                Objects = {
                new ObjectSettings() {
                    HtmlContent = html,
                    WebSettings = { DefaultEncoding = "utf-8" }
                }
            }
            };
            return converter.Convert(doc);
        }
    }
}
