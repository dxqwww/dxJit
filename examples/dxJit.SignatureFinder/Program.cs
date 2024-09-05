using System;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace dxJit.SignatureFinder;

public static class Program
{
    public static void Main(string[] args)
    {
        var assembly = Assembly.LoadFrom(@"path/to/application.exe");

        var targetType = assembly.GetType("TypeName");
        var targetMethod = targetType.GetMethod("MethodName", (BindingFlags)~0);

        if (targetMethod is null)
            throw new Exception($"Method is null");
            
        using var sigFinder = new SignatureFinder(targetMethod);

        sigFinder.Init();

        RuntimeHelpers.PrepareMethod(targetMethod.MethodHandle);
    }
}