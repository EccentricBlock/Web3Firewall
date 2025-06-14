﻿
namespace Web3Firewall.Shared.Models;
public struct RPCResponse
{
    // Positional parameters: (ulong id, string? result, RPCError error)
    public RPCResponse(string id, string? result, RPCError? error)
    {
        this.id = id;
        this.result = result;
        this.error = error;
        this.jsonrpc = "2.0";
    }

    public string id { get; }
    public string? result { get; }
    public RPCError? error { get; }
    public string jsonrpc { get; set; }
}

public struct RPCError
{
    public RPCError(RPCErrorCode code, string? message)
    {
        this.code = code;
        this.message = message;
    }
    public RPCErrorCode code { get; }
    public string? message { get; }
}




public enum RPCErrorCode
{
    //! Standard JSON-RPC 2.0 errors
    RPC_INVALID_REQUEST = -32600,
    RPC_METHOD_NOT_FOUND = -32601,
    RPC_INVALID_PARAMS = -32602,
    RPC_INTERNAL_ERROR = -32603,
    RPC_PARSE_ERROR = -32700,

    //! General application defined errors
    RPC_MISC_ERROR = -1, //! std::exception thrown in command handling
    RPC_FORBIDDEN_BY_SAFE_MODE = -2, //! Server is in safe mode, and command is not allowed in safe mode
    RPC_TYPE_ERROR = -3, //! Unexpected type was passed as parameter
    RPC_INVALID_ADDRESS_OR_KEY = -5, //! Invalid address or key
    RPC_OUT_OF_MEMORY = -7, //! Ran out of memory during operation
    RPC_INVALID_PARAMETER = -8, //! Invalid, missing or duplicate parameter
    RPC_DATABASE_ERROR = -20, //! Database error
    RPC_DESERIALIZATION_ERROR = -22, //! Error parsing or validating structure in raw format
    RPC_VERIFY_ERROR = -25, //! General error during transaction or block submission
    RPC_VERIFY_REJECTED = -26, //! Transaction or block was rejected by network rules
    RPC_VERIFY_ALREADY_IN_CHAIN = -27, //! Transaction already in chain
    RPC_IN_WARMUP = -28, //! Client still warming up

    //! Aliases for backward compatibility
    RPC_TRANSACTION_ERROR = RPC_VERIFY_ERROR,
    RPC_TRANSACTION_REJECTED = RPC_VERIFY_REJECTED,
    RPC_TRANSACTION_ALREADY_IN_CHAIN = RPC_VERIFY_ALREADY_IN_CHAIN,

    //! P2P client errors
    RPC_CLIENT_NOT_CONNECTED = -9, //! Bitcoin is not connected
    RPC_CLIENT_IN_INITIAL_DOWNLOAD = -10, //! Still downloading initial blocks
    RPC_CLIENT_NODE_ALREADY_ADDED = -23, //! Node is already added
    RPC_CLIENT_NODE_NOT_ADDED = -24, //! Node has not been added before

    //! Wallet errors
    RPC_WALLET_ERROR = -4, //! Unspecified problem with wallet (key not found etc.)
    RPC_WALLET_INSUFFICIENT_FUNDS = -6, //! Not enough funds in wallet or account
    RPC_WALLET_INVALID_ACCOUNT_NAME = -11, //! Invalid account name
    RPC_WALLET_KEYPOOL_RAN_OUT = -12, //! Keypool ran out, call keypoolrefill first
    RPC_WALLET_UNLOCK_NEEDED = -13, //! Enter the wallet passphrase with walletpassphrase first
    RPC_WALLET_PASSPHRASE_INCORRECT = -14, //! The wallet passphrase entered was incorrect
    RPC_WALLET_WRONG_ENC_STATE = -15, //! Command given in wrong wallet encryption state (encrypting an encrypted wallet etc.)
    RPC_WALLET_ENCRYPTION_FAILED = -16, //! Failed to encrypt the wallet
    RPC_WALLET_ALREADY_UNLOCKED = -17, //! Wallet is already unlocked
}