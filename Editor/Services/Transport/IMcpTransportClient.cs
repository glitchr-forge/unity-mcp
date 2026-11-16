using System.Threading.Tasks;

namespace ForgeMCP.Editor.Services.Transport
{
    /// <summary>
    /// Abstraction for MCP transport implementations (e.g. WebSocket push, stdio).
    /// </summary>
    public interface IMcpTransportClient
    {
        bool IsConnected { get; }
        string TransportName { get; }
        TransportState State { get; }

        Task<bool> StartAsync();
        Task StopAsync();
        Task<bool> VerifyAsync();
        Task ReregisterToolsAsync();
    }

    /// <summary>
    /// A transport that, its connection lost, keeps trying to get it back (the WebSocket bridge): while it does, it is
    /// not connected but still wanted, so a domain reload must resume it as it resumes a connected one.
    /// </summary>
    public interface IReconnectingTransport
    {
        bool IsReconnecting { get; }
    }
}
