using System;
using System.Data.Common;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Newtonsoft.Json;
using Npgsql;
using StackExchange.Redis;

namespace Worker
{
    public class Program
    {
        public static int Main(string[] args)
        {
            try
            {
                var pgsql = OpenDbConnection("Server=db;Username=postgres;Password=postgres;");
                var redisConn = OpenRedisConnection("redis");
                var redis = redisConn.GetDatabase();

                var keepAliveCommand = pgsql.CreateCommand();
                keepAliveCommand.CommandText = "SELECT 1";

                // Definición de la estructura para las recomendaciones
                var definition = new { voter_id = "", recommendations = new string[] { } };

                while (true)
                {
                    Thread.Sleep(100);

                    if (redisConn == null || !redisConn.IsConnected)
                    {
                        Console.WriteLine("Reconnecting Redis");
                        redisConn = OpenRedisConnection("redis");
                        redis = redisConn.GetDatabase();
                    }

                    string json = redis.ListLeftPopAsync("recommendations").Result;
                    if (json != null)
                    {
                        var recommendationData = JsonConvert.DeserializeAnonymousType(json, definition);
                        if (string.IsNullOrEmpty(recommendationData.voter_id) || recommendationData.recommendations.Length == 0)
                        {
                            Console.WriteLine("Invalid recommendation data received, skipping...");
                            continue;
                        }

                        Console.WriteLine($"Processing recommendations from user '{recommendationData.voter_id}'");

                        if (!pgsql.State.Equals(System.Data.ConnectionState.Open))
                        {
                            Console.WriteLine("Reconnecting DB");
                            pgsql = OpenDbConnection("Server=db;Username=postgres;Password=postgres;");
                        }
                        else
                        {
                            // Iterar sobre las recomendaciones y almacenarlas
                            foreach (var recommendation in recommendationData.recommendations)
                            {
                                StoreRecommendation(pgsql, recommendationData.voter_id, recommendation);
                            }
                        }
                    }
                    else
                    {
                        keepAliveCommand.ExecuteNonQuery();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.ToString());
                return 1;
            }
        }

        private static NpgsqlConnection OpenDbConnection(string connectionString)
        {
            NpgsqlConnection connection;

            while (true)
            {
                try
                {
                    connection = new NpgsqlConnection(connectionString);
                    connection.Open();
                    break;
                }
                catch (SocketException)
                {
                    Console.Error.WriteLine("Waiting for db");
                    Thread.Sleep(1000);
                }
                catch (DbException)
                {
                    Console.Error.WriteLine("Waiting for db");
                    Thread.Sleep(1000);
                }
            }

            Console.Error.WriteLine("Connected to db");

            var command = connection.CreateCommand();
            // Eliminar la tabla si existe y crear una nueva
            command.CommandText = @"
                DROP TABLE IF EXISTS recommendations;
                CREATE TABLE IF NOT EXISTS recommendations (
                    user_id VARCHAR(255) NOT NULL,
                    recommendation TEXT NOT NULL,
                    PRIMARY KEY (user_id, recommendation)
                );";
            command.ExecuteNonQuery();

            return connection;
        }

        private static ConnectionMultiplexer OpenRedisConnection(string hostname)
        {
            var ipAddress = GetIp(hostname);
            Console.WriteLine($"Found redis at {ipAddress}");

            while (true)
            {
                try
                {
                    Console.Error.WriteLine("Connecting to redis");
                    return ConnectionMultiplexer.Connect(ipAddress);
                }
                catch (RedisConnectionException)
                {
                    Console.Error.WriteLine("Waiting for redis");
                    Thread.Sleep(1000);
                }
            }
        }

        private static string GetIp(string hostname)
            => Dns.GetHostEntryAsync(hostname)
                .Result
                .AddressList
                .First(a => a.AddressFamily == AddressFamily.InterNetwork)
                .ToString();

        // Método ajustado para almacenar recomendaciones
        private static void StoreRecommendation(NpgsqlConnection connection, string userId, string recommendation)
        {
            var command = connection.CreateCommand();
            try
            {
                command.CommandText = "INSERT INTO recommendations (user_id, recommendation) VALUES (@user_id, @recommendation)";
                command.Parameters.AddWithValue("@user_id", userId);
                command.Parameters.AddWithValue("@recommendation", recommendation);
                command.ExecuteNonQuery();
            }
            catch (DbException ex)
            {
                // Si hay un error, probablemente sea porque ya existe un par (user_id, recommendation)
                Console.WriteLine($"Failed to insert recommendation '{recommendation}' for user '{userId}': {ex.Message}");
            }
            finally
            {
                command.Dispose();
            }
        }
    }
}



