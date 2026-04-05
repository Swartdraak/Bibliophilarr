namespace NzbDrone.Common.Instrumentation
{
    /// <summary>
    /// Strips control characters (CR/LF/tab) from user-supplied values before
    /// they reach structured log templates, preventing log-forging (CWE-117).
    /// </summary>
    public static class LogSanitizer
    {
        public static string Sanitize(string value)
        {
            if (value == null)
            {
                return null;
            }

            return value.Replace("\r", "").Replace("\n", "").Replace("\t", " ");
        }
    }
}
