/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

Pure (COM-free) serialization of Sw2gzDoc to/from a UTF-8 XML string using
DataContractSerializer. Mirrors Sw2gzConfigCodec for the legacy config tree;
the COM layer that stores this string in a SolidWorks Attribute lives in
Sw2gzDocSerialization.

DataContractSerializer in POCO mode walks public properties — no DataContract
attributes required on Sw2gzDoc / Sw2gzRobotConfig / Sw2gzWorldConfig /
Sw2gzAssetConfig. LinkDef + JointDef already carry [DataContract] so they
serialize with their explicit member set.
*/
using System.IO;
using System.Runtime.Serialization;
using System.Text;

namespace SW2GZ.URDFExport
{
    public static class Sw2gzDocCodec
    {
        private static readonly DataContractSerializer Serializer =
            new DataContractSerializer(typeof(Sw2gzDoc));

        /// Serialize a doc to a UTF-8 XML string. Never returns null.
        public static string ToXmlString(Sw2gzDoc doc)
        {
            using (var stream = new MemoryStream())
            {
                Serializer.WriteObject(stream, doc);
                stream.Flush();
                return Encoding.UTF8.GetString(stream.GetBuffer(), 0, (int)stream.Position);
            }
        }

        /// Deserialize a doc from an XML string. Returns null for empty/blank
        /// input (no checkpoint saved yet).
        public static Sw2gzDoc FromXmlString(string data)
        {
            if (string.IsNullOrWhiteSpace(data)) return null;
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(data)))
            {
                return (Sw2gzDoc)Serializer.ReadObject(stream);
            }
        }
    }
}
