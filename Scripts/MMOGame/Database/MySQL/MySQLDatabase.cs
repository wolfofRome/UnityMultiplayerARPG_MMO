﻿using MySql.Data.MySqlClient;
using System;
using System.Threading.Tasks;

namespace Insthync.MMOG
{
    public partial class MySQLDatabase : BaseDatabase
    {
        public enum InventoryType : byte
        {
            NonEquipItems,
            EquipItems,
            EquipWeaponRight,
            EquipWeaponLeft,
        }
        public string address = "127.0.0.1";
        public int port = 3306;
        public string username = "root";
        public string password = "";
        public string dbName = "mmorpgtemplate";

        public string GetConnectionString()
        {
            var connectionString = "Server=" + address + ";" +
                "Port=" + port + ";" +
                "Uid=" + username + ";" +
                (string.IsNullOrEmpty(password) ? "" : "Pwd=\"" + password + "\";") +
                "Database=" + dbName + ";";
            return connectionString;
        }

        public void SetupConnection(string address, int port, string username, string password, string dbName)
        {
            this.address = address;
            this.port = port;
            this.username = username;
            this.password = password;
            this.dbName = dbName;
        }

        public async Task<long> ExecuteInsertData(string sql, params MySqlParameter[] args)
        {
            long result = 0;
            using (var connection = new MySqlConnection(GetConnectionString()))
            {
                connection.Open();
                using (var cmd = new MySqlCommand(sql, connection))
                {
                    foreach (var arg in args)
                    {
                        cmd.Parameters.Add(arg);
                    }
                    var task = await cmd.ExecuteNonQueryAsync();
                    result = cmd.LastInsertedId;
                }
                connection.Close();
            }
            return result;
        }

        public async Task ExecuteNonQuery(string sql, params MySqlParameter[] args)
        {
            using (var connection = new MySqlConnection(GetConnectionString()))
            {
                connection.Open();
                using (var cmd = new MySqlCommand(sql, connection))
                {
                    foreach (var arg in args)
                    {
                        cmd.Parameters.Add(arg);
                    }
                    var task = await cmd.ExecuteNonQueryAsync();
                }
                connection.Close();
            }
        }

        public async Task<object> ExecuteScalar(string sql, params MySqlParameter[] args)
        {
            object result;
            using (var connection = new MySqlConnection(GetConnectionString()))
            {
                connection.Open();
                using (var cmd = new MySqlCommand(sql, connection))
                {
                    foreach (var arg in args)
                    {
                        cmd.Parameters.Add(arg);
                    }
                    result = await cmd.ExecuteScalarAsync();
                }
                connection.Close();
            }
            return result;
        }

        public async Task<MySQLRowsReader> ExecuteReader(string sql, params MySqlParameter[] args)
        {
            MySQLRowsReader result = new MySQLRowsReader();
            using (var connection = new MySqlConnection(GetConnectionString()))
            {
                connection.Open();
                using (var cmd = new MySqlCommand(sql, connection))
                {
                    foreach (var arg in args)
                    {
                        cmd.Parameters.Add(arg);
                    }
                    result.Init(await cmd.ExecuteReaderAsync());
                }
                connection.Close();
            }
            return result;
        }

        public override async Task<string> ValidateUserLogin(string username, string password)
        {
            var id = string.Empty;
            var reader = await ExecuteReader("SELECT id FROM userLogin WHERE username=@username AND password=@password LIMIT 1",
                new MySqlParameter("@username", username),
                new MySqlParameter("@password", password));

            if (reader.Read())
                id = reader.GetString("id");

            return id;
        }

        public override async Task CreateUserLogin(string username, string password)
        {
            await ExecuteNonQuery("INSERT INTO userLogin (id, username, password) VALUES (@id, @username, @password)",
                new MySqlParameter("@id", System.Guid.NewGuid().ToString()),
                new MySqlParameter("@username", username),
                new MySqlParameter("@password", password));
        }

        public override async Task<long> FindUsername(string username)
        {
            var result = await ExecuteScalar("SELECT COUNT(*) FROM userLogin WHERE username LIKE @username", 
                new MySqlParameter("@username", username));
            return result != null ? (long)result : 0;
        }
    }
}
