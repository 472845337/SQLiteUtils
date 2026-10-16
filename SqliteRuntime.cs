using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace SqliteUtils {
    /// <summary>
    /// SQLite 依赖自包含运行时。
    ///
    /// SqliteUtils.dll 内部已经嵌入了三个文件（见 SqliteUtils.csproj 中的 EmbeddedResource）：
    ///   SqliteUtils.Embedded.System.Data.SQLite.dll          托管程序集
    ///   SqliteUtils.Embedded.x64.SQLite.Interop.dll          x64 原生库
    ///   SqliteUtils.Embedded.x86.SQLite.Interop.dll          x86 原生库
    ///
    /// 首次使用时会（仅一次）：
    ///   1. 把托管程序集 + 当前进程架构对应的原生库释放到一个可写目录，目录结构保持
    ///      “托管程序集所在目录 + x64/x86 子目录”的标准形式；
    ///   2. 设置环境变量 PreLoadSQLite_BaseDirectory 指向该目录，
    ///      System.Data.SQLite 的原生库预加载器会在基目录及其架构子目录中查找 SQLite.Interop.dll；
    ///   3. 直接把原生库 LoadLibrary 进进程（双保险）；
    ///   4. 挂 AppDomain.AssemblyResolve，把托管程序集从释放目录/内嵌资源中解析出来。
    ///
    /// 因此引用方项目发布时只需要携带 SqliteUtils.dll，不必再复制任何 SQLite 相关 dll。
    ///
    /// 注意：必须在接触任何 SQLite 类型之前完成挂接。SqliteConfig 的静态构造函数已经自动调用
    /// 本方法；若某项目绕开 SqliteConfig.Init 使用，请先自行调用一次 SqliteRuntime.EnsureLoaded()。
    /// </summary>
    public static class SqliteRuntime {

        #region 常量

        /// <summary>内嵌资源名：托管程序集</summary>
        private const string ManagedResourceName = "SqliteUtils.Embedded.System.Data.SQLite.dll";
        /// <summary>内嵌资源名：x64 原生库</summary>
        private const string InteropResourceNameX64 = "SqliteUtils.Embedded.x64.SQLite.Interop.dll";
        /// <summary>内嵌资源名：x86 原生库</summary>
        private const string InteropResourceNameX86 = "SqliteUtils.Embedded.x86.SQLite.Interop.dll";

        /// <summary>托管程序集简单名（用于 AssemblyResolve 匹配）</summary>
        private const string ManagedAssemblySimpleName = "System.Data.SQLite";
        /// <summary>托管程序集文件名</summary>
        private const string ManagedFileName = "System.Data.SQLite.dll";
        /// <summary>原生库文件名（不可改名，System.Data.SQLite 按此名加载）</summary>
        private const string InteropFileName = "SQLite.Interop.dll";

        #endregion

        private static readonly object SyncRoot = new object();
        private static bool _loaded;
        private static string _runtimeDirectory;
        private static string _managedAssemblyPath;
        private static string _interopFilePath;

        /// <summary>
        /// 依赖释放目录（根目录，其下是 x64/x86 子目录）；
        /// 释放失败时为 null。可用于排查问题。
        /// </summary>
        public static string RuntimeDirectory {
            get {
                EnsureLoaded();
                return _runtimeDirectory;
            }
        }

        /// <summary>加载/释放过程中的异常（正常情况下为 null）</summary>
        public static Exception LastError { get; private set; }

        /// <summary>
        /// 确保内嵌的 SQLite 依赖可用。可重复调用、线程安全、幂等。
        /// </summary>
        public static void EnsureLoaded() {
            if (_loaded) {
                return;
            }
            lock (SyncRoot) {
                if (_loaded) {
                    return;
                }
                // 先置位：即使 Prepare 抛异常也不再重复尝试，避免死循环
                _loaded = true;

                // 必须在任何 SQLite 类型被加载之前挂上解析事件
                AppDomain.CurrentDomain.AssemblyResolve += OnAssemblyResolve;

                try {
                    Prepare();
                } catch (Exception e) {
                    LastError = e;
                }
            }
        }

        #region 内部实现

        /// <summary>
        /// 释放依赖并让 System.Data.SQLite 能找到原生库
        /// </summary>
        private static void Prepare() {
            string arch = Environment.Is64BitProcess ? "x64" : "x86";
            byte[] managed = ReadResource(ManagedResourceName);
            byte[] interop = ReadResource("x64".Equals(arch) ? InteropResourceNameX64 : InteropResourceNameX86);

            if (managed == null || interop == null) {
                // 资源缺失（例如工程里把这三个 dll 的生成操作改掉了），交由常规的 dll 探测逻辑处理
                LastError = new InvalidOperationException(
                    "SqliteUtils.dll 中未找到内嵌的 SQLite 依赖资源。请确认工程中 System.Data.SQLite.dll、" +
                    "X64\\SQLite.Interop.dll、X86\\SQLite.Interop.dll 三项的生成操作均为“嵌入的资源”，且 LogicalName 未被改动。");
                return;
            }

            // 用文件长度做目录版本标识：更换 SQLite 版本后长度必然不同，会自动释放到新目录
            string token = arch + "-" + managed.Length + "-" + interop.Length;

            List<string> failures = new List<string>();
            foreach (string root in GetCandidateRoots(token)) {
                try {
                    WriteIfNeeded(Path.Combine(root, ManagedFileName), managed);
                    WriteIfNeeded(Path.Combine(root, arch, InteropFileName), interop);
                    _runtimeDirectory = root;
                    _managedAssemblyPath = Path.Combine(root, ManagedFileName);
                    _interopFilePath = Path.Combine(root, arch, InteropFileName);
                    break;
                } catch (Exception e) {
                    failures.Add(root + " -> " + e.Message);
                }
            }

            if (_runtimeDirectory == null) {
                LastError = new InvalidOperationException(
                    "释放内嵌 SQLite 依赖失败（所有候选目录均不可写）：" + string.Join("; ", failures.ToArray()));
                return;
            }

            // 1) 告诉原生库预加载器：基目录改为释放目录，其下按 x64/x86 子目录查找 SQLite.Interop.dll
            try {
                Environment.SetEnvironmentVariable("PreLoadSQLite_BaseDirectory", _runtimeDirectory);
            } catch {
                // 环境变量设置失败不影响后面的 LoadLibrary 兜底
            }

            // 2) 把当前架构的原生库直接加载进进程，作为双保险
            try {
                LoadLibraryW(_interopFilePath);
            } catch {
                // 失败不抛：预加载器仍会按上面的基目录自行查找
            }
        }

        /// <summary>
        /// 提取内嵌资源为 byte[]；不存在时返回 null
        /// </summary>
        private static byte[] ReadResource(string resourceName) {
            using (Stream stream = typeof(SqliteRuntime).Assembly.GetManifestResourceStream(resourceName)) {
                if (stream == null) {
                    return null;
                }
                byte[] buffer = new byte[stream.Length];
                int read = 0;
                while (read < buffer.Length) {
                    int n = stream.Read(buffer, read, buffer.Length - read);
                    if (n <= 0) {
                        break;
                    }
                    read += n;
                }
                return buffer;
            }
        }

        /// <summary>
        /// 文件不存在或长度不一致时写入（先写临时文件再改名，避免并发写坏）
        /// </summary>
        private static void WriteIfNeeded(string path, byte[] data) {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) {
                Directory.CreateDirectory(dir);
            }
            if (File.Exists(path) && new FileInfo(path).Length == data.Length) {
                return;
            }
            string temp = path + ".tmp";
            File.WriteAllBytes(temp, data);
            if (File.Exists(path)) {
                File.Delete(path);
            }
            File.Move(temp, path);
        }

        /// <summary>
        /// 释放目录候选（按优先级）：本地应用程序数据 -> 临时目录 -> 程序目录下的隐藏子目录
        /// </summary>
        private static List<string> GetCandidateRoots(string token) {
            List<string> roots = new List<string>();
            try {
                string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (!string.IsNullOrEmpty(local)) {
                    roots.Add(Path.Combine(local, "SqliteUtils", "runtime", token));
                }
            } catch {
            }
            try {
                string temp = Path.GetTempPath();
                if (!string.IsNullOrEmpty(temp)) {
                    roots.Add(Path.Combine(temp, "SqliteUtils", "runtime", token));
                }
            } catch {
            }
            try {
                roots.Add(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".sqliteutils", "runtime", token));
            } catch {
            }
            return roots;
        }

        /// <summary>
        /// 从内嵌资源解析 System.Data.SQLite
        /// </summary>
        private static Assembly OnAssemblyResolve(object sender, ResolveEventArgs args) {
            try {
                AssemblyName name = new AssemblyName(args.Name);
                if (!string.Equals(name.Name, ManagedAssemblySimpleName, StringComparison.OrdinalIgnoreCase)) {
                    return null;
                }
                // 优先从释放目录加载（这样该程序集的 Location 有值，原生库查找会以它所在目录为起点）
                if (!string.IsNullOrEmpty(_managedAssemblyPath) && File.Exists(_managedAssemblyPath)) {
                    return Assembly.LoadFrom(_managedAssemblyPath);
                }
                byte[] bytes = ReadResource(ManagedResourceName);
                return bytes == null ? null : Assembly.Load(bytes);
            } catch {
                return null;
            }
        }

        [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "LoadLibraryW")]
        private static extern IntPtr LoadLibraryW(string lpFileName);

        #endregion
    }
}
