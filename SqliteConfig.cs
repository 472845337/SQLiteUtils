using System;

namespace SqliteUtils {
    /// <summary>
    /// SQLite 全局配置：使用前请先在程序启动时调用 Init 设置数据库文件名与密码。
    /// 示例：SqliteConfig.Init(DbConstants.DbName, DbConstants.DbPassword);
    /// </summary>
    public static class SqliteConfig {

        /// <summary>
        /// 数据库文件名（可写相对名称，如 "db.dll"；也可写绝对路径）
        /// </summary>
        public static string DbName { get; set; }

        /// <summary>
        /// 数据库密码，无密码时可留空
        /// </summary>
        public static string DbPassword { get; set; }

        /// <summary>
        /// 数据库所在目录，默认取程序运行目录
        /// </summary>
        public static string BasePath { get; set; } = AppDomain.CurrentDomain.BaseDirectory;

        /// <summary>
        /// 最终使用的数据库文件全路径
        /// </summary>
        public static string DbFilePath {
            get {
                if (string.IsNullOrEmpty(DbName)) {
                    throw new InvalidOperationException("SqliteConfig.DbName 未设置，请先在程序启动时调用 SqliteConfig.Init(...)。");
                }
                // 绝对路径直接使用，相对名称拼接到 BasePath
                return DbName.IndexOf(':') > 0 || DbName.StartsWith("\\\\") ? DbName : BasePath + DbName;
            }
        }

        /// <summary>
        /// 初始化数据库配置
        /// </summary>
        /// <param name="dbName">数据库文件名</param>
        /// <param name="dbPassword">数据库密码</param>
        /// <param name="basePath">数据库所在目录，默认程序运行目录</param>
        public static void Init(string dbName, string dbPassword = null, string basePath = null) {
            DbName = dbName;
            DbPassword = dbPassword;
            if (!string.IsNullOrEmpty(basePath)) {
                BasePath = basePath;
            }
        }
    }
}
