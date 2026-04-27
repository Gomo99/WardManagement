using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using WARDMANAGEMENTSYSTEM.AppStatus;
using WARDMANAGEMENTSYSTEM.Data;

namespace WARDMANAGEMENTSYSTEM.Services
{
    public class PdfReportService : IPdfReportService
    {
        private readonly WardDbContext _context;

        public PdfReportService(WardDbContext context)
        {
            _context = context;
            QuestPDF.Settings.License = LicenseType.Community;
        }


    }
}
