using System.Text.Json;
using Web3Firewall.Shared.Models;

namespace Web3Firewall.Shared.Utils.JsonRPC;

public static class RPCUtils
{
    /// <summary>
    /// Writes any object value (primitive, dictionary, list, etc.) as JSON.
    /// </summary>
    public static void WriteJsonValue(Utf8JsonWriter writer, object val)
    {
        if (val is null)
        {
            writer.WriteNullValue();
            return;
        }

        switch (val)
        {
            case string s:
                writer.WriteStringValue(s);
                return;
            case bool b:
                writer.WriteBooleanValue(b);
                return;
            case long l:
                writer.WriteNumberValue(l);
                return;
            case int i:
                writer.WriteNumberValue(i);
                return;
            case double d:
                writer.WriteNumberValue(d);
                return;
            case float f:
                writer.WriteNumberValue(f);
                return;
            case decimal dec:
                writer.WriteNumberValue(dec);
                return;
            case Dictionary<string, object> dict:
                writer.WriteStartObject();
                foreach (var kv in dict)
                {
                    writer.WritePropertyName(kv.Key);
                    WriteJsonValue(writer, kv.Value);
                }
                writer.WriteEndObject();
                return;
            case List<object> list:
                writer.WriteStartArray();
                foreach (object item in list)
                {
                    WriteJsonValue(writer, item);
                }
                writer.WriteEndArray();
                return;
            default:
                // Fallback for unrecognized or user-defined types
                writer.WriteStringValue(val.ToString());
                return;
        }
    }

    /// <summary>
    /// Parses the upstream JSON-RPC response into a list of RPCResponse.
    /// If upstreamResponse is null or has no content, returns an empty list.
    /// Then appends "blocked" error responses for each item in blockedRequests.
    /// </summary>
    public static async Task<List<RPCResponse>> ConvertUpstreamResponseAsync(HttpResponseMessage? upstreamResponse)
    {
        var responses = new List<RPCResponse>();

        // 1) Parse upstream response (if any).
        if (upstreamResponse is not null && upstreamResponse.Content is not null)
        {
            string json = await upstreamResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(json))
            {
                using var doc = JsonDocument.Parse(json);
                // Single or batch response?
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var element in doc.RootElement.EnumerateArray())
                    {
                        responses.Add(ParseJsonRpcResponse(element));
                    }
                }
                else if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    responses.Add(ParseJsonRpcResponse(doc.RootElement));
                }
            }
        }

        // TODO: Add a blocked error response for each blocked request.
        //foreach (var blockedRequest in blockedRequests)
        //{
        //    ulong blockedId = (blockedRequest.Kind == RpcRequestKind.Value)
        //        ? blockedRequest.Value.id
        //        : blockedRequest.KeyValue.id;

        //    // Example: using "ServerError" (-32000) to indicate it was blocked
        //    // Adjust your code/message as needed.
        //    var error = new RPCError(RPCErrorCode.RPC_FORBIDDEN_BY_SAFE_MODE, "Request blocked.");
        //    responses.Add(new RPCResponse(
        //        blockedId,
        //        result: null,
        //        error: error
        //    ));
        //}

        return responses;
    }

    /// <summary>
    /// Helper to parse a single JSON-RPC response element into an RPCResponse struct.
    /// </summary>
    private static RPCResponse ParseJsonRpcResponse(JsonElement element)
    {
        // Default/empty
        string id = string.Empty;
        string? result = null;
        var error = new RPCError(default, null);  // code=0, message=null


        element.TryGetProperty("id", out JsonElement idProp);

        switch (idProp.ValueKind)
        {
            case JsonValueKind.String:
                id = idProp.GetString()!;
                break;
            case JsonValueKind.Number:
                // Convert to string
                id = idProp.ToString();
                break;
            default:
                throw new InvalidDataException("Invalid 'id' field.");
        }


        // "result" or "error"?
        // If there's a "result" property, parse it. Otherwise, look for "error".
        if (element.TryGetProperty("result", out JsonElement resultProp))
        {
            // For simplicity, store either the string or the raw JSON. 
            // This depends on your usage. If you only expect a string, do `resultProp.GetString()`.
            if (resultProp.ValueKind == JsonValueKind.String)
            {
                result = resultProp.GetString();
            }
            else
            {
                // Optionally store raw text for more complex results.
                result = resultProp.GetRawText();
            }
        }
        else if (element.TryGetProperty("error", out JsonElement errorProp)
                 && errorProp.ValueKind == JsonValueKind.Object)
        {
            RPCErrorCode code = default;
            string? message = null;

            // code
            if (errorProp.TryGetProperty("code", out JsonElement codeElem)
                && codeElem.ValueKind == JsonValueKind.Number)
            {
                int codeInt = codeElem.GetInt32();
                code = (RPCErrorCode)codeInt;
            }

            // message
            if (errorProp.TryGetProperty("message", out JsonElement messageElem)
                && messageElem.ValueKind == JsonValueKind.String)
            {
                message = messageElem.GetString();
            }

            error = new RPCError(code, message);
        }

        return new RPCResponse(
            id,
            result,
            error
        )
        {
            jsonrpc = "2.0"
        };
    }
}
