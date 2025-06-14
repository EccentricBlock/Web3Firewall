using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Web3Firewall.Shared.Database;
using Web3Firewall.Shared.Database.Tables;
using Web3Firewall.Shared.Models;

namespace Web3Firewall.App.Code.APIs;

public static class LogsEngpoint
{
    public static WebApplication AddLogsAPIEndpoints(this WebApplication app)
    {
        app.UseRouting();
        RouteGroupBuilder networkGroup = app.MapGroup("/Logs")
                                                .WithTags("Log Management");

        networkGroup.MapGet("/", async (AppDBContext db, int page = 1, int pageSize = 20) =>
        {
            int totalLogs = await db.RPCRequestLogs.CountAsync();

            List<RPCRequestLogEntry> logs = await db.RPCRequestLogs
                               .OrderByDescending(l => l.Timestamp)
                               .Skip((page - 1) * pageSize)
                               .Take(pageSize)
                               .ToListAsync();
            return Results.Ok(new { TotalCount = totalLogs, Page = page, PageSize = pageSize, Data = logs });
        })
            .WithSummary("Get All Logs")
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .Produces<List<RPCRequestLogEntry>>(StatusCodes.Status200OK);


        networkGroup.MapPost("/ByMethod", async (AppDBContext db, [FromBody] LogQueryRequest request) =>
        {
            int page = request.Page < 1 ? 1 : request.Page;
            int pageSize = request.PageSize < 1 ? 20 : request.PageSize; // Default page size



            int totalLogs = await db.RPCRequestLogs.CountAsync();
            List<RPCRequestLogEntry> logs = await db.RPCRequestLogs
                               .OrderByDescending(l => l.Timestamp)
                               .Skip((page - 1) * pageSize)
                               .Take(pageSize)
                               .ToListAsync();
            return Results.Ok(new { TotalCount = totalLogs, Page = page, PageSize = pageSize, Data = logs });
        })
            .WithSummary("Get Logs By RPC Method")
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .Produces<List<RPCRequestLogEntry>>(StatusCodes.Status200OK);

        return app;
    }//AddLogsAPIEndpoints(this WebApplication app)
}
