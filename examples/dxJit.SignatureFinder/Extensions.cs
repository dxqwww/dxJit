using dnlib.DotNet;

namespace dxJit.SignatureFinder;

public static class Extensions
{
    public static ITypeDefOrRef? GetScopeType(this ITypeDefOrRef? type) {
        if (type is TypeSpec ts) {
            var sig = ts.TypeSig.RemovePinnedAndModifiers();
            if (sig is GenericInstSig gis)
                return gis.GenericType?.TypeDefOrRef;
            if (sig is TypeDefOrRefSig tdrs)
                return tdrs.TypeDefOrRef;
        }
        return type;
    }
}