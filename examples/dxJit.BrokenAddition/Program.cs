using System;

namespace dxJit.BrokenAddition;

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