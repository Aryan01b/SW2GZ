/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

Localhost-only HTTP server hosting the browser-side three.js preview.
Bound to 127.0.0.1:<random port>, lifetime tied to PreviewDialog.

Routes:
  GET /                  → preview index.html (bin/Release/preview/index.html)
  GET /index.html        → same
  GET /robot.urdf        → workspace URDF, xacro:include stripped server-side
  GET /meshes/<name>     → DAE / STL from the workspace meshes dir
  GET /joint_states      → JSON {jointName: value_rad_or_m} (sampled live)
  *                      → 404

Keep-alive disabled to simplify shutdown. Each request handled on a
thread-pool worker. Stop() cancels accept loop + closes listener.
*/
#if SW_INTEROP
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SW2GZ.URDFExport
{
    public sealed class PreviewServer : IDisposable
    {
        private readonly string _assetsDir;       // bin/Release/preview/
        private readonly string _meshesDir;       // <workspace>/src/<pkg>/meshes
        private readonly string _urdfText;        // pre-flattened URDF body
        private readonly Func<IReadOnlyDictionary<string, double>> _jointSampler;
        private HttpListener _listener;
        private CancellationTokenSource _cts;
        public int Port { get; private set; }
        public string Url => "http://127.0.0.1:" + Port + "/";

        public PreviewServer(
            string previewAssetsDir,
            string meshesDir,
            string urdfText,
            Func<IReadOnlyDictionary<string, double>> jointSampler)
        {
            _assetsDir = previewAssetsDir ?? throw new ArgumentNullException(nameof(previewAssetsDir));
            _meshesDir = meshesDir       ?? throw new ArgumentNullException(nameof(meshesDir));
            _urdfText  = urdfText        ?? throw new ArgumentNullException(nameof(urdfText));
            _jointSampler = jointSampler ?? (() => new Dictionary<string, double>());
        }

        public void Start()
        {
            Port = PickFreePort();
            _listener = new HttpListener();
            _listener.Prefixes.Add("http://127.0.0.1:" + Port + "/");
            _listener.Start();
            _cts = new CancellationTokenSource();
            Task.Run(() => AcceptLoop(_cts.Token));
        }

        private async Task AcceptLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                HttpListenerContext ctx;
                try
                {
                    ctx = await _listener.GetContextAsync();
                }
                catch
                {
                    // Listener closed → exit cleanly.
                    return;
                }
                _ = Task.Run(() => HandleRequest(ctx));
            }
        }

        private void HandleRequest(HttpListenerContext ctx)
        {
            try
            {
                string path = (ctx.Request.Url.AbsolutePath ?? "/").TrimStart('/');
                if (string.IsNullOrEmpty(path) || path.Equals("index.html", StringComparison.OrdinalIgnoreCase))
                {
                    ServeFile(ctx, Path.Combine(_assetsDir, "index.html"), "text/html; charset=utf-8");
                    return;
                }
                if (path.Equals("robot.urdf", StringComparison.OrdinalIgnoreCase))
                {
                    ServeText(ctx, _urdfText, "application/xml; charset=utf-8");
                    return;
                }
                if (path.Equals("joint_states", StringComparison.OrdinalIgnoreCase))
                {
                    string json = JsonifyJointStates(_jointSampler());
                    ServeText(ctx, json, "application/json; charset=utf-8");
                    return;
                }
                if (path.StartsWith("meshes/", StringComparison.OrdinalIgnoreCase))
                {
                    // Hard-clamp to filename only — no traversal outside meshesDir.
                    string filename = Path.GetFileName(path.Substring("meshes/".Length));
                    string full = Path.Combine(_meshesDir, filename);
                    if (File.Exists(full))
                        ServeFile(ctx, full, MimeFor(full));
                    else
                        Respond(ctx, 404, "Mesh not found: " + filename);
                    return;
                }
                if (path.StartsWith("vendor/", StringComparison.OrdinalIgnoreCase))
                {
                    // Vendored three.js + urdf-loader shipped under
                    // <assetsDir>\vendor\ so the preview runs fully offline.
                    // Reject any segment containing ".." (path traversal) by
                    // resolving the combined path and verifying it stays
                    // inside the assets dir.
                    string relUnix = path; // e.g. "vendor/three/build/three.module.js"
                    string rel = relUnix.Replace('/', Path.DirectorySeparatorChar);
                    string full = Path.GetFullPath(Path.Combine(_assetsDir, rel));
                    string assetsFull = Path.GetFullPath(_assetsDir);
                    if (!full.StartsWith(assetsFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    {
                        Respond(ctx, 403, "Forbidden");
                        return;
                    }
                    if (File.Exists(full))
                        ServeFile(ctx, full, MimeFor(full));
                    else
                        Respond(ctx, 404, "Asset not found: " + path);
                    return;
                }
                Respond(ctx, 404, "Not Found");
            }
            catch (Exception e)
            {
                try { Respond(ctx, 500, e.Message); } catch { /* swallow */ }
            }
            finally
            {
                try { ctx.Response.OutputStream.Close(); } catch { }
            }
        }

        private static void ServeText(HttpListenerContext ctx, string body, string mime)
        {
            byte[] data = Encoding.UTF8.GetBytes(body);
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = mime;
            ctx.Response.ContentLength64 = data.Length;
            ctx.Response.OutputStream.Write(data, 0, data.Length);
        }

        private static void ServeFile(HttpListenerContext ctx, string path, string mime)
        {
            if (!File.Exists(path))
            {
                Respond(ctx, 404, "File not found: " + Path.GetFileName(path));
                return;
            }
            byte[] data = File.ReadAllBytes(path);
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = mime;
            ctx.Response.ContentLength64 = data.Length;
            ctx.Response.OutputStream.Write(data, 0, data.Length);
        }

        private static void Respond(HttpListenerContext ctx, int status, string message)
        {
            byte[] data = Encoding.UTF8.GetBytes(message);
            ctx.Response.StatusCode = status;
            ctx.Response.ContentType = "text/plain; charset=utf-8";
            ctx.Response.ContentLength64 = data.Length;
            ctx.Response.OutputStream.Write(data, 0, data.Length);
        }

        private static string MimeFor(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            switch (ext)
            {
                case ".dae":  return "model/vnd.collada+xml";
                case ".stl":  return "application/sla";
                case ".html": return "text/html; charset=utf-8";
                case ".js":
                case ".mjs":  return "text/javascript; charset=utf-8";
                case ".css":  return "text/css; charset=utf-8";
                case ".json": return "application/json; charset=utf-8";
                case ".map":  return "application/json; charset=utf-8";
                case ".png":  return "image/png";
                case ".jpg":  return "image/jpeg";
                case ".jpeg": return "image/jpeg";
                default:      return "application/octet-stream";
            }
        }

        // Tiny JSON encoder so we don't pull System.Text.Json into net48.
        // Joint values are doubles → use InvariantCulture explicitly.
        private static string JsonifyJointStates(IReadOnlyDictionary<string, double> states)
        {
            var sb = new StringBuilder();
            sb.Append('{');
            bool first = true;
            if (states != null)
            {
                foreach (var kvp in states)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    sb.Append('"').Append(JsonEscape(kvp.Key)).Append("\":");
                    sb.Append(kvp.Value.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture));
                }
            }
            sb.Append('}');
            return sb.ToString();
        }

        private static string JsonEscape(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"':  sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n");  break;
                    case '\r': sb.Append("\\r");  break;
                    case '\t': sb.Append("\\t");  break;
                    default:
                        if (c < ' ') sb.AppendFormat("\\u{0:X4}", (int)c);
                        else sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }

        private static int PickFreePort()
        {
            // OS-assigned ephemeral port: open + immediately release.
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            int port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            return port;
        }

        public void Stop()
        {
            try { _cts?.Cancel(); } catch { }
            try { _listener?.Stop(); } catch { }
            try { _listener?.Close(); } catch { }
        }

        public void Dispose() => Stop();
    }
}
#endif
