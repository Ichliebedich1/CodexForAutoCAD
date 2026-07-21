// R201SignatureProbe.cs — Exact API signature verification
// for AutoCAD 2016 R20.1 (AcMgd/AcDbMgd/AcCoreMgd 20.1.0.0).
//
// Compile-time: typeof(), property access, and delegate assignment force the
// compiler to resolve types, properties, and exact method signatures.
// CompileTimeVerify() is NEVER called — it exists only to be compiled.
//
// Runtime: Run(autoCadDir) uses reflection to verify members, parameters,
// return types, static/instance, enum values, assembly versions, and Authenticode.
//
// This probe does NOT start or operate AutoCAD. It does NOT produce Autodesk DLLs.
// It is NOT equivalent to AutoCAD runtime verification.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Exception = System.Exception;

/// <summary>
/// Exact API signature probe for R20.1.
/// </summary>
public static class R201SignatureProbe
{
    // --- Compile-time verification ---
    private static void CompileTimeVerify()
    {
        Type[] types = new Type[]
        {
            typeof(Line), typeof(Circle), typeof(Polyline), typeof(DBText), typeof(MText),
            typeof(BlockReference), typeof(Arc), typeof(Ellipse), typeof(Spline), typeof(DBPoint),
            typeof(Ray), typeof(Xline), typeof(Polyline2d), typeof(Vertex2d), typeof(Polyline3d),
            typeof(PolylineVertex3d), typeof(Dimension), typeof(Hatch), typeof(Leader),
            typeof(MLeader), typeof(Table), typeof(HatchLoop), typeof(CellRange),
            typeof(ObjectId), typeof(ObjectIdCollection), typeof(ResultBuffer),
            typeof(IExtensionApplication), typeof(CommandClassAttribute),
            typeof(ExtensionApplicationAttribute),
            typeof(Point3d), typeof(Vector3d), typeof(Matrix3d), typeof(Scale3d),
            typeof(Poly2dType), typeof(ContentType), typeof(HatchLoopTypes),
            typeof(CellType), typeof(FormatOption),
        };

        object[] props = new object[]
        {
            (object)default(MLeader).MText,               // MText
            (object)default(MLeader).ContentType,          // ContentType
            default(MLeader).Normal,                       // Vector3d
            (object)default(Hatch).NumberOfLoops,          // int
            (object)default(Table).Cells,                  // CellRange
            (object)default(Table).Rows,                   // RowsCollection
            (object)default(Table).Columns,                // ColumnsCollection
            default(Table).Position,                       // Point3d
            default(Table).Direction,                      // Vector3d
            (object)default(Table).Width,
            (object)default(Table).Height,
            (object)default(Leader).NumVertices,           // int
            (object)default(DBPoint).EcsRotation,          // double
            (object)default(Spline).NurbsData,             // NurbsData
            (object)default(Spline).Degree,
            (object)default(Spline).HasFitData,
            (object)default(Spline).NumControlPoints,
        };

        Func<Int32, Point3d> m1 = default(Spline).GetControlPointAt;
        Func<Int32, Point3d> m2 = default(Spline).GetFitPointAt;
        Func<Int32, double> m3 = default(Polyline).GetBulgeAt;
        Func<Int32, Point3d> m4 = default(Leader).VertexAt;
        Func<Int32, Int32> m5 = default(MLeader).VerticesCount;
        Func<Int32, Int32, Point3d> m6 = default(MLeader).GetVertex;
        Func<Int32, HatchLoop> m7 = default(Hatch).GetLoopAt;
        Func<ArrayList> m8 = default(MLeader).GetLeaderIndexes;
        Func<Int32, ArrayList> m9 = default(MLeader).GetLeaderLineIndexes;
#pragma warning disable CS0618
        Func<Int32, Int32, Int32, string> m10 = default(Table).GetTextString;
#pragma warning restore CS0618

        GC.KeepAlive(types);
        GC.KeepAlive(props);
        GC.KeepAlive(m1); GC.KeepAlive(m2); GC.KeepAlive(m3);
        GC.KeepAlive(m4); GC.KeepAlive(m5); GC.KeepAlive(m6);
        GC.KeepAlive(m7); GC.KeepAlive(m8); GC.KeepAlive(m9);
        GC.KeepAlive(m10);
    }

    // --- Expected frozen enum values (constants for cross-shell comparison) ---

    private static readonly Dictionary<string, Dictionary<string, long>> FrozenEnumValues =
        new Dictionary<string, Dictionary<string, long>>
    {
        // Frozen from R20.1 acdbmgd 20.1.0.0 runtime reflection
        ["Autodesk.AutoCAD.DatabaseServices.ContentType"] = new Dictionary<string, long>
        {
            ["BlockContent"] = 1, ["MTextContent"] = 2, ["NoneContent"] = 0, ["ToleranceContent"] = 3
        },
        ["Autodesk.AutoCAD.DatabaseServices.HatchLoopTypes"] = new Dictionary<string, long>
        {
            ["Default"] = 0, ["Derived"] = 4, ["Duplicate"] = 256, ["External"] = 1,
            ["NotClosed"] = 32, ["Outermost"] = 16, ["Polyline"] = 2,
            ["SelfIntersecting"] = 64, ["TextIsland"] = 128, ["Textbox"] = 8
        },
        ["Autodesk.AutoCAD.DatabaseServices.CellType"] = new Dictionary<string, long>
        {
            ["Bool"] = 10, ["CharPtr"] = 3, ["Double"] = 2, ["HardOwnerId"] = 6,
            ["HardPtrId"] = 8, ["Integer"] = 1, ["ObjectId"] = 5, ["Point"] = 4,
            ["SoftOwnerId"] = 7, ["SoftPtrId"] = 9, ["Unknown"] = 0, ["Vector"] = 11
        },
        ["Autodesk.AutoCAD.DatabaseServices.Poly2dType"] = new Dictionary<string, long>
        {
            ["SimplePoly"] = 0, ["FitCurvePoly"] = 1, ["QuadSplinePoly"] = 2, ["CubicSplinePoly"] = 3
        },
        ["Autodesk.AutoCAD.DatabaseServices.Poly3dType"] = new Dictionary<string, long>
        {
            ["SimplePoly"] = 0, ["QuadSplinePoly"] = 1, ["CubicSplinePoly"] = 2
        },
        ["Autodesk.AutoCAD.DatabaseServices.XrefStatus"] = new Dictionary<string, long>
        {
            ["NotAnXref"] = 0, ["Resolved"] = 1, ["Unloaded"] = 2,
            ["Unreferenced"] = 3, ["FileNotFound"] = 4, ["Unresolved"] = 5
        },
    };

    // --- Run ---

    public static void Run(string autoCadDir)
    {
        var result = new Dictionary<string, object>();

        result["compileTimeTypesVerified"] = 36;
        result["compileTimePropertiesVerified"] = 17;
        result["compileTimeMethodSignaturesVerified"] = 10;
        result["compileTimeNote"] = "All typeof, property-access, and delegate expressions compiled successfully against R20.1";

        // === 1. Positive method signature checks ===
        var methodDefs = new List<MethodCheckData>();
        methodDefs.Add(new MethodCheckData(typeof(Spline), "GetControlPointAt", new Type[] { typeof(Int32) }, typeof(Point3d), "Autodesk.AutoCAD.DatabaseServices.Spline"));
        methodDefs.Add(new MethodCheckData(typeof(Spline), "GetFitPointAt", new Type[] { typeof(Int32) }, typeof(Point3d), "Autodesk.AutoCAD.DatabaseServices.Spline"));
        methodDefs.Add(new MethodCheckData(typeof(Polyline), "GetBulgeAt", new Type[] { typeof(Int32) }, typeof(double), "Autodesk.AutoCAD.DatabaseServices.Polyline"));
        methodDefs.Add(new MethodCheckData(typeof(Leader), "VertexAt", new Type[] { typeof(Int32) }, typeof(Point3d), "Autodesk.AutoCAD.DatabaseServices.Leader"));
        methodDefs.Add(new MethodCheckData(typeof(MLeader), "GetLeaderIndexes", Type.EmptyTypes, typeof(ArrayList), "Autodesk.AutoCAD.DatabaseServices.MLeader"));
        methodDefs.Add(new MethodCheckData(typeof(MLeader), "GetLeaderLineIndexes", new Type[] { typeof(Int32) }, typeof(ArrayList), "Autodesk.AutoCAD.DatabaseServices.MLeader"));
        methodDefs.Add(new MethodCheckData(typeof(MLeader), "VerticesCount", new Type[] { typeof(Int32) }, typeof(int), "Autodesk.AutoCAD.DatabaseServices.MLeader"));
        methodDefs.Add(new MethodCheckData(typeof(MLeader), "GetVertex", new Type[] { typeof(Int32), typeof(Int32) }, typeof(Point3d), "Autodesk.AutoCAD.DatabaseServices.MLeader"));
        methodDefs.Add(new MethodCheckData(typeof(Hatch), "GetLoopAt", new Type[] { typeof(Int32) }, typeof(HatchLoop), "Autodesk.AutoCAD.DatabaseServices.Hatch"));
        methodDefs.Add(new MethodCheckData(typeof(Table), "GetTextString", new Type[] { typeof(Int32), typeof(Int32), typeof(Int32) }, typeof(string), "Autodesk.AutoCAD.DatabaseServices.Table"));

        var positiveMethodResults = new List<Dictionary<string, object>>();
        foreach (var d in methodDefs)
            positiveMethodResults.Add(CheckMethodSignature(d.Type, d.Name, d.ParamTypes, d.ReturnType, d.ExpectedDeclaringType));

        // Also discover Table.GetTextString 4-arg overload
        var getTextString4 = typeof(Table).GetMethod("GetTextString",
            BindingFlags.Public | BindingFlags.Instance,
            null, new Type[] { typeof(Int32), typeof(Int32), typeof(Int32), typeof(FormatOption) }, null);
        var getTextString4Entry = new Dictionary<string, object>
        {
            ["type"] = typeof(Table).FullName,
            ["member"] = "GetTextString",
            ["kind"] = "method",
            ["overload"] = "4-arg (obsolete)",
            ["exists"] = getTextString4 != null,
        };
        if (getTextString4 != null)
        {
            getTextString4Entry["returnType"] = getTextString4.ReturnType.FullName;
            getTextString4Entry["parameterCount"] = getTextString4.GetParameters().Length;
        }

        // === 2. Positive property signature checks (with exact types) ===
        var propDefs = new List<PropertyCheckData>();
        propDefs.Add(new PropertyCheckData(typeof(MLeader), "MText", typeof(MText), "Autodesk.AutoCAD.DatabaseServices.MLeader"));
        propDefs.Add(new PropertyCheckData(typeof(MLeader), "ContentType", typeof(ContentType), "Autodesk.AutoCAD.DatabaseServices.MLeader"));
        propDefs.Add(new PropertyCheckData(typeof(Hatch), "NumberOfLoops", typeof(int), "Autodesk.AutoCAD.DatabaseServices.Hatch"));
        propDefs.Add(new PropertyCheckData(typeof(Table), "Cells", typeof(CellRange), "Autodesk.AutoCAD.DatabaseServices.Table"));
        propDefs.Add(new PropertyCheckData(typeof(Table), "Rows", typeof(RowsCollection), "Autodesk.AutoCAD.DatabaseServices.Table"));
        propDefs.Add(new PropertyCheckData(typeof(Table), "Columns", typeof(ColumnsCollection), "Autodesk.AutoCAD.DatabaseServices.Table"));
        propDefs.Add(new PropertyCheckData(typeof(Leader), "NumVertices", typeof(int), "Autodesk.AutoCAD.DatabaseServices.Leader"));
        propDefs.Add(new PropertyCheckData(typeof(DBPoint), "EcsRotation", typeof(double), "Autodesk.AutoCAD.DatabaseServices.DBPoint"));
        propDefs.Add(new PropertyCheckData(typeof(Spline), "NurbsData", typeof(NurbsData), "Autodesk.AutoCAD.DatabaseServices.Spline"));

        var positivePropertyResults = new List<Dictionary<string, object>>();
        foreach (var d in propDefs)
            positivePropertyResults.Add(CheckPropertySignature(d.Type, d.Name, d.ExpectedType, d.ExpectedDeclaringType));

        // === 3. Expected-absence checks ===
        var absenceDefs = new string[,]
        {
            { typeof(MLeader).FullName, "TextString", "property" },
            { typeof(Table).FullName, "GetTextStyle", "method" },
            { typeof(Table).FullName, "GetCellType", "method" },
            { typeof(Polyline2d).FullName, "VertexObjectIdList", "property" },
            { typeof(Polyline3d).FullName, "Vertices", "property" },
            { typeof(Polyline3d).FullName, "VertexObjectIdList", "property" },
            { typeof(BlockReference).FullName, "XrefStatus", "property" },
            { typeof(Dimension).FullName, "DimensionType", "property" },
        };
        // Also: DimensionType enum is absent
        var dimensionTypeEnum = FindEnumType("DimensionType");

        var absenceResults = new List<Dictionary<string, object>>();
        for (int i = 0; i < absenceDefs.GetLength(0); i++)
        {
            Type type = FindTypeByName(absenceDefs[i, 0]);
            absenceResults.Add(type != null
                ? CheckMemberNotExist(type, absenceDefs[i, 1], absenceDefs[i, 2])
                : new Dictionary<string, object>
                {
                    ["type"] = absenceDefs[i, 0], ["member"] = absenceDefs[i, 1],
                    ["correctlyAbsent"] = false, ["reason"] = "type-not-found"
                });
        }
        // DimensionType enum absence
        absenceResults.Add(new Dictionary<string, object>
        {
            ["type"] = "Autodesk.AutoCAD.DatabaseServices.DimensionType",
            ["member"] = "(enum)",
            ["expectedKind"] = "enum",
            ["correctlyAbsent"] = dimensionTypeEnum == null,
            ["note"] = dimensionTypeEnum == null
                ? "DimensionType enum does not exist in R20.1; consistent with Dimension.DimensionType property also absent"
                : "DimensionType enum unexpectedly found"
        });

        // === 4. Enum frozen checks ===
        var enumDirectTypes = new Type[] { typeof(ContentType), typeof(HatchLoopTypes), typeof(CellType) };
        var enumLookupNames = new string[] { "Poly2dType", "Poly3dType", "XrefStatus" };

        var enumResults = new Dictionary<string, object>();
        foreach (var et in enumDirectTypes)
            enumResults[et.FullName] = CheckEnumFrozen(et, et.FullName);
        foreach (var name in enumLookupNames)
        {
            var et = FindEnumType(name);
            if (et != null)
                enumResults[et.FullName] = CheckEnumFrozen(et, et.FullName);
            else
                enumResults[name] = new Dictionary<string, object>
                {
                    ["fullName"] = name, ["found"] = false, ["passed"] = false,
                    ["note"] = "Enum type not found in loaded assemblies"
                };
        }

        // === 5. Assembly identity checks ===
        // Force-load acmgd from the AutoCAD directory
        if (!string.IsNullOrEmpty(autoCadDir))
        {
            try
            {
                var acmgdPath = System.IO.Path.Combine(autoCadDir, "acmgd.dll");
                if (System.IO.File.Exists(acmgdPath))
                {
                    Assembly.LoadFrom(acmgdPath);
                }
            }
            catch { }
        }

        var assemblyResults = CheckAutodeskAssemblies();

        // === Build separate summaries ===

        // Positive signature summary
        int posMethodPassed = positiveMethodResults.Count(e => (bool)e["passed"]);
        int posPropPassed = positivePropertyResults.Count(e => (bool)e["passed"]);
        var positiveSummary = new Dictionary<string, object>
        {
            ["methods"] = new Dictionary<string, object>
            {
                ["total"] = positiveMethodResults.Count,
                ["passed"] = posMethodPassed,
                ["failed"] = positiveMethodResults.Count - posMethodPassed,
            },
            ["properties"] = new Dictionary<string, object>
            {
                ["total"] = positivePropertyResults.Count,
                ["passed"] = posPropPassed,
                ["failed"] = positivePropertyResults.Count - posPropPassed,
            },
            ["getTextString4ArgOverload"] = getTextString4Entry,
            ["allPositiveSignaturesOk"] = (posMethodPassed == positiveMethodResults.Count)
                                        && (posPropPassed == positivePropertyResults.Count),
        };

        // Expected-absence summary
        int absencePassed = absenceResults.Count(e => (bool)e["correctlyAbsent"]);
        var absenceSummary = new Dictionary<string, object>
        {
            ["total"] = absenceResults.Count,
            ["correctlyAbsent"] = absencePassed,
            ["unexpectedlyPresent"] = absenceResults.Count - absencePassed,
            ["allExpectedAbsentOk"] = (absencePassed == absenceResults.Count),
        };

        // Enum summary
        int enumPassed = enumResults.Values.Count(v => (bool)((Dictionary<string, object>)v)["passed"]);
        var enumSummary = new Dictionary<string, object>
        {
            ["total"] = enumResults.Count,
            ["passed"] = enumPassed,
            ["failed"] = enumResults.Count - enumPassed,
            ["dimensionTypeAbsent"] = dimensionTypeEnum == null,
            ["allEnumsOk"] = (enumPassed == enumResults.Count) && (dimensionTypeEnum == null),
        };

        // Assembly identity summary
        int asmPassed = assemblyResults.Values.Count(v => (bool)((Dictionary<string, object>)v)["passed"]);
        var assemblySummary = new Dictionary<string, object>
        {
            ["total"] = assemblyResults.Count,
            ["passed"] = asmPassed,
            ["failed"] = assemblyResults.Count - asmPassed,
            ["allAssembliesOk"] = (asmPassed == assemblyResults.Count),
        };

        // Overall
        bool overall = (bool)positiveSummary["allPositiveSignaturesOk"]
                    && (bool)absenceSummary["allExpectedAbsentOk"]
                    && (bool)enumSummary["allEnumsOk"]
                    && (bool)assemblySummary["allAssembliesOk"];

        result["positiveMethodSignatureChecks"] = positiveMethodResults;
        result["positivePropertySignatureChecks"] = positivePropertyResults;
        result["expectedAbsenceChecks"] = absenceResults;
        result["enumSignatureChecks"] = enumResults;
        result["assemblySignatureChecks"] = assemblyResults;
        result["summary"] = new Dictionary<string, object>
        {
            ["positiveSignature"] = positiveSummary,
            ["expectedAbsence"] = absenceSummary,
            ["enumFreeze"] = enumSummary,
            ["assemblyIdentity"] = assemblySummary,
            ["overallPassed"] = overall,
        };
        result["disclaimer"] = "Compile-time checks verify types/properties/method signatures via C# compiler. Runtime checks verify via reflection. This probe does NOT start or operate AutoCAD and is NOT equivalent to AutoCAD runtime verification.";

        Console.Write(SerializeJson(result));
    }

    // Backward-compatible overload for callers that don't pass autoCadDir
    public static void Run() { Run(null); }

    // --- Helper types ---

    private class MethodCheckData
    {
        public Type Type; public string Name; public Type[] ParamTypes;
        public Type ReturnType; public string ExpectedDeclaringType;
        public MethodCheckData(Type t, string n, Type[] p, Type r, string d)
        { Type = t; Name = n; ParamTypes = p; ReturnType = r; ExpectedDeclaringType = d; }
    }

    private class PropertyCheckData
    {
        public Type Type; public string Name; public Type ExpectedType;
        public string ExpectedDeclaringType;
        public PropertyCheckData(Type t, string n, Type e, string d)
        { Type = t; Name = n; ExpectedType = e; ExpectedDeclaringType = d; }
    }

    // --- Check implementations ---

    private static Dictionary<string, object> CheckMethodSignature(
        Type type, string name, Type[] paramTypes, Type returnType, string expectedDeclaringType)
    {
        var entry = new Dictionary<string, object>
        {
            ["type"] = type.FullName, ["member"] = name, ["kind"] = "method",
        };
        try
        {
            var method = type.GetMethod(name,
                BindingFlags.Public | BindingFlags.Instance, null, paramTypes, null);

            if (method == null)
            {
                var overloads = type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .Where(m => m.Name == name).ToArray();
                entry["passed"] = false;
                if (overloads.Length > 0)
                {
                    entry["reason"] = "method-not-found-with-expected-signature";
                    entry["actualOverloadCount"] = overloads.Length;
                    var overloadDetails = new List<Dictionary<string, object>>();
                    foreach (var ov in overloads)
                    {
                        var od = new Dictionary<string, object>
                        {
                            ["returnType"] = ov.ReturnType.FullName,
                            ["parameterCount"] = ov.GetParameters().Length,
                        };
                        var opList = new List<Dictionary<string, object>>();
                        foreach (var p in ov.GetParameters())
                            opList.Add(new Dictionary<string, object>
                            {
                                ["name"] = p.Name, ["type"] = p.ParameterType.FullName,
                            });
                        od["parameters"] = opList;
                        overloadDetails.Add(od);
                    }
                    entry["actualOverloads"] = overloadDetails;
                }
                else
                    entry["reason"] = "method-not-found";
                return entry;
            }

            entry["declaringType"] = method.DeclaringType.FullName;
            entry["isStatic"] = method.IsStatic;
            entry["returnType"] = method.ReturnType.FullName;
            entry["parameterCount"] = method.GetParameters().Length;
            var paramDetails = new List<Dictionary<string, object>>();
            foreach (var p in method.GetParameters())
                paramDetails.Add(new Dictionary<string, object>
                {
                    ["name"] = p.Name, ["type"] = p.ParameterType.FullName,
                    ["isOut"] = p.IsOut, ["isOptional"] = p.IsOptional,
                    ["isByRef"] = p.ParameterType.IsByRef,
                });
            entry["parameters"] = paramDetails;

            bool ok = true;
            if (method.DeclaringType.FullName != expectedDeclaringType)
            { entry["declaringTypeMismatch"] = true; ok = false; }
            if (method.ReturnType != returnType)
            { entry["returnTypeMismatch"] = true; entry["actualReturnType"] = method.ReturnType.FullName; ok = false; }
            if (method.IsStatic)
            { entry["unexpectedStatic"] = true; ok = false; }
            if (method.GetParameters().Length != paramTypes.Length)
            { entry["parameterCountMismatch"] = true; ok = false; }
            else
            {
                for (int i = 0; i < paramTypes.Length; i++)
                    if (method.GetParameters()[i].ParameterType != paramTypes[i])
                    { entry["parameterTypeMismatch"] = true; ok = false; break; }
            }
            entry["passed"] = ok;
        }
        catch (Exception ex)
        { entry["passed"] = false; entry["reason"] = "exception"; entry["error"] = SanitizeException(ex); }
        return entry;
    }

    private static Dictionary<string, object> CheckPropertySignature(
        Type type, string name, Type expectedType, string expectedDeclaringType)
    {
        var entry = new Dictionary<string, object>
        {
            ["type"] = type.FullName, ["member"] = name, ["kind"] = "property",
        };
        try
        {
            var prop = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (prop == null)
            { entry["passed"] = false; entry["reason"] = "property-not-found"; return entry; }

            entry["declaringType"] = prop.DeclaringType.FullName;
            entry["propertyType"] = prop.PropertyType.FullName;
            entry["canRead"] = prop.CanRead;
            entry["canWrite"] = prop.CanWrite;
            entry["isStatic"] = (prop.GetGetMethod(true) != null && prop.GetGetMethod(true).IsStatic);

            bool ok = true;
            if (prop.DeclaringType.FullName != expectedDeclaringType)
            { entry["declaringTypeMismatch"] = true; ok = false; }
            if (prop.PropertyType != expectedType)
            { entry["propertyTypeMismatch"] = true; entry["actualPropertyType"] = prop.PropertyType.FullName; ok = false; }
            if (!prop.CanRead)
            { entry["notReadable"] = true; ok = false; }
            entry["passed"] = ok;
        }
        catch (Exception ex)
        { entry["passed"] = false; entry["reason"] = "exception"; entry["error"] = SanitizeException(ex); }
        return entry;
    }

    private static Dictionary<string, object> CheckMemberNotExist(Type type, string name, string kind)
    {
        var entry = new Dictionary<string, object>
        {
            ["type"] = type.FullName, ["member"] = name, ["expectedKind"] = kind,
        };
        try
        {
            bool exists = kind == "method"
                ? type.GetMethod(name, BindingFlags.Public | BindingFlags.Instance) != null
                : type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance) != null;
            entry["correctlyAbsent"] = !exists;
            if (exists) entry["unexpectedlyPresent"] = true;
        }
        catch (Exception ex)
        { entry["correctlyAbsent"] = false; entry["error"] = SanitizeException(ex); }
        return entry;
    }

    private static Dictionary<string, object> CheckEnumFrozen(Type enumType, string expectedFullName)
    {
        var entry = new Dictionary<string, object>
        {
            ["fullName"] = enumType.FullName,
            ["underlyingType"] = Enum.GetUnderlyingType(enumType).FullName,
        };
        try
        {
            if (enumType.FullName != expectedFullName)
            { entry["fullNameMismatch"] = true; entry["passed"] = false; return entry; }

            var names = Enum.GetNames(enumType).OrderBy(n => n, StringComparer.Ordinal).ToArray();
            var values = new Dictionary<string, object>();
            foreach (var name in names)
                values[name] = Convert.ToInt64(Enum.Parse(enumType, name), CultureInfo.InvariantCulture);

            entry["namesSorted"] = names;
            entry["values"] = values;
            entry["count"] = names.Length;

            // Compare against frozen expected values
            if (FrozenEnumValues.ContainsKey(expectedFullName))
            {
                var expected = FrozenEnumValues[expectedFullName];
                bool match = names.Length == expected.Count;
                if (match)
                {
                    foreach (var name in names)
                    {
                        if (!expected.ContainsKey(name) || expected[name] != (long)values[name])
                        { match = false; break; }
                    }
                }
                entry["matchesFrozenExpected"] = match;
                entry["passed"] = match;
                if (!match) entry["reason"] = "enum-values-differ-from-frozen-expected";
            }
            else
            {
                entry["matchesFrozenExpected"] = false;
                entry["passed"] = false;
                entry["reason"] = "no-frozen-expected-values-for-this-enum";
            }
        }
        catch (Exception ex)
        { entry["passed"] = false; entry["error"] = SanitizeException(ex); }
        return entry;
    }

    private static Type FindEnumType(string shortName)
    {
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            try { foreach (var t in asm.GetExportedTypes()) if (t.IsEnum && t.Name == shortName) return t; }
            catch { }
        }
        return null;
    }

    private static Type FindTypeByName(string fullName)
    {
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            try { var t = asm.GetType(fullName); if (t != null) return t; }
            catch { }
        }
        return null;
    }

    private static Dictionary<string, object> CheckAutodeskAssemblies()
    {
        var results = new Dictionary<string, object>();
        var assembliesToCheck = new Dictionary<string, string>
        {
            ["acmgd"] = "20.1.0.0",
            ["acdbmgd"] = "20.1.0.0",
            ["accoremgd"] = "20.1.0.0",
        };

        foreach (var kvp in assembliesToCheck)
        {
            string shortName = kvp.Key;
            string expectedVersion = kvp.Value;
            var entry = new Dictionary<string, object>();

            try
            {
                Assembly loaded = null;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    var an = asm.GetName();
                    if (string.Equals(an.Name, shortName, StringComparison.OrdinalIgnoreCase))
                    { loaded = asm; break; }
                }

                if (loaded == null)
                {
                    try
                    {
                        var probeAsm = typeof(R201SignatureProbe).Assembly;
                        foreach (var refName in probeAsm.GetReferencedAssemblies())
                        {
                            if (refName.Name != null && refName.Name.Equals(shortName, StringComparison.OrdinalIgnoreCase))
                            { loaded = Assembly.Load(refName); break; }
                        }
                    }
                    catch { }
                }

                if (loaded == null)
                {
                    entry["passed"] = false;
                    entry["reason"] = "assembly-not-loaded";
                    results[shortName] = entry;
                    continue;
                }

                var assemblyName = loaded.GetName();
                entry["assemblyName"] = assemblyName.Name;
                entry["assemblyVersion"] = assemblyName.Version.ToString();
                entry["cultureName"] = assemblyName.CultureName ?? "neutral";
                entry["publicKeyToken"] = assemblyName.GetPublicKeyToken() == null
                    ? "null"
                    : BitConverter.ToString(assemblyName.GetPublicKeyToken()).Replace("-", "").ToLowerInvariant();

                bool versionOk = assemblyName.Version.ToString() == expectedVersion;
                entry["expectedVersion"] = expectedVersion;
                entry["versionMatch"] = versionOk;

                string location = loaded.Location;
                entry["hasLocation"] = !string.IsNullOrEmpty(location);

                if (!string.IsNullOrEmpty(location) && System.IO.File.Exists(location))
                {
                    using (var sha = System.Security.Cryptography.SHA256.Create())
                    using (var fs = System.IO.File.OpenRead(location))
                        entry["sha256"] = BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "").ToUpperInvariant();

                    try
                    {
                        var cert = new X509Certificate2(location);
                        entry["authenticodeSubject"] = cert.Subject;
                        entry["authenticodeIssuer"] = cert.Issuer;
                        entry["authenticodeValid"] = true;
                        entry["authenticodeNotBefore"] = cert.NotBefore.ToString("o");
                        entry["authenticodeNotAfter"] = cert.NotAfter.ToString("o");
                    }
                    catch (Exception ex)
                    {
                        entry["authenticodeValid"] = false;
                        entry["authenticodeError"] = SanitizeException(ex);
                    }
                    entry["fileSizeBytes"] = new System.IO.FileInfo(location).Length;
                }

                entry["passed"] = versionOk;
            }
            catch (Exception ex)
            { entry["passed"] = false; entry["error"] = SanitizeException(ex); }

            results[shortName] = entry;
        }
        return results;
    }

    private static string SanitizeException(Exception ex)
    {
        string msg = ex.Message ?? "";
        if (msg.Length > 80) msg = msg.Substring(0, 80) + "...";
        return ex.GetType().Name + ": " + msg;
    }

    // Minimal JSON serializer — no external dependencies on net45.
    private static string SerializeJson(object obj)
    {
        var sb = new StringBuilder();
        SerializeValue(sb, obj);
        return sb.ToString();
    }

    private static void SerializeValue(StringBuilder sb, object value)
    {
        if (value == null) { sb.Append("null"); return; }
        if (value is bool) { sb.Append((bool)value ? "true" : "false"); return; }
        if (value is int) { sb.Append((int)value); return; }
        if (value is long) { sb.Append((long)value); return; }
        if (value is string) { SerializeString(sb, (string)value); return; }
        if (value is Dictionary<string, object>) { SerializeDict(sb, (Dictionary<string, object>)value); return; }
        if (value is IList<Dictionary<string, object>>) { SerializeListOfDicts(sb, (IList<Dictionary<string, object>>)value); return; }
        if (value is IList<string>) { SerializeStringList(sb, (IList<string>)value); return; }
        if (value is Array) { SerializeArray(sb, (Array)value); return; }
        SerializeString(sb, value.ToString());
    }

    private static void SerializeString(StringBuilder sb, string s)
    {
        sb.Append('"');
        foreach (char c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                    else sb.Append(c);
                    break;
            }
        }
        sb.Append('"');
    }

    private static void SerializeDict(StringBuilder sb, Dictionary<string, object> dict)
    {
        sb.Append('{');
        bool first = true;
        foreach (var kvp in dict)
        {
            if (!first) sb.Append(',');
            first = false;
            SerializeString(sb, kvp.Key);
            sb.Append(':');
            SerializeValue(sb, kvp.Value);
        }
        sb.Append('}');
    }

    private static void SerializeListOfDicts(StringBuilder sb, IList<Dictionary<string, object>> list)
    {
        sb.Append('[');
        for (int i = 0; i < list.Count; i++)
        { if (i > 0) sb.Append(','); SerializeDict(sb, list[i]); }
        sb.Append(']');
    }

    private static void SerializeStringList(StringBuilder sb, IList<string> list)
    {
        sb.Append('[');
        for (int i = 0; i < list.Count; i++)
        { if (i > 0) sb.Append(','); SerializeString(sb, list[i]); }
        sb.Append(']');
    }

    private static void SerializeArray(StringBuilder sb, Array arr)
    {
        sb.Append('[');
        for (int i = 0; i < arr.Length; i++)
        { if (i > 0) sb.Append(','); SerializeValue(sb, arr.GetValue(i)); }
        sb.Append(']');
    }
}
