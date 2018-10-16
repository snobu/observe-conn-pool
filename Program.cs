using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace pooling
{
    public class Connections
    {
        public string login { get; set; }
        public int session_count { get; set; }
    }

    class Program
    {
        static string pool = "Min Pool Size=30;Max Pool Size=40;";
        static string retry = "ConnectRetryCount=12;ConnectRetryInterval=5;Connection Timeout=60;";
        static string connStr = Environment.GetEnvironmentVariable("CONNSTR") != null ?
            Environment.GetEnvironmentVariable("CONNSTR") + retry + pool :
            throw new Exception("\n\nYOU FORGOT TO EXPORT CONNSTR ENV VAR\n");
        static string sqlQuery = "SELECT TOP 200 * FROM SalesLT.Product";
        static async Task Main(string[] args)
        {
            await SprayWithClose(800, close: true);
            // await SprayWithDispose(800);
            await PrintSessions();
        }

        private static async Task PrintSessions()
        {
            var list = await GetSessions();
            Console.WriteLine($"\nUSER {new String(' ', 20)} SESSIONS");
            Console.WriteLine(new String('-', 34));
            foreach (var item in list)
            {
                Console.WriteLine($"{item.login.PadRight(28)}{item.session_count.ToString().PadLeft(4)}");
            }
            Console.WriteLine(new String('-', 34));
        }

        public static void SprayWithParallelFor(int damage, bool close = true)
        {
            Parallel.For(0, damage, (d) =>
            {
                // hand me one SQL Server connection from the pool
                SqlConnection sqlConn = new SqlConnection(connStr);
                var command = new SqlCommand(sqlQuery, sqlConn);
                if (sqlConn.State == ConnectionState.Closed)
                {
                    sqlConn.Open();
                }
                var reader = command.ExecuteReader();
                if (reader.HasRows)
                {
                    // var tid = Thread.CurrentThread.ManagedThreadId;
                    // Console.Write($"thread_id {tid} //  ");
                    Task.Run(() => Console.WriteLine(Netstat()));
                }
                if (close)
                {
                    sqlConn.Close(); // Gracefully return the connection to the pool
                }
            });
        }

        public static async Task SprayWithClose(int damage, bool close = true)
        {
            List<Task> tasks = new List<Task>();
            for (var i = 0; i < damage; i++)
            {
                tasks.Add(Task.Run(async () =>
                {
                    // hand me one SQL Server connection from the pool
                    SqlConnection sqlConn = new SqlConnection(connStr);
                    var command = new SqlCommand(sqlQuery, sqlConn);
                    if (sqlConn.State == ConnectionState.Closed)
                    {
                        await sqlConn.OpenAsync();
                    }
                    var reader = await command.ExecuteReaderAsync();
                    if (reader.HasRows)
                    {
                        var tid = Thread.CurrentThread.ManagedThreadId;
                        Console.WriteLine(Netstat());
                    }
                    if (close)
                    {
                        sqlConn.Close(); // Gracefully return the connection to the pool
                    }
                }));
            }
            await Task.WhenAll(tasks);
        }

        public static async Task SprayWithDispose(int damage)
        {
            List<Task> tasks = new List<Task>();
            for (var i = 0; i < damage; i++)
            {
                tasks.Add(Task.Run(async () =>
                {
                    // hand me one SQL Server connection from the pool
                    using (SqlConnection sqlConn = new SqlConnection(connStr))
                    {
                        var command = new SqlCommand(sqlQuery, sqlConn);
                        if (sqlConn.State == ConnectionState.Closed)
                        {
                            await sqlConn.OpenAsync();
                        }
                        var reader = await command.ExecuteReaderAsync();
                        if (reader.HasRows)
                        {
                            // var tid = Thread.CurrentThread.ManagedThreadId;
                            // Console.WriteLine($"We have rows. Managed Thread Id: {tid}");
                            Console.WriteLine(Netstat());
                        }
                    } // sqlConn gets Disposed here (GC calls .Finalize())
                }));
            }
            await Task.WhenAll(tasks);
        }

        public static async Task<List<Connections>> GetSessions()
        {
            SqlConnection sqlConn = new SqlConnection(connStr);
            var connList = new List<Connections>();

            var command = new SqlCommand(
                @"DECLARE @Table TABLE(
                    SPID INT,
                    Status VARCHAR(MAX),
                    LOGIN VARCHAR(MAX),
                    HostName VARCHAR(MAX),
                    BlkBy VARCHAR(MAX),
                    DBName VARCHAR(MAX),
                    Command VARCHAR(MAX),
                    CPUTime INT,
                    DiskIO INT,
                    LastBatch VARCHAR(MAX),
                    ProgramName VARCHAR(MAX),
                    SPID_1 INT,
                    REQUESTID INT
                )

                INSERT INTO @Table EXEC sp_who2

                SELECT [@Table].login, COUNT([@Table].login) AS count
                FROM @Table
                GROUP BY [@Table].login",
            sqlConn);

            if (sqlConn.State == ConnectionState.Closed)
            {
                await sqlConn.OpenAsync();
            }

            try
            {
                var reader = await command.ExecuteReaderAsync();
                while (reader.Read())
                {
                    connList.Add(
                        new Connections
                        {
                            login = reader.GetString(0),
                            session_count = reader.GetInt32(1)
                        });
                }
                sqlConn.Close(); // gracefully return connection to ADO.NET connection pool

                return connList;
            }
            catch (Exception e)
            {
                sqlConn.Close();
                Console.WriteLine(e);

                return null;
            }
        }

        public static int Netstat()
        {
            int estabConnNumber = -1;

            try
            {
                using (Process p = new Process())
                {

                    ProcessStartInfo ps = new ProcessStartInfo();
                    ps.Arguments = "-an";
                    ps.FileName = "netstat";
                    ps.UseShellExecute = false;
                    ps.WindowStyle = ProcessWindowStyle.Hidden;
                    ps.RedirectStandardInput = true;
                    ps.RedirectStandardOutput = true;
                    ps.RedirectStandardError = true;

                    p.StartInfo = ps;
                    p.Start();

                    StreamReader stdOutput = p.StandardOutput;
                    StreamReader stdError = p.StandardError;

                    string content = stdOutput.ReadToEnd() + stdError.ReadToEnd();
                    string[] rows = Regex.Split(content, "\n");
                    estabConnNumber = rows.Where(
                            r => r.Contains("1433") &&
                            r.Contains("ESTAB"))
                        .ToArray().Length;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }

            return estabConnNumber;
        }
    }
}
