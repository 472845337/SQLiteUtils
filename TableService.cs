using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Reflection;
using System.Text;

namespace SqliteUtils {
    /// <summary>
    /// 通用 SQLite 表操作服务（基于实体上的 Table / TableParam 特性自动建表并完成增删改查）。
    /// 
    /// 使用方式：
    /// 1. 程序启动时先配置 SqliteConfig.Init(dbName, dbPassword);
    /// 2. 实体继承 SqliteTableData，并用 [Table("表名")] / [TableParam("列名", "类型")] 标注；
    /// 3. 直接使用 TableService&lt;实体&gt;，或派生出自己的 Service。
    /// </summary>
    public class TableService<T> where T : SqliteTableData {

        /// <summary>
        /// 主键列名：默认取实体 SqliteTableData.KeyColumn（默认 "id"，可在实体基类中重写为 "section" 等）
        /// </summary>
        private static readonly string KeyColumn = ResolveKeyColumn();

        private static string ResolveKeyColumn() {
            try {
                return Activator.CreateInstance<T>().KeyColumn ?? "id";
            } catch {
                return "id";
            }
        }

        /// <summary>
        /// 数据库帮助类（由 SqLiteFactory 缓存，按库名复用同一连接）
        /// </summary>
        private static SqLiteHelper SqlLiteHelper => SqLiteFactory.GetSqLiteHelper(SqliteConfig.DbFilePath, SqliteConfig.DbPassword);

        public TableService() {
            InitTable();
        }

        private void InitTable() {
            string tableName = GetTableName();
            // 判断表是否存在
            if (!SqlLiteHelper.TableExists(tableName)) {
                // 创建表
                CreateTable();
            } else {
                // SQLLite数据库添加新增的列
                List<string> addColumnList = new List<string>();
                List<string> nameList = GetTableParam();
                // 获取自己和父类的属性
                var infos = typeof(T).GetProperties();
                foreach (var info in infos) {
                    var tableParam = GetAttributeByProperty<TableParam>(info);
                    if (null == tableParam) {
                        continue;
                    }
                    if (!nameList.Contains(tableParam.param)) {
                        StringBuilder sb = new StringBuilder();
                        sb.Append(tableParam.param).Append(" ").Append(tableParam.type);
                        if (tableParam.isKey) {
                            sb.Append(" PRIMARY KEY AUTOINCREMENT");
                        }
                        addColumnList.Add(sb.ToString());
                    }
                }
                if (addColumnList.Count > 0) {
                    foreach (string addColumn in addColumnList) {
                        SqlLiteHelper.ExecuteNonQuery($"ALTER TABLE {tableName} ADD COLUMN {addColumn}", null);
                    }
                }
            }
        }

        /// <summary>
        /// 插入数据并返回ID
        /// </summary>
        /// <param name="t"></param>
        /// <returns></returns>
        public long Insert(T t) {
            t?.OnBeforeInsert();
            return SqlLiteHelper.InsertData(GetTableName(), GetParams(t, true));
        }

        /// <summary>
        /// 清除所有数据
        /// </summary>
        public void Clear() {
            // 清空所有的数据
            SqlLiteHelper.ExecuteNonQuery("DELETE FROM " + GetTableName(), null);
        }

        /// <summary>
        /// 根据主键值删除某个数据
        /// </summary>
        /// <param name="keyValue">主键值（如 Id / Section 的值）</param>
        /// <returns></returns>
        public int Delete(string keyValue) {
            if (string.IsNullOrEmpty(keyValue)) {
                return 0;
            }
            return SqlLiteHelper.ExecuteNonQuery($"DELETE FROM {GetTableName()} where {KeyColumn}=@{KeyColumn}", new[] { new SQLiteParameter(KeyColumn, keyValue) });
        }

        /// <summary>
        /// 按查询对象删除数据（where 条件由查询对象中已赋值的属性决定，取值规则同 SelectList）
        /// </summary>
        /// <param name="queryModel">查询对象</param>
        /// <returns>
        /// 受影响的行数；查询对象未指定任何条件时返回 0。
        /// （不指定条件时会拒绝执行，避免误删整表——清空整表请使用 Clear）
        /// </returns>
        public int Delete(T queryModel) {
            string paramSql = string.Empty;
            string whereSql = string.Empty;
            List<SQLiteParameter> paramList = new List<SQLiteParameter>();
            InitQuerySql(queryModel, ref paramSql, ref whereSql, ref paramList);
            if (string.IsNullOrEmpty(whereSql)) {
                return 0;
            }
            return SqlLiteHelper.Delete(GetTableName(), whereSql, paramList.ToArray());
        }

        public void Vacuum() {
            SqlLiteHelper.Vacuum();
        }

        /// <summary>
        /// 修改数据（where 条件使用实体的主键 KeyValue）。
        /// 调用前会先执行实体的 OnBeforeUpdate 回调，便于统一维护 update_time 等字段
        /// </summary>
        /// <param name="t"></param>
        /// <param name="hasNull">为 true 时，null 值的列也会参与更新</param>
        /// <returns></returns>
        public int Update(T t, bool hasNull = false) {
            t?.OnBeforeUpdate();
            return SqlLiteHelper.Update(GetTableName(), GetParams(t, false, hasNull), $"{KeyColumn}=@{KeyColumn}", new[] { new SQLiteParameter(KeyColumn, t.KeyValue) });
        }

        /// <summary>
        /// 更新排序值（UPDATE 表 SET sort=@sort WHERE 主键=@主键）
        /// </summary>
        /// <param name="keyValue">主键值</param>
        /// <param name="sort">排序值</param>
        /// <returns></returns>
        public int UpdateSort(string keyValue, int sort) {
            if (string.IsNullOrEmpty(keyValue)) {
                return 0;
            }
            string sql = $"UPDATE {GetTableName()} SET sort=@sort WHERE {KeyColumn}=@{KeyColumn}";
            return SqlLiteHelper.ExecuteNonQuery(sql, new[] { new SQLiteParameter("sort", sort), new SQLiteParameter(KeyColumn, keyValue) });
        }

        /// <summary>
        /// 根据对象查询匹配的数据
        /// </summary>
        /// <param name="queryModel"></param>
        /// <returns></returns>
        public List<T> SelectList(T queryModel) {
            List<T> list = new List<T>();
            // sql拼装
            List<SQLiteParameter> paramList = new List<SQLiteParameter>();
            string paramSql = string.Empty;
            string whereSql = string.Empty;
            StringBuilder selectSql = new StringBuilder();
            InitQuerySql(queryModel, ref paramSql, ref whereSql, ref paramList);
            selectSql.Append("SELECT ").Append(paramSql).Append(" FROM ").Append(GetTableName());
            if (!string.IsNullOrEmpty(whereSql)) {
                selectSql.Append(" WHERE ").Append(whereSql);
            }
            if (!string.IsNullOrEmpty(queryModel?.OrderBy)) {
                selectSql.Append(" ORDER BY ").Append(queryModel.OrderBy);
            }
            // 执行查询
            SQLiteDataReader reader = SqlLiteHelper.ExecuteReader(selectSql.ToString(), paramList.ToArray());
            if (null != reader) {
                // 数据拼装
                while (reader.Read()) {
                    T t = Activator.CreateInstance<T>();
                    PropertyInfo[] infos = typeof(T).GetProperties();
                    foreach (var info in infos) {
                        TableParam tableParam = GetAttributeByProperty<TableParam>(info);
                        if (null == tableParam) {
                            continue;
                        }
                        if (TryConvertValue(reader[tableParam.param], info.PropertyType, out object converted)) {
                            info.SetValue(t, converted);
                        }
                    }

                    list.Add(t);
                }
            }
            return list;
        }

        /// <summary>
        /// 查询匹配的第一条数据，不存在时返回 null
        /// </summary>
        public T SelectFirst(T queryModel) {
            List<T> list = SelectList(queryModel);
            if (null != list && list.Count > 0) {
                return list[0];
            } else {
                return null;
            }
        }

        private void InitQuerySql(T t, ref string paramSql, ref string whereSql, ref List<SQLiteParameter> paramList) {
            var infos = typeof(T).GetProperties();
            paramList = new List<SQLiteParameter>();
            StringBuilder paramSb = new StringBuilder();
            StringBuilder whereSb = new StringBuilder();
            foreach (var property in infos) {
                var tableParam = GetAttributeByProperty<TableParam>(property);
                if (null == tableParam) {
                    continue;
                }
                if (null != t && IsQueryValue(property, property.GetValue(t, null))) {
                    if (whereSb.Length > 0) {
                        whereSb.Append(" AND ");
                    }
                    if (TableParam.QueryLikeType.Normal != tableParam.likeType) {
                        // 使用Like查询
                        if (TableParam.QueryLikeType.Like == tableParam.likeType) {
                            whereSb.Append(tableParam.param).Append(" LIKE '%'||@").Append(property.Name).Append("||'%'");
                        } else if (TableParam.QueryLikeType.LeftLike == tableParam.likeType) {
                            whereSb.Append(tableParam.param).Append(" LIKE @").Append(property.Name).Append("||'%'");
                        } else if (TableParam.QueryLikeType.RightLike == tableParam.likeType) {
                            whereSb.Append(tableParam.param).Append(" LIKE '%'||@").Append(property.Name);
                        }
                    } else {
                        whereSb.Append(tableParam.param).Append("=@").Append(property.Name);
                    }
                    SQLiteParameter param = new SQLiteParameter(property.Name, property.GetValue(t, null));
                    paramList.Add(param);
                }
                if (paramSb.Length > 0) {
                    paramSb.Append(", ");
                }
                paramSb.Append(tableParam.param);
            }
            paramSql = paramSb.ToString();
            whereSql = whereSb.ToString();
        }

        /// <summary>
        /// 判断属性值是否应作为查询条件。
        /// 值为 null 表示「未设置」，不作为条件；
        /// 非空值类型属性（如 int / bool）的默认值（0 / false）无法与「未设置」区分，
        /// 也按未设置处理——否则 new T { 某字段 = x } 会把其它字段的默认值一并拼进 where。
        /// 若确实需要按 0 / false 过滤，请把该属性声明为可空类型（int? / bool?）后显式赋值。
        /// </summary>
        private static bool IsQueryValue(PropertyInfo property, object value) {
            if (null == value) {
                return false;
            }
            Type type = property.PropertyType;
            if (type.IsValueType && null == Nullable.GetUnderlyingType(type)
                && value.Equals(Activator.CreateInstance(type))) {
                return false;
            }
            return true;
        }

        /// <summary>
        /// 把数据库读出的值转换成实体属性的类型。
        /// SQLite 是依据列的声明类型决定返回的 .NET 类型的（如 INTEGER→long、INT→int、BIT→bool），
        /// 与实体属性的声明类型不一定一致，直接用 SetValue 赋值会抛异常，因此这里统一安全转换一次；
        /// 转换失败时跳过该列（保持属性默认值），不影响整条记录的读取。
        /// </summary>
        private static bool TryConvertValue(object value, Type targetType, out object result) {
            result = null;
            if (null == value || DBNull.Value == value) {
                return false;
            }
            Type type = Nullable.GetUnderlyingType(targetType) ?? targetType;
            if (type.IsInstanceOfType(value)) {
                result = value;
                return true;
            }
            try {
                if (typeof(bool) == type) {
                    if (value is string text) {
                        result = bool.TryParse(text, out bool parsed) ? parsed : text == "1";
                    } else {
                        result = Convert.ToInt64(value) != 0;
                    }
                    return true;
                }
                if (type.IsEnum) {
                    result = Enum.ToObject(type, value);
                    return true;
                }
                result = Convert.ChangeType(value, type);
                return true;
            } catch {
                return false;
            }
        }

        /// <summary>
        /// 创建表
        /// </summary>
        private void CreateTable() {
            // 获取自己和父类的属性
            var infos = typeof(T).GetProperties();
            var insertSql = new StringBuilder("create table ").Append(GetTableName()).Append(" (");
            var sb = new StringBuilder();
            foreach (var info in infos) {
                var tableParam = GetAttributeByProperty<TableParam>(info);
                if (null == tableParam) {
                    continue;
                }
                if (sb.Length > 0) {
                    sb.Append(", ");
                }
                sb.Append(tableParam.param).Append(" ").Append(tableParam.type);
                // 自增主键：SQLite 规定 AUTOINCREMENT 只能用在「声明类型恰为 INTEGER」的主键列上，
                // 因此这里只对 INTEGER 主键生效（自增数值 Id）；
                // 用 VARCHAR 业务主键（如 XStart2.0 的 section）的项目保持原样，不加主键约束。
                if (tableParam.isKey && "INTEGER".Equals(tableParam.type?.Trim(), StringComparison.OrdinalIgnoreCase)) {
                    sb.Append(" PRIMARY KEY AUTOINCREMENT");
                }
            }
            insertSql.Append(sb).Append(")");
            SqlLiteHelper.ExecuteNonQuery(insertSql.ToString(), null);
        }

        private List<string> GetTableParam() {
            List<string> nameList = new List<string>();
            string sql = $"PRAGMA table_info('{GetTableName()}')";
            using (SQLiteDataReader reader = SqlLiteHelper.ExecuteReader(sql, null)) {
                while (reader.Read()) {
                    nameList.Add((string)reader["name"]);
                }
            }
            return nameList;
        }

        private string GetTableName() {
            string tableName;
            var tableAttrs = typeof(T).GetCustomAttributes(typeof(Table), true);
            if (tableAttrs.Length > 0) {
                var table = (Table)tableAttrs[0];
                tableName = table.TableName;
            } else {
                throw new Exception(typeof(T) + " class not set Table attributes");
            }
            return tableName;
        }

        /// <summary>
        /// 获取属性对应的参数字典项,自动排除主键属性 比如ID
        /// </summary>
        /// <param name="t">:SqliteTableData</param>
        /// <param name="key">是否包含主键，insert填true，update填false</param>
        /// <returns></returns>
        private Dictionary<string, object> GetParams(T t, bool key, bool hasNull = false) {
            if (null == t) return null;
            var dict = new Dictionary<string, object>();
            // 遍历所有的属性
            var infos = typeof(T).GetProperties();
            foreach (PropertyInfo info in infos) {
                var tableParam = GetAttributeByProperty<TableParam>(info);
                if (null == tableParam) {
                    continue;
                }
                if (tableParam.isKey) {
                    if (!key) {
                        continue;
                    }
                }
                object value = info.GetValue(t, null);
                if (!hasNull && null == value) {
                    continue;
                }
                dict.Add(tableParam.param, value);
            }
            return dict;
        }

        private static TA GetAttributeByProperty<TA>(PropertyInfo propertyInfo) where TA : Attribute {
            var attributes = propertyInfo.GetCustomAttributes(typeof(TA), true);
            TA attribute = null;
            if (attributes.Length > 0) {
                attribute = (TA)attributes[0];
            }
            return attribute;
        }
    }
}
