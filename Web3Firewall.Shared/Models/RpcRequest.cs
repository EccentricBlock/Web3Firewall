using System.Text;
using System.Text.Json;
using Web3Firewall.Shared.Utils.JsonRPC;

namespace Web3Firewall.Shared.Models;

public enum RpcRequestKind
{
    Value,
    KeyValue
}

public readonly struct RpcRequest
{
    public RpcRequestKind Kind { get; }

    public RPCRequestValue Value { get; }
    public RPCRequestKeyValue KeyValue { get; }

    public string Id => Kind == RpcRequestKind.Value ? Value.id : KeyValue.id;
    public string Method => Kind == RpcRequestKind.Value ? Value.method : KeyValue.method;
    public string Json => Kind == RpcRequestKind.Value ? Value.ToJson() : KeyValue.ToJson();

    public List<object> ValueParams => Value.@params ?? [];
    public List<Dictionary<string, object>> KeyValueParams => KeyValue.@params ?? [];

    public RpcRequest(RPCRequestValue value) : this()
    {
        Kind = RpcRequestKind.Value;
        Value = value;
        KeyValue = default;
    }

    public RpcRequest(RPCRequestKeyValue keyValue) : this()
    {
        Kind = RpcRequestKind.KeyValue;
        Value = default;
        KeyValue = keyValue;
    }
}


/// <summary>
/// Represents a JSON-RPC request where 'params' is an array of primitive values.
/// </summary>
public struct RPCRequestValue
{
    public RPCRequestValue(string id, string method, List<object> @params)
    {
        this.id = id;
        this.method = method;
        this.@params = @params;
        this.jsonrpc = "2.0";
    }

    public string id { get; }
    public string method { get; }
    public List<object> @params { get; }
    public string jsonrpc { get; set; }

    /// <summary>
    /// Generates the valid JSON-RPC request object as a JSON string.
    /// </summary>
    public string ToJson()
    {
        using MemoryStream ms = new MemoryStream();
        using (Utf8JsonWriter writer = new Utf8JsonWriter(ms))
        {
            writer.WriteStartObject();

            writer.WriteString("jsonrpc", jsonrpc);
            writer.WriteString("id", id);
            writer.WriteString("method", method);

            writer.WritePropertyName("params");
            writer.WriteStartArray();
            foreach (object param in @params)
            {
                RPCUtils.WriteJsonValue(writer, param);
            }
            writer.WriteEndArray();

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(ms.ToArray());
    }
}

/// <summary>
/// Represents a JSON-RPC request where 'params' is an array of object dictionaries.
/// </summary>
public struct RPCRequestKeyValue
{
    public RPCRequestKeyValue(string id, string method, List<Dictionary<string, object>> @params)
    {
        this.id = id;
        this.method = method;
        this.@params = @params;
        this.jsonrpc = "2.0";
    }

    public string id { get; }
    public string method { get; }
    public List<Dictionary<string, object>> @params { get; }
    public string jsonrpc { get; set; }

    /// <summary>
    /// Generates the valid JSON-RPC request object as a JSON string.
    /// </summary>
    public string ToJson()
    {
        using var ms = new MemoryStream();
        using (var writer = new Utf8JsonWriter(ms))
        {
            writer.WriteStartObject();

            writer.WriteString("jsonrpc", jsonrpc);
            writer.WriteString("id", id);
            writer.WriteString("method", method);

            writer.WritePropertyName("params");
            writer.WriteStartArray();
            foreach (Dictionary<string, object> dict in @params)
            {
                writer.WriteStartObject();
                foreach (var kv in dict)
                {
                    writer.WritePropertyName(kv.Key);
                    RPCUtils.WriteJsonValue(writer, kv.Value);
                }
                writer.WriteEndObject();
            }
            writer.WriteEndArray();

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(ms.ToArray());
    }
}

