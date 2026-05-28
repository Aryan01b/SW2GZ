/*
Copyright (c) 2015 Stephen Brawner

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in
all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.  IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
THE SOFTWARE.
*/

using MathNet.Numerics.LinearAlgebra;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SW2GZ.Gz;
using SW2GZ.Ros2;
using SW2GZ.URDF;
using SW2GZ.URDFExport.CSV;
using SW2GZ.Utilities;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Xml;
using System.Xml.Serialization;

namespace SW2GZ.URDFExport
{
    // This class contains a long list of methods that are used throughout the export process.
    // Methods for building links and joints are contained in here.
    // Many of the methods are overloaded, but seek to reduce repeated code as much as possible
    // (i.e. the overloaded methods call eachother).
    // These methods are used by the PartExportForm, the AssemblyExportForm and the PropertyManager Page
    public partial class ExportHelper
    {
        #region class variables

        private static readonly log4net.ILog logger = Logger.GetLogger();

        [XmlIgnore]
        public ISldWorks iSwApp = null;

        [XmlIgnore]
        private bool mBinary;

        private bool mshowInfo;
        private bool mSTLPreview;
        private bool mTranslateToPositive;
        private bool mSaveComponentsIntoOneFile;
        private int mSTLUnits;
        private int mSTLQuality;
        private double mHideTransitionSpeed;

        private UserProgressBar progressBar;

        [XmlIgnore]
        public ModelDoc2 ActiveSWModel;

        [XmlIgnore]
        public MathUtility swMath;

        [XmlIgnore]
        public Object SWMathPID
        { get; set; }

        public Robot URDFRobot
        { get; set; }

        public string PackageName
        { get; set; }

        public string SavePath
        { get; set; }

        // SW2GZ Phase 4: per-export selection wired from UI (Phase 5).
        public TargetProfile Profile { get; set; } = new TargetProfile();
        internal string Profile_Author { get; set; } = System.Environment.UserName;
        internal string Profile_Email { get; set; } = "TODO@example.com";
        internal string Profile_License { get; set; } = "Apache-2.0";

        // Set the error reason when ExportRobot fails (consumed by UI MessageBox).
        public string ExportErrorWhy { get; private set; }

        public readonly List<Link> Links;

        private readonly List<string> ReferenceCoordinateSystemNames;
        private readonly List<string> ReferenceAxesNames;

        private bool ComputeInertialValues;
        private bool ComputeVisualCollision;
        private bool ComputeJointKinematics;
        private bool ComputeJointLimits;

        #endregion class variables

        // Constructor for SW2GZ Exporter class
        public ExportHelper(SldWorks iSldWorksApp)
        {
            ConstructExporter(iSldWorksApp);
            iSwApp.GetUserProgressBar(out progressBar);

            SavePath = System.Environment.ExpandEnvironmentVariables("%HOMEDRIVE%%HOMEPATH%");
            PackageName = ActiveSWModel.GetTitle();

            ReferenceCoordinateSystemNames = FindRefGeoNames("CoordSys");
            ReferenceAxesNames = FindRefGeoNames("RefAxis");

            ComputeInertialValues = true;
            ComputeVisualCollision = true;
            ComputeJointKinematics = true;
            ComputeJointLimits = true;
        }

        public void SetComputeInertial(bool computeInertial)
        {
            ComputeInertialValues = computeInertial;
        }

        public void SetComputeVisualCollision(bool computeVisual)
        {
            ComputeVisualCollision = computeVisual;
        }

        public void SetComputeJointKinematics(bool computeKinematics)
        {
            ComputeJointKinematics = computeKinematics;
        }

        public void SetComputeJointLimits(bool computeJointLimits)
        {
            ComputeJointLimits = computeJointLimits;
        }

        private void ConstructExporter(SldWorks iSldWorksApp)
        {
            iSwApp = iSldWorksApp;
            ActiveSWModel = (ModelDoc2)iSwApp.ActiveDoc;
            swMath = iSwApp.GetMathUtility();
        }

        #region Export Methods

        // Phase 4 (SW2GZ): branches on Profile.Mode to drive the new Ros2Package
        // and Sdf* writers. Mesh export pipeline (ExportFiles) is restored in a
        // follow-up commit once the SW per-link mesh save is reworked.
        // Narrow catches per B11 — no generic Exception swallow.
        public void ExportRobot(bool exportSTL = true, MeshExportFormat meshFormat = MeshExportFormat.STL)
        {
            ExportErrorWhy = null;
            try
            {
                if (URDFRobot == null)
                    throw new InvalidOperationException("URDFRobot is null. Call CreateRobotFromActiveModel first.");
                if (string.IsNullOrWhiteSpace(SavePath))
                    throw new InvalidOperationException("SavePath is not set.");

                string outDir = SavePath;
                Directory.CreateDirectory(outDir);

                List<string> jointNames = GetJointNames();

                switch (Profile.Mode)
                {
                    case ExportMode.RobotPackage:
                        new Ros2Package(new Ros2Package.Options
                        {
                            PackageName = (PackageName ?? URDFRobot.Name) + "_description",
                            Maintainer = Profile_Author,
                            MaintainerEmail = Profile_Email,
                            License = Profile_License,
                            JointNames = jointNames,
                            Profile = Profile,
                            UrdfBodyXml = BuildUrdfBodyXml(),
                        }).Write(outDir);
                        break;
                    case ExportMode.SdfModel:
                        new ModelConfigWriter(new ModelConfigWriter.Input
                        {
                            Name = URDFRobot.Name,
                            SdfVersion = TargetProfile.SdfVersion[Profile.Gz],
                            Author = Profile_Author,
                            Email = Profile_Email,
                        }).Write(outDir);
                        new SdfModelWriter(BuildSdfModelInput(), Profile).Write(outDir);
                        break;
                    case ExportMode.SdfWorld:
                        new SdfWorldWriter(Profile, URDFRobot.Name)
                            .WriteEmptyWorld(outDir, URDFRobot.Name + ".world");
                        break;
                }

                logger.Info("SW2GZ export complete. Output: " + outDir);
            }
            catch (COMException comEx)
            {
                logger.Error("SolidWorks COM failure during export", comEx);
                ExportErrorWhy = "SolidWorks COM failure: " + comEx.Message;
                throw;
            }
            catch (IOException ioEx)
            {
                logger.Error("File system error during export", ioEx);
                ExportErrorWhy = "File system error: " + ioEx.Message;
                throw;
            }
            catch (XmlException xmlEx)
            {
                logger.Error("XML emit failure", xmlEx);
                ExportErrorWhy = "XML emit failure: " + xmlEx.Message;
                throw;
            }
            // No generic Exception catch (B11): unknown errors propagate.
        }

        // Serializes the URDF link/joint subtree as XML for embedding inside a
        // <robot> xacro root. Uses URDFElement.WriteURDF which recurses through
        // ChildElements (links and joints both implement WriteURDF).
        private string BuildUrdfBodyXml()
        {
            using (var ms = new MemoryStream())
            {
                var settings = new XmlWriterSettings
                {
                    OmitXmlDeclaration = true,
                    Indent = true,
                    ConformanceLevel = ConformanceLevel.Fragment,
                    Encoding = new UTF8Encoding(false),
                };
                using (var w = XmlWriter.Create(ms, settings))
                {
                    URDFRobot.BaseLink.WriteURDF(w);
                }
                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }

        // Walks URDFRobot tree into a SW-free POCO that SdfModelWriter consumes.
        private SdfModelInput BuildSdfModelInput()
        {
            var links = new List<SdfLinkData>();
            var joints = new List<SdfJointData>();
            void Walk(Link l)
            {
                links.Add(new SdfLinkData { Name = l.Name });
                if (l.Children != null)
                {
                    foreach (Link c in l.Children)
                    {
                        if (c.Joint != null)
                        {
                            joints.Add(new SdfJointData
                            {
                                Name = c.Joint.Name,
                                Type = c.Joint.Type ?? "fixed",
                                Parent = l.Name,
                                Child = c.Name,
                            });
                        }
                        Walk(c);
                    }
                }
            }
            if (URDFRobot?.BaseLink != null) Walk(URDFRobot.BaseLink);
            return new SdfModelInput { Name = URDFRobot.Name, Links = links, Joints = joints };
        }

        public List<string> GetJointNames()
        {
            List<string> jointNames = new List<string>();

            Queue<Link> queue = new Queue<Link>();
            queue.Enqueue(URDFRobot.BaseLink);
            while (queue.Count > 0)
            {
                Link current = queue.Dequeue();
                if (current.Parent != null)
                {
                    jointNames.Add(current.Joint.Name);
                }

                foreach (Link child in current.Children)
                {
                    queue.Enqueue(child);
                }
            }

            return jointNames;
        }

        // Phase 0 (SW2GZ): The per-link mesh + texture pipeline is being
        // rewritten to flow through the new Ros2Package layout in Phase 4.
        // The old recursive ExportFiles took a URDFPackage which has been
        // deleted. Stubbed here so the project compiles.
        private void ExportFiles(Link link, object package, int count, bool exportSTL = true, MeshExportFormat meshFormat = MeshExportFormat.STL)
        {
            throw new NotImplementedException(
                "ExportFiles is disabled in Phase 0 of the SW2GZ rewrite. " +
                "Reimplemented in Phase 4 against Ros2Package.");
        }

        private void Save3dxml(Link link, string windowsMeshFilename)
        {
            int errors = 0;
            int warnings = 0;

            string coordsysName = link.Joint.CoordinateSystemName;

            logger.Info(link.Name + ": Exporting 3dxml with coordinate frame " + coordsysName);

            Dictionary<string, string> names = GetComponentRefGeoNames(coordsysName);
            ModelDoc2 ActiveDoc = ActiveSWModel;

            logger.Info(link.Name + ": Reference geometry name " + names["component"]);

            CommonSwOperations.ShowComponents(ActiveSWModel, link.SWComponents);

            int saveOptions = (int)swSaveAsOptions_e.swSaveAsOptions_Silent |
                (int)swSaveAsOptions_e.swSaveAsOptions_Copy;
            SetLinkSpecificSTLPreferences(names["geo"], link.STLQualityFine, ActiveDoc);

            logger.Info("Saving 3dxml to " + windowsMeshFilename);

            // === 3dxml Localize Link === //

            // Remove suffix from coordinate-system name.
            // ex. "Joint Origin <Arm_link-1>" -> "Joint Origin"
            // Suffix is included when coordinate is inside sub-assembly.
            string linkModelName = names["component"];
            string linkModelSuffix = " <" + linkModelName + ">";
            if(coordsysName.Contains(linkModelSuffix))
            {
                coordsysName = coordsysName.Replace(linkModelSuffix, "");
                logger.Info($"Suffix of {linkModelName} was removed from coordsysName : {coordsysName}");
            }

            // Get the model document of the link.
            ModelDoc2 linkModel;
            bool isBaseLink = linkModelName == "";
            if (isBaseLink)
            {
                linkModel = ActiveDoc;
            }
            else
            {
                if (link.SWMainComponent != null)
                {
                    linkModel = link.SWMainComponent.GetModelDoc2();
                }
                else
                {
                    logger.Warn("Could not get linkModel because SWMainComponent was null");
                    linkModel = null;
                }
            }

            // Localize the link to the certain place.
            if (linkModel != null)
            {
                MathTransform coordSysTransform =
                    linkModel.Extension.GetCoordinateSystemTransformByName(coordsysName);
                if (coordSysTransform != null)
                {
                    logger.Info("Localizing Link : " + coordsysName);
                    Matrix<double> GlobalTransform = MathOps.GetTransformation(coordSysTransform);
                    LocalizeLink(link, GlobalTransform);
                }
                else
                {
                    logger.Warn("coordSysTransform was null : " + coordsysName);
                }
            }
            else
            { 
                logger.Warn("Link model was null.");
            }
            // === 3dxml Localize Link === //

            ActiveDoc.Extension.SaveAs(windowsMeshFilename,
                (int)swSaveAsVersion_e.swSaveAsCurrentVersion, saveOptions, null, ref errors, ref warnings);

            if (errors + warnings != 0)
            {
                logger.Warn("Exporting 3dxml for link " + link.Name + " failed with error " + errors +
                    " or warnings " + warnings);
            }
            CommonSwOperations.HideComponents(ActiveSWModel, link.SWComponents);
        }

        private bool SaveSTL(Link link, string windowsMeshFilename)
        {
            int errors = 0;
            int warnings = 0;

            string coordsysName = link.Joint.CoordinateSystemName;

            logger.Info(link.Name + ": Exporting STL with coordinate frame " + coordsysName);

            Dictionary<string, string> names = GetComponentRefGeoNames(coordsysName);
            ModelDoc2 ActiveDoc = ActiveSWModel;

            logger.Info(link.Name + ": Reference geometry name " + names["component"]);

            CommonSwOperations.ShowComponents(ActiveSWModel, link.SWComponents);

            int saveOptions = (int)swSaveAsOptions_e.swSaveAsOptions_Silent |
                (int)swSaveAsOptions_e.swSaveAsOptions_Copy;
            SetLinkSpecificSTLPreferences(names["geo"], link.STLQualityFine, ActiveDoc);

            logger.Info("Saving STL to " + windowsMeshFilename);
            ActiveDoc.Extension.SaveAs(windowsMeshFilename,
                (int)swSaveAsVersion_e.swSaveAsCurrentVersion, saveOptions, null, ref errors, ref warnings);
            if (errors + warnings != 0)
            {
                logger.Warn("Exporting STL for link " + link.Name + " failed with error " + errors + 
                    " or warnings " + warnings);
            }
            CommonSwOperations.HideComponents(ActiveSWModel, link.SWComponents);

            bool success = CorrectSTLMesh(windowsMeshFilename);
            if (!success)
            {
                logger.Warn("There was an issue exporting the STL for " + link.Name + ". It " +
                    "may not be readable by CAD programs that aren't SolidWorks");
            }
            return success;
        }

        // Phase 0 (SW2GZ): single-part ExportLink path also depended on URDFPackage
        // and PackageXMLWriter. Stubbed; revisited in Phase 4.
        public void ExportLink(bool zIsUp)
        {
            throw new NotImplementedException(
                "ExportLink is disabled in Phase 0 of the SW2GZ rewrite. " +
                "Reimplemented in Phase 4 against the new Ros2Package layout.");
        }

        //Writes an empty header to the STL to get rid of the BS that SolidWorks adds to a binary STL file
        public static bool CorrectSTLMesh(string filename)
        {
            logger.Info("Removing SW header in STL file");
            try
            {
                using (FileStream fileStream = new FileStream(filename, FileMode.Open, FileAccess.Write, FileShare.None))
                {
                    byte[] emptyHeader = new byte[80];
                    fileStream.Write(emptyHeader, 0, emptyHeader.Length);
                }
            }
            catch (Exception e)
            {
                logger.Warn("Correcting the STL " + filename + " failed. This STL may not be " +
                    "readable by ROS or other CAD programs", e);
                return false;
            }
            return true;
        }

        #endregion Export Methods

        // Phase 0 (SW2GZ): depended on URDFPackage.WindowsPackageDirectory.
        // Reimplemented in Phase 4 against the new Ros2Package layout.
        private static void CopyLogFile(object package)
        {
            // intentionally a no-op until Phase 4
        }

        #region STL Preference shuffling

        //Saves the preferences that the user had setup so that I can change them and revert back to their configuration
        private void SaveUserPreferences()
        {
            logger.Info("Saving users preferences");
            mBinary = iSwApp.GetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSTLBinaryFormat);
            mTranslateToPositive = iSwApp.GetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSTLDontTranslateToPositive);
            mSTLUnits = iSwApp.GetUserPreferenceIntegerValue((int)swUserPreferenceIntegerValue_e.swExportStlUnits);
            mSTLQuality = iSwApp.GetUserPreferenceIntegerValue((int)swUserPreferenceIntegerValue_e.swSTLQuality);
            mshowInfo = iSwApp.GetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSTLShowInfoOnSave);
            mSTLPreview = iSwApp.GetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSTLPreview);
            mHideTransitionSpeed = iSwApp.GetUserPreferenceDoubleValue((int)swUserPreferenceDoubleValue_e.swViewTransitionHideShowComponent);
            mSaveComponentsIntoOneFile = iSwApp.GetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSTLComponentsIntoOneFile);
        }

        //This is how the STL export preferences need to be to properly export
        private void SetSTLExportPreferences()
        {
            logger.Info("Setting STL preferences");
            iSwApp.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSTLBinaryFormat, true);
            iSwApp.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSTLDontTranslateToPositive, true);
            iSwApp.SetUserPreferenceIntegerValue((int)swUserPreferenceIntegerValue_e.swExportStlUnits, 2);
            iSwApp.SetUserPreferenceIntegerValue((int)swUserPreferenceIntegerValue_e.swSTLQuality, (int)swSTLQuality_e.swSTLQuality_Coarse);
            iSwApp.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSTLShowInfoOnSave, false);
            iSwApp.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSTLPreview, false);
            iSwApp.SetUserPreferenceDoubleValue((int)swUserPreferenceDoubleValue_e.swViewTransitionHideShowComponent, 0);
            iSwApp.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSTLComponentsIntoOneFile, true);
        }

        //This resets the user preferences back to what they were.
        private void ResetUserPreferences()
        {
            logger.Info("Returning STL preferences to user preferences");
            iSwApp.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSTLBinaryFormat, mBinary);
            iSwApp.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSTLDontTranslateToPositive, mTranslateToPositive);
            iSwApp.SetUserPreferenceIntegerValue((int)swUserPreferenceIntegerValue_e.swExportStlUnits, mSTLUnits);
            iSwApp.SetUserPreferenceIntegerValue((int)swUserPreferenceIntegerValue_e.swSTLQuality, mSTLQuality);
            iSwApp.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSTLShowInfoOnSave, mshowInfo);
            iSwApp.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSTLPreview, mSTLPreview);
            iSwApp.SetUserPreferenceDoubleValue((int)swUserPreferenceDoubleValue_e.swViewTransitionHideShowComponent, mHideTransitionSpeed);
            iSwApp.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSTLComponentsIntoOneFile, mSaveComponentsIntoOneFile);
        }

        //If the user selected something specific for a particular link, that is handled here.
        private void SetLinkSpecificSTLPreferences(string CoordinateSystemName, bool qualityFine, ModelDoc2 doc)
        {
            doc.Extension.SetUserPreferenceString((int)swUserPreferenceStringValue_e.swFileSaveAsCoordinateSystem,
                (int)swUserPreferenceOption_e.swDetailingNoOptionSpecified, CoordinateSystemName);
            if (qualityFine)
            {
                iSwApp.SetUserPreferenceIntegerValue((int)swUserPreferenceIntegerValue_e.swSTLQuality, (int)swSTLQuality_e.swSTLQuality_Fine);
            }
            else
            {
                iSwApp.SetUserPreferenceIntegerValue((int)swUserPreferenceIntegerValue_e.swSTLQuality, (int)swSTLQuality_e.swSTLQuality_Coarse);
            }
        }

        #endregion STL Preference shuffling
    }
}
