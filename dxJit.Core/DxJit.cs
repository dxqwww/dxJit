using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Policy;

namespace dxJit.Core;

public abstract class DxJit : IDisposable
{
    #region Constants (private)

    // note @dxqwww:
    // see reference: https://github.com/dotnet/runtime/blob/88f9aba91e11d1695ebe9ab572736ee62ac7ad61/src/coreclr/inc/corjit.h#L114-L152
    //
    // ICorJitCompiler vtable indices
    private const int ICorJitCompiler_compileMethod_index = 0;

    #endregion

    #region Static fields (private)
    
    private static readonly byte[] DelegateTrampolineCode32 =
    {
        // mov eax, 00000000h ;Pointer address to _overridedCompileMethodPtr
        0x48, 0xB8, 0x00, 0x00, 0x00, 0x00,
        // jmp eax
        0xFF, 0xE0
    };

    private static readonly byte[] DelegateTrampolineCode64 =
    {
        // mov rax, 0000000000000000h ;Pointer address to _overridedCompiledMethodPtr
        0x48, 0xB8, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        // jmp rax
        0xFF, 0xE0
    };

    private static byte[] DelegateTrampolineCode => _bitness == 32 ? DelegateTrampolineCode32 : DelegateTrampolineCode64;

    #endregion

    #region Fields (protected)
    
    protected readonly IntPtr JitVTable;
    protected readonly IntPtr JitCompileMethodPtr;
    protected IntPtr _overridedCompileMethodPtr;

    protected readonly CompileMethodDelegate JitCompileMethod;
    protected CompileMethodDelegate _overridedCompileMethod;

    protected static readonly int _bitness = IntPtr.Size * 8;
    protected readonly object _locker;

    protected bool _isHooked;
    protected bool _isDisposed;

    #endregion

    #region Constructor (protected)

    protected DxJit()
    {
        _locker = new object();

        var process = Process.GetCurrentProcess();

        foreach (ProcessModule module in process.Modules)
        {
            if (module.ModuleName != "clrjit.dll")
                continue;

            var jitAddress = Native.GetProcAddress(module.BaseAddress, "getJit");
            if (jitAddress == IntPtr.Zero)
                throw new Exception("Cannot find getJit address!");

            var getJit = (GetJitDelegate)Marshal.GetDelegateForFunctionPointer(jitAddress, typeof(GetJitDelegate));
            var jit = getJit();
            if (jit == IntPtr.Zero)
                throw new Exception("Cannot get jit!");

            JitVTable = Marshal.ReadIntPtr(jit);

            JitCompileMethod = GetUnmanagedDelegate<CompileMethodDelegate>(JitVTable,
                ICorJitCompiler_compileMethod_index, out JitCompileMethodPtr);

            break;
        }
    }

    #endregion

    #region Methods (public)

    public void Init()
    {
        if (JitCompileMethod is null)
            return;

        _overridedCompileMethod = MagicCompileMethod;
        _overridedCompileMethodPtr = Marshal.GetFunctionPointerForDelegate(_overridedCompileMethod);

        var trampolinePtr = AllocateTrampoline(_overridedCompileMethodPtr);
        var trampoline = GetUnmanagedDelegate<CompileMethodDelegate>(trampolinePtr);

        var methodInfo = default(Native.CORINFO_METHOD_INFO);
        trampoline(IntPtr.Zero, IntPtr.Zero, ref methodInfo, 0, out _, out _);
        FreeTrampoline(trampolinePtr);

        HookJitCompile(_overridedCompileMethodPtr);
    }

    public virtual void Dispose()
    {
        lock (_locker)
        {
            if (_isDisposed || _overridedCompileMethodPtr == IntPtr.Zero)
                return;

            UnhookJitCompile();
            _overridedCompileMethodPtr = IntPtr.Zero;
            _overridedCompileMethod = null;
            _isDisposed = true;
        }
    }

    #endregion

    #region Methods (protected)

    protected abstract int MagicCompileMethod(
        IntPtr thisPtr,
        IntPtr comp,
        ref Native.CORINFO_METHOD_INFO info,
        uint flags,
        out IntPtr nativeEntry,
        out int nativeSizeOfCode
    );

    protected void HookJitCompile(IntPtr compileMethodPtr)
    {
        Native.VirtualProtect(JitVTable + ICorJitCompiler_compileMethod_index, new IntPtr(IntPtr.Size),
            Native.MemoryProtection.ReadWrite,
            out var oldProtection);
        Marshal.WriteIntPtr(JitVTable, ICorJitCompiler_compileMethod_index, compileMethodPtr);
        Native.VirtualProtect(JitVTable + ICorJitCompiler_compileMethod_index, new IntPtr(IntPtr.Size), oldProtection,
            out _);

        _isHooked = true;
    }

    protected void UnhookJitCompile()
    {
        HookJitCompile(JitCompileMethodPtr);
        _isHooked = false;
    }

    #endregion

    #region Methods (private)

    private static T GetUnmanagedDelegate<T>(IntPtr vTable, int index, out IntPtr delegatePtr) where T : Delegate
    {
        delegatePtr = Marshal.ReadIntPtr(vTable, IntPtr.Size * index);

        return (T)Marshal.GetDelegateForFunctionPointer(delegatePtr, typeof(T));
    }
    
    private static T GetUnmanagedDelegate<T>(IntPtr delegatePtr) where T : Delegate => 
        (T)Marshal.GetDelegateForFunctionPointer(delegatePtr, typeof(T));

    private static IntPtr AllocateTrampoline(IntPtr ptr)
    {
        var jmpNative = Native.VirtualAlloc(IntPtr.Zero, DelegateTrampolineCode.Length, Native.AllocationType.Commit,
            Native.MemoryProtection.ExecuteReadWrite);

        Marshal.Copy(DelegateTrampolineCode, 0, jmpNative, DelegateTrampolineCode.Length);

        Marshal.WriteIntPtr(jmpNative, 2, ptr);
        return jmpNative;
    }

    private static void FreeTrampoline(IntPtr ptr) =>
        Native.VirtualFree(ptr, new IntPtr(DelegateTrampolineCode.Length), Native.FreeType.Release);

    #endregion

    #region Delegates (private)

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    protected delegate IntPtr GetJitDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    protected delegate int CompileMethodDelegate(
        IntPtr thisPtr,
        IntPtr comp,
        ref Native.CORINFO_METHOD_INFO info,
        uint flags,
        out IntPtr nativeEntry,
        out int nativeSizeOfCode
    );

    #endregion
}