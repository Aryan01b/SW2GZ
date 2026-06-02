/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

P8 — pure (COM-free) serialization of Sw2gzExportConfig to/from an XML string
using DataContractSerializer, mirroring the scheme in ConfigurationSerialization
but for the wizard checkpoint rather than the URDF link tree. The COM layer that
stores this string in a SolidWorks Attribute lives in Sw2gzConfigSerialization.

UTF-8 (not ASCII) so non-ASCII author names / metadata round-trip intact.
*/
using System.IO;
using System.Runtime.Serialization;
using System.Text;

namespace SW2GZ.URDFExport
{
    public static class Sw2gzConfigCodec
    {
        private static readonly DataContractSerializer Serializer =
            new DataContractSerializer(typeof(Sw2gzExportConfig));

        /// Serialize a config to a UTF-8 XML string. Never returns null.
        public static string ToXmlString(Sw2gzExportConfig config)
        {
            using (var stream = new MemoryStream())
            {
                Serializer.WriteObject(stream, config);
                stream.Flush();
                return Encoding.UTF8.GetString(stream.GetBuffer(), 0, (int)stream.Position);
            }
        }

        /// Deserialize a config from an XML string. Returns null for empty/blank
        /// input (e.g. when no checkpoint has been saved yet).
        public static Sw2gzExportConfig FromXmlString(string data)
        {
            if (string.IsNullOrWhiteSpace(data))
            {
                return null;
            }

            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(data)))
            {
                return (Sw2gzExportConfig)Serializer.ReadObject(stream);
            }
        }
    }
}
