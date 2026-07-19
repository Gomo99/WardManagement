namespace WARDMANAGEMENTSYSTEM.ViewModel
{
    public class VitalAlertViewModel
    {
        public string Parameter { get; set; }
        public string Value { get; set; }
        public string Severity { get; set; }  // "Critical", "High", "Low", "Warning"
        public string Message { get; set; }
        public string Icon { get; set; }
    }
}
