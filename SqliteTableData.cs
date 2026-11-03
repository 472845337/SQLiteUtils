using System;

namespace SqliteUtils {
    /// <summary>
    /// 数据库实体的公共基类。
    /// 各项目可在自己的实体基类中继承本类，并按需重写 KeyColumn / KeyValue / OnBeforeInsert。
    /// （注意：类名特意加 Sqlite 前缀，避免与各项目自定义的实体基类 TableData 重名冲突。）
    /// </summary>
    [Serializable]
    public abstract class SqliteTableData {
        /// <summary>
        /// 排序字段（仅作为查询条件使用，不映射到数据库列）
        /// </summary>
        public string OrderBy { get; set; }

        /// <summary>
        /// 主键列名，默认 "id"；如主键列不是 id，请在实体基类中重写（例如返回 "section"）
        /// </summary>
        public virtual string KeyColumn => "id";

        /// <summary>
        /// 主键值，用于 Update / Delete 的 where 条件；请在实体基类中重写为返回主键属性的值
        /// </summary>
        public virtual object KeyValue => null;

        /// <summary>
        /// 插入前的回调，子类可重写（例如自动生成主键 Guid、写入创建时间）
        /// </summary>
        public virtual void OnBeforeInsert() { }

        /// <summary>
        /// 更新前的回调，子类可重写（例如自动维护更新时间）
        /// </summary>
        public virtual void OnBeforeUpdate() { }
    }
}
