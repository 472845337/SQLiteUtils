using System.Collections.Generic;

namespace SqliteUtils {
    /// <summary>
    /// SqLiteHelper 工厂：同一个数据库文件只会创建一个 SqLiteHelper，并在内部做缓存
    /// </summary>
    public static class SqLiteFactory {
        private static readonly Dictionary<string, SqLiteHelper> SqLiteHelperDic = new Dictionary<string, SqLiteHelper>();
        /// <summary>
        /// 获取（或创建并打开）指定数据库的 SqLiteHelper
        /// </summary>
        /// <param name="dbName">数据库路径，在当前运行位置生成dbName，示例："/database.db"</param>
        /// <param name="password">数据库密码</param>
        /// <returns></returns>
        public static SqLiteHelper GetSqLiteHelper(string dbName, string password) {
            lock (SqLiteHelperDic) {
                if (SqLiteHelperDic.ContainsKey(dbName)) {
                    // 存在该库，直接返回库工具类
                    return SqLiteHelperDic[dbName];
                } else {
                    // 没有该库，创建库并打开库
                    var sqliteHeper = string.IsNullOrEmpty(password) ? new SqLiteHelper(dbName) : new SqLiteHelper(dbName, password);
                    sqliteHeper.Open();
                    SqLiteHelperDic.Add(dbName, sqliteHeper);
                    return sqliteHeper;
                }
            }
        }

        /// <summary>
        /// 关闭所有已打开的数据库连接
        /// </summary>
        public static void CloseAllSqLite() {
            lock (SqLiteHelperDic) {
                foreach (var pair in SqLiteHelperDic) {
                    pair.Value.Close();
                }
                SqLiteHelperDic.Clear();
            }
        }
    }
}
