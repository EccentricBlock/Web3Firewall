using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Options;
using Scalar.AspNetCore;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Web3Firewall.Code.Settings;
using Web3Firewall.Shared.Database;
using Web3Firewall.Shared.Database.Tables;
using Web3Firewall.Shared.Models;
using Web3Firewall.Shared.Services;
using Web3Firewall.Shared.Utils.JsonRPC;

namespace Web3Firewall.Code.RpcEndpoints;

public static class JSONRrpcEndpoint
{
    public static WebApplication AddStandardJsonRpcProxyEndpoints(this WebApplication app)
    {

        app.MapPost("/", async (
                        HttpRequest request,
                        [FromServices] Channel<RPCRequestLogEntry> logWriter, // Use ChannelWriter
                        [FromServices] IHttpClientFactory httpClientFactory,
                        [FromServices] ILogger<Program> logger,
                        [FromServices] HybridCache cache,
                        [FromServices] AppDBContext dBFactory,
                        [FromServices] RPCFilterService filterService,
                        [FromServices] GlobalSettings globalSettings,
                        [FromServices] IOptions<PROXY> proxyOptions) =>
        {

            PROXY proxySettings = proxyOptions.Value;

            if (string.IsNullOrWhiteSpace(proxySettings.UPSTREAM_URI))
            {
                logger.LogWarning("Upstream JSON-RPC URI is NULL");
                // Return a generic 404 to avoid revealing network existence
                return Results.NotFound(new { message = $"Upstream network endpoint not found or is disabled." });
            }//if (string.IsNullOrWhiteSpace(proxySettings.UPSTREAM_URI))



            List<RpcRequest> requestList;
            try
            {
                requestList = await RPCSerialiser.DeserializeJsonRpcRequestsAsync(request);
            }
            catch (JsonException jsonEx)
            {
                logger.LogError(jsonEx, $"Failed to deserialize JSON-RPC request for network {proxySettings.UPSTREAM_URI}");
                // Use a generic ID or null if batch deserialization failed early
                var errorResponse = new RPCResponse("1", null, new RPCError(RPCErrorCode.RPC_PARSE_ERROR, "Failed to parse request JSON"));
                return Results.Json(new[] { errorResponse }, contentType: "application/json", statusCode: StatusCodes.Status400BadRequest);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"Unexpected error deserializing request for network {proxySettings.UPSTREAM_URI}");
                var errorResponse = new RPCResponse("1", null, new RPCError(RPCErrorCode.RPC_INTERNAL_ERROR, "Internal server error during request parsing"));
                return Results.Json(new[] { errorResponse }, contentType: "application/json", statusCode: StatusCodes.Status500InternalServerError);
            }//try-catch

            DateTime requestTimestamp = DateTime.UtcNow;
            string blockedResponseText = "Blocked in read-only mode";

            List<RpcRequest> blockedRequests = new();
            List<RpcRequest> allowedRequests = new();
            List<RPCRequestLogEntry> initialLogEntries = new(); // Store initial log entries

            // 3. Process each request in the batch (check blocking, create initial log)
            foreach (RpcRequest requestItem in requestList)
            {
                logger.LogInformation("+ Processing request ({NetworkKey}): Method={Method}, ID={Id}", proxySettings.UPSTREAM_URI, requestItem.Method, requestItem.Id);

                // Create the initial LogEntry with NetworkId
                RPCRequestLogEntry logEntry = new RPCRequestLogEntry
                {
                    Method = requestItem.Method,
                    RequestId = requestItem.Id,
                    ChainProtocol = proxySettings.CHAIN_PROTOCOL,
                    Request = requestItem.Json, // Log the full original request
                    Response = null, // Response will be updated later
                    Timestamp = requestTimestamp,
                    Blocked = false, // Assume allowed initially
                };

                if (filterService.IsBlocked(requestItem.Method, proxySettings.CHAIN_PROTOCOL, globalSettings.IsReadOnlyMode))
                {
                    logger.LogWarning("Blocking write method '{Method}' with ID '{Id}' for network {NetworkKey} ({NetworkProtocol}) in read-only mode.", requestItem.Method, requestItem.Id, proxySettings.UPSTREAM_URI, proxySettings.CHAIN_PROTOCOL);
                    logEntry.Blocked = true;
                    logEntry.Response = $"{blockedResponseText} (method: {requestItem.Method})";
                    blockedRequests.Add(requestItem);
                }
                else
                {
                    allowedRequests.Add(requestItem);
                }//if-else (filterService.IsBlocked

                initialLogEntries.Add(logEntry); // Add to list
                                                 // Write the initial log entry (request part) to the channel
                logWriter.Writer.TryWrite(logEntry);
            }// foreach (RpcRequest requestItem in requestList)

            // If all requests were blocked, return error responses immediately
            if (allowedRequests.Count == 0)
            {
                logger.LogWarning("All {RequestCount} requests in the batch for network {NetworkKey} were blocked in read-only mode.", requestList.Count, proxySettings.UPSTREAM_URI);
                return Results.Json(RPCSerialiser.GenerateErrorResponses(blockedRequests), contentType: "application/json");
            }//if (allowedRequests.Count == 0)

            logger.LogInformation("Processing batch for {NetworkKey}: {AllowedCount} allowed, {BlockedCount} blocked.", proxySettings.UPSTREAM_URI, allowedRequests.Count, blockedRequests.Count);

            // 4. Send allowed requests upstream
            string upstreamRequestBody = RPCSerialiser.GenerateUpstreamRequestBody(allowedRequests);
            HttpClient httpClient = httpClientFactory.CreateClient("JsonRpcClient");
            StringContent content = new StringContent(upstreamRequestBody, Encoding.UTF8, "application/json");
            HttpResponseMessage? upstreamResponse = null;
            string upstreamResponseText = "<Upstream communication failed>"; // Default error text

            try
            {
                logger.LogDebug("Sending request to upstream: {UpstreamUrl} for network {NetworkKey}", proxySettings.UPSTREAM_URI, proxySettings.CHAIN_PROTOCOL);
                upstreamResponse = await httpClient.PostAsync(proxySettings.UPSTREAM_URI, content);

                // Handle Upstream HTTP Errors (Non-2xx)
                if (!upstreamResponse.IsSuccessStatusCode)
                {
                    try { upstreamResponseText = await upstreamResponse.Content.ReadAsStringAsync(); } catch (Exception readEx) { logger.LogWarning(readEx, "Failed to read upstream error response body for {NetworkKey}.", proxySettings.UPSTREAM_URI); upstreamResponseText = "<Failed to read upstream error body>"; }
                    logger.LogError("Upstream RPC call to {UpstreamUrl} for network {NetworkKey} failed with status {StatusCode}. Response: {Response}", proxySettings.UPSTREAM_URI, proxySettings.CHAIN_PROTOCOL, upstreamResponse.StatusCode, upstreamResponseText);

                    string errorResponseBody = RPCSerialiser.GenerateErrorResponses(allowedRequests, (int)upstreamResponse.StatusCode, upstreamResponseText);

                    // Update log entries with failure information
                    foreach (RpcRequest req in allowedRequests)
                    {
                        logWriter.Writer.TryWrite(new RPCRequestLogEntry
                        {
                            RequestId = req.Id,
                            Response = $"Upstream Error: {(int)upstreamResponse.StatusCode} - {upstreamResponseText}",
                            ChainProtocol = proxySettings.CHAIN_PROTOCOL,
                            Timestamp = DateTime.UtcNow, // Use current time for update
                            Method = string.Empty, // Indicate this is an update
                            Request = string.Empty // Indicate this is an update
                        });
                    }//foreach (var req in allowedRequests)

                    // Merge blocked responses if any
                    string finalErrorResponse = RPCSerialiser.MergeErrorResponses(errorResponseBody, blockedRequests);
                    return Results.Json(finalErrorResponse, contentType: "application/json");
                }//if (!upstreamResponse.IsSuccessStatusCode)

                // Process successful upstream response
                List<RPCResponse> upstreamResponseObjects = await RPCUtils.ConvertUpstreamResponseAsync(upstreamResponse).ConfigureAwait(false);

                // Update original log entries with the response data via the channel
                foreach (var responseEntry in upstreamResponseObjects) // Corresponds to allowedRequests
                {
                    string responseContent;
                    if ((responseEntry.error?.code ?? 0) < 0)
                    {
                        responseContent = $"Error: {responseEntry.error!.Value.code}\nMessage: {responseEntry.error.Value.message}";


                        logger.LogWarning($"+ Upstream response error for request ID {responseEntry.id} / {requestList.FirstOrDefault(x => x.Id == responseEntry.id).Method}: {responseContent}");
                    }
                    else
                    {
                        responseContent = responseEntry.result ?? string.Empty;
                    }//if-else ((responseEntry.error?.code ?? 0) < 0)


                    logWriter.Writer.TryWrite(new RPCRequestLogEntry
                    {
                        RequestId = responseEntry.id,
                        ChainProtocol = proxySettings.CHAIN_PROTOCOL,
                        Response = responseContent,
                        Timestamp = DateTime.UtcNow, // Use current time for update
                        Method = string.Empty, // Indicate this is an update
                        Request = string.Empty // Indicate this is an update
                    });



                }//foreach (var responseEntry in upstreamResponseObjects)

                // Merge upstream responses with blocked request errors
                if (blockedRequests.Count > 0)
                {
                    foreach (var blockedRequest in blockedRequests)
                    {
                        var error = new RPCError(RPCErrorCode.RPC_FORBIDDEN_BY_SAFE_MODE, "Request blocked in read-only mode.");
                        upstreamResponseObjects.Add(new RPCResponse(
                            blockedRequest.Id,
                            result: null,
                            error: error
                        ));
                    }//foreach (var blockedRequest in blockedRequests)
                     // TODO: Ensure response order matches request order if possible (complex for batches)
                     // For simplicity, we append blocked errors. Client needs to handle mixed responses.
                }//if (blockedRequests.Count > 0)

                return Results.Json(upstreamResponseObjects, contentType: "application/json");
            }
            catch (HttpRequestException httpEx)
            {
                logger.LogError(httpEx, "HTTP Exception occurred calling upstream {UpstreamUrl} for network {NetworkKey}", proxySettings.UPSTREAM_URI, proxySettings.CHAIN_PROTOCOL);
                string responseBody = RPCSerialiser.GenerateErrorResponses(allowedRequests, StatusCodes.Status502BadGateway, $"Upstream communication error: {httpEx.Message}");
                var finalResponse = RPCSerialiser.MergeErrorResponses(responseBody, blockedRequests);
                // Update logs with failure
                foreach (var req in allowedRequests)
                {
                    logWriter.Writer.TryWrite(new RPCRequestLogEntry
                    {
                        RequestId = req.Id,
                        Response = $"Upstream Communication Error: {httpEx.Message}",
                        ChainProtocol = proxySettings.CHAIN_PROTOCOL,
                        Timestamp = DateTime.UtcNow,
                        Errored = true,
                        Method = string.Empty,
                        Request = string.Empty
                    });
                }
                return Results.Json(finalResponse, contentType: "application/json", statusCode: StatusCodes.Status502BadGateway);
            }
            catch (JsonException jsonEx)
            {
                logger.LogError(jsonEx, "JSON Exception occurred processing upstream response from {UpstreamUrl} for network {NetworkKey}", proxySettings.UPSTREAM_URI, proxySettings.CHAIN_PROTOCOL);
                string responseBody = RPCSerialiser.GenerateErrorResponses(allowedRequests, StatusCodes.Status500InternalServerError, "Failed to process upstream response JSON");
                var finalResponse = RPCSerialiser.MergeErrorResponses(responseBody, blockedRequests);
                // Update logs with failure
                foreach (var req in allowedRequests)
                {
                    logWriter.Writer.TryWrite(new RPCRequestLogEntry
                    {
                        RequestId = req.Id,
                        Response = "Upstream Response JSON Error",
                        ChainProtocol = proxySettings.CHAIN_PROTOCOL,
                        Timestamp = DateTime.UtcNow,
                        Errored = true,
                        Method = string.Empty,
                        Request = string.Empty
                    });
                }
                return Results.Json(finalResponse, contentType: "application/json", statusCode: StatusCodes.Status500InternalServerError);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unhandled Exception occurred during request processing for network {NetworkKey}", proxySettings.UPSTREAM_URI);
                string responseBody = RPCSerialiser.GenerateErrorResponses(allowedRequests, StatusCodes.Status500InternalServerError, "Internal server error");
                var finalResponse = RPCSerialiser.MergeErrorResponses(responseBody, blockedRequests);
                // Update logs with failure
                foreach (var req in allowedRequests)
                {
                    logWriter.Writer.TryWrite(new RPCRequestLogEntry
                    {
                        RequestId = req.Id,
                        Response = $"Internal Server Error: {ex.Message}",
                        ChainProtocol = proxySettings.CHAIN_PROTOCOL,
                        Timestamp = DateTime.UtcNow,
                        Errored = true,
                        Method = string.Empty,
                        Request = string.Empty
                    });
                }
                return Results.Json(finalResponse, contentType: "application/json", statusCode: StatusCodes.Status500InternalServerError);
            }//try-ctahc



        })
        .ExcludeFromApiReference();//endpoints.MapPost("/rpc", async context =>

        return app;
    }//public static WebApplication AddStandardJsonRpcProxyEndpoints(this WebApplication app, int listeningPort)
}//class JSONRrpcEndpoint