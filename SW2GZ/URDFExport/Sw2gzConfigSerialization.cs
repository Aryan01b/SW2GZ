/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

P8 — stores/loads the wizard checkpoint (Sw2gzExportConfig) as a SolidWorks
Attribute feature in the top-level assembly document tree. This is the "instance
in tree": a named Attribute carrying the DataContract-serialized config string,
so reopening the assembly and clicking the SW2GZ button resumes the wizard.

Mirrors the attribute plumbing of ConfigurationSerialization (the legacy URDF
link-tree store) but writes a NEW, dedicated attribute. The legacy
"URDF Export Configuration (v1.4)" attribute is intentionally left untouched
here — not read, not deleted (full migration is a later increment).
*/
using System;

#if SW_INTEROP
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SW2GZ.Utilities;

namespace SW2GZ.URDFExport
{
    public static class Sw2gzConfigSerialization
    {
        private static readonly log4net.ILog logger = Logger.GetLogger();

        /// Serialization version stored alongside the data for forward-compat.
        private const double SerializationVersion = 1.0;

        /// Attribute feature name shown in the document tree.
        public const string Sw2gzConfigAttributeName = "SW2GZ Export Configuration (v1)";

        /// Save the wizard checkpoint into the model's Attribute (create or update).
        public static void Save(SldWorks swApp, ModelDoc2 model, Sw2gzExportConfig config)
        {
            if (swApp == null) throw new ArgumentNullException(nameof(swApp));
            if (model == null) throw new ArgumentNullException(nameof(model));
            if (config == null) throw new ArgumentNullException(nameof(config));

            string data = Sw2gzConfigCodec.ToXmlString(config);
            SaveDataToModelDoc(swApp, model, data);
            logger.Info("Saved SW2GZ wizard checkpoint to the assembly document.");
        }

        /// Load the wizard checkpoint from the model. Returns a fresh default
        /// config (Mode=RobotPackage, blank fields, LastStep=0) when none exists.
        public static Sw2gzExportConfig Load(ModelDoc2 model)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));

            string data = GetConfigData(model);
            Sw2gzExportConfig config = Sw2gzConfigCodec.FromXmlString(data);
            return config ?? new Sw2gzExportConfig();
        }

        // ───────────────────────────── private ───────────────────────────────

        private static string GetConfigData(ModelDoc2 model)
        {
            SolidWorks.Interop.sldworks.Attribute swAtt =
                FindSWSaveAttribute(model, Sw2gzConfigAttributeName);
            if (swAtt == null)
            {
                return "";
            }

            Parameter param = swAtt.GetParameter("data");
            return param.GetStringValue();
        }

        private static Feature GetFeatureAttributeByName(ModelDoc2 model, string featName)
        {
            object[] objects = (object[])model.FeatureManager.GetFeatures(true);
            if (objects == null)
            {
                return null;
            }
            foreach (object obj in objects)
            {
                Feature feature = (Feature)obj;
                if (feature.GetTypeName2() == "Attribute")
                {
                    SolidWorks.Interop.sldworks.Attribute att =
                        (SolidWorks.Interop.sldworks.Attribute)feature.GetSpecificFeature2();
                    if (att.GetName() == featName)
                    {
                        return feature;
                    }
                }
            }
            return null;
        }

        private static SolidWorks.Interop.sldworks.Attribute
            FindSWSaveAttribute(ModelDoc2 model, string name)
        {
            Feature feature = GetFeatureAttributeByName(model, name);
            if (feature == null)
            {
                return null;
            }
            return (SolidWorks.Interop.sldworks.Attribute)feature.GetSpecificFeature2();
        }

        private static SolidWorks.Interop.sldworks.Attribute
            CreateSWSaveAttribute(SldWorks swApp, ModelDoc2 model)
        {
            SolidWorks.Interop.sldworks.Attribute existing =
                FindSWSaveAttribute(model, Sw2gzConfigAttributeName);
            if (existing != null)
            {
                return existing;
            }

            int options = 0;
            int configurationOptions = (int)swInConfigurationOpts_e.swAllConfiguration;

            AttributeDef def = swApp.DefineAttribute(Sw2gzConfigAttributeName);
            def.AddParameter("data", (int)swParamType_e.swParamTypeString, 0, options);
            def.AddParameter("date", (int)swParamType_e.swParamTypeString, 0, options);
            def.AddParameter("version", (int)swParamType_e.swParamTypeDouble,
                SerializationVersion, options);
            def.Register();

            return def.CreateInstance5(
                model, null, Sw2gzConfigAttributeName, options, configurationOptions);
        }

        private static void SaveDataToModelDoc(SldWorks swApp, ModelDoc2 model, string data)
        {
            int configurationOptions = (int)swInConfigurationOpts_e.swAllConfiguration;
            SolidWorks.Interop.sldworks.Attribute att =
                CreateSWSaveAttribute(swApp, model);

            Parameter param = att.GetParameter("data");
            param.SetStringValue2(data, configurationOptions, "");
            param = att.GetParameter("date");
            param.SetStringValue2(DateTime.Now.ToString(), configurationOptions, "");
            param = att.GetParameter("version");
            param.SetDoubleValue2(SerializationVersion, configurationOptions, "");
        }
    }
}
#endif
