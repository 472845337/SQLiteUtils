using System;

namespace SqliteUtils {
    /// <summary>
    /// 表字段映射特性：标记实体属性对应的数据库列
    /// </summary>
    public class TableParam : Attribute {
        /// <summary>
        /// 是否主键（主键列不参与 update 的 set）
        /// </summary>
        public readonly bool isKey;
        /// <summary>
        /// 数据库列名
        /// </summary>
        public readonly string param;
        /// <summary>
        /// 数据库列类型（如 VARCHAR / INT / BIT / REAL）
        /// </summary>
        public readonly string type;
        /// <summary>
        /// 查询时该列的匹配方式（默认等值匹配）
        /// </summary>
        public QueryLikeType likeType = QueryLikeType.Normal;

        public TableParam(string param, string type) {
            this.param = param;
            this.type = type;
        }

        public TableParam(bool isKey, string param, string type) {
            this.isKey = isKey;
            this.param = param;
            this.type = type;
        }

        public TableParam(string param, string type, QueryLikeType likeType) {
            this.param = param;
            this.type = type;
            this.likeType = likeType;
        }

        public TableParam(bool isKey, string param, string type, QueryLikeType likeType) {
            this.isKey = isKey;
            this.param = param;
            this.type = type;
            this.likeType = likeType;
        }

        /// <summary>
        /// 查询匹配方式
        /// </summary>
        public enum QueryLikeType {
            /// <summary>等值匹配</summary>
            Normal,
            /// <summary>模糊匹配 %value%</summary>
            Like,
            /// <summary>左匹配 value%</summary>
            LeftLike,
            /// <summary>右匹配 %value</summary>
            RightLike
        }
    }
}
