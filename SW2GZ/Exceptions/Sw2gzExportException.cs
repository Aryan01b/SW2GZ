using System;

namespace SW2GZ.Exceptions
{
    public class Sw2gzExportException : Exception
    {
        public Sw2gzExportException(string message) : base(message) { }
        public Sw2gzExportException(string message, Exception inner) : base(message, inner) { }
    }

    public sealed class MaterialMissingException : Sw2gzExportException
    {
        public string LinkName { get; }
        public MaterialMissingException(string linkName)
            : base($"Link '{linkName}' has no SW material; mass=0. Assign a material in SolidWorks before export.")
        { LinkName = linkName; }
    }

    public sealed class Sw2gzMeshException : Sw2gzExportException
    {
        public Sw2gzMeshException(string message) : base(message) { }
        public Sw2gzMeshException(string message, Exception inner) : base(message, inner) { }
    }

    public sealed class Sw2gzGeometryException : Sw2gzExportException
    {
        public Sw2gzGeometryException(string message) : base(message) { }
        public Sw2gzGeometryException(string message, Exception inner) : base(message, inner) { }
    }

    public sealed class Sw2gzValidationException : Sw2gzExportException
    {
        public Sw2gzValidationException(string message) : base(message) { }
        public Sw2gzValidationException(string message, Exception inner) : base(message, inner) { }
    }
}
