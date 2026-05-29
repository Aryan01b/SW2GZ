#!/bin/bash
URDF=/home/rnd/ros2_ws/install/assem1/share/assem1/urdf/assem1.urdf.xacro
echo "PATCHING $URDF"
ls -la "$URDF" || { echo "Not found"; exit 1; }
cp "$URDF" "$URDF.bak"
sed -i 's|</robot>|<joint name="a3_1_to_a3_2" type="fixed"><parent link="a3_1"/><child link="a3_2"/><origin xyz="0 0 0" rpy="0 0 0"/></joint>\n</robot>|' "$URDF"
echo "--- last 8 lines ---"
tail -8 "$URDF"
