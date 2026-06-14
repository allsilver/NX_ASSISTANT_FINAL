namespace NxMcpBridge
{
    public interface INxMcpBridgeService
    {
        string Status(string token);
        string CreateBasicSketch(string token, string sketchName, double widthMm, double heightMm);
        string Stop(string token);
    }
}
