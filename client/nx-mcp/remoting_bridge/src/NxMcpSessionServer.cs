using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Runtime.Remoting;
using System.Runtime.Remoting.Channels;
using System.Runtime.Remoting.Channels.Http;
using System.Runtime.Remoting.Lifetime;
using System.Runtime.Serialization.Formatters;
using System.Threading;
using NXOpen;
using NXOpen.UF;

public class NxMcpSessionServer
{
    private const int Port = 8792;
    private static Session theSession = null;
    private static UFSession theUFSession = null;
    private static HttpChannel channel = null;
    private static bool isUnloaded = false;
    private static bool serviceEnded = false;
    private static bool isStarted = false;
    private static string logPath = null;
    private static readonly object startLock = new object();

    public static void Main(string[] args)
    {
        Start();
    }

    public static void Start()
    {
        lock (startLock)
        {
            string baseDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            logPath = Path.Combine(baseDir, "nx_session_server.log");

            if (isStarted && !isUnloaded)
            {
                Log("NX MCP session server already started");
                return;
            }

            isUnloaded = false;
            serviceEnded = false;

            // Siemens' remoting sample notes that Session objects should be obtained
            // on the NX main thread, before the background remoting thread starts.
            theSession = Session.GetSession();
            theUFSession = UFSession.GetUFSession();

            Thread serverThread = new Thread(new ThreadStart(Run));
            serverThread.Name = "NxMcpSessionServer";
            serverThread.IsBackground = true;
            serverThread.Start();
            isStarted = true;

            Log("NX MCP session server starting on http://127.0.0.1:" + Port);
        }
    }

    public static int StartFromNxAssistant()
    {
        Start();
        return 0;
    }

    public static void Run()
    {
        try
        {
            LifetimeServices.LeaseTime = TimeSpan.FromDays(10000);

            SoapServerFormatterSinkProvider provider = new SoapServerFormatterSinkProvider();
            provider.TypeFilterLevel = TypeFilterLevel.Full;

            IDictionary props = new Hashtable();
            props["port"] = Port;
            props["name"] = "NxMcpSessionChannel";
            props["bindTo"] = "127.0.0.1";

            channel = new HttpChannel(props, null, provider);
            ChannelServices.RegisterChannel(channel, false);

            RemotingServices.Marshal(theSession, "NXOpenSession");
            RemotingServices.Marshal(theUFSession, "UFSession");

            Log("NX MCP session server started");
        }
        catch (Exception ex)
        {
            Log("Session server failed: " + ex);
        }

        while (!isUnloaded)
        {
            Thread.Sleep(1000);
        }

        serviceEnded = true;
        isStarted = false;
        Log("NX MCP session server ended");
    }

    public static int GetUnloadOption(string arg)
    {
        return Convert.ToInt32(Session.LibraryUnloadOption.Explicitly);
    }

    public static void UnloadLibrary(string arg)
    {
        isUnloaded = true;
        while (!serviceEnded)
        {
            Thread.Sleep(100);
        }

        try
        {
            RemotingServices.Disconnect(theSession);
            RemotingServices.Disconnect(theUFSession);
            if (channel != null)
            {
                ChannelServices.UnregisterChannel(channel);
                channel = null;
            }
        }
        catch (Exception ex)
        {
            Log("Unload failed: " + ex);
        }
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
}
