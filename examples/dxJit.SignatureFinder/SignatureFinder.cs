using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using dxJit.Core;
using Iced.Intel;

namespace dxJit.SignatureFinder;

public class SignatureFinder : DxJit
{
    #region Static Fields (private)

    private static CompileEntry? _compileEntry;

    #endregion
        
    #region Fields (private)

    private MethodInfo _method;

    #endregion

    #region Constructors (public)

    public SignatureFinder(MethodInfo method) => _method = method;

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
            
            if (compileEntry.Count == 1)
            {
                if (_method.MethodHandle.Value == info.ftn)
                {
                    var bytes = new byte[nativeSizeOfCode];
                    var strBytes = new string[nativeSizeOfCode];

                    for (var i = 0; i < nativeSizeOfCode; i++)
                    {
                        bytes[i] = Marshal.ReadByte(nativeEntry + i);
                        strBytes[i] = bytes[i].ToString("X2");
                    }

                    var decoder = Decoder.Create(_bitness, bytes);
                    var instructions = decoder.ToList();
                    
                    Console.WriteLine(
                        $"Method {_method.DeclaringType?.Name ?? "Unknown"}.{_method.Name} - {instructions.Count} native instructions:");
                    
                    var strBytesIndex = 0;
                    foreach (var instruction in instructions)
                    {
                        var __strBytes = new List<string>();
                        
                        for (var i = 0; i < instruction.Length; i++) 
                            __strBytes.Add(strBytes[strBytesIndex++]);

                        if (instruction.IPRelativeMemoryAddress > 0)
                        {
                            var rawReversedAddress = string.Join("",
                                instruction.IPRelativeMemoryAddress.ToString("X8"));
                            
                            var k = 0;
                            var reversedBytes = rawReversedAddress
                                .ToLookup(c => Math.Floor(k++ / 2d))
                                .Select(e => string.Join("", e.ToArray())).Reverse().ToArray();
                    
                            for (var i = 0; i + reversedBytes.Length <= __strBytes.Count; i++)
                            {
                                var found = false;
                                
                                for (var j = 0; j < reversedBytes.Length; j++)
                                {
                                    if (reversedBytes[j] != __strBytes[i + j])
                                        break;
                    
                                    if (j + 1 == reversedBytes.Length)
                                    {
                                        for (var l = 0; l < reversedBytes.Length; l++) 
                                            __strBytes[i + j - l] = "??";
                    
                                        found = true;
                                        break;
                                    }
                                }
                    
                                if (found) 
                                    break;
                            }
                        }
                    
                        foreach (var b in __strBytes) 
                            Console.Write($"{b} ");
                    
                        Console.Write($"| {instruction}\n");
                    }
                }
            }

            return result;
        }
        finally
        {
            compileEntry.Count--;
        }
    }
        
    #endregion

    #region Classes (private)

    private class CompileEntry
    {
        public int Count;
    }

    #endregion 
}