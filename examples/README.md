# Examples

I've put together a few examples of how you can get the JIT compiler to do what you want.

## Broken Addition

We're going to tweak the way a method that takes two integers `(int, int)` so that the result is always one more than we're expecting:

```csharp
internal class Program
{
    private static int Add(int x, int y) => x + y;

    public static void Main(string[] args)
    {
        var brokenAddition = new BrokenAddition(nameof(Add));
        
        brokenAddition.Init();
        
        var result = Add(2, 2);

        Console.WriteLine(result); // 5
    }
}
```

# Signature Finder (WIP)

There is a way to literally get all the native x86 instructions from the method:

```csharp
internal static class Program
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
```