using System;

namespace SqliteUtils {
    /// <summary>
    /// 数据表基础对象（用于标记实体对应的表名）
    /// </summary>
    public class Table : Attribute {

        public Table(string tableName) {
            TableName = tableName;
        }

        public string TableName { get; }
    }
}
