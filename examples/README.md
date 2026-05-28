# Example exported packages

`three_dof_arm_ros2/` is the package SW2GZ produces for the bundled
3-DOF arm fixture, targeting ROS 2 Jazzy + Gz Harmonic. Drop into any
ROS 2 workspace `src/` and `colcon build --packages-select three_dof_arm_description`.

This folder is a copy of `Test/Golden/expected/harmonic_jazzy/`. It is
regenerated whenever golden tests change (run `SW2GZ_UPDATE_GOLDENS=1
dotnet test Test/SW2GZ.Writers.Test.csproj --filter TestGoldenRobotPackage`
and copy the result here).
