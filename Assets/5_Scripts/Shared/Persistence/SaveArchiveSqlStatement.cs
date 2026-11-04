using System;
using System.Runtime.InteropServices;
using SQLite;

namespace FlatWorld.Persistence
{
    #region SQLite 预编译语句

    internal sealed class SaveArchiveSqlStatement : IDisposable
    {
        private static readonly IntPtr Transient = new IntPtr(-1);
        private static readonly byte[] EmptyBlob = new byte[1];
        private readonly SQLiteConnection _database;
        private readonly string _sql;
        private IntPtr _statement;

        public SaveArchiveSqlStatement(SQLiteConnection database, string sql)
        {
            _database = database;
            _sql = sql;
            _statement = SQLite3.Prepare2(database.Handle, sql);
        }

        public void Bind(int index, string value)
        {
            Check((SQLite3.Result)SQLite3.BindText(_statement, index, value, value.Length * sizeof(char), Transient));
        }

        public void Bind(int index, int value) => Check((SQLite3.Result)SQLite3.BindInt(_statement, index, value));
        public void Bind(int index, long value) => Check((SQLite3.Result)SQLite3.BindInt64(_statement, index, value));

        public void Bind(int index, byte[] value)
        {
            if (value == null)
                Check((SQLite3.Result)SQLite3.BindNull(_statement, index));
            else
                Check((SQLite3.Result)SQLite3.BindBlob(_statement, index, value.Length == 0 ? EmptyBlob : value,
                    value.Length, Transient));
        }

        public SQLite3.Result Step()
        {
            var result = SQLite3.Step(_statement);
            if (result != SQLite3.Result.Row && result != SQLite3.Result.Done)
                Check(result);
            return result;
        }

        public string Text(int column) => SQLite3.ColumnString(_statement, column);
        public int Int(int column) => SQLite3.ColumnInt(_statement, column);
        public long Long(int column) => SQLite3.ColumnInt64(_statement, column);
        public byte[] Bytes(int column) => SQLite3.ColumnByteArray(_statement, column);

        public void Reset()
        {
            SQLite3.Reset(_statement);
            ClearBindings(_statement);
        }

        public void Dispose()
        {
            if (_statement == IntPtr.Zero)
                return;
            SQLite3.Finalize(_statement);
            _statement = IntPtr.Zero;
        }

        private void Check(SQLite3.Result result)
        {
            if (result != SQLite3.Result.OK)
            {
                // 在语句重置和事务回滚覆盖原生错误前保留扩展码，不记录绑定的存档内容。
                int extendedCode = (int)SQLite3.ExtendedErrCode(_database.Handle);
                throw SQLiteException.New(result,
                    $"{SQLite3.GetErrmsg(_database.Handle)} [SQLite={extendedCode}, SQL={_sql}]");
            }
        }

        [DllImport(SQLite3.LibraryPath, EntryPoint = "sqlite3_clear_bindings", CallingConvention = CallingConvention.Cdecl)]
        private static extern int ClearBindings(IntPtr statement);
    }

    #endregion
}
