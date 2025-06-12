using Microsoft.Extensions.Logging;
using Web3Firewall.Shared.Enums;

namespace Web3Firewall.Shared.Services;

public class RPCFilterService(ILogger<RPCFilterService> logger)
{
    private readonly ILogger<RPCFilterService> _logger = logger;

    // Define methods that MODIFY the blockchain state (write methods)
    public readonly HashSet<string> EVMWriteMethods = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                                        {
                                            "eth_sendTransaction",
                                            "eth_sendRawTransaction",
                                        //    "eth_newFilter",          // Creates state on the node
                                        //    "eth_newBlockFilter",     // Creates state on the node
                                        //    "eth_newPendingTransactionFilter", // Creates state on the node
                                        };

    public readonly HashSet<string> SolanaWriteMethods = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                                        {
                                            "requestAirdrop",
                                            "sendTransaction",
                                        //    "eth_newFilter",          // Creates state on the node
                                        //    "eth_newBlockFilter",     // Creates state on the node
                                        //    "eth_newPendingTransactionFilter", // Creates state on the node
                                        };

    public bool IsBlocked(string method, ChainProtocol protocol, bool isReadOnlyMode)
    {
        switch (protocol)
        {
            case ChainProtocol.EVM:
                return EVMWriteMethods.Contains(method, StringComparer.OrdinalIgnoreCase) && isReadOnlyMode;
            case ChainProtocol.Solana:
                return SolanaWriteMethods.Contains(method, StringComparer.OrdinalIgnoreCase) && isReadOnlyMode;
            default:
                return true;
        }//switch (protocol)

    }//public bool IsBlocked(string method, ChainProtocol protocol, bool isReadOnlyMode)
}//class RPCFilterService
