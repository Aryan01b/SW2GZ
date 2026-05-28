/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.
*/
using System;
using System.IO;
using System.Xml.Linq;

namespace SW2GZ.Test.Writers
{
    public abstract class WriterTestBase : IDisposable
    {
        protected string TempDir { get; }

        protected WriterTestBase()
        {
            TempDir = Path.Combine(Path.GetTempPath(), "sw2gz-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(TempDir);
        }

        protected string ReadAllText(string relPath) =>
            File.ReadAllText(Path.Combine(TempDir, relPath));

        protected XDocument LoadXml(string relPath) =>
            XDocument.Load(Path.Combine(TempDir, relPath));

        protected bool Exists(string relPath) =>
            File.Exists(Path.Combine(TempDir, relPath)) ||
            Directory.Exists(Path.Combine(TempDir, relPath));

        public void Dispose()
        {
            try { Directory.Delete(TempDir, recursive: true); }
            catch { /* swallow on Windows file-handle races */ }
        }
    }
}
