using System;
using System.Collections.Generic;
using System.Text;

namespace OrbitClient.Scripts
{
    internal class ErrorManager
    {
        public static void Log(string message, Exception? ex = null)
        {
            System.Diagnostics.Trace.WriteLine(message + (ex == null ? "" : ": " + ex));
        }
        public static void LogError(string message, Exception? ex = null)
        {
            Log(message, ex);
        }
        public static void LogWarning(string message, Exception? ex = null)
        {
            Log(message, ex);
        }
    }
}
