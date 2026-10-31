# Только чтение полного пути процесса. WMI может скрывать путь клиента с античитом.
function Get-MreProcessPath($ProcessInfo) {
    if ($ProcessInfo.ExecutablePath) { return [string]$ProcessInfo.ExecutablePath }
    if (-not ('MreProcessPathNative' -as [type])) {
        Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Text;
public static class MreProcessPathNative {
    [DllImport("kernel32.dll", SetLastError=true)] private static extern IntPtr OpenProcess(uint access, bool inherit, int id);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)] private static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder path, ref int size);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    public static string Read(int id) {
        IntPtr handle=OpenProcess(0x1000,false,id); // PROCESS_QUERY_LIMITED_INFORMATION
        if(handle==IntPtr.Zero)return null;
        try { var path=new StringBuilder(32768); int size=path.Capacity; return QueryFullProcessImageName(handle,0,path,ref size)?path.ToString():null; }
        finally { CloseHandle(handle); }
    }
}
'@
    }
    return [MreProcessPathNative]::Read([int]$ProcessInfo.ProcessId)
}
