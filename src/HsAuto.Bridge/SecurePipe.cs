using System;
using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
namespace HsAuto.UnityBridge {
// Explicit Windows DACL: only the current Windows user can connect. No TCP listener.
internal static class SecurePipe {
    [StructLayout(LayoutKind.Sequential)] struct SecurityAttributes { public int Length; public IntPtr Descriptor; public bool Inherit; }
    [DllImport("advapi32.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern bool ConvertStringSecurityDescriptorToSecurityDescriptor(string sddl,uint revision,out IntPtr descriptor,out uint length);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern SafePipeHandle CreateNamedPipe(string name,uint openMode,uint pipeMode,uint maxInstances,uint outSize,uint inSize,uint timeout,ref SecurityAttributes attributes);
    [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr memory);
    [DllImport("kernel32.dll")] static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
    [DllImport("advapi32.dll",SetLastError=true)] static extern bool OpenProcessToken(IntPtr process,uint access,out IntPtr token);
    [DllImport("advapi32.dll",SetLastError=true)] static extern bool GetTokenInformation(IntPtr token,int kind,IntPtr buffer,int length,out int required);
    [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool ConvertSidToStringSid(IntPtr sid,out IntPtr text);
    static string UserSid() {
        if(!OpenProcessToken(GetCurrentProcess(),8,out var token)) throw new Win32Exception(Marshal.GetLastWin32Error());
        try {
            GetTokenInformation(token,1,IntPtr.Zero,0,out var length);
            var buffer=Marshal.AllocHGlobal(length);
            try {
                if(!GetTokenInformation(token,1,buffer,length,out length) || !ConvertSidToStringSid(Marshal.ReadIntPtr(buffer),out var text)) throw new Win32Exception(Marshal.GetLastWin32Error());
                try {return Marshal.PtrToStringUni(text) ?? throw new InvalidOperationException("Windows SID unavailable.");} finally {LocalFree(text);}
            } finally {Marshal.FreeHGlobal(buffer);}
        } finally {CloseHandle(token);}
    }
    public static NamedPipeServerStream Create(string name) {
        string sid = UserSid();
        if(!ConvertStringSecurityDescriptorToSecurityDescriptor("D:P(A;;GA;;;"+sid+")",1,out var descriptor,out var _)) throw new Win32Exception(Marshal.GetLastWin32Error());
        try {
            var attrs = new SecurityAttributes { Length=Marshal.SizeOf(typeof(SecurityAttributes)), Descriptor=descriptor, Inherit=false };
            var handle=CreateNamedPipe(@"\\.\pipe\"+name,0x40000003,0x8,1,65536,65536,0,ref attrs);
            if(handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
            return new NamedPipeServerStream(PipeDirection.InOut,true,false,handle);
        } finally { LocalFree(descriptor); }
    }
}
}

