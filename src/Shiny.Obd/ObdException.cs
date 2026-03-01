using System;

namespace Shiny.Obd;

public class ObdException : Exception
{
    public ObdException(string message) : base(message) { }
    public ObdException(string message, Exception innerException) : base(message, innerException) { }
}
