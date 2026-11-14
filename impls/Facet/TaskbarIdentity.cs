using System.Runtime.InteropServices;
using Facet.Deployment;

namespace Facet;

internal static class TaskbarIdentity
{
    private static readonly Guid Format = new("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3");
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SetCurrentProcessExplicitAppUserModelID(string id);
    [DllImport("shell32.dll")] private static extern int SHGetPropertyStoreForWindow(IntPtr hwnd, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out IPropertyStore store);
    [DllImport("ole32.dll")] private static extern int PropVariantClear(ref PropVariant value);
    public static void Initialize() => Marshal.ThrowExceptionForHR(SetCurrentProcessExplicitAppUserModelID(LaunchProtocol.AppId));
    public static void Configure(IntPtr hwnd, string launcher)
    {
        var store = Open(hwnd);
        try
        {
            Set(store, 2, $"\"{launcher}\"");
            Set(store, 3, launcher + ",0");
            Set(store, 4, LaunchProtocol.DisplayName);
            Set(store, 5, LaunchProtocol.AppId);
            Marshal.ThrowExceptionForHR(store.Commit());
        }
        finally { Marshal.ReleaseComObject(store); }
    }
    internal static string? Read(IntPtr hwnd, uint id)
    {
        var store = Open(hwnd); var key = new PropertyKey { FormatId = Format, Id = id };
        try
        {
            Marshal.ThrowExceptionForHR(store.GetValue(ref key, out var value));
            try { return value.Type == 31 ? Marshal.PtrToStringUni(value.Pointer) : null; }
            finally { PropVariantClear(ref value); }
        }
        finally { Marshal.ReleaseComObject(store); }
    }
    private static IPropertyStore Open(IntPtr hwnd)
    {
        var iid = typeof(IPropertyStore).GUID;
        Marshal.ThrowExceptionForHR(SHGetPropertyStoreForWindow(hwnd, ref iid, out var store));
        return store;
    }
    private static void Set(IPropertyStore store, uint id, string text)
    {
        var key = new PropertyKey { FormatId = Format, Id = id };
        var value = new PropVariant { Type = 31, Pointer = Marshal.StringToCoTaskMemUni(text) };
        try { Marshal.ThrowExceptionForHR(store.SetValue(ref key, ref value)); }
        finally { PropVariantClear(ref value); }
    }
    [StructLayout(LayoutKind.Sequential)] private struct PropertyKey { public Guid FormatId; public uint Id; }
    [StructLayout(LayoutKind.Sequential)] private struct PropVariant
    {
        public ushort Type, Reserved1, Reserved2, Reserved3;
        public IntPtr Pointer, Padding;
    }
    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
        [PreserveSig] int SetValue(ref PropertyKey key, ref PropVariant value);
        [PreserveSig] int Commit();
    }
}
