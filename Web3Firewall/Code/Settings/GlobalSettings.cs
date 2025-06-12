namespace Web3Firewall.Code.Settings;

public class GlobalSettings
{
    /// <summary>
    /// Sets the proxy to read-only mode, which blocks all write operations to the blockchain.
    /// </summary>
    public bool IsReadOnlyMode { get; set; } = true;
}
