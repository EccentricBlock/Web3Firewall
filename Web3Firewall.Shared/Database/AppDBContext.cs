using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Web3Firewall.Shared.Database.Tables;

namespace Web3Firewall.Shared.Database;
public class AppDBContext(DbContextOptions<AppDBContext> options) : DbContext(options)
{
    public DbSet<RPCRequestLogEntry> RPCRequestLogs { get; set; } = default!;
}
