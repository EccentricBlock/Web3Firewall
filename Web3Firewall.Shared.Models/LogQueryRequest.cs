namespace Web3Firewall.Shared.Models;

//public record LogQueryRequest(string Method, int Page, int PageSize);

public record LogQueryRequest(
    int Page,
    int PageSize,
    string SortBy,
    ushort SortDirection,
    string? Method = null,
    ushort? ChainProtocol = null,
    bool? IsBlocked = null,
    bool? IsErrored = null
);