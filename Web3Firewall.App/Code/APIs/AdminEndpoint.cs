using Microsoft.AspNetCore.Mvc;
using Web3Firewall.App.Code.Settings;
using Web3Firewall.Shared.Database;

namespace Web3Firewall.App.Code.APIs;

public static class AdminEndpoint
{
    public static WebApplication AddAdminAPIEndpoints(this WebApplication app)
    {
        app.UseRouting();
        RouteGroupBuilder networkGroup = app.MapGroup("/Admin")
                                                .WithTags("Admin Management");

        networkGroup.MapPost("/SetReadOnly", async (AppDBContext db, GlobalSettings globalSettings, [FromBody] bool readOnlyEnabled) =>
        {

            if (readOnlyEnabled && globalSettings.IsReadOnlyMode)
            {
                return Results.BadRequest("Read-only mode is already enabled.");
            }

            globalSettings.IsReadOnlyMode = readOnlyEnabled;
            return Results.Ok($"Proxy Read-Only Mode Changed To '{readOnlyEnabled}'");
        })
            .WithSummary("Set Read-Only Mode")
            .Produces(StatusCodes.Status400BadRequest)
            .Produces<string>(StatusCodes.Status200OK);

        return app;
    } // AddAdminAPIEndpoints(this WebApplication app)
}
