/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.
*/
using SW2GZ.Ros2;
using Xunit;

namespace SW2GZ.Test.Writers
{
    public class TestAmentCMakeWriter : WriterTestBase
    {
        [Fact]
        [Trait("Category", "Unit")]
        public void EmitsCMakeListsWithAmentCmakeAndInstallDirs()
        {
            new AmentCMakeWriter("my_robot_description").Write(TempDir);
            Assert.True(Exists("CMakeLists.txt"));
            var txt = ReadAllText("CMakeLists.txt");
            Assert.Contains("cmake_minimum_required(VERSION 3.8)", txt);
            Assert.Contains("project(my_robot_description)", txt);
            Assert.Contains("find_package(ament_cmake REQUIRED)", txt);
            Assert.Contains("install(DIRECTORY urdf launch config worlds meshes", txt);
            Assert.Contains("ament_package()", txt);
        }
    }
}
