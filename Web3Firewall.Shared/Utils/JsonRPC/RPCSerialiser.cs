using Microsoft.AspNetCore.Http;
using System.Text;
using System.Text.Json;
using Web3Firewall.Shared.Models;

namespace Web3Firewall.Shared.Utils.JsonRPC;
public static class RPCSerialiser
{
    /// <summary>
    /// Deserialises the HTTP JSON-RPC request payload (single or batch) into a List of RpcRequestUnion.
    /// </summary>
    public static async ValueTask<List<RpcRequest>> DeserializeJsonRpcRequestsAsync(HttpRequest request)
    {
        // Ensure the body is at the start of the stream
        //request.Body.Seek(0, SeekOrigin.Begin);

        // Read the JSON once into memory. For very large requests, consider streaming with JsonDocument.ParseAsync.
        using StreamReader reader = new StreamReader(request.Body, Encoding.UTF8, leaveOpen: true);
        string json = await reader.ReadToEndAsync().ConfigureAwait(false);

        // Parse with JsonDocument
        using JsonDocument doc = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip
        });//using JsonDocument doc = JsonDocument.Parse(json, new JsonDocumentOptions

        List<RpcRequest> results = new List<RpcRequest>();

        // Distinguish single vs batched
        if (doc.RootElement.ValueKind == JsonValueKind.Array)
        {
            // Batched requests
            foreach (JsonElement element in doc.RootElement.EnumerateArray())
            {
                if (element.ValueKind == JsonValueKind.Object)
                {
                    RpcRequest rpcRequest = ParseRequestElement(element);
                    results.Add(rpcRequest);
                }
            }//foreach (JsonElement element in doc.RootElement.EnumerateArray())
        }
        else if (doc.RootElement.ValueKind == JsonValueKind.Object)
        {
            // Single request
            RpcRequest rpcRequest = ParseRequestElement(doc.RootElement);
            results.Add(rpcRequest);
        }
        else
        {
            // Not valid JSON-RPC content
            throw new InvalidDataException("Invalid JSON-RPC payload.");
        }//if (doc.RootElement.ValueKind == JsonValueKind.Array)

        return results;
    }

    /// <summary>
    /// Generates a single JSON array containing a JSON-RPC error response object
    /// for each blocked request
    /// </summary>
    /// <returns>A JSON string suitable for returning in an HTTP response.</returns>
    public static string GenerateErrorResponses(List<RpcRequest> blockedRequests, int code = (int)RPCErrorCode.RPC_FORBIDDEN_BY_SAFE_MODE, string message = "Request blocked.")
    {
        if (blockedRequests is null || blockedRequests.Count == 0)
            return "[]"; // no blocked requests => return empty array

        using var ms = new MemoryStream();
        using (var writer = new Utf8JsonWriter(ms))
        {
            // Always output an array (valid for both single & batch)
            writer.WriteStartArray();

            foreach (var blockedRequest in blockedRequests)
            {
                // Extract the ID from whichever variant is stored
                string requestId = (blockedRequest.Kind == RpcRequestKind.Value)
                    ? blockedRequest.Value.id
                    : blockedRequest.KeyValue.id;

                writer.WriteStartObject();

                writer.WriteString("jsonrpc", "2.0");
                writer.WriteString("id", requestId);

                writer.WriteStartObject("error");
                // -32000 is commonly used for "server error" in the JSON-RPC specification
                writer.WriteNumber("code", code);
                writer.WriteString("message", message);
                writer.WriteEndObject();  // end of "error" object

                writer.WriteEndObject();   // end of the entire response object
            }//foreach (var blockedRequest in blockedRequests)

            writer.WriteEndArray();
        }//using (var writer = new Utf8JsonWriter(ms))

        return Encoding.UTF8.GetString(ms.ToArray());
    }//GenerateErrorResponses

    /// <summary>
    /// Merges a JSON string containing an array of upstream error responses
    /// with a list of blocked requests, generating a single JSON array string.
    /// </summary>
    public static string MergeErrorResponses(string upstreamErrorJson, List<RpcRequest> blockedRequests)
    {
        using MemoryStream ms = new MemoryStream();
        using (Utf8JsonWriter writer = new Utf8JsonWriter(ms))
        {
            writer.WriteStartArray();

            // Write upstream errors first
            if (!string.IsNullOrEmpty(upstreamErrorJson) && upstreamErrorJson.Trim().StartsWith("[") && upstreamErrorJson.Trim().EndsWith("]"))
            {
                try
                {
                    using JsonDocument doc = JsonDocument.Parse(upstreamErrorJson);
                    if (doc.RootElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (JsonElement element in doc.RootElement.EnumerateArray())
                        {
                            element.WriteTo(writer);
                        }
                    }//if (doc.RootElement.ValueKind == JsonValueKind.Array)
                }
                catch (JsonException ex)
                {
                    // Log or handle error if upstreamErrorJson is invalid JSON, maybe write a single generic error?
                    // For now, we'll just skip adding the upstream errors if parsing fails.
                    Console.Error.WriteLine($"Error parsing upstreamErrorJson: {ex.Message}");
                }//try-catch
            }//if (!string.IsNullOrEmpty(upstreamErrorJson) && upstreamErrorJson.Trim().StartsWith("[") && upstreamErrorJson.Trim().EndsWith("]"))

            // Add errors for blocked requests
            if (blockedRequests != null)
            {
                foreach (RpcRequest blockedRequest in blockedRequests)
                {
                    writer.WriteStartObject();
                    writer.WriteString("jsonrpc", "2.0");
                    writer.WriteString("id", blockedRequest.Id);
                    writer.WriteStartObject("error");
                    writer.WriteNumber("code", (int)RPCErrorCode.RPC_FORBIDDEN_BY_SAFE_MODE);
                    writer.WriteString("message", "Request blocked in read-only mode.");
                    writer.WriteEndObject(); // end error
                    writer.WriteEndObject(); // end response object
                }//foreach (var blockedRequest in blockedRequests)
            }//if (blockedRequests != null)

            writer.WriteEndArray();
        }//using (var writer = new Utf8JsonWriter(ms))
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    /// <summary>
    /// Generates a single JSON-RPC batch request body from a list of requests.
    /// All requests are placed into a single JSON array for forwarding to an upstream RPC server.
    /// </summary>
    /// <remarks>
    /// If there's only one request in the list, you'll end up with a JSON array containing a single object,
    /// which is still valid JSON-RPC for batch or single-call usage.
    /// </remarks>
    public static string GenerateUpstreamRequestBody(List<RpcRequest> requests)
    {
        if (requests == null || requests.Count == 0)
        {
            // No requests => empty array
            return "[]";
        }//if (requests == null || requests.Count == 0)

        using MemoryStream ms = new MemoryStream();
        using (Utf8JsonWriter writer = new Utf8JsonWriter(ms))
        {
            // Always start with an array (valid for single or batch)
            writer.WriteStartArray();

            foreach (var rpcUnion in requests)
            {




                writer.WriteStartObject();

                writer.WriteString("jsonrpc", "2.0");

                // Extract the ID and method from whichever variant it is
                string id = (rpcUnion.Kind == RpcRequestKind.Value)
                    ? rpcUnion.Value.id
                    : rpcUnion.KeyValue.id;
                string method = (rpcUnion.Kind == RpcRequestKind.Value)
                    ? rpcUnion.Value.method
                    : rpcUnion.KeyValue.method;

                writer.WriteString("id", id);
                writer.WriteString("method", method);

                // Write the params array
                writer.WritePropertyName("params");
                writer.WriteStartArray();

                if (rpcUnion.Kind == RpcRequestKind.Value)
                {
                    // RPCRequestValue => List<object>
                    List<object> paramList = rpcUnion.Value.@params;
                    foreach (var item in paramList)
                    {
                        RPCUtils.WriteJsonValue(writer, item);
                    }
                }
                else
                {
                    // RPCRequestKeyValue => List<Dictionary<string, object>>
                    List<Dictionary<string, object>> paramList = rpcUnion.KeyValue.@params;
                    foreach (var dict in paramList)
                    {
                        writer.WriteStartObject();
                        foreach (KeyValuePair<string, object> kv in dict)
                        {
                            writer.WritePropertyName(kv.Key);
                            RPCUtils.WriteJsonValue(writer, kv.Value);
                        }//foreach (var dict in paramList)
                        writer.WriteEndObject();
                    }
                }//if-else if (rpcUnion.Kind == RpcRequestKind.Value)

                writer.WriteEndArray(); // end "params"
                writer.WriteEndObject(); // end request object
            }//foreach (var rpcUnion in requests)

            writer.WriteEndArray(); // end array of requests
        }//using (var writer = new Utf8JsonWriter(ms))

        return Encoding.UTF8.GetString(ms.ToArray());
    }// GenerateUpstreamRequestBody

    /// <summary>
    /// Parses a single JSON-RPC request object and wraps it in the appropriate union variant.
    /// </summary>
    private static RpcRequest ParseRequestElement(JsonElement element)
    {
        bool result = element.TryGetProperty("id", out JsonElement idElema);
        string id = string.Empty;



        try
        {
            element.TryGetProperty("id", out JsonElement idElem);

            switch (idElem.ValueKind)
            {
                case JsonValueKind.String:
                    id = idElem.GetString()!;
                    break;
                case JsonValueKind.Number:
                    // Convert to string
                    id = idElem.ToString();
                    break;
                default:
                    throw new InvalidDataException("Invalid 'id' field.");
            }// switch (idElem.ValueKind)
        }
        catch
        {
            // Handle the case where 'id' is not a valid number
            throw new InvalidDataException("Invalid 'id' field.");
        }//try-catch


        string method = element.TryGetProperty("method", out JsonElement methodElem) && methodElem.ValueKind == JsonValueKind.String
                        ? methodElem.GetString()!
                        : throw new InvalidDataException("Missing or invalid 'method' field.");

        // If 'params' is missing, treat as empty array
        if (!element.TryGetProperty("params", out JsonElement paramsElem))
        {
            // Return a Value variant with empty params
            return new RpcRequest(new RPCRequestValue(id, method, new List<object>()));
        }//if (!element.TryGetProperty("params", out JsonElement paramsElem))

        // If 'params' is not an array, treat as empty
        if (paramsElem.ValueKind != JsonValueKind.Array)
        {
            return new RpcRequest(new RPCRequestValue(id, method, new List<object>()));
        }//if (paramsElem.ValueKind != JsonValueKind.Array)

        // Distinguish by examining the first element in 'params'
        JsonElement.ArrayEnumerator arr = paramsElem.EnumerateArray();
        if (!arr.MoveNext())
        {
            // 'params' is an empty array
            return new RpcRequest(new RPCRequestValue(id, method, new List<object>()));
        }

        // The first element in the array
        JsonElement firstElement = arr.Current;

        // If the first item is an object, assume KeyValue pattern.
        if (firstElement.ValueKind == JsonValueKind.Object)
        {
            // Parse entire array as a List<Dictionary<string, object>>
            var paramListKeyValue = new List<Dictionary<string, object>>();
            foreach (JsonElement child in paramsElem.EnumerateArray())
            {
                if (child.ValueKind != JsonValueKind.Object)
                    continue;

                paramListKeyValue.Add(ParseJsonObject(child));
            }
            return new RpcRequest(new RPCRequestKeyValue(id, method, paramListKeyValue));
        }
        else
        {
            // Otherwise parse entire array as a List<object> (primitives, arrays, etc.)
            var paramListValue = new List<object>();
            // We already advanced the enumerator once, so let's re-iterate from scratch.
            foreach (JsonElement child in paramsElem.EnumerateArray())
            {
                paramListValue.Add(ParseJsonValue(child));
            }
            return new RpcRequest(new RPCRequestValue(id, method, paramListValue));
        }//if-else if (firstElement.ValueKind == JsonValueKind.Object)
    }// ParseRequestElement

    /// <summary>
    /// Parses an object into a Dictionary<string, object>.
    /// </summary>
    private static Dictionary<string, object> ParseJsonObject(JsonElement element)
    {
        var dict = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (JsonProperty prop in element.EnumerateObject())
        {
            dict[prop.Name] = ParseJsonValue(prop.Value);
        }
        return dict;
    }// ParseJsonObject

    /// <summary>
    /// Parses a JsonElement into a CLR type.
    /// </summary>
    private static object ParseJsonValue(JsonElement elem)
    {
        return elem.ValueKind switch
        {
            JsonValueKind.String => elem.GetString()!,
            JsonValueKind.Number => elem.TryGetInt64(out long longVal) ? longVal : elem.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null!,
            JsonValueKind.Object => ParseJsonObject(elem),
            JsonValueKind.Array => ParseJsonArray(elem),
            _ => throw new InvalidDataException("Unsupported JSON value type.")
        };
    }// ParseJsonValue

    /// <summary>
    /// Parses an array into a List<object>.
    /// </summary>
    private static List<object> ParseJsonArray(JsonElement arrayEl)
    {
        var list = new List<object>();
        foreach (JsonElement item in arrayEl.EnumerateArray())
        {
            list.Add(ParseJsonValue(item));
        }
        return list;
    }// ParseJsonArray

}//class RPCSerialiser
