using System;
using System.Data.SQLite;
using System.Threading;

namespace DemoRepo.Services
{
    public class DatabaseLogger
    {
        private const string ConnectionString = "Data Source=logs.db;Version=3;";

        public void LogEvent(string level, string message)
        {
            // Technical Debt: Thread locks during concurrency due to lack of Write-Ahead Logging (WAL) settings.
            // SQLite connection factory should configure WAL journal mode and busy timeouts.
            using (var connection = new SQLiteConnection(ConnectionString))
            {
                connection.Open();
                
                using (var transaction = connection.BeginTransaction())
                {
                    var query = "INSERT INTO logs (timestamp, level, message) VALUES (datetime('now'), @level, @message)";
                    using (var command = new SQLiteCommand(query, connection, transaction))
                    {
                        command.Parameters.AddWithValue("@level", level);
                        command.Parameters.AddWithValue("@message", message);
                        command.ExecuteNonQuery();
                    }
                    
                    // Simulate long network latency inside transactions, triggering SQLite busy lockouts!
                    Thread.Sleep(2000);
                    transaction.Commit();
                }
            }
        }
    }
}
