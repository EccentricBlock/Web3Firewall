using Web3Firewall.Shared.Enums;

namespace Web3Firewall.Code.Settings;

public class PROXY
{

    public ushort UPSTREAM_TIMEOUT { get; set; } = 30;

    public bool DEFAULT_READ_ONLY { get; set; } = true;

    public string UPSTREAM_URI { get; set; } = string.Empty;

    public string DB_PATH { get; set; } = "RpcRequests.db";

    public ChainProtocol CHAIN_PROTOCOL { get; set; } = ChainProtocol.EVM;
}
