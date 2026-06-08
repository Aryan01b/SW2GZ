/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

Reads live joint values from the open SolidWorks assembly so the
PreviewServer's /joint_states endpoint can stream them to the browser.

For each joint we snapshot the relative pose
    Pose_ref = parentTransform⁻¹ ∘ childTransform
at preview start. On each Sample() call we read the CURRENT relative
pose and compute the delta from the reference, then extract:
  - revolute / continuous: twist angle about the joint axis (swing-twist).
  - prismatic:             translation along the joint axis.

Joint name in the JSON matches the URDF <joint name="..."> exactly,
which equals JointDef.Name (the wizard does not re-sanitize joint
names downstream).
*/
#if SW_INTEROP
using System;
using System.Collections.Generic;
using System.Numerics;
using SolidWorks.Interop.sldworks;
using SW2GZ.Build;
using SW2GZ.Build.Model;
using SW2GZ.Build.Urdf;
using SW2GZ.Math;
using SW2GZ.SwSurface;

namespace SW2GZ.URDFExport
{
    public sealed class SwJointStateSampler
    {
        public sealed class Sample
        {
            public string Name { get; }
            public string ParentPart { get; }
            public string ChildPart { get; }
            public Vector3 Axis { get; }            // assembly-frame at reference time
            public bool IsPrismatic { get; }
            public Pose ReferenceRelative { get; }   // child in parent, at preview start

            public Sample(string name, string parent, string child,
                Vector3 axis, bool prismatic, Pose referenceRel)
            {
                Name = name;
                ParentPart = parent;
                ChildPart = child;
                Axis = axis;
                IsPrismatic = prismatic;
                ReferenceRelative = referenceRel;
            }
        }

        private readonly AssemblyDoc _doc;
        private readonly IReadOnlyList<Sample> _joints;

        public SwJointStateSampler(AssemblyDoc doc, IReadOnlyList<Sample> joints)
        {
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
            _joints = joints ?? Array.Empty<Sample>();
        }

        /// Builds the sampler list by walking config.Joints + config.Links,
        /// resolving each joint's parent + child first-part paths, and
        /// capturing the current relative pose as the reference.
        public static SwJointStateSampler Build(
            AssemblyDoc doc,
            IReadOnlyList<LinkDef> links,
            IReadOnlyList<JointDef> joints)
        {
            // linkName → first part path
            var firstPart = new Dictionary<string, string>(StringComparer.Ordinal);
            var byName = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            object[] comps = (object[])doc.GetComponents(true);
            if (comps != null)
                foreach (object o in comps)
                {
                    var c = (Component2)o;
                    if (!byName.TryGetValue(c.Name2, out var list))
                        byName[c.Name2] = list = new List<string>();
                    list.Add(c.Name2);
                }
            foreach (LinkDef l in links ?? new List<LinkDef>())
            {
                if (l.ComponentIds == null) continue;
                foreach (string id in l.ComponentIds)
                {
                    if (id != null && byName.TryGetValue(id, out var paths) && paths.Count > 0)
                    {
                        firstPart[l.Name] = paths[0];
                        break;
                    }
                }
            }

            var samples = new List<Sample>();
            foreach (JointDef j in joints ?? new List<JointDef>())
            {
                if (!firstPart.TryGetValue(j.ParentLink, out string pPath)) continue;
                if (!firstPart.TryGetValue(j.ChildLink,  out string cPath)) continue;

                Pose pRef = ReadComponentPose(doc, pPath);
                Pose cRef = ReadComponentPose(doc, cPath);
                Pose refRel = PoseMath.Relative(pRef, cRef);

                bool prismatic = j.Type == UrdfJointType.Prismatic;
                var axis = new Vector3((float)j.AxisX, (float)j.AxisY, (float)j.AxisZ);
                samples.Add(new Sample(j.Name, pPath, cPath, axis, prismatic, refRel));
            }
            return new SwJointStateSampler(doc, samples);
        }

        /// Called by PreviewServer's /joint_states handler each request
        /// (≈10 Hz). Safe to call from any thread — the SW COM proxy is
        /// single-threaded apartment, so calls serialize at the COM layer.
        public IReadOnlyDictionary<string, double> ReadAll()
        {
            var result = new Dictionary<string, double>(_joints.Count);
            foreach (Sample s in _joints)
            {
                try
                {
                    Pose pNow = ReadComponentPose(_doc, s.ParentPart);
                    Pose cNow = ReadComponentPose(_doc, s.ChildPart);
                    Pose currRel = PoseMath.Relative(pNow, cNow);
                    Pose delta = PoseMath.Relative(s.ReferenceRelative, currRel);

                    double v = s.IsPrismatic
                        ? SwingTwist.DisplacementAlong(delta.Position, s.Axis)
                        : SwingTwist.TwistAngleAround(delta.Rotation, s.Axis);
                    result[s.Name] = v;
                }
                catch
                {
                    // Component may have been deleted / suppressed mid-preview;
                    // skip its entry and let the next poll retry.
                }
            }
            return result;
        }

        // Reads Component2.Transform2 from the assembly and produces a Pose
        // matching the convention used by WizardAssemblyWalker.GetComponentPose
        // (row-major rotation + translation + scale).
        private static Pose ReadComponentPose(AssemblyDoc doc, string partPath)
        {
            object[] comps = (object[])doc.GetComponents(false);
            Component2 comp = SolidWorksMassProperties.FindComponent(comps, partPath);
            if (comp == null) return Pose.Identity;
            MathTransform xform = comp.Transform2;
            if (!(xform?.ArrayData is double[] d) || d.Length < 12) return Pose.Identity;

            var translation = new Vector3((float)d[9], (float)d[10], (float)d[11]);
            float m00 = (float)d[0], m01 = (float)d[1], m02 = (float)d[2];
            float m10 = (float)d[3], m11 = (float)d[4], m12 = (float)d[5];
            float m20 = (float)d[6], m21 = (float)d[7], m22 = (float)d[8];
            float trace = m00 + m11 + m22;
            float qx, qy, qz, qw;
            if (trace > 0f)
            {
                float s = (float)System.Math.Sqrt(trace + 1f) * 2f;
                qw = 0.25f * s;
                qx = (m21 - m12) / s;
                qy = (m02 - m20) / s;
                qz = (m10 - m01) / s;
            }
            else if (m00 > m11 && m00 > m22)
            {
                float s = (float)System.Math.Sqrt(1f + m00 - m11 - m22) * 2f;
                qw = (m21 - m12) / s;
                qx = 0.25f * s;
                qy = (m01 + m10) / s;
                qz = (m02 + m20) / s;
            }
            else if (m11 > m22)
            {
                float s = (float)System.Math.Sqrt(1f + m11 - m00 - m22) * 2f;
                qw = (m02 - m20) / s;
                qx = (m01 + m10) / s;
                qy = 0.25f * s;
                qz = (m12 + m21) / s;
            }
            else
            {
                float s = (float)System.Math.Sqrt(1f + m22 - m00 - m11) * 2f;
                qw = (m10 - m01) / s;
                qx = (m02 + m20) / s;
                qy = (m12 + m21) / s;
                qz = 0.25f * s;
            }
            return new Pose(translation, Quaternion.Normalize(new Quaternion(qx, qy, qz, qw)));
        }
    }
}
#endif
