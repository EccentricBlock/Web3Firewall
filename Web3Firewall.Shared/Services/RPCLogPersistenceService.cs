using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Threading.Channels;
using Web3Firewall.Shared.Database;
using Web3Firewall.Shared.Database.Tables;

namespace Web3Firewall.Shared.Services;

public class RPCLogPersistenceService(
                                        IServiceProvider serviceProvider, // Use IServiceProvider to create scoped DbContext
                                        Channel<RPCRequestLogEntry> channel,
                                        ILogger<RPCLogPersistenceService> logger
                                     ) : BackgroundService
{
    private readonly ChannelReader<RPCRequestLogEntry> _channelReader = channel.Reader;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("RPC Log Persistence Service started.");

        //// Ensure database is created
        //using (IServiceScope scope = _serviceProvider.CreateScope())
        //{
        //    AppDbContext dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        //    _ = await dbContext.Database.EnsureCreatedAsync(stoppingToken).ConfigureAwait(false); // Or EnsureCreatedAsync() if not using migrations
        //}

        // Batching configuration
        const int maxBatchSize = 100;
        TimeSpan batchTimeout = TimeSpan.FromSeconds(5);

        List<RPCRequestLogEntry> batch = new List<RPCRequestLogEntry>(maxBatchSize);
        CancellationTokenSource timerCts = new CancellationTokenSource(batchTimeout);
        // CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, timerCts.Token);

        while (!stoppingToken.IsCancellationRequested)
        {
            timerCts = new CancellationTokenSource(batchTimeout);


            try
            {

                await _channelReader.WaitToReadAsync(timerCts.Token).ConfigureAwait(false); // Wait for items to be available

                // Try to fill the batch or wait until timeout
                await foreach (RPCRequestLogEntry? logEntry in _channelReader.ReadAllAsync(timerCts.Token).WithCancellation(timerCts.Token))
                {
                    if (logEntry is null) continue; // Skip null entries
                    batch.Add(logEntry);
                    if (batch.Count >= maxBatchSize)
                        break;
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when timeout occurs or service stops, check if stoppingToken was the cause
                if (stoppingToken.IsCancellationRequested) break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error reading from log channel.");
                continue; // Continue processing if possible
            }
            finally
            {
                timerCts.Dispose();
            }

            if (batch.Count == 0) continue; // No items in batch

            logger.LogDebug("Processing batch of {Count} rpc log entries.", batch.Count);

            try
            {
                using IServiceScope scope = serviceProvider.CreateScope();
                AppDBContext dbContext = scope.ServiceProvider.GetRequiredService<AppDBContext>();


                Dictionary<string, RPCRequestLogEntry> entriesToAdd = new Dictionary<string, RPCRequestLogEntry>(batch.Count, StringComparer.OrdinalIgnoreCase); // RequestId -> LogEntry

                Dictionary<string, RPCRequestLogEntry> entriesToUpdate = new Dictionary<string, RPCRequestLogEntry>(batch.Count, StringComparer.OrdinalIgnoreCase); // RequestId -> LogEntry
                int updateCount = 0; // Initialize update count here

                // Separate new entries from updates
                foreach (RPCRequestLogEntry entry in batch)
                {
                    if (!string.IsNullOrWhiteSpace(entry.Response)) // This entry represents an update or a blocked request
                    {
                        if (entriesToAdd.ContainsKey(entry.RequestId))
                        {
                            entriesToAdd[entry.RequestId].Response = entry.Response; // Keep the latest update for a given RequestId in the batch
                        }
                        else
                        {
                            entriesToUpdate.Add(entry.RequestId, entry); // This is an update
                        }
                    }
                    else // This is the initial request log
                    {
                        // Avoid adding duplicates if an update for the same RequestId is also in the batch
                        if (!entriesToAdd.ContainsKey(entry.RequestId))
                        {
                            entriesToAdd.Add(entry.RequestId, entry);
                        }
                    }
                }

                // Process updates first
                if (entriesToUpdate.Any())
                {
                    List<string> requestIdsToUpdate = entriesToUpdate.Keys.ToList();
                    List<RPCRequestLogEntry> existingEntries = await dbContext.RPCRequestLogs
                                                  .Where(e => requestIdsToUpdate.Contains(e.RequestId))
                                                  .ToListAsync(stoppingToken);

                    foreach (RPCRequestLogEntry existingEntry in existingEntries)
                    {
                        if (entriesToUpdate.TryGetValue(existingEntry.RequestId, out RPCRequestLogEntry? updateEntry))
                        {
                            // Only update if the response is currently null or if it's a blocked status update
                            if (string.IsNullOrWhiteSpace(existingEntry.Response))
                            {
                                existingEntry.Response = updateEntry.Response;
                                dbContext.RPCRequestLogs.Update(existingEntry);
                            }
                            // Remove processed update to avoid adding it as a new entry later
                            entriesToUpdate.Remove(existingEntry.RequestId);
                        }
                    }
                    // Calculate update count here, where existingEntries is in scope
                    updateCount = existingEntries.Count(e => dbContext.Entry(e).State == EntityState.Modified);
                }



                if (entriesToAdd.Any())
                {
                    // Add new entries (including updates for entries not found in DB)
                    dbContext.RPCRequestLogs.AddRange(entriesToAdd.Values);

                }
                // Add any remaining entries from entriesToUpdate (these were updates for requests not yet in DB)
                if (entriesToUpdate.Any())
                {
                    dbContext.RPCRequestLogs.AddRange(entriesToUpdate.Values);
                }

                if (dbContext.ChangeTracker.HasChanges())
                {
                    await dbContext.SaveChangesAsync(stoppingToken).ConfigureAwait(false);
                    // Use the calculated updateCount variable here
                    logger.LogInformation("Persisted {AddCount} new and updated {UpdateCount} existing log entries.", entriesToAdd.Count, updateCount);
                }
                else
                {
                    logger.LogDebug("No database changes detected in this batch.");
                }

            }
            catch (DbUpdateException ex)
            {
                logger.LogError(ex, "Database error occurred while persisting logs.");
                // Consider retry logic or moving failed batch to a dead-letter queue
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unhandled error occurred during log persistence.");
            }

            batch.Clear(); // Clear the batch for the next iteration
        }//while

        logger.LogInformation("Log Persistence Service stopped.");
    }
}