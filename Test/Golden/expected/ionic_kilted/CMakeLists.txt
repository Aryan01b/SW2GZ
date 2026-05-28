cmake_minimum_required(VERSION 3.8)
project(three_dof_arm_description)

find_package(ament_cmake REQUIRED)

install(DIRECTORY urdf launch config worlds meshes
  DESTINATION share/${PROJECT_NAME}
)

ament_package()
