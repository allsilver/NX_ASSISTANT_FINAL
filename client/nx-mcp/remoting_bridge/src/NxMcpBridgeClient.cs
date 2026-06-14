using System;
using System.IO;
using System.Reflection;
using System.Runtime.Remoting;
using System.Runtime.Remoting.Channels;
using System.Runtime.Remoting.Channels.Http;
using NxMcpBridge;

public class NxMcpBridgeClient
{
    private const int Port = 8791;
    private const string Uri = "NxMcpBridgeService";

    public static int Main(string[] args)
    {
        try
        {
            AppDomain.CurrentDomain.AssemblyResolve += ResolveNxAssembly;
            string command = args.Length > 0 ? args[0] : "status";
            string baseDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            string tokenPath = Path.Combine(baseDir, "nx_bridge_token.txt");
            if (!File.Exists(tokenPath))
            {
                Console.WriteLine("{\"ok\":false,\"error\":\"Token file not found. Load NxMcpBridgeServer.dll in NX first.\"}");
                return 2;
            }
            string token = File.ReadAllText(tokenPath).Trim();

            try
            {
                ChannelServices.RegisterChannel(new HttpChannel(), false);
            }
            catch
            {
                // Channel may already be registered if embedded by another host.
            }

            INxMcpBridgeService service = (INxMcpBridgeService)Activator.GetObject(
                typeof(INxMcpBridgeService),
                "http://127.0.0.1:" + Port + "/" + Uri
            );

            if (command == "status")
            {
                Console.WriteLine(service.Status(token));
                return 0;
            }

            if (command == "sketch")
            {
                string sketchName = args.Length > 1 ? args[1] : "MCP Remoting Sketch";
                double width = args.Length > 2 ? Double.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture) : 50.0;
                double height = args.Length > 3 ? Double.Parse(args[3], System.Globalization.CultureInfo.InvariantCulture) : width;
                Console.WriteLine(service.CreateBasicSketch(token, sketchName, width, height));
                return 0;
            }

            if (command == "stop")
            {
                Console.WriteLine(service.Stop(token));
                return 0;
            }

            Console.WriteLine("{\"ok\":false,\"error\":\"Usage: NxMcpBridgeClient.exe [status|sketch|stop] [name] [width] [height]\"}");
            return 2;
        }
        catch (Exception ex)
        {
            Console.WriteLine("{\"ok\":false,\"error\":\"" + JsonEscape(ex.Message) + "\"}");
            return 1;
        }
    }

    private static Assembly ResolveNxAssembly(object sender, ResolveEventArgs args)
    {
        string simpleName = new AssemblyName(args.Name).Name + ".dll";
        string[] roots = new string[]
        {
            @"C:\SCAD\NX2406\NXBIN\managed",
            @"C:\SCAD\NX2406\NXBIN\managed_core"
        };

        foreach (string root in roots)
        {
            string candidate = Path.Combine(root, simpleName);
            if (File.Exists(candidate))
            {
                return Assembly.LoadFrom(candidate);
            }
        }

        return null;
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
