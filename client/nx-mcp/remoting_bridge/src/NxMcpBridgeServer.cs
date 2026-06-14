using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Runtime.Remoting;
using System.Runtime.Remoting.Channels;
using System.Runtime.Remoting.Channels.Http;
using System.Runtime.Remoting.Lifetime;
using System.Runtime.Serialization.Formatters;
using System.Security.Cryptography;
using NXOpen;
using NxMcpBridge;

public class NxMcpBridgeServer : MarshalByRefObject, INxMcpBridgeService
{
    private const int Port = 8791;
    private const string UriName = "NxMcpBridgeService";
    private static readonly object SyncLock = new object();
    private static Session theSession;
    private static HttpChannel channel;
    private static NxMcpBridgeServer service;
    private static string token;
    private static string baseDir;
    private static string tokenPath;
    private static string logPath;

    public static void Main(string[] args)
    {
        Start();
    }

    public static void Start()
    {
        baseDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        tokenPath = Path.Combine(baseDir, "nx_bridge_token.txt");
        logPath = Path.Combine(baseDir, "nx_bridge_server.log");
        token = EnsureToken();
        theSession = Session.GetSession();

        try
        {
            if (channel == null)
            {
                LifetimeServices.LeaseTime = TimeSpan.FromDays(10000);

                SoapServerFormatterSinkProvider provider = new SoapServerFormatterSinkProvider();
                provider.TypeFilterLevel = TypeFilterLevel.Full;

                IDictionary props = new Hashtable();
                props["port"] = Port;
                props["name"] = "NxMcpBridgeChannel";
                props["bindTo"] = "127.0.0.1";

                channel = new HttpChannel(props, null, provider);
                ChannelServices.RegisterChannel(channel, false);
            }

            if (service == null)
            {
                service = new NxMcpBridgeServer();
                RemotingServices.Marshal(service, UriName, typeof(INxMcpBridgeService));
            }

            Log("NX MCP bridge server started on http://127.0.0.1:" + Port + "/" + UriName);
            Log("Token path: " + tokenPath);
        }
        catch (Exception ex)
        {
            Log("Start failed: " + ex);
            throw;
        }
    }

    public string Status(string requestToken)
    {
        ValidateToken(requestToken);
        Part workPart = theSession.Parts.Work;
        Part displayPart = theSession.Parts.Display;
        return "{"
            + "\"ok\":true,"
            + "\"bridge\":\"nx-remoting-bridge\","
            + "\"port\":" + Port + ","
            + "\"has_work_part\":" + BoolJson(workPart != null) + ","
            + "\"work_part_name\":\"" + JsonEscape(workPart == null ? "" : workPart.Name) + "\","
            + "\"work_part_full_path\":\"" + JsonEscape(workPart == null ? "" : workPart.FullPath) + "\","
            + "\"display_part_name\":\"" + JsonEscape(displayPart == null ? "" : displayPart.Name) + "\""
            + "}";
    }

    public string CreateBasicSketch(string requestToken, string sketchName, double widthMm, double heightMm)
    {
        ValidateToken(requestToken);
        lock (SyncLock)
        {
            Part workPart = theSession.Parts.Work;
            if (workPart == null)
            {
                return ErrorJson("No work part is currently loaded. Create or open a part first.");
            }

            string safeName = string.IsNullOrWhiteSpace(sketchName) ? "MCP Remoting Sketch" : sketchName;
            if (safeName.Length > 60)
            {
                safeName = safeName.Substring(0, 60);
            }

            widthMm = Math.Max(widthMm, 1.0);
            heightMm = Math.Max(heightMm, 1.0);
            double halfW = widthMm / 2.0;
            double halfH = heightMm / 2.0;

            try
            {
                Session.UndoMarkId markId = theSession.SetUndoMark(
                    Session.MarkVisibility.Visible,
                    "MCP remoting create basic sketch"
                );

                Point3d origin = new Point3d(0.0, 0.0, 0.0);
                Vector3d normal = new Vector3d(0.0, 0.0, 1.0);
                Plane plane = workPart.Planes.CreatePlane(origin, normal, SmartObject.UpdateOption.WithinModeling);

                SketchInPlaceBuilder sketchBuilder = workPart.Sketches.CreateSketchInPlaceBuilder2(null);
                sketchBuilder.PlaneReference = plane;
                try
                {
                    sketchBuilder.PlaneOption = Sketch.PlaneOption.ExistingPlane;
                }
                catch
                {
                    // Older or customized NX environments may not require this.
                }

                NXObject nxObject = sketchBuilder.Commit();
                sketchBuilder.Destroy();

                Sketch sketch = (Sketch)nxObject;
                try
                {
                    sketch.SetName(safeName);
                }
                catch
                {
                }

                sketch.Activate(Sketch.ViewReorient.False);

                Point3d p1 = new Point3d(-halfW, -halfH, 0.0);
                Point3d p2 = new Point3d(halfW, -halfH, 0.0);
                Point3d p3 = new Point3d(halfW, halfH, 0.0);
                Point3d p4 = new Point3d(-halfW, halfH, 0.0);

                Line line1 = workPart.Curves.CreateLine(p1, p2);
                Line line2 = workPart.Curves.CreateLine(p2, p3);
                Line line3 = workPart.Curves.CreateLine(p3, p4);
                Line line4 = workPart.Curves.CreateLine(p4, p1);

                theSession.ActiveSketch.AddGeometry(line1);
                theSession.ActiveSketch.AddGeometry(line2);
                theSession.ActiveSketch.AddGeometry(line3);
                theSession.ActiveSketch.AddGeometry(line4);

                sketch.Update();
                theSession.ActiveSketch.Deactivate(Sketch.ViewReorient.False, Sketch.UpdateLevel.Model);
                theSession.UpdateManager.DoUpdate(markId);

                Log("Created sketch " + safeName + " " + widthMm + "x" + heightMm);

                return "{"
                    + "\"ok\":true,"
                    + "\"created\":true,"
                    + "\"sketch_name\":\"" + JsonEscape(safeName) + "\","
                    + "\"width_mm\":" + widthMm.ToString(System.Globalization.CultureInfo.InvariantCulture) + ","
                    + "\"height_mm\":" + heightMm.ToString(System.Globalization.CultureInfo.InvariantCulture) + ","
                    + "\"work_part_name\":\"" + JsonEscape(workPart.Name) + "\""
                    + "}";
            }
            catch (Exception ex)
            {
                Log("CreateBasicSketch failed: " + ex);
                return ErrorJson(ex.Message);
            }
        }
    }

    public string Stop(string requestToken)
    {
        ValidateToken(requestToken);
        try
        {
            if (service != null)
            {
                RemotingServices.Disconnect(service);
                service = null;
            }
            if (channel != null)
            {
                ChannelServices.UnregisterChannel(channel);
                channel = null;
            }
            Log("Bridge stopped");
            return "{\"ok\":true,\"stopped\":true}";
        }
        catch (Exception ex)
        {
            return ErrorJson(ex.Message);
        }
    }

    public override object InitializeLifetimeService()
    {
        return null;
    }

    public static int GetUnloadOption(string arg)
    {
        return Convert.ToInt32(Session.LibraryUnloadOption.Explicitly);
    }

    public static void UnloadLibrary(string arg)
    {
        try
        {
            if (service != null)
            {
                RemotingServices.Disconnect(service);
                service = null;
            }
            if (channel != null)
            {
                ChannelServices.UnregisterChannel(channel);
                channel = null;
            }
            Log("Bridge unloaded");
        }
        catch (Exception ex)
        {
            Log("Unload failed: " + ex);
        }
    }

    private static void ValidateToken(string requestToken)
    {
        if (string.IsNullOrEmpty(token))
        {
            token = EnsureToken();
        }
        if (!string.Equals(token, requestToken, StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException("Invalid bridge token");
        }
    }

    private static string EnsureToken()
    {
        Directory.CreateDirectory(baseDir);
        if (File.Exists(tokenPath))
        {
            string existing = File.ReadAllText(tokenPath).Trim();
            if (!string.IsNullOrEmpty(existing))
            {
                return existing;
            }
        }

        byte[] bytes = new byte[32];
        using (RandomNumberGenerator rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(bytes);
        }
        string generated = Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        File.WriteAllText(tokenPath, generated);
        return generated;
    }

    private static void Log(string message)
    {
        try
        {
            File.AppendAllText(
                logPath,
                DateTime.Now.ToString("s") + " " + message + Environment.NewLine
            );
        }
        catch
        {
        }
    }

    private static string ErrorJson(string message)
    {
        return "{\"ok\":false,\"error\":\"" + JsonEscape(message) + "\"}";
    }

    private static string BoolJson(bool value)
    {
        return value ? "true" : "false";
    }

    private static string JsonEscape(string value)
    {
        if (value == null)
        {
            return "";
        }
        return value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\r", "\\r")
            .Replace("\n", "\\n");
    }
}
