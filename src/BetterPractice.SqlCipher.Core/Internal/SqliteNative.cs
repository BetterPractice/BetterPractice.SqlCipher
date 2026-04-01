using System.Runtime.InteropServices;

namespace BetterPractice.SqlCipher.Internal;

/// <summary>
/// P/Invoke declarations for the SQLCipher native library.
/// This is the P/Invoke Layer — not part of the public API.
/// All callers must go through DatabaseHandle, never directly through this class.
/// </summary>
internal static partial class SqliteNative
{
    internal const string LibraryName = "sqlcipher";

    // -------------------------------------------------------------------------
    // Result codes
    // -------------------------------------------------------------------------

    internal const int SQLITE_OK        = 0;
    internal const int SQLITE_ERROR     = 1;
    internal const int SQLITE_BUSY      = 5;
    internal const int SQLITE_NOTADB    = 26;
    internal const int SQLITE_ROW       = 100;
    internal const int SQLITE_DONE      = 101;

    // sqlite3_column_type values
    internal const int SQLITE_INTEGER   = 1;
    internal const int SQLITE_FLOAT     = 2;
    internal const int SQLITE_TEXT      = 3;
    internal const int SQLITE_BLOB      = 4;
    internal const int SQLITE_NULL      = 5;

    // sqlite3_open_v2 flags
    internal const int SQLITE_OPEN_READWRITE = 0x00000002;
    internal const int SQLITE_OPEN_CREATE    = 0x00000004;
    internal const int SQLITE_OPEN_URI       = 0x00000040;

    // -------------------------------------------------------------------------
    // Database connection
    // -------------------------------------------------------------------------

    [LibraryImport(LibraryName, EntryPoint = "sqlite3_open_v2",
        StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int sqlite3_open_v2(
        string filename,
        out IntPtr db,
        int flags,
        IntPtr vfs);

    [LibraryImport(LibraryName, EntryPoint = "sqlite3_close_v2")]
    internal static partial int sqlite3_close_v2(IntPtr db);

    // sqlite3_errmsg returns a const char* owned by SQLite — must NOT be freed by caller.
    // Use IntPtr return + Marshal.PtrToStringUTF8 to copy without freeing.
    [LibraryImport(LibraryName, EntryPoint = "sqlite3_errmsg")]
    private static partial IntPtr sqlite3_errmsg_ptr(IntPtr db);

    internal static string sqlite3_errmsg(IntPtr db) =>
        Marshal.PtrToStringUTF8(sqlite3_errmsg_ptr(db)) ?? string.Empty;

    [LibraryImport(LibraryName, EntryPoint = "sqlite3_extended_errcode")]
    internal static partial int sqlite3_extended_errcode(IntPtr db);

    // -------------------------------------------------------------------------
    // Statement preparation and execution
    // -------------------------------------------------------------------------

    [LibraryImport(LibraryName, EntryPoint = "sqlite3_prepare_v2",
        StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int sqlite3_prepare_v2(
        IntPtr db,
        string sql,
        int nByte,
        out IntPtr stmt,
        out IntPtr pzTail);

    [LibraryImport(LibraryName, EntryPoint = "sqlite3_step")]
    internal static partial int sqlite3_step(IntPtr stmt);

    [LibraryImport(LibraryName, EntryPoint = "sqlite3_reset")]
    internal static partial int sqlite3_reset(IntPtr stmt);

    [LibraryImport(LibraryName, EntryPoint = "sqlite3_finalize")]
    internal static partial int sqlite3_finalize(IntPtr stmt);

    [LibraryImport(LibraryName, EntryPoint = "sqlite3_clear_bindings")]
    internal static partial int sqlite3_clear_bindings(IntPtr stmt);

    // -------------------------------------------------------------------------
    // Parameter binding
    // -------------------------------------------------------------------------

    [LibraryImport(LibraryName, EntryPoint = "sqlite3_bind_parameter_index",
        StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int sqlite3_bind_parameter_index(IntPtr stmt, string name);

    [LibraryImport(LibraryName, EntryPoint = "sqlite3_bind_null")]
    internal static partial int sqlite3_bind_null(IntPtr stmt, int index);

    [LibraryImport(LibraryName, EntryPoint = "sqlite3_bind_int64")]
    internal static partial int sqlite3_bind_int64(IntPtr stmt, int index, long value);

    [LibraryImport(LibraryName, EntryPoint = "sqlite3_bind_double")]
    internal static partial int sqlite3_bind_double(IntPtr stmt, int index, double value);

    [LibraryImport(LibraryName, EntryPoint = "sqlite3_bind_text",
        StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int sqlite3_bind_text(
        IntPtr stmt,
        int index,
        string value,
        int nByte,
        IntPtr destructor);

    [LibraryImport(LibraryName, EntryPoint = "sqlite3_bind_blob")]
    internal static partial int sqlite3_bind_blob(
        IntPtr stmt,
        int index,
        byte[] value,
        int nByte,
        IntPtr destructor);

    // -------------------------------------------------------------------------
    // Column reading
    // -------------------------------------------------------------------------

    [LibraryImport(LibraryName, EntryPoint = "sqlite3_column_count")]
    internal static partial int sqlite3_column_count(IntPtr stmt);

    // sqlite3_column_name returns a const char* owned by SQLite — must NOT be freed by caller.
    [LibraryImport(LibraryName, EntryPoint = "sqlite3_column_name")]
    private static partial IntPtr sqlite3_column_name_ptr(IntPtr stmt, int col);

    internal static string sqlite3_column_name(IntPtr stmt, int col) =>
        Marshal.PtrToStringUTF8(sqlite3_column_name_ptr(stmt, col)) ?? string.Empty;

    [LibraryImport(LibraryName, EntryPoint = "sqlite3_column_type")]
    internal static partial int sqlite3_column_type(IntPtr stmt, int col);

    [LibraryImport(LibraryName, EntryPoint = "sqlite3_column_int64")]
    internal static partial long sqlite3_column_int64(IntPtr stmt, int col);

    [LibraryImport(LibraryName, EntryPoint = "sqlite3_column_double")]
    internal static partial double sqlite3_column_double(IntPtr stmt, int col);

    // sqlite3_column_text returns a const char* owned by SQLite — must NOT be freed by caller.
    [LibraryImport(LibraryName, EntryPoint = "sqlite3_column_text")]
    private static partial IntPtr sqlite3_column_text_ptr(IntPtr stmt, int col);

    internal static string? sqlite3_column_text(IntPtr stmt, int col)
    {
        IntPtr ptr = sqlite3_column_text_ptr(stmt, col);
        return ptr == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(ptr);
    }

    [LibraryImport(LibraryName, EntryPoint = "sqlite3_column_blob")]
    internal static partial IntPtr sqlite3_column_blob(IntPtr stmt, int col);

    [LibraryImport(LibraryName, EntryPoint = "sqlite3_column_bytes")]
    internal static partial int sqlite3_column_bytes(IntPtr stmt, int col);

    // -------------------------------------------------------------------------
    // Row changes
    // -------------------------------------------------------------------------

    [LibraryImport(LibraryName, EntryPoint = "sqlite3_changes")]
    internal static partial int sqlite3_changes(IntPtr db);

    [LibraryImport(LibraryName, EntryPoint = "sqlite3_last_insert_rowid")]
    internal static partial long sqlite3_last_insert_rowid(IntPtr db);

    // -------------------------------------------------------------------------
    // Special destructor constants for bind_text / bind_blob
    // -------------------------------------------------------------------------

    /// <summary>SQLITE_TRANSIENT (-1): SQLite copies the data before returning.</summary>
    internal static readonly IntPtr SQLITE_TRANSIENT = new(-1);

    /// <summary>SQLITE_STATIC (0): data is static and will outlive the statement.</summary>
    internal static readonly IntPtr SQLITE_STATIC = IntPtr.Zero;
}
