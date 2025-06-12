using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Web3Firewall.Shared.Enums;


namespace Web3Firewall.Shared.Database.Tables;

[Index(nameof(RequestId), nameof(Method))]
public class RPCRequestLogEntry
{
    [Key]
    public long Id { get; set; }

    [Required]
    public required string RequestId { get; init; }

    [Required]
    public required string Method { get; init; }

    [Required]
    public required string Request { get; init; }

    public string? Response { get; set; }

    public long? BlockNumber { get; set; }

    [Required]
    public required ChainProtocol ChainProtocol { get; set; }

    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public bool Blocked { get; set; } = false;
    public bool Errored { get; set; } = false;
}
