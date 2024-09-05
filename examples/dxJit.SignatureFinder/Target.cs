using System.Numerics;

namespace dxJit.SignatureFinder;

public static class Target
{
    public static void set_Vector(ref object value)
    {
        // var _vector = (dynamic) value;
        //
        // _vector.X = 1024;
        // _vector.Y = 1024;
        //
        // value = _vector;
    }
}