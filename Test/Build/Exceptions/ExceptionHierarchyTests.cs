using SW2GZ.Exceptions;
using Xunit;

namespace SW2GZ.Build.Exceptions.Tests
{
    public class ExceptionHierarchyTests
    {
        [Fact]
        public void MaterialMissingException_DerivesFromSw2gzExportException()
        {
            var ex = new MaterialMissingException("part1");
            Assert.IsAssignableFrom<Sw2gzExportException>(ex);
            Assert.Contains("part1", ex.Message);
        }

        [Fact]
        public void Sw2gzMeshException_CarriesInner()
        {
            var inner = new System.IO.IOException("boom");
            var ex = new Sw2gzMeshException("tessellation failed", inner);
            Assert.Same(inner, ex.InnerException);
        }

        [Fact]
        public void Sw2gzGeometryException_Constructs()
        {
            var ex = new Sw2gzGeometryException("degenerate hull");
            Assert.IsAssignableFrom<Sw2gzExportException>(ex);
        }

        [Fact]
        public void Sw2gzValidationException_Constructs()
        {
            var ex = new Sw2gzValidationException("urdf invalid");
            Assert.IsAssignableFrom<Sw2gzExportException>(ex);
        }
    }
}
