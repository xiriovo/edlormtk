using System.Windows.Media;

namespace tools.Modules.Common
{
    /// <summary>
    /// 日志项数据模型
    /// </summary>
    public class LogItem
    {
        public string Text { get; set; } = "";
        public Brush Color { get; set; } = Brushes.Gray;
        public DateTime Timestamp { get; set; } = DateTime.Now;

        public LogItem() { }

        public LogItem(string text, string hexColor = "#444444")
        {
            Text = $"[{Timestamp:HH:mm:ss}] {text}";
            Color = new BrushConverter().ConvertFromString(hexColor) as Brush ?? Brushes.Gray;
        }
    }
}
