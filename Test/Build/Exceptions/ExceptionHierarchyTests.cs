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
        public void Sw2gzGeometryException_RoundTripsMessageAndInner()
        {
            var ex1 = new Sw2gzGeometryException("degenerate hull");
            Assert.IsAssignableFrom<Sw2gzExportException>(ex1);
            Assert.Equal("degenerate hull", ex1.Message);

            var inner = new System.InvalidOperationException("bad input");
            var ex2 = new Sw2gzGeometryException("hull wrap", inner);
            Assert.Same(inner, ex2.InnerException);
            Assert.Equal("hull wrap", ex2.Message);
        }

        [Fact]
        public void Sw2gzValidationException_RoundTripsMessageAndInner()
        {
            var ex1 = new Sw2gzValidationException("urdf invalid");
            Assert.IsAssignableFrom<Sw2gzExportException>(ex1);
            Assert.Equal("urdf invalid", ex1.Message);

            var inner = new System.Xml.XmlException("bad xml");
            var ex2 = new Sw2gzValidationException("validation wrap", inner);
            Assert.Same(inner, ex2.InnerException);
            Assert.Equal("validation wrap", ex2.Message);
        }
    }
}
