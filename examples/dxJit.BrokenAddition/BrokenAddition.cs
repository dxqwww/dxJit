using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using dxJit.Core;

namespace dxJit.BrokenAddition;

public class BrokenAddition : DxJit
{
    #region Constants (private)

    // note @dxqwww:
    // see reference: https://github.com/dotnet/runtime/blob/88f9aba91e11d1695ebe9ab572736ee62ac7ad61/src/coreclr/inc/corinfo.h#L1958-L3028
    //
    // ICorJitCompiler vtable indices
    private const int ICorJitInfo_getMethodDefFromMethod_index = 116;
    private const int ICorJitInfo_getModuleAssembly_index = 48;
    private const int ICorJitInfo_getAssemblyName_index = 49;

    #endregion

    #region Static Fields (private)

    private readonly Dictionary<IntPtr, Assembly> MapHandleToAssembly;

    [ThreadStatic] private static CompileEntry _compileEntry;

    #endregion

    #region Fields (public)

    private readonly string _targetAdditionMethodName;

    #endregion

    #region Constructors (public)

    public BrokenAddition(string methodName)
    {
        _targetAdditionMethodName = methodName;
        MapHandleToAssembly = new Dictionary<IntPtr, Assembly>(IntPtrEqualityComparer.Instance);
    }

    #endregion

    #region Methods (protected)

    protected override int MagicCompileMethod(
        IntPtr thisPtr,
        IntPtr comp,
        ref Native.CORINFO_METHOD_INFO info,
        uint flags,
        out IntPtr nativeEntry,
        out int nativeSizeOfCode
    )
    {
        var compileEntry = _compileEntry ??= new CompileEntry();
        compileEntry.Count++;

        try
        {
            if (JitCompileMethod is null || !_isHooked || thisPtr == IntPtr.Zero)
            {
                nativeEntry = default;
                nativeSizeOfCode = 0;
                return 0;
            }

            var result = JitCompileMethod(thisPtr, comp, ref info, flags, out nativeEntry, out nativeSizeOfCode);

            if (compileEntry.Count != 1)
                return result;

            var corJitInfoVTable = Marshal.ReadIntPtr(comp);

            var getMethodDefFromMethodPtr = Marshal.ReadIntPtr(corJitInfoVTable,
                IntPtr.Size * ICorJitInfo_getMethodDefFromMethod_index);
            var getMethodDefFromMethod =
                (GetMethodDefFromMethodDelegate)Marshal.GetDelegateForFunctionPointer(getMethodDefFromMethodPtr,
                    typeof(GetMethodDefFromMethodDelegate));


            var methodToken = getMethodDefFromMethod(comp, info.ftn);

            var getModuleAssemblyPtr =
                Marshal.ReadIntPtr(corJitInfoVTable, IntPtr.Size * ICorJitInfo_getModuleAssembly_index);
            var getModuleAssembly = (GetModuleAssemblyDelegate)Marshal.GetDelegateForFunctionPointer(
                getModuleAssemblyPtr, typeof(GetModuleAssemblyDelegate));
            var assemblyHandle = getModuleAssembly(comp, info.scope);

            Assembly? foundAssembly;

            lock (_locker)
            {
                if (!MapHandleToAssembly.TryGetValue(assemblyHandle, out foundAssembly))
                {
                    var getAssemblyNamePtr = Marshal.ReadIntPtr(corJitInfoVTable,
                        IntPtr.Size * ICorJitInfo_getAssemblyName_index);
                    var getAssemblyName = (GetAssemblyNameDelegate)Marshal.GetDelegateForFunctionPointer(
                        getAssemblyNamePtr, typeof(GetAssemblyNameDelegate));
                    var assemblyNamePtr = getAssemblyName(comp, assemblyHandle);

                    var assemblyName = Marshal.PtrToStringAnsi(assemblyNamePtr);

                    foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        if (assembly.GetName().Name != assemblyName)
                            continue;

                        foundAssembly = assembly;
                        break;
                    }

                    if (foundAssembly is null)
                        throw new Exception($"Assembly [{assemblyName}] has not found!");

                    MapHandleToAssembly.Add(assemblyHandle, foundAssembly);
                }
            }


            MethodBase? method = null;
            foreach (var module in foundAssembly.Modules)
            {
                try
                {
                    method = module.ResolveMethod(methodToken);
                }
                catch (Exception)
                {
                    // ignored
                }
            }

            if (method is not null)
                MagicReplaceCompile(method, info.ILCode, info.ILCodeSize, nativeEntry, nativeSizeOfCode);

            return result;
        }
        finally
        {
            compileEntry.Count--;
        }
    }

    #endregion

    #region Methods (private)

    private void MagicReplaceCompile(MethodBase method, IntPtr ilCodePtr, int ilSize, IntPtr nativeCodePtr,
        int nativeCodeSize)
    {
        if (method.Name != _targetAdditionMethodName) // Program.Add(int, int) int
            return;

        // 0:  01 d1                   add    ecx,edx
        // 2:  89 c8                   mov    eax,ecx
        // 4:  40                      inc    eax
        // 5:  c3                      ret

        var instructions = new byte[]
        {
            0x01, 0xD1, 0x89, 0xC8, 0xFF, 0xC0, 0xC3
        };

        Marshal.Copy(instructions, 0, nativeCodePtr, instructions.Length);
    }

    #endregion

    #region Delegates (private)

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int GetMethodDefFromMethodDelegate(IntPtr thisPtr, IntPtr hMethodHandle);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr GetModuleAssemblyDelegate(IntPtr thisPtr, IntPtr moduleHandle);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr GetAssemblyNameDelegate(IntPtr thisPtr, IntPtr assemblyHandle);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr GetMethodAttribsDelegate(IntPtr thisPtr, IntPtr ftn);

    #endregion

    #region Classes (private)

    private class CompileEntry
    {
        public int Count;
    }

    private class IntPtrEqualityComparer : IEqualityComparer<IntPtr>
    {
        public static readonly IntPtrEqualityComparer Instance = new();

        public bool Equals(IntPtr x, IntPtr y) => x == y;

        public int GetHashCode(IntPtr obj) => obj.GetHashCode();
    }

    #endregion
}