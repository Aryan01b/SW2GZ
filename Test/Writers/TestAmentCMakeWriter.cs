/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.
*/
using SW2GZ.Ros2;
using Xunit;

namespace SW2GZ.Writers.Tests
{
    public class TestAmentCMakeWriter
    {
        [Fact]
        public void Write_IncludesMeshesInstall_WhenMeshesPresent()
        {
            var cmake = AmentCMakeWriter.Write(new AmentCMakeInput("pkg", hasMeshes: true));
            Assert.Contains("install(DIRECTORY", cmake);
            Assert.Contains("meshes", cmake);
        }

        [Fact]
        public void Write_OmitsMeshesFromInstall_WhenNoMeshes()
        {
            var cmake = AmentCMakeWriter.Write(new AmentCMakeInput("pkg", hasMeshes: false));
            Assert.DoesNotContain("meshes", cmake);
        }

        [Fact]
        public void Write_AlwaysInstallsCoreDirs()
        {
            var cmake = AmentCMakeWriter.Write(new AmentCMakeInput("pkg", hasMeshes: false));
            Assert.Contains("urdf", cmake);
            Assert.Contains("launch", cmake);
            Assert.Contains("config", cmake);
            Assert.Contains("worlds", cmake);
        }

        [Fact]
        public void Write_GzMode_InstallsModelsNotUrdfOrConfig()
        {
            var cmake = AmentCMakeWriter.Write(new AmentCMakeInput(
                "asset_pkg", hasMeshes: false, hasModels: true, hasUrdf: false, hasConfig: false));
            Assert.Contains("models", cmake);
            Assert.Contains("launch", cmake);
            Assert.Contains("worlds", cmake);
            Assert.DoesNotContain("urdf", cmake);
            Assert.DoesNotContain("config", cmake);
        }

        [Fact]
        public void Write_EmitsProjectName()
        {
            var cmake = AmentCMakeWriter.Write(new AmentCMakeInput("arm_2dof_description", hasMeshes: true));
            Assert.Contains("project(arm_2dof_description)", cmake);
        }

        [Fact]
        public void Write_EmitsAmentCmakeFindPackage()
        {
            var cmake = AmentCMakeWriter.Write(new AmentCMakeInput("pkg", hasMeshes: true));
            Assert.Contains("find_package(ament_cmake REQUIRED)", cmake);
        }

        [Fact]
        public void Write_EmitsAmentPackageCall()
        {
            var cmake = AmentCMakeWriter.Write(new AmentCMakeInput("pkg", hasMeshes: true));
            Assert.Contains("ament_package()", cmake);
        }

        [Fact]
        public void Write_NullInput_Throws()
        {
            Assert.Throws<System.ArgumentNullException>(() => AmentCMakeWriter.Write(null));
        }

        [Fact]
        public void Write_NullPackageName_Throws()
        {
            Assert.Throws<System.ArgumentException>(() => AmentCMakeWriter.Write(new AmentCMakeInput(null, hasMeshes: true)));
        }

        [Fact]
        public void Write_WhitespacePackageName_Throws()
        {
            Assert.Throws<System.ArgumentException>(() => AmentCMakeWriter.Write(new AmentCMakeInput(" ", hasMeshes: true)));
        }
    }
}
