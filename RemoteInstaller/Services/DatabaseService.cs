using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using RemoteInstaller.Models;

namespace RemoteInstaller.Services;

/// <summary>
/// 数据库服务
/// </summary>
public class DatabaseService : IDisposable
{
    private readonly string _connectionString;
    private bool _disposed;

    public DatabaseService(string? databasePath = null, string? legacyDatabasePath = null)
    {
        var resolvedDatabasePath = ResolveDatabasePath(databasePath, legacyDatabasePath);
        _connectionString = BuildConnectionString(resolvedDatabasePath);
        EnsureDatabaseDirectoryExists(resolvedDatabasePath);
        InitializeDatabase();
    }

    private static string ResolveDatabasePath(string? databasePath, string? legacyDatabasePath)
    {
        var resolvedDatabasePath = string.IsNullOrWhiteSpace(databasePath)
            ? GetDefaultDatabasePath()
            : databasePath;

        var resolvedLegacyDatabasePath = string.IsNullOrWhiteSpace(legacyDatabasePath)
            ? (string.IsNullOrWhiteSpace(databasePath) ? GetLegacyDatabasePath() : null)
            : legacyDatabasePath;

        EnsureDatabaseDirectoryExists(resolvedDatabasePath);

        if (File.Exists(resolvedDatabasePath))
        {
            return resolvedDatabasePath;
        }

        if (string.IsNullOrWhiteSpace(resolvedLegacyDatabasePath) ||
            string.Equals(resolvedDatabasePath, resolvedLegacyDatabasePath, StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(resolvedLegacyDatabasePath))
        {
            return resolvedDatabasePath;
        }

        try
        {
            CopyDatabaseBundle(resolvedLegacyDatabasePath, resolvedDatabasePath);
            return resolvedDatabasePath;
        }
        catch
        {
            return resolvedLegacyDatabasePath;
        }
    }

    private static string GetDefaultDatabasePath()
    {
        var appDataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RemoteInstaller");
        return Path.Combine(appDataDirectory, "data.db");
    }

    private static string GetLegacyDatabasePath() => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data.db");

    private static string BuildConnectionString(string databasePath) =>
        $"Data Source={databasePath};Version=3;Pooling=true;MinPoolSize=3;MaxPoolSize=20;";

    private static void EnsureDatabaseDirectoryExists(string databasePath)
    {
        var directory = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    private static void CopyDatabaseBundle(string sourceDatabasePath, string targetDatabasePath)
    {
        try
        {
            CopyDatabaseFile(sourceDatabasePath, targetDatabasePath);
            CopyDatabaseFile(sourceDatabasePath + "-wal", targetDatabasePath + "-wal");
            CopyDatabaseFile(sourceDatabasePath + "-shm", targetDatabasePath + "-shm");
        }
        catch
        {
            DeleteDatabaseBundle(targetDatabasePath);
            throw;
        }
    }

    private static void CopyDatabaseFile(string sourcePath, string targetPath)
    {
        if (!File.Exists(sourcePath))
        {
            return;
        }

        File.Copy(sourcePath, targetPath, overwrite: false);
    }

    private static void DeleteDatabaseBundle(string databasePath)
    {
        DeleteFileIfExists(databasePath);
        DeleteFileIfExists(databasePath + "-wal");
        DeleteFileIfExists(databasePath + "-shm");
    }

    private static void DeleteFileIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// 初始化数据库
    /// </summary>
    private void InitializeDatabase()
    {
        using var connection = new SQLiteConnection(_connectionString);
        connection.Open();

        var createHostsTable = @"
            CREATE TABLE IF NOT EXISTS hosts (
                id TEXT PRIMARY KEY,
                name TEXT NOT NULL,
                ip_address TEXT NOT NULL,
                port INTEGER NOT NULL,
                username TEXT NOT NULL,
                encrypted_password TEXT,
                auth_type INTEGER NOT NULL,
                key_path TEXT,
                encrypted_key_passphrase TEXT,
                os_type INTEGER NOT NULL,
                os_version TEXT,
                cpu_architecture TEXT,
                group_name TEXT,
                status INTEGER NOT NULL,
                status_message TEXT,
                last_connected DATETIME,
                created_at DATETIME NOT NULL,
                updated_at DATETIME NOT NULL
            )";

        var createTasksTable = @"
            CREATE TABLE IF NOT EXISTS tasks (
                id TEXT PRIMARY KEY,
                host_id TEXT NOT NULL,
                host_name TEXT NOT NULL,
                app_id TEXT NOT NULL,
                app_name TEXT NOT NULL,
                app_version TEXT NOT NULL,
                status INTEGER NOT NULL,
                stage INTEGER NOT NULL,
                progress REAL NOT NULL,
                error_message TEXT,
                start_time DATETIME,
                end_time DATETIME,
                FOREIGN KEY (host_id) REFERENCES hosts(id)
            )";

        var createSettingsTable = @"
            CREATE TABLE IF NOT EXISTS settings (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL
            )";

        var createLogsTable = @"
            CREATE TABLE IF NOT EXISTS logs (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                task_id TEXT,
                timestamp DATETIME NOT NULL,
                level INTEGER NOT NULL,
                message TEXT NOT NULL,
                FOREIGN KEY (task_id) REFERENCES tasks(id)
            )";

        // P1 功能：任务历史记录表
        var createInstallHistoryTable = @"
            CREATE TABLE IF NOT EXISTS install_history (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                host_id TEXT NOT NULL,
                host_name TEXT NOT NULL,
                application_id TEXT NOT NULL,
                application_name TEXT NOT NULL,
                application_version TEXT NOT NULL,
                operation_type INTEGER NOT NULL,
                status INTEGER NOT NULL,
                start_time DATETIME NOT NULL,
                end_time DATETIME,
                error_message TEXT,
                log_content TEXT,
                FOREIGN KEY (host_id) REFERENCES hosts(id)
            )";

        var createCustomAppsTable = @"
            CREATE TABLE IF NOT EXISTS custom_apps (
                id TEXT PRIMARY KEY,
                app_key TEXT NOT NULL UNIQUE,
                name TEXT NOT NULL,
                icon TEXT,
                description TEXT,
                app_type TEXT NOT NULL,
                remote_directory TEXT,
                start_command TEXT,
                stop_command TEXT,
                config_file_path TEXT,
                remote_frontend_directory TEXT,
                pid_file_path TEXT,
                config_directory TEXT,
                config_file_name TEXT,
                log_directory TEXT,
                is_builtin INTEGER NOT NULL DEFAULT 0,
                is_enabled INTEGER NOT NULL DEFAULT 1,
                sort_order INTEGER NOT NULL DEFAULT 0,
                created_at DATETIME NOT NULL,
                updated_at DATETIME NOT NULL
            )";

        using var cmd = connection.CreateCommand();
        cmd.CommandText = createHostsTable;
        cmd.ExecuteNonQuery();

        cmd.CommandText = createTasksTable;
        cmd.ExecuteNonQuery();

        cmd.CommandText = createSettingsTable;
        cmd.ExecuteNonQuery();

        cmd.CommandText = createLogsTable;
        cmd.ExecuteNonQuery();

        cmd.CommandText = createInstallHistoryTable;
        cmd.ExecuteNonQuery();

        cmd.CommandText = createCustomAppsTable;
        cmd.ExecuteNonQuery();

        EnsureHostColumns(connection);
        EnsureCustomAppsColumns(connection);
        SeedBuiltInCustomApps(connection);
        CleanupDeprecatedBuiltInCustomApps(connection);
    }

    /// <summary>
    /// 保存主机
    /// </summary>
    public void SaveHost(RemoteHost host)
    {
        try
        {
            using var connection = new SQLiteConnection(_connectionString);
            connection.Open();

            var sql = @"
                INSERT OR REPLACE INTO hosts
                (id, name, ip_address, port, username, encrypted_password, auth_type,
                 key_path, encrypted_key_passphrase, os_type, os_version, cpu_architecture, group_name, status,
                 status_message, last_connected, created_at, updated_at)
                VALUES
                (@id, @name, @ip_address, @port, @username, @encrypted_password, @auth_type,
                 @key_path, @encrypted_key_passphrase, @os_type, @os_version, @cpu_architecture, @group_name, @status,
                 @status_message, @last_connected, @created_at, @updated_at)";

            using var cmd = connection.CreateCommand();
            cmd.CommandText = sql;

            // 如果 ID 为空，生成新 ID
            if (string.IsNullOrEmpty(host.Id))
            {
                host.Id = Guid.NewGuid().ToString("N");
            }

            cmd.Parameters.AddWithValue("@id", host.Id);
            cmd.Parameters.AddWithValue("@name", host.Name);
            cmd.Parameters.AddWithValue("@ip_address", host.IpAddress);
            cmd.Parameters.AddWithValue("@port", host.Port);
            cmd.Parameters.AddWithValue("@username", host.Username);
            cmd.Parameters.AddWithValue("@encrypted_password", host.EncryptedPassword ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@auth_type", (int)host.AuthType);
            cmd.Parameters.AddWithValue("@key_path", host.KeyPath ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@encrypted_key_passphrase", host.EncryptedKeyPassphrase ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@os_type", (int)host.OsType);
            cmd.Parameters.AddWithValue("@os_version", string.IsNullOrWhiteSpace(host.OsVersion) ? (object)DBNull.Value : host.OsVersion);
            cmd.Parameters.AddWithValue("@cpu_architecture", string.IsNullOrWhiteSpace(host.CpuArchitecture) ? (object)DBNull.Value : host.CpuArchitecture);
            cmd.Parameters.AddWithValue("@group_name", host.GroupName ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@status", (int)host.Status);
            cmd.Parameters.AddWithValue("@status_message", host.StatusMessage ?? (object)DBNull.Value);

            // 修复 LastConnected 可能为默认值的问题
            if (host.LastConnected == DateTime.MinValue)
            {
                cmd.Parameters.AddWithValue("@last_connected", (object)DBNull.Value);
            }
            else
            {
                cmd.Parameters.AddWithValue("@last_connected", host.LastConnected.ToString("yyyy-MM-dd HH:mm:ss"));
            }

            cmd.Parameters.AddWithValue("@created_at", host.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.Parameters.AddWithValue("@updated_at", host.UpdatedAt.ToString("yyyy-MM-dd HH:mm:ss"));

            cmd.ExecuteNonQuery();
        }
        catch
        {
            throw;
        }
    }

    /// <summary>
    /// 获取所有主机
    /// </summary>
    public List<RemoteHost> GetAllHosts()
    {
        var hosts = new List<RemoteHost>();

        using var connection = new SQLiteConnection(_connectionString);
        connection.Open();

        var sql = "SELECT * FROM hosts ORDER BY group_name, name";
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var host = DatabaseServiceExtensions.MapToHost(reader);
            hosts.Add(host);
        }

        return hosts;
    }

    /// <summary>
    /// 根据 ID 获取主机
    /// </summary>
    public RemoteHost? GetHostById(string id)
    {
        using var connection = new SQLiteConnection(_connectionString);
        connection.Open();

        var sql = "SELECT * FROM hosts WHERE id = @id";
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@id", id);

        using var reader = cmd.ExecuteReader();
        if (reader.Read())
        {
            return DatabaseServiceExtensions.MapToHost(reader);
        }

        return null;
    }

    /// <summary>
    /// 删除主机
    /// </summary>
    public void DeleteHost(string id)
    {
        using var connection = new SQLiteConnection(_connectionString);
        connection.Open();

        using (var pragmaCmd = connection.CreateCommand())
        {
            pragmaCmd.CommandText = "PRAGMA foreign_keys = ON";
            pragmaCmd.ExecuteNonQuery();
        }

        using var transaction = connection.BeginTransaction();
        try
        {
            using (var deleteLogsCmd = connection.CreateCommand())
            {
                deleteLogsCmd.Transaction = transaction;
                deleteLogsCmd.CommandText = "DELETE FROM logs WHERE task_id IN (SELECT id FROM tasks WHERE host_id = @host_id)";
                deleteLogsCmd.Parameters.AddWithValue("@host_id", id);
                deleteLogsCmd.ExecuteNonQuery();
            }

            using (var deleteTasksCmd = connection.CreateCommand())
            {
                deleteTasksCmd.Transaction = transaction;
                deleteTasksCmd.CommandText = "DELETE FROM tasks WHERE host_id = @host_id";
                deleteTasksCmd.Parameters.AddWithValue("@host_id", id);
                deleteTasksCmd.ExecuteNonQuery();
            }

            using (var deleteHistoryCmd = connection.CreateCommand())
            {
                deleteHistoryCmd.Transaction = transaction;
                deleteHistoryCmd.CommandText = "DELETE FROM install_history WHERE host_id = @host_id";
                deleteHistoryCmd.Parameters.AddWithValue("@host_id", id);
                deleteHistoryCmd.ExecuteNonQuery();
            }

            using (var deleteHostCmd = connection.CreateCommand())
            {
                deleteHostCmd.Transaction = transaction;
                deleteHostCmd.CommandText = "DELETE FROM hosts WHERE id = @host_id";
                deleteHostCmd.Parameters.AddWithValue("@host_id", id);
                deleteHostCmd.ExecuteNonQuery();
            }

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    private static string DateTimeToDbString(DateTime value) => value.ToString("yyyy-MM-dd HH:mm:ss");

    private void EnsureHostColumns(SQLiteConnection connection)
    {
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "PRAGMA table_info(hosts)";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                columns.Add(reader["name"]?.ToString() ?? string.Empty);
            }
        }

        EnsureHostColumn(connection, columns, "os_version", "TEXT");
        EnsureHostColumn(connection, columns, "cpu_architecture", "TEXT");
    }

    private static void EnsureHostColumn(SQLiteConnection connection, HashSet<string> columns, string columnName, string columnType)
    {
        if (columns.Contains(columnName))
        {
            return;
        }

        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"ALTER TABLE hosts ADD COLUMN {columnName} {columnType}";
        cmd.ExecuteNonQuery();
        columns.Add(columnName);
    }

    private void EnsureCustomAppsColumns(SQLiteConnection connection)
    {
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "PRAGMA table_info(custom_apps)";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                columns.Add(reader["name"]?.ToString() ?? string.Empty);
            }
        }

        EnsureCustomAppsColumn(connection, columns, "remote_frontend_directory", "TEXT");
        EnsureCustomAppsColumn(connection, columns, "pid_file_path", "TEXT");
        EnsureCustomAppsColumn(connection, columns, "config_directory", "TEXT");
        EnsureCustomAppsColumn(connection, columns, "config_file_name", "TEXT");
        EnsureCustomAppsColumn(connection, columns, "log_directory", "TEXT");
    }

    private static void EnsureCustomAppsColumn(SQLiteConnection connection, HashSet<string> columns, string columnName, string columnType)
    {
        if (columns.Contains(columnName))
        {
            return;
        }

        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"ALTER TABLE custom_apps ADD COLUMN {columnName} {columnType}";
        cmd.ExecuteNonQuery();
        columns.Add(columnName);
    }

    private void SeedBuiltInCustomApps(SQLiteConnection connection)
    {
        var builtIns = new List<CustomAppDefinition>
        {
            new()
            {
                AppKey = "SUPPORT",
                Name = "Support",
                Icon = "🛠️",
                Description = "Support 运维支持平台",
                AppType = "SUPPORT",
                RemoteDirectory = "/opt/zeus-support",
                StartCommand = "cd /opt/zeus-support && bash start.sh",
                StopCommand = "cd /opt/zeus-support && bash stop.sh",
                ConfigFilePath = "/opt/zeus-support/conf/application-prod.properties",
                RemoteFrontendDirectory = "/var/www/zeus-support",
                PidFilePath = "/opt/zeus-support/run/run.PID",
                ConfigDirectory = "/opt/zeus-support/conf",
                ConfigFileName = "application-prod.properties",
                LogDirectory = "/opt/zeus-support/log",
                IsBuiltIn = true,
                SortOrder = 10
            },
            new()
            {
                AppKey = "XXL_JOB",
                Name = "XXL-JOB",
                Icon = "⏱️",
                Description = "XXL-JOB 分布式任务调度中心",
                AppType = "SUPPORT",
                RemoteDirectory = "/opt/xxl-job",
                StartCommand = "cd /opt/xxl-job && bash start.sh",
                StopCommand = "cd /opt/xxl-job && bash stop.sh",
                ConfigFilePath = "/opt/xxl-job/conf/application-prod.properties",
                RemoteFrontendDirectory = "/var/www/xxl-job",
                PidFilePath = "/opt/xxl-job/run/run.PID",
                ConfigDirectory = "/opt/xxl-job/conf",
                ConfigFileName = "application-prod.properties",
                LogDirectory = "/opt/xxl-job/log",
                IsBuiltIn = true,
                SortOrder = 20
            },
            new()
            {
                AppKey = "NACOS",
                Name = "Nacos",
                Icon = "🧭",
                Description = "Nacos 配置与注册中心",
                AppType = "SUPPORT",
                RemoteDirectory = "/opt/nacos",
                StartCommand = "cd /opt/nacos && bash start.sh",
                StopCommand = "cd /opt/nacos && bash stop.sh",
                ConfigFilePath = "/opt/nacos/conf/application-prod.properties",
                RemoteFrontendDirectory = "/var/www/nacos",
                PidFilePath = "/opt/nacos/run/run.PID",
                ConfigDirectory = "/opt/nacos/conf",
                ConfigFileName = "application-prod.properties",
                LogDirectory = "/opt/nacos/log",
                IsBuiltIn = true,
                SortOrder = 30
            }
        };

        foreach (var app in builtIns)
        {
            app.Id = Guid.NewGuid().ToString("N");
            app.CreatedAt = DateTime.Now;
            app.UpdatedAt = DateTime.Now;

            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                INSERT OR IGNORE INTO custom_apps
                (id, app_key, name, icon, description, app_type, remote_directory, start_command, stop_command,
                 config_file_path, remote_frontend_directory, pid_file_path, config_directory, config_file_name, log_directory,
                 is_builtin, is_enabled, sort_order, created_at, updated_at)
                VALUES
                (@id, @app_key, @name, @icon, @description, @app_type, @remote_directory, @start_command, @stop_command,
                 @config_file_path, @remote_frontend_directory, @pid_file_path, @config_directory, @config_file_name, @log_directory,
                 @is_builtin, @is_enabled, @sort_order, @created_at, @updated_at)";

            cmd.Parameters.AddWithValue("@id", app.Id);
            cmd.Parameters.AddWithValue("@app_key", app.AppKey);
            cmd.Parameters.AddWithValue("@name", app.Name);
            cmd.Parameters.AddWithValue("@icon", app.Icon);
            cmd.Parameters.AddWithValue("@description", app.Description);
            cmd.Parameters.AddWithValue("@app_type", app.AppType);
            cmd.Parameters.AddWithValue("@remote_directory", string.IsNullOrWhiteSpace(app.RemoteDirectory) ? (object)DBNull.Value : app.RemoteDirectory);
            cmd.Parameters.AddWithValue("@start_command", string.IsNullOrWhiteSpace(app.StartCommand) ? (object)DBNull.Value : app.StartCommand);
            cmd.Parameters.AddWithValue("@stop_command", string.IsNullOrWhiteSpace(app.StopCommand) ? (object)DBNull.Value : app.StopCommand);
            cmd.Parameters.AddWithValue("@config_file_path", string.IsNullOrWhiteSpace(app.ConfigFilePath) ? (object)DBNull.Value : app.ConfigFilePath);
            cmd.Parameters.AddWithValue("@remote_frontend_directory", string.IsNullOrWhiteSpace(app.RemoteFrontendDirectory) ? (object)DBNull.Value : app.RemoteFrontendDirectory);
            cmd.Parameters.AddWithValue("@pid_file_path", string.IsNullOrWhiteSpace(app.PidFilePath) ? (object)DBNull.Value : app.PidFilePath);
            cmd.Parameters.AddWithValue("@config_directory", string.IsNullOrWhiteSpace(app.ConfigDirectory) ? (object)DBNull.Value : app.ConfigDirectory);
            cmd.Parameters.AddWithValue("@config_file_name", string.IsNullOrWhiteSpace(app.ConfigFileName) ? (object)DBNull.Value : app.ConfigFileName);
            cmd.Parameters.AddWithValue("@log_directory", string.IsNullOrWhiteSpace(app.LogDirectory) ? (object)DBNull.Value : app.LogDirectory);
            cmd.Parameters.AddWithValue("@is_builtin", app.IsBuiltIn ? 1 : 0);
            cmd.Parameters.AddWithValue("@is_enabled", app.IsEnabled ? 1 : 0);
            cmd.Parameters.AddWithValue("@sort_order", app.SortOrder);
            cmd.Parameters.AddWithValue("@created_at", DateTimeToDbString(app.CreatedAt));
            cmd.Parameters.AddWithValue("@updated_at", DateTimeToDbString(app.UpdatedAt));
            cmd.ExecuteNonQuery();
        }
    }

    private static void CleanupDeprecatedBuiltInCustomApps(SQLiteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            DELETE FROM custom_apps
            WHERE is_builtin = 1
              AND app_key IN ('WMS', 'WCS', 'FMS')";
        cmd.ExecuteNonQuery();
    }

    public List<CustomAppDefinition> GetAllCustomApps(bool includeDisabled = false)
    {
        var result = new List<CustomAppDefinition>();

        using var connection = new SQLiteConnection(_connectionString);
        connection.Open();

        var sql = includeDisabled
            ? "SELECT * FROM custom_apps ORDER BY sort_order, name"
            : "SELECT * FROM custom_apps WHERE is_enabled = 1 ORDER BY sort_order, name";

        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var app = new CustomAppDefinition
            {
                Id = reader["id"]?.ToString() ?? string.Empty,
                AppKey = reader["app_key"]?.ToString() ?? string.Empty,
                Name = reader["name"]?.ToString() ?? string.Empty,
                Icon = reader["icon"]?.ToString() ?? "📦",
                Description = reader["description"]?.ToString() ?? string.Empty,
                AppType = reader["app_type"]?.ToString() ?? "GENERIC",
                RemoteDirectory = reader["remote_directory"]?.ToString() ?? string.Empty,
                StartCommand = reader["start_command"]?.ToString() ?? string.Empty,
                StopCommand = reader["stop_command"]?.ToString() ?? string.Empty,
                ConfigFilePath = reader["config_file_path"]?.ToString() ?? string.Empty,
                RemoteFrontendDirectory = reader["remote_frontend_directory"]?.ToString() ?? string.Empty,
                PidFilePath = reader["pid_file_path"]?.ToString() ?? string.Empty,
                ConfigDirectory = reader["config_directory"]?.ToString() ?? string.Empty,
                ConfigFileName = reader["config_file_name"]?.ToString() ?? string.Empty,
                LogDirectory = reader["log_directory"]?.ToString() ?? string.Empty,
                IsBuiltIn = Convert.ToInt32(reader["is_builtin"]) == 1,
                IsEnabled = Convert.ToInt32(reader["is_enabled"]) == 1,
                SortOrder = Convert.ToInt32(reader["sort_order"]),
                CreatedAt = DateTime.TryParse(reader["created_at"]?.ToString(), out var createdAt) ? createdAt : DateTime.Now,
                UpdatedAt = DateTime.TryParse(reader["updated_at"]?.ToString(), out var updatedAt) ? updatedAt : DateTime.Now
            };
            result.Add(app);
        }

        return result;
    }

    public void SaveCustomApp(CustomAppDefinition app)
    {
        if (string.IsNullOrWhiteSpace(app.Id))
        {
            app.Id = Guid.NewGuid().ToString("N");
        }

        if (string.IsNullOrWhiteSpace(app.AppKey))
        {
            app.AppKey = app.Id;
        }

        if (app.CreatedAt == default)
        {
            app.CreatedAt = DateTime.Now;
        }

        app.UpdatedAt = DateTime.Now;

        using var connection = new SQLiteConnection(_connectionString);
        connection.Open();

        var sql = @"
            INSERT OR REPLACE INTO custom_apps
            (id, app_key, name, icon, description, app_type, remote_directory, start_command, stop_command,
             config_file_path, remote_frontend_directory, pid_file_path, config_directory, config_file_name, log_directory,
             is_builtin, is_enabled, sort_order, created_at, updated_at)
            VALUES
            (@id, @app_key, @name, @icon, @description, @app_type, @remote_directory, @start_command, @stop_command,
             @config_file_path, @remote_frontend_directory, @pid_file_path, @config_directory, @config_file_name, @log_directory,
             @is_builtin, @is_enabled, @sort_order, @created_at, @updated_at)";

        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@id", app.Id);
        cmd.Parameters.AddWithValue("@app_key", app.AppKey);
        cmd.Parameters.AddWithValue("@name", app.Name);
        cmd.Parameters.AddWithValue("@icon", app.Icon);
        cmd.Parameters.AddWithValue("@description", app.Description);
        cmd.Parameters.AddWithValue("@app_type", app.AppType);
        cmd.Parameters.AddWithValue("@remote_directory", string.IsNullOrWhiteSpace(app.RemoteDirectory) ? (object)DBNull.Value : app.RemoteDirectory);
        cmd.Parameters.AddWithValue("@start_command", string.IsNullOrWhiteSpace(app.StartCommand) ? (object)DBNull.Value : app.StartCommand);
        cmd.Parameters.AddWithValue("@stop_command", string.IsNullOrWhiteSpace(app.StopCommand) ? (object)DBNull.Value : app.StopCommand);
        cmd.Parameters.AddWithValue("@config_file_path", string.IsNullOrWhiteSpace(app.ConfigFilePath) ? (object)DBNull.Value : app.ConfigFilePath);
        cmd.Parameters.AddWithValue("@remote_frontend_directory", string.IsNullOrWhiteSpace(app.RemoteFrontendDirectory) ? (object)DBNull.Value : app.RemoteFrontendDirectory);
        cmd.Parameters.AddWithValue("@pid_file_path", string.IsNullOrWhiteSpace(app.PidFilePath) ? (object)DBNull.Value : app.PidFilePath);
        cmd.Parameters.AddWithValue("@config_directory", string.IsNullOrWhiteSpace(app.ConfigDirectory) ? (object)DBNull.Value : app.ConfigDirectory);
        cmd.Parameters.AddWithValue("@config_file_name", string.IsNullOrWhiteSpace(app.ConfigFileName) ? (object)DBNull.Value : app.ConfigFileName);
        cmd.Parameters.AddWithValue("@log_directory", string.IsNullOrWhiteSpace(app.LogDirectory) ? (object)DBNull.Value : app.LogDirectory);
        cmd.Parameters.AddWithValue("@is_builtin", app.IsBuiltIn ? 1 : 0);
        cmd.Parameters.AddWithValue("@is_enabled", app.IsEnabled ? 1 : 0);
        cmd.Parameters.AddWithValue("@sort_order", app.SortOrder);
        cmd.Parameters.AddWithValue("@created_at", DateTimeToDbString(app.CreatedAt));
        cmd.Parameters.AddWithValue("@updated_at", DateTimeToDbString(app.UpdatedAt));
        cmd.ExecuteNonQuery();
    }

    public void DeleteCustomApp(string id)
    {
        using var connection = new SQLiteConnection(_connectionString);
        connection.Open();

        var sql = "DELETE FROM custom_apps WHERE id = @id AND is_builtin = 0";
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// 保存任务
    /// </summary>
    public void SaveTask(InstallTask task)
    {
        using var connection = new SQLiteConnection(_connectionString);
        connection.Open();

        var sql = @"
            INSERT OR REPLACE INTO tasks 
            (id, host_id, host_name, app_id, app_name, app_version, status, stage, 
             progress, error_message, start_time, end_time)
            VALUES 
            (@id, @host_id, @host_name, @app_id, @app_name, @app_version, @status, @stage, 
             @progress, @error_message, @start_time, @end_time)";

        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@id", task.Id);
        cmd.Parameters.AddWithValue("@host_id", task.HostId);
        cmd.Parameters.AddWithValue("@host_name", task.HostName);
        cmd.Parameters.AddWithValue("@app_id", task.AppId);
        cmd.Parameters.AddWithValue("@app_name", task.AppName);
        cmd.Parameters.AddWithValue("@app_version", task.AppVersion);
        cmd.Parameters.AddWithValue("@status", (int)task.Status);
        cmd.Parameters.AddWithValue("@stage", (int)task.Stage);
        cmd.Parameters.AddWithValue("@progress", task.Progress);
        cmd.Parameters.AddWithValue("@error_message", task.ErrorMessage ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@start_time", task.StartTime.ToString("yyyy-MM-dd HH:mm:ss"));
        cmd.Parameters.AddWithValue("@end_time", task.EndTime.ToString("yyyy-MM-dd HH:mm:ss"));

        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// 获取所有任务
    /// </summary>
    public List<InstallTask> GetAllTasks()
    {
        var tasks = new List<InstallTask>();

        using var connection = new SQLiteConnection(_connectionString);
        connection.Open();

        var sql = "SELECT * FROM tasks ORDER BY start_time DESC LIMIT 100";
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            tasks.Add(DatabaseServiceExtensions.MapToTask(reader));
        }

        return tasks;
    }

    /// <summary>
    /// 获取设置
    /// </summary>
    public string? GetSetting(string key, string? defaultValue = null)
    {
        using var connection = new SQLiteConnection(_connectionString);
        connection.Open();

        var sql = "SELECT value FROM settings WHERE key = @key";
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@key", key);

        var result = cmd.ExecuteScalar();
        return result?.ToString() ?? defaultValue;
    }

    /// <summary>
    /// 保存设置
    /// </summary>
    public void SaveSetting(string key, string value)
    {
        using var connection = new SQLiteConnection(_connectionString);
        connection.Open();

        var sql = "INSERT OR REPLACE INTO settings (key, value) VALUES (@key, @value)";
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@key", key);
        cmd.Parameters.AddWithValue("@value", value);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// 保存任务日志（批量插入）
    /// </summary>
    public void SaveTaskLogs(string taskId, List<LogEntry> logs)
    {
        if (logs == null || logs.Count == 0) return;

        using var connection = new SQLiteConnection(_connectionString);
        connection.Open();

        var sql = @"
            INSERT INTO logs (task_id, timestamp, level, message)
            VALUES (@task_id, @timestamp, @level, @message)";

        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@task_id", taskId);
        cmd.Parameters.Add("@timestamp", System.Data.DbType.DateTime);
        cmd.Parameters.Add("@level", System.Data.DbType.Int32);
        cmd.Parameters.Add("@message", System.Data.DbType.String);

        using var transaction = connection.BeginTransaction();
        cmd.Transaction = transaction;

        try
        {
            foreach (var log in logs)
            {
                cmd.Parameters["@timestamp"].Value = log.Timestamp.ToString("yyyy-MM-dd HH:mm:ss");
                cmd.Parameters["@level"].Value = (int)log.Level;
                cmd.Parameters["@message"].Value = log.Message;
                cmd.ExecuteNonQuery();
            }
            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    /// <summary>
    /// 获取任务日志
    /// </summary>
    public List<LogEntry> GetTaskLogs(string taskId, int? limit = null)
    {
        var logs = new List<LogEntry>();

        using var connection = new SQLiteConnection(_connectionString);
        connection.Open();

        var sql = limit.HasValue
            ? "SELECT * FROM logs WHERE task_id = @task_id ORDER BY timestamp DESC LIMIT @limit"
            : "SELECT * FROM logs WHERE task_id = @task_id ORDER BY timestamp DESC";

        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@task_id", taskId);
        if (limit.HasValue)
        {
            cmd.Parameters.AddWithValue("@limit", limit.Value);
        }

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            logs.Add(MapToLogEntry(reader));
        }

        // 按时间正序返回
        logs.Reverse();
        return logs;
    }

    /// <summary>
    /// 获取任务日志（按级别过滤）
    /// </summary>
    public List<LogEntry> GetTaskLogsByLevel(string taskId, LogLevel level, int? limit = null)
    {
        var logs = new List<LogEntry>();

        using var connection = new SQLiteConnection(_connectionString);
        connection.Open();

        var sql = limit.HasValue
            ? "SELECT * FROM logs WHERE task_id = @task_id AND level = @level ORDER BY timestamp DESC LIMIT @limit"
            : "SELECT * FROM logs WHERE task_id = @task_id AND level = @level ORDER BY timestamp DESC";

        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@task_id", taskId);
        cmd.Parameters.AddWithValue("@level", (int)level);
        if (limit.HasValue)
        {
            cmd.Parameters.AddWithValue("@limit", limit.Value);
        }

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            logs.Add(MapToLogEntry(reader));
        }

        logs.Reverse();
        return logs;
    }

    /// <summary>
    /// 删除任务日志
    /// </summary>
    public void DeleteTaskLogs(string taskId)
    {
        using var connection = new SQLiteConnection(_connectionString);
        connection.Open();

        var sql = "DELETE FROM logs WHERE task_id = @task_id";
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@task_id", taskId);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// 清理旧日志（保留最近 N 天的日志）
    /// </summary>
    public void CleanupOldLogs(int daysToKeep = 30)
    {
        using var connection = new SQLiteConnection(_connectionString);
        connection.Open();

        var cutoffDate = DateTime.Now.AddDays(-daysToKeep);
        var sql = "DELETE FROM logs WHERE timestamp < @cutoff_date";
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@cutoff_date", cutoffDate.ToString("yyyy-MM-dd HH:mm:ss"));
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// 获取所有任务的日志摘要
    /// </summary>
    public List<LogSummary> GetLogSummaries(int limit = 100)
    {
        var summaries = new List<LogSummary>();

        using var connection = new SQLiteConnection(_connectionString);
        connection.Open();

        var sql = @"
            SELECT task_id, 
                   MIN(timestamp) as first_log,
                   MAX(timestamp) as last_log,
                   COUNT(*) as total_count,
                   SUM(CASE WHEN level = 0 THEN 1 ELSE 0 END) as info_count,
                   SUM(CASE WHEN level = 1 THEN 1 ELSE 0 END) as success_count,
                   SUM(CASE WHEN level = 2 THEN 1 ELSE 0 END) as warning_count,
                   SUM(CASE WHEN level = 3 THEN 1 ELSE 0 END) as error_count
            FROM logs
            GROUP BY task_id
            ORDER BY last_log DESC
            LIMIT @limit";

        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@limit", limit);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            summaries.Add(new LogSummary
            {
                TaskId = reader["task_id"]?.ToString(),
                FirstLogTime = reader["first_log"] != null ? DateTime.Parse(reader["first_log"].ToString()) : DateTime.MinValue,
                LastLogTime = reader["last_log"] != null ? DateTime.Parse(reader["last_log"].ToString()) : DateTime.MinValue,
                TotalCount = Convert.ToInt32(reader["total_count"]),
                InfoCount = Convert.ToInt32(reader["info_count"] ?? 0),
                SuccessCount = Convert.ToInt32(reader["success_count"] ?? 0),
                WarningCount = Convert.ToInt32(reader["warning_count"] ?? 0),
                ErrorCount = Convert.ToInt32(reader["error_count"] ?? 0)
            });
        }

        return summaries;
    }

    /// <summary>
    /// 更新主机最后心跳时间
    /// </summary>
    public void UpdateHostHeartbeat(string hostId, DateTime heartbeatTime)
    {
        using var connection = new SQLiteConnection(_connectionString);
        connection.Open();

        var sql = "UPDATE hosts SET last_connected = @last_connected WHERE id = @id";
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@last_connected", heartbeatTime.ToString("yyyy-MM-dd HH:mm:ss"));
        cmd.Parameters.AddWithValue("@id", hostId);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// 获取离线主机（超过指定时间未心跳）
    /// </summary>
    public List<string> GetOfflineHostIds(int offlineThresholdMinutes = 5)
    {
        var offlineIds = new List<string>();

        using var connection = new SQLiteConnection(_connectionString);
        connection.Open();

        var cutoffTime = DateTime.Now.AddMinutes(-offlineThresholdMinutes);
        var sql = "SELECT id FROM hosts WHERE last_connected < @cutoff_time OR last_connected IS NULL";
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@cutoff_time", cutoffTime.ToString("yyyy-MM-dd HH:mm:ss"));

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            offlineIds.Add(reader["id"]?.ToString() ?? string.Empty);
        }

        return offlineIds;
    }

    #region P1 功能：任务历史记录

    /// <summary>
    /// 保存安装历史记录
    /// </summary>
    public void SaveInstallHistory(InstallHistory history)
    {
        using var connection = new SQLiteConnection(_connectionString);
        connection.Open();

        var sql = @"
            INSERT INTO install_history
            (host_id, host_name, application_id, application_name, application_version,
             operation_type, status, start_time, end_time, error_message, log_content)
            VALUES
            (@host_id, @host_name, @application_id, @application_name, @application_version,
             @operation_type, @status, @start_time, @end_time, @error_message, @log_content)";

        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@host_id", history.HostId);
        cmd.Parameters.AddWithValue("@host_name", history.HostName);
        cmd.Parameters.AddWithValue("@application_id", history.ApplicationId);
        cmd.Parameters.AddWithValue("@application_name", history.ApplicationName);
        cmd.Parameters.AddWithValue("@application_version", history.ApplicationVersion);
        cmd.Parameters.AddWithValue("@operation_type", (int)history.OperationType);
        cmd.Parameters.AddWithValue("@status", (int)history.Status);
        cmd.Parameters.AddWithValue("@start_time", history.StartTime.ToString("yyyy-MM-dd HH:mm:ss"));
        cmd.Parameters.AddWithValue("@end_time", history.EndTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@error_message", history.ErrorMessage ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@log_content", history.LogContent ?? (object)DBNull.Value);

        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// 更新安装历史记录
    /// </summary>
    public void UpdateInstallHistory(InstallHistory history)
    {
        using var connection = new SQLiteConnection(_connectionString);
        connection.Open();

        var sql = @"
            UPDATE install_history
            SET status = @status,
                end_time = @end_time,
                error_message = @error_message,
                log_content = @log_content
            WHERE id = @id";

        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@status", (int)history.Status);
        cmd.Parameters.AddWithValue("@end_time", history.EndTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@error_message", history.ErrorMessage ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@log_content", history.LogContent ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@id", history.Id);

        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// 获取所有安装历史记录
    /// </summary>
    /// <param name="limit">返回记录数限制，null 表示无限制</param>
    /// <param name="offset">偏移量</param>
    public List<InstallHistory> GetInstallHistory(int? limit = null, int offset = 0)
    {
        var histories = new List<InstallHistory>();

        using var connection = new SQLiteConnection(_connectionString);
        connection.Open();

        var sql = limit.HasValue
            ? "SELECT * FROM install_history ORDER BY start_time DESC LIMIT @limit OFFSET @offset"
            : "SELECT * FROM install_history ORDER BY start_time DESC OFFSET @offset";

        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        if (limit.HasValue)
        {
            cmd.Parameters.AddWithValue("@limit", limit.Value);
        }
        cmd.Parameters.AddWithValue("@offset", offset);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            histories.Add(MapToInstallHistory(reader));
        }

        return histories;
    }

    /// <summary>
    /// 根据主机 ID 获取安装历史记录
    /// </summary>
    public List<InstallHistory> GetInstallHistoryByHost(string hostId, int? limit = null)
    {
        var histories = new List<InstallHistory>();

        using var connection = new SQLiteConnection(_connectionString);
        connection.Open();

        var sql = limit.HasValue
            ? "SELECT * FROM install_history WHERE host_id = @host_id ORDER BY start_time DESC LIMIT @limit"
            : "SELECT * FROM install_history WHERE host_id = @host_id ORDER BY start_time DESC";

        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@host_id", hostId);
        if (limit.HasValue)
        {
            cmd.Parameters.AddWithValue("@limit", limit.Value);
        }

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            histories.Add(MapToInstallHistory(reader));
        }

        return histories;
    }

    /// <summary>
    /// 根据应用 ID 获取安装历史记录
    /// </summary>
    public List<InstallHistory> GetInstallHistoryByApplication(string applicationId, int? limit = null)
    {
        var histories = new List<InstallHistory>();

        using var connection = new SQLiteConnection(_connectionString);
        connection.Open();

        var sql = limit.HasValue
            ? "SELECT * FROM install_history WHERE application_id = @application_id ORDER BY start_time DESC LIMIT @limit"
            : "SELECT * FROM install_history WHERE application_id = @application_id ORDER BY start_time DESC";

        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@application_id", applicationId);
        if (limit.HasValue)
        {
            cmd.Parameters.AddWithValue("@limit", limit.Value);
        }

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            histories.Add(MapToInstallHistory(reader));
        }

        return histories;
    }

    /// <summary>
    /// 根据操作类型获取安装历史记录
    /// </summary>
    public List<InstallHistory> GetInstallHistoryByOperation(HistoryOperationType operationType, int? limit = null)
    {
        var histories = new List<InstallHistory>();

        using var connection = new SQLiteConnection(_connectionString);
        connection.Open();

        var sql = limit.HasValue
            ? "SELECT * FROM install_history WHERE operation_type = @operation_type ORDER BY start_time DESC LIMIT @limit"
            : "SELECT * FROM install_history WHERE operation_type = @operation_type ORDER BY start_time DESC";

        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@operation_type", (int)operationType);
        if (limit.HasValue)
        {
            cmd.Parameters.AddWithValue("@limit", limit.Value);
        }

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            histories.Add(MapToInstallHistory(reader));
        }

        return histories;
    }

    /// <summary>
    /// 根据状态获取安装历史记录
    /// </summary>
    public List<InstallHistory> GetInstallHistoryByStatus(RemoteInstaller.Models.TaskStatus status, int? limit = null)
    {
        var histories = new List<InstallHistory>();

        using var connection = new SQLiteConnection(_connectionString);
        connection.Open();

        var sql = limit.HasValue
            ? "SELECT * FROM install_history WHERE status = @status ORDER BY start_time DESC LIMIT @limit"
            : "SELECT * FROM install_history WHERE status = @status ORDER BY start_time DESC";

        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@status", (int)status);
        if (limit.HasValue)
        {
            cmd.Parameters.AddWithValue("@limit", limit.Value);
        }

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            histories.Add(MapToInstallHistory(reader));
        }

        return histories;
    }

    /// <summary>
    /// 删除安装历史记录
    /// </summary>
    public void DeleteInstallHistory(int historyId)
    {
        using var connection = new SQLiteConnection(_connectionString);
        connection.Open();

        var sql = "DELETE FROM install_history WHERE id = @id";
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@id", historyId);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// 批量删除安装历史记录
    /// </summary>
    public void DeleteInstallHistoryBatch(List<int> historyIds)
    {
        if (historyIds == null || historyIds.Count == 0) return;

        using var connection = new SQLiteConnection(_connectionString);
        connection.Open();

        // SQLite 不支持重复参数名，使用事务逐个删除
        using var transaction = connection.BeginTransaction();
        try
        {
            var sql = "DELETE FROM install_history WHERE id = @id";
            using var cmd = connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.Transaction = transaction;
            cmd.Parameters.AddWithValue("@id", 0);

            foreach (var id in historyIds)
            {
                cmd.Parameters["@id"].Value = id;
                cmd.ExecuteNonQuery();
            }
            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    /// <summary>
    /// 删除指定主机所有历史记录
    /// </summary>
    public void DeleteInstallHistoryByHost(string hostId)
    {
        using var connection = new SQLiteConnection(_connectionString);
        connection.Open();

        var sql = "DELETE FROM install_history WHERE host_id = @host_id";
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@host_id", hostId);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// 清理旧历史记录（保留最近 N 天的记录）
    /// </summary>
    public void CleanupOldHistory(int daysToKeep = 90)
    {
        using var connection = new SQLiteConnection(_connectionString);
        connection.Open();

        var cutoffDate = DateTime.Now.AddDays(-daysToKeep);
        var sql = "DELETE FROM install_history WHERE start_time < @cutoff_date";
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@cutoff_date", cutoffDate.ToString("yyyy-MM-dd HH:mm:ss"));
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// 获取历史记录统计信息
    /// </summary>
    public HistoryStatistics GetHistoryStatistics()
    {
        var stats = new HistoryStatistics();

        using var connection = new SQLiteConnection(_connectionString);
        connection.Open();

        // 总记录数
        var countSql = "SELECT COUNT(*) FROM install_history";
        using var countCmd = connection.CreateCommand();
        countCmd.CommandText = countSql;
        stats.TotalCount = Convert.ToInt32(countCmd.ExecuteScalar());

        // 按操作类型统计
        var opSql = "SELECT operation_type, COUNT(*) FROM install_history GROUP BY operation_type";
        using var opCmd = connection.CreateCommand();
        opCmd.CommandText = opSql;
        using var opReader = opCmd.ExecuteReader();
        while (opReader.Read())
        {
            var opType = (HistoryOperationType)Convert.ToInt32(opReader["operation_type"]);
            var count = Convert.ToInt32(opReader["COUNT(*)"]);
            if (opType == HistoryOperationType.Install)
            {
                stats.InstallCount = count;
            }
            else if (opType == HistoryOperationType.Uninstall)
            {
                stats.UninstallCount = count;
            }
        }

        // 按状态统计
        var statusSql = "SELECT status, COUNT(*) FROM install_history GROUP BY status";
        using var statusCmd = connection.CreateCommand();
        statusCmd.CommandText = statusSql;
        using var statusReader = statusCmd.ExecuteReader();
        while (statusReader.Read())
        {
            var status = (RemoteInstaller.Models.TaskStatus)Convert.ToInt32(statusReader["status"]);
            var count = Convert.ToInt32(statusReader["COUNT(*)"]);
            switch (status)
            {
                case RemoteInstaller.Models.TaskStatus.Completed:
                    stats.CompletedCount = count;
                    break;
                case RemoteInstaller.Models.TaskStatus.Failed:
                    stats.FailedCount = count;
                    break;
                case RemoteInstaller.Models.TaskStatus.Cancelled:
                    stats.CancelledCount = count;
                    break;
            }
        }

        return stats;
    }

    #endregion

    private LogEntry MapToLogEntry(System.Data.Common.DbDataReader reader)
    {
        return new LogEntry
        {
            Timestamp = DateTime.Parse(reader["timestamp"].ToString()),
            Level = (RemoteInstaller.Models.LogLevel)Convert.ToInt32(reader["level"]),
            Message = reader["message"]?.ToString() ?? string.Empty,
            TaskId = reader["task_id"]?.ToString()
        };
    }

    /// <summary>
    /// 映射历史记录
    /// </summary>
    private InstallHistory MapToInstallHistory(System.Data.Common.DbDataReader reader)
    {
        return new InstallHistory
        {
            Id = Convert.ToInt32(reader["id"]),
            HostId = reader["host_id"]?.ToString() ?? string.Empty,
            HostName = reader["host_name"]?.ToString() ?? string.Empty,
            ApplicationId = reader["application_id"]?.ToString() ?? string.Empty,
            ApplicationName = reader["application_name"]?.ToString() ?? string.Empty,
            ApplicationVersion = reader["application_version"]?.ToString() ?? string.Empty,
            OperationType = (HistoryOperationType)Convert.ToInt32(reader["operation_type"]),
            Status = (RemoteInstaller.Models.TaskStatus)Convert.ToInt32(reader["status"]),
            StartTime = DateTime.Parse(reader["start_time"].ToString()),
            EndTime = reader["end_time"] == null || reader["end_time"].ToString() == "" ? (DateTime?)null : DateTime.Parse(reader["end_time"].ToString()),
            ErrorMessage = reader["error_message"]?.ToString(),
            LogContent = reader["log_content"]?.ToString()
        };
    }

    #region IDisposable 实现

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                // 释放托管资源
            }
            _disposed = true;
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    #endregion
}

/// <summary>
/// 日志摘要
/// </summary>
public class LogSummary
{
    public string? TaskId { get; set; }
    public DateTime FirstLogTime { get; set; }
    public DateTime LastLogTime { get; set; }
    public int TotalCount { get; set; }
    public int InfoCount { get; set; }
    public int SuccessCount { get; set; }
    public int WarningCount { get; set; }
    public int ErrorCount { get; set; }
}

/// <summary>
/// 历史记录统计信息
/// P1 功能：任务历史记录统计
/// </summary>
public class HistoryStatistics
{
    /// <summary>
    /// 总记录数
    /// </summary>
    public int TotalCount { get; set; }

    /// <summary>
    /// 安装次数
    /// </summary>
    public int InstallCount { get; set; }

    /// <summary>
    /// 卸载次数
    /// </summary>
    public int UninstallCount { get; set; }

    /// <summary>
    /// 成功次数
    /// </summary>
    public int CompletedCount { get; set; }

    /// <summary>
    /// 失败次数
    /// </summary>
    public int FailedCount { get; set; }

    /// <summary>
    /// 取消次数
    /// </summary>
    public int CancelledCount { get; set; }

    /// <summary>
    /// 成功率
    /// </summary>
    public double SuccessRate => TotalCount > 0 
        ? (CompletedCount * 100.0) / TotalCount 
        : 0;
}

/// <summary>
/// 数据库服务辅助方法
/// </summary>
public static class DatabaseServiceExtensions
{
    public static RemoteHost MapToHost(System.Data.Common.DbDataReader reader)
    {
        return new RemoteHost
        {
            Id = reader["id"]?.ToString(),
            Name = reader["name"]?.ToString(),
            IpAddress = reader["ip_address"]?.ToString(),
            Port = Convert.ToInt32(reader["port"]),
            Username = reader["username"]?.ToString(),
            EncryptedPassword = reader["encrypted_password"]?.ToString(),
            AuthType = (AuthType)Convert.ToInt32(reader["auth_type"]),
            KeyPath = reader["key_path"]?.ToString(),
            EncryptedKeyPassphrase = reader["encrypted_key_passphrase"]?.ToString(),
            OsType = (OperatingSystemType)Convert.ToInt32(reader["os_type"]),
            OsVersion = reader["os_version"]?.ToString() ?? string.Empty,
            CpuArchitecture = reader["cpu_architecture"]?.ToString() ?? string.Empty,
            GroupName = reader["group_name"]?.ToString(),
            Status = (HostStatus)Convert.ToInt32(reader["status"]),
            StatusMessage = reader["status_message"]?.ToString(),
            LastConnected = reader["last_connected"] == null || reader["last_connected"].ToString() == "" ? DateTime.MinValue : DateTime.Parse(reader["last_connected"].ToString()),
            CreatedAt = DateTime.Parse(reader["created_at"].ToString()),
            UpdatedAt = DateTime.Parse(reader["updated_at"].ToString())
        };
    }

    public static InstallTask MapToTask(System.Data.Common.DbDataReader reader)
    {
        return new InstallTask
        {
            Id = reader["id"].ToString(),
            HostId = reader["host_id"].ToString(),
            HostName = reader["host_name"].ToString(),
            AppId = reader["app_id"].ToString(),
            AppName = reader["app_name"].ToString(),
            AppVersion = reader["app_version"].ToString(),
            Status = (RemoteInstaller.Models.TaskStatus)Convert.ToInt32(reader["status"]),
            Stage = (InstallStage)Convert.ToInt32(reader["stage"]),
            Progress = Convert.ToDouble(reader["progress"]),
            ErrorMessage = reader["error_message"]?.ToString(),
            StartTime = DateTime.Parse(reader["start_time"].ToString()),
            EndTime = DateTime.Parse(reader["end_time"].ToString())
        };
    }
}
