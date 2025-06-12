using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Data.Common;


namespace Web3Firewall.Shared.Database;
public class SQLiteInterceptor : DbConnectionInterceptor
{
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        string commandText = "pragma journal_mode = WAL;\r\npragma synchronous = normal;\r\npragma temp_store = memory;\r\npragma mmap_size = 30000000000;\r\npragma page_size = 32768;\r\npragma cache_size -16000;";

        using (DbCommand command = connection.CreateCommand())
        {
            command.CommandText = commandText;
            command.ExecuteNonQuery();
        }//using (DbCommand command = connection.CreateCommand())

        base.ConnectionOpened(connection, eventData);
    }//public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
}//class 