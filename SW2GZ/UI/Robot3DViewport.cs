/*
Copyright (c) 2026 Aryan Arlikar. MIT License â€” see CONTRIBUTING.md.

WPF UserControl that renders the exported robot in 3D â€” collision STLs
positioned in the world frame via UrdfTransforms, world axes (X red,
Y green, Z blue), and an orbit camera. Hosted in the PreviewDialog via
ElementHost so the user sees what Gz/RViz will see BEFORE writing files.

Mouse:
  left-drag    â€” orbit the camera around the scene centroid
  wheel        â€” zoom in / out (clamped to scene radius)

ROS convention: Z is up. The camera's UpDirection is fixed to +Z and the
world axes use the same orientation, so the rotation baked into
`world_to_<root>` lines up visually with the rendered scene.

Pure-WPF / no external 3D library â€” uses System.Windows.Media.Media3D
exclusively. Loaded only by the SW_INTEROP build because the
PreviewDialog that hosts it is SW_INTEROP-only.
*/
#if SW_INTEROP
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using SW2GZ.Build.Model;
using NumVec = System.Numerics.Vector3;
using NumMat = System.Numerics.Matrix4x4;

namespace SW2GZ.UI
{
    public sealed class Robot3DViewport : UserControl
    {
        private readonly Viewport3D _viewport;
        private readonly PerspectiveCamera _camera;
        private readonly DirectionalLight _headLight;
        private NumVec _sceneCenter;
        private double _sceneRadius = 1.0;
        private double _camTheta = System.Math.PI * 0.25;   // azimuth around +Z
        private double _camPhi   = System.Math.PI * 0.20;   // elevation above XY plane
        private double _camDistance;
        private Point? _dragAnchor;

        public Robot3DViewport(string meshesDir, string urdfText)
        {
            _viewport = new Viewport3D { ClipToBounds = true };
            _camera = new PerspectiveCamera
            {
                FieldOfView = 45,
                NearPlaneDistance = 0.001,
                FarPlaneDistance = 1000,
            };
            _viewport.Camera = _camera;

            var group = new Model3DGroup();
            group.Children.Add(new AmbientLight(Color.FromRgb(70, 70, 70)));
            _headLight = new DirectionalLight(Colors.White, new Vector3D(0, 0, -1));
            group.Children.Add(_headLight);

            // World axes â€” colored cylinders along +X / +Y / +Z.
            group.Children.Add(BuildAxisCylinder(new Vector3D(1, 0, 0), Colors.Red));
            group.Children.Add(BuildAxisCylinder(new Vector3D(0, 1, 0), Colors.Lime));
            group.Children.Add(BuildAxisCylinder(new Vector3D(0, 0, 1), Colors.DeepSkyBlue));

            // Robot meshes â€” one MeshGeometry3D per link, transformed to world.
            var placements = UrdfTransforms.Compute(urdfText);
            var allWorldPoints = new List<NumVec>();
            int linksRendered = 0;

            foreach (UrdfTransforms.LinkPlacement lp in placements)
            {
                string stlPath = ResolveCollisionStlPath(meshesDir, lp.LinkName);
                if (stlPath == null || !File.Exists(stlPath)) continue;

                try
                {
                    StlBinaryParser.Triangles tris = StlBinaryParser.ParseFile(stlPath);
                    var mesh = new MeshGeometry3D();
                    foreach (NumVec v in tris.Vertices)
                    {
                        NumVec w = NumVec.Transform(v, lp.LinkToWorld);
                        mesh.Positions.Add(new Point3D(w.X, w.Y, w.Z));
                        allWorldPoints.Add(w);
                    }
                    foreach (int idx in tris.Indices) mesh.TriangleIndices.Add(idx);

                    var brush = new SolidColorBrush(Color.FromRgb(180, 190, 210));
                    var material = new DiffuseMaterial(brush);
                    var geom = new GeometryModel3D(mesh, material)
                    {
                        BackMaterial = material,
                    };
                    group.Children.Add(geom);
                    linksRendered++;
                }
                catch
                {
                    // Skip unreadable mesh â€” preview is best-effort, never throws
                    // through to the user just because one STL was malformed.
                }
            }

            _viewport.Children.Add(new ModelVisual3D { Content = group });

            FrameScene(allWorldPoints);

            // Composition: viewport + overlay status text.
            var grid = new Grid();
            grid.Children.Add(_viewport);
            var statusLabel = new TextBlock
            {
                Text = linksRendered + " link(s) rendered  |  Z-up world frame  |  drag: orbit  |  wheel: zoom",
                Foreground = new SolidColorBrush(Color.FromRgb(200, 210, 220)),
                Margin = new Thickness(8),
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Bottom,
                HorizontalAlignment = HorizontalAlignment.Left,
                IsHitTestVisible = false,
            };
            grid.Children.Add(statusLabel);
            Content = grid;

            Background = new SolidColorBrush(Color.FromRgb(34, 36, 42));
            Focusable = true;
            MouseDown += OnMouseDown;
            MouseMove += OnMouseMove;
            MouseUp += OnMouseUp;
            MouseWheel += OnMouseWheel;
        }

        /// Resolves <meshesDir>/<link>_collision.stl, with a fall-through to
        /// <meshesDir>/<link>.stl for SDF-mode exports (which write the
        /// collision under that name).
        private static string ResolveCollisionStlPath(string meshesDir, string linkName)
        {
            if (string.IsNullOrEmpty(meshesDir) || string.IsNullOrEmpty(linkName)) return null;
            string p = Path.Combine(meshesDir, linkName + "_collision.stl");
            if (File.Exists(p)) return p;
            string alt = Path.Combine(meshesDir, linkName + ".stl");
            return File.Exists(alt) ? alt : null;
        }

        private void FrameScene(List<NumVec> pts)
        {
            if (pts.Count == 0)
            {
                _sceneCenter = NumVec.Zero;
                _sceneRadius = 1.0;
            }
            else
            {
                NumVec min = pts[0], max = pts[0];
                for (int i = 1; i < pts.Count; i++)
                {
                    min = NumVec.Min(min, pts[i]);
                    max = NumVec.Max(max, pts[i]);
                }
                _sceneCenter = (min + max) * 0.5f;
                double extent = (max - min).Length();
                _sceneRadius = System.Math.Max(extent * 0.5, 0.5);
            }
            _camDistance = _sceneRadius * 3.5;
            UpdateCamera();
        }

        private void UpdateCamera()
        {
            double cosPhi = System.Math.Cos(_camPhi);
            double sinPhi = System.Math.Sin(_camPhi);
            double cosTheta = System.Math.Cos(_camTheta);
            double sinTheta = System.Math.Sin(_camTheta);
            var offset = new Vector3D(
                _camDistance * cosPhi * cosTheta,
                _camDistance * cosPhi * sinTheta,
                _camDistance * sinPhi);

            var center = new Point3D(_sceneCenter.X, _sceneCenter.Y, _sceneCenter.Z);
            _camera.Position = new Point3D(center.X + offset.X, center.Y + offset.Y, center.Z + offset.Z);
            _camera.LookDirection = new Vector3D(-offset.X, -offset.Y, -offset.Z);
            _camera.UpDirection = new Vector3D(0, 0, 1);   // ROS convention â€” Z up
            _headLight.Direction = _camera.LookDirection;
        }

        // Thin cylinder oriented along `dir`, colored. ScreenSpaceLines3D would
        // be lighter but isn't available outside HelixToolkit; pure WPF cylinders
        // are cheap enough at 8 segments Ã— 3 axes.
        private GeometryModel3D BuildAxisCylinder(Vector3D dir, Color color)
        {
            const double length = 0.5;
            const double radius = 0.005;
            const int segments = 12;

            var mesh = new MeshGeometry3D();
            for (int i = 0; i < segments; i++)
            {
                double a0 = 2 * System.Math.PI * i / segments;
                double a1 = 2 * System.Math.PI * (i + 1) / segments;
                Point3D p0 = new Point3D(radius * System.Math.Cos(a0), radius * System.Math.Sin(a0), 0);
                Point3D p1 = new Point3D(radius * System.Math.Cos(a1), radius * System.Math.Sin(a1), 0);
                Point3D p2 = new Point3D(radius * System.Math.Cos(a1), radius * System.Math.Sin(a1), length);
                Point3D p3 = new Point3D(radius * System.Math.Cos(a0), radius * System.Math.Sin(a0), length);
                int b = mesh.Positions.Count;
                mesh.Positions.Add(p0); mesh.Positions.Add(p1);
                mesh.Positions.Add(p2); mesh.Positions.Add(p3);
                mesh.TriangleIndices.Add(b);     mesh.TriangleIndices.Add(b + 1); mesh.TriangleIndices.Add(b + 2);
                mesh.TriangleIndices.Add(b);     mesh.TriangleIndices.Add(b + 2); mesh.TriangleIndices.Add(b + 3);
            }

            // Built along +Z by default â€” rotate to dir.
            var transform = new MatrixTransform3D();
            var zAxis = new Vector3D(0, 0, 1);
            Vector3D crossZD = Vector3D.CrossProduct(zAxis, dir);
            double crossLenSq = crossZD.LengthSquared;
            if (crossLenSq < 1e-9)
            {
                if (Vector3D.DotProduct(zAxis, dir) < 0)
                    transform.Matrix = new Matrix3D(1, 0, 0, 0,  0, 1, 0, 0,  0, 0, -1, 0,  0, 0, 0, 1);
                // else identity
            }
            else
            {
                crossZD.Normalize();
                double angleDeg = System.Math.Acos(System.Math.Max(-1.0, System.Math.Min(1.0, Vector3D.DotProduct(zAxis, dir)))) * 180.0 / System.Math.PI;
                var rot = new RotateTransform3D(new AxisAngleRotation3D(crossZD, angleDeg));
                transform.Matrix = rot.Value;
            }

            var material = new EmissiveMaterial(new SolidColorBrush(color));
            return new GeometryModel3D(mesh, material)
            {
                BackMaterial = material,
                Transform = transform,
            };
        }

        private void OnMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                _dragAnchor = e.GetPosition(this);
                CaptureMouse();
                Focus();
            }
        }

        private void OnMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                _dragAnchor = null;
                ReleaseMouseCapture();
            }
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (_dragAnchor == null) return;
            Point pos = e.GetPosition(this);
            double dx = pos.X - _dragAnchor.Value.X;
            double dy = pos.Y - _dragAnchor.Value.Y;
            _dragAnchor = pos;
            _camTheta -= dx * 0.01;
            _camPhi = System.Math.Max(-System.Math.PI * 0.5 + 0.05, System.Math.Min(System.Math.PI * 0.5 - 0.05, _camPhi + dy * 0.01));
            UpdateCamera();
        }

        private void OnMouseWheel(object sender, MouseWheelEventArgs e)
        {
            double factor = e.Delta > 0 ? 0.88 : 1.14;
            _camDistance = System.Math.Max(_sceneRadius * 0.5,
                            System.Math.Min(_sceneRadius * 30, _camDistance * factor));
            UpdateCamera();
        }
    }
}
#endif
