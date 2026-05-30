using SNMP.Core.Models;

namespace SNMP.Core.Interfaces;

public interface ITrapReceiver : IDisposable
{
    event EventHandler<TrapEntry> TrapReceived;
    bool IsListening { get; }
    void Start(int port = 162);
    void Stop();
}
