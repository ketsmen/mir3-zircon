namespace Library
{
    public enum LogLevel
    {
        Info,
        Warning,
        Error,
    }

    public enum LogCategory
    {
        General,
        Network,
        Web,
        Database,
        Rendering,
        CustomSystem,
    }

    public static class LogText
    {
        public static string Format(LogLevel level, LogCategory category, string message)
        {
            string prefix = level == LogLevel.Info ? string.Empty : $"[{level}] ";

            if (category != LogCategory.General)
                prefix += $"[{category}] ";

            return prefix + message;
        }

        public static string WithCategory(LogCategory category, string message)
        {
            return Format(LogLevel.Info, category, message);
        }
    }
}
