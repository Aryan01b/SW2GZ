/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

Stores / loads the Sw2gzDoc as a SolidWorks Attribute feature on the
top-level assembly. Attribute name = "SW2GZ Doc (v1)" — distinct from the
legacy "SW2GZ Export Configuration (v1)" attribute Sw2gzConfigSerialization
manages.

When this attribute exists the assembly is considered "locked" — mode pills
go grey, and the user must Delete the config from the ribbon before changing
modes. This mirrors the model where the user picks a mode FIRST, runs the
Create wizard for it, and the result becomes the doc's persisted state.
*/
using System;

#if SW_INTEROP
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SW2GZ.Utilities;

namespace SW2GZ.URDFExport
{
    public static class Sw2gzDocSerialization
    {
        private static readonly log4net.ILog logger = Logger.GetLogger();

        private const double SerializationVersion = 1.0;

        public const string Sw2gzDocAttributeName = "SW2GZ Doc (v1)";

        /// True when the assembly has a persisted Sw2gzDoc.
        public static bool HasSaved(ModelDoc2 model)
        {
            if (model == null) return false;
            return FindSWAttribute(model) != null;
        }

        /// Save (create-or-update) the doc into the model's Attribute.
        public static void Save(SldWorks swApp, ModelDoc2 model, Sw2gzDoc doc)
        {
            if (swApp == null) throw new ArgumentNullException(nameof(swApp));
            if (model == null) throw new ArgumentNullException(nameof(model));
            if (doc == null) throw new ArgumentNullException(nameof(doc));

            string data = Sw2gzDocCodec.ToXmlString(doc);
            SaveDataToModelDoc(swApp, model, data);
            logger.Info("Saved SW2GZ doc (v1) to assembly. Mode=" + doc.Mode
                + " RobotLinks=" + (doc.Robot?.Links?.Count ?? 0)
                + " RobotJoints=" + (doc.Robot?.Joints?.Count ?? 0));
        }

        /// Load the doc or null if no attribute exists.
        public static Sw2gzDoc Load(ModelDoc2 model)
        {
            if (model == null) return null;
            string data = GetData(model);
            if (string.IsNullOrWhiteSpace(data)) return null;
            try { return Sw2gzDocCodec.FromXmlString(data); }
            catch (Exception e)
            {
                logger.Warn("Sw2gzDocSerialization.Load: codec threw — treating as no-config", e);
                return null;
            }
        }

        /// Delete the persisted doc attribute. Returns true if an attribute
        /// existed and was deleted; false if nothing to delete.
        public static bool Delete(ModelDoc2 model)
        {
            if (model == null) return false;
            Feature feat = GetAttributeFeature(model);
            if (feat == null) return false;
            try
            {
                feat.Select2(false, 0);
                model.EditDelete();
                logger.Info("Deleted SW2GZ Doc (v1) attribute.");
                return true;
            }
            catch (Exception e)
            {
                logger.Warn("Sw2gzDocSerialization.Delete failed", e);
                return false;
            }
        }

        // ───────────────────────────── private ───────────────────────────────

        private static string GetData(ModelDoc2 model)
        {
            var att = FindSWAttribute(model);
            if (att == null) return "";
            Parameter param = att.GetParameter("data");
            return param.GetStringValue();
        }

        private static Feature GetAttributeFeature(ModelDoc2 model)
        {
            object[] features = (object[])model.FeatureManager.GetFeatures(true);
            if (features == null) return null;
            foreach (object obj in features)
            {
                Feature feat = (Feature)obj;
                if (feat.GetTypeName2() != "Attribute") continue;
                var a = (SolidWorks.Interop.sldworks.Attribute)feat.GetSpecificFeature2();
                if (a.GetName() == Sw2gzDocAttributeName) return feat;
            }
            return null;
        }

        private static SolidWorks.Interop.sldworks.Attribute FindSWAttribute(ModelDoc2 model)
        {
            Feature feat = GetAttributeFeature(model);
            if (feat == null) return null;
            return (SolidWorks.Interop.sldworks.Attribute)feat.GetSpecificFeature2();
        }

        private static SolidWorks.Interop.sldworks.Attribute CreateAttribute(SldWorks swApp, ModelDoc2 model)
        {
            var existing = FindSWAttribute(model);
            if (existing != null) return existing;

            int options = 0;
            int configurationOptions = (int)swInConfigurationOpts_e.swAllConfiguration;

            AttributeDef def = swApp.DefineAttribute(Sw2gzDocAttributeName);
            def.AddParameter("data", (int)swParamType_e.swParamTypeString, 0, options);
            def.AddParameter("date", (int)swParamType_e.swParamTypeString, 0, options);
            def.AddParameter("version", (int)swParamType_e.swParamTypeDouble,
                SerializationVersion, options);
            def.Register();

            return def.CreateInstance5(
                model, null, Sw2gzDocAttributeName, options, configurationOptions);
        }

        private static void SaveDataToModelDoc(SldWorks swApp, ModelDoc2 model, string data)
        {
            int configurationOptions = (int)swInConfigurationOpts_e.swAllConfiguration;
            var att = CreateAttribute(swApp, model);

            Parameter p = att.GetParameter("data");
            p.SetStringValue2(data, configurationOptions, "");
            p = att.GetParameter("date");
            p.SetStringValue2(DateTime.Now.ToString(), configurationOptions, "");
            p = att.GetParameter("version");
            p.SetDoubleValue2(SerializationVersion, configurationOptions, "");
        }
    }
}
#endif
