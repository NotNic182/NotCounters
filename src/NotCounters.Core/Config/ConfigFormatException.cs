using System;

namespace NotCounters.Core.Config
{
    public sealed class ConfigFormatException : Exception
    {
        public ConfigFormatException(string message)
            : base(message)
        {
        }

        public ConfigFormatException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
