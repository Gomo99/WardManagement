namespace WARDMANAGEMENTSYSTEM.ViewModel
{
    public class TimelineEntry
    {
        public string Step { get; set; }
        public string Status { get; set; }   // "Completed", "Active", "Pending"
        public string? Date { get; set; }
        public string Icon { get; set; }
    }
}