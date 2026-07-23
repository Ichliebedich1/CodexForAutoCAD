// V2ApiSurfaceProbe.cs — Compile-time and runtime API surface verification
// for CadContextJson v2 against AutoCAD 2016 R20.1 (AcMgd/AcDbMgd 20.1.0.0).
//
// Compile-time: typeof(T) and typeof(T).GetProperty() in the CompileTimeVerify()
// method force the compiler to resolve types and properties. If any are missing,
// the C# compiler emits an error. This method is never called — it exists only
// to be compiled.
//
// Runtime: Run() method uses reflection to verify methods and additional
// properties, then writes JSON results to stdout.
//
// This probe does NOT start or operate AutoCAD. It does NOT produce Autodesk DLLs.
// It is NOT equivalent to AutoCAD runtime verification.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;

/// <summary>
/// API surface probe for CadContextJson v2 against R20.1.
/// </summary>
public static class V2ApiSurfaceProbe
{
    /// <summary>
    /// Compile-time verification method. This method is NEVER called — it exists
    /// solely to force the C# compiler to resolve all referenced types and properties.
    /// If any type or property is missing from the R20.1 assemblies, the compiler
    /// will emit an error and the build will fail.
    /// </summary>
    private static void CompileTimeVerify()
    {
        // Type existence checks — each typeof forces the compiler to resolve the type.
        var types = new Type[]
        {
            typeof(Line), typeof(Circle), typeof(Polyline), typeof(DBText), typeof(MText),
            typeof(BlockReference), typeof(BlockTableRecord), typeof(AttributeCollection),
            typeof(AttributeReference), typeof(DynamicBlockReferencePropertyCollection),
            typeof(DynamicBlockReferenceProperty), typeof(Layout), typeof(Arc), typeof(Ellipse), typeof(Spline), typeof(DBPoint),
            typeof(Ray), typeof(Xline), typeof(Polyline2d), typeof(Vertex2d), typeof(Polyline3d),
            typeof(PolylineVertex3d), typeof(Dimension), typeof(Hatch), typeof(Leader),
            typeof(MLeader), typeof(Table), typeof(HatchLoop),
            typeof(ObjectId), typeof(ObjectIdCollection), typeof(ResultBuffer),
            typeof(IExtensionApplication), typeof(CommandClassAttribute),
            typeof(ExtensionApplicationAttribute),
            typeof(Point3d), typeof(Vector3d), typeof(Matrix3d), typeof(Scale3d),
            typeof(Poly2dType),
        };

        // Property existence checks — each property access forces the compiler to
        // resolve the property on the target type.
        object[] checks = new object[]
        {
            // Line
            default(Line).StartPoint, default(Line).EndPoint,
            // Circle
            default(Circle).Center, default(Circle).Radius, (object)default(Circle).Normal,
            // Polyline
            default(Polyline).Closed, default(Polyline).Elevation, (object)default(Polyline).Normal,
            // DBText
            (object)default(DBText).TextString, default(DBText).Position, default(DBText).Height, default(DBText).Rotation,
            // MText
            (object)default(MText).Contents, default(MText).Location, default(MText).TextHeight, default(MText).Rotation,
            // BlockReference
            default(BlockReference).Position, default(BlockReference).Rotation,
            (object)default(BlockReference).ScaleFactors, (object)default(BlockReference).Name,
            default(BlockReference).IsDynamicBlock,
            (object)default(BlockReference).BlockTableRecord,
            (object)default(BlockReference).DynamicBlockTableRecord,
            (object)default(BlockReference).AttributeCollection,
            (object)default(BlockReference).DynamicBlockReferencePropertyCollection,
            // Block definition, attribute and dynamic property metadata
            default(BlockTableRecord).IsFromExternalReference,
            default(BlockTableRecord).IsFromOverlayReference,
            default(BlockTableRecord).IsAnonymous,
            default(BlockTableRecord).IsLayout,
            default(BlockTableRecord).HasAttributeDefinitions,
            (object)default(BlockTableRecord).LayoutId,
            (object)default(AttributeReference).Tag,
            (object)default(AttributeReference).TextString,
            default(AttributeReference).Invisible,
            default(AttributeReference).IsMTextAttribute,
            (object)default(DynamicBlockReferenceProperty).PropertyName,
            (object)default(DynamicBlockReferenceProperty).Value,
            default(DynamicBlockReferenceProperty).ReadOnly,
            default(DynamicBlockReferenceProperty).VisibleInCurrentVisibilityState,
            (object)default(Layout).LayoutName,
            default(Layout).ModelType,
            (object)default(Layout).BlockTableRecordId,
            // Arc
            default(Arc).Center, default(Arc).Radius, default(Arc).StartAngle, default(Arc).EndAngle,
            (object)default(Arc).Normal,
            // Ellipse
            default(Ellipse).Center, (object)default(Ellipse).MajorAxis, default(Ellipse).RadiusRatio,
            default(Ellipse).StartParam, default(Ellipse).EndParam, (object)default(Ellipse).Normal,
            // Spline
            default(Spline).Degree, default(Spline).HasFitData, default(Spline).NumControlPoints,
            // DBPoint
            default(DBPoint).Position, (object)default(DBPoint).Normal,
            // Ray
            default(Ray).BasePoint, (object)default(Ray).UnitDir,
            // Xline
            default(Xline).BasePoint, (object)default(Xline).UnitDir,
            // Polyline2d
            default(Polyline2d).Closed, default(Polyline2d).Elevation, default(Polyline2d).PolyType,
            // Polyline3d
            default(Polyline3d).Closed,
            // Dimension
            default(Dimension).Measurement, (object)default(Dimension).DimensionText,
            default(Dimension).TextPosition, default(Dimension).TextRotation, (object)default(Dimension).Normal,
            // Hatch
            default(Hatch).Associative, default(Hatch).IsGradient, default(Hatch).IsSolidFill,
            (object)default(Hatch).PatternName, default(Hatch).PatternAngle, default(Hatch).PatternScale,
            default(Hatch).Elevation, (object)default(Hatch).Normal,
            // Leader
            default(Leader).IsSplined, default(Leader).HasArrowHead, (object)default(Leader).Normal,
            // MLeader
            (object)default(MLeader).Normal,
            // Table
            default(Table).Position, (object)default(Table).Direction, default(Table).Width, default(Table).Height,
            // Vertex2d
            default(Vertex2d).Bulge, default(Vertex2d).StartWidth, default(Vertex2d).EndWidth,
            default(Vertex2d).Position.X,
        };

        // Prevent dead-code elimination by using the variables.
        GC.KeepAlive(types);
        GC.KeepAlive(checks);
    }

    /// <summary>
    /// Runs the probe and writes JSON results to stdout.
    /// </summary>
    public static void Run()
    {
        var passed = new List<string>();
        var failed = new List<string>();

        // Compile-time verification summary (types and properties compiled successfully)
        int compileTimeTypes = 39;  // number of typeof() checks in CompileTimeVerify
        int compileTimeProperties = 87;  // number of property-access checks in CompileTimeVerify

        // Runtime method checks
        CheckMember(typeof(Spline), "GetControlPointAt", MemberKind.Method, passed, failed);
        CheckMember(typeof(Spline), "GetFitPointAt", MemberKind.Method, passed, failed);
        CheckMember(typeof(Polyline), "GetBulgeAt", MemberKind.Method, passed, failed);
        CheckMember(typeof(Leader), "VertexAt", MemberKind.Method, passed, failed);
        CheckMember(typeof(MLeader), "GetLeaderIndexes", MemberKind.Method, passed, failed);
        CheckMember(typeof(MLeader), "GetLeaderLineIndexes", MemberKind.Method, passed, failed);
        CheckMember(typeof(MLeader), "VerticesCount", MemberKind.Method, passed, failed);
        CheckMember(typeof(MLeader), "GetVertex", MemberKind.Method, passed, failed);
        CheckMember(typeof(Hatch), "GetLoopAt", MemberKind.Method, passed, failed);
        CheckMember(typeof(AttributeCollection), "GetEnumerator", MemberKind.Method, passed, failed);
        CheckMember(typeof(DynamicBlockReferencePropertyCollection), "GetEnumerator", MemberKind.Method, passed, failed);

        // Members that might be methods OR properties in R20.1
        CheckMember(typeof(MLeader), "MText", MemberKind.Any, passed, failed);
        CheckMember(typeof(MLeader), "ContentType", MemberKind.Any, passed, failed);
        CheckMember(typeof(MLeader), "TextString", MemberKind.Any, passed, failed);
        CheckMember(typeof(Hatch), "NumberOfLoops", MemberKind.Any, passed, failed);
        CheckMember(typeof(Table), "GetTextStyle", MemberKind.Method, passed, failed);
        CheckMember(typeof(Table), "GetTextString", MemberKind.Method, passed, failed);
        CheckMember(typeof(Table), "GetCellType", MemberKind.Method, passed, failed);

        // Runtime property checks
        CheckMember(typeof(Polyline2d), "VertexObjectIdList", MemberKind.Property, passed, failed);
        CheckMember(typeof(Polyline3d), "Vertices", MemberKind.Property, passed, failed);
        CheckMember(typeof(Polyline3d), "VertexObjectIdList", MemberKind.Property, passed, failed);
        CheckMember(typeof(Table), "Cells", MemberKind.Property, passed, failed);
        CheckMember(typeof(Table), "Rows", MemberKind.Property, passed, failed);
        CheckMember(typeof(Table), "Columns", MemberKind.Property, passed, failed);
        CheckMember(typeof(Leader), "NumVertices", MemberKind.Property, passed, failed);
        CheckMember(typeof(DBPoint), "EcsRotation", MemberKind.Property, passed, failed);
        CheckMember(typeof(BlockReference), "XrefStatus", MemberKind.Property, passed, failed);
        CheckMember(typeof(Dimension), "DimensionType", MemberKind.Property, passed, failed);
        CheckMember(typeof(Spline), "NurbsData", MemberKind.Property, passed, failed);
        CheckMember(typeof(BlockReference), "AttributeCollection", MemberKind.Property, passed, failed);
        CheckMember(typeof(BlockReference), "DynamicBlockReferencePropertyCollection", MemberKind.Property, passed, failed);
        CheckMember(typeof(BlockReference), "DynamicBlockTableRecord", MemberKind.Property, passed, failed);
        CheckMember(typeof(BlockTableRecord), "IsFromOverlayReference", MemberKind.Property, passed, failed);
        CheckMember(typeof(BlockTableRecord), "HasAttributeDefinitions", MemberKind.Property, passed, failed);
        CheckMember(typeof(BlockTableRecord), "LayoutId", MemberKind.Property, passed, failed);
        CheckMember(typeof(AttributeReference), "IsMTextAttribute", MemberKind.Property, passed, failed);
        CheckMember(typeof(DynamicBlockReferenceProperty), "VisibleInCurrentVisibilityState", MemberKind.Property, passed, failed);

        // Emit JSON
        var sb = new StringBuilder();
        sb.AppendLine("{");
        sb.AppendLine("  \"probeVersion\": \"1.0.0\",");
        sb.AppendLine("  \"targetAssembly\": \"AcDbMgd/AcMgd 20.1.0.0\",");
        sb.AppendLine("  \"framework\": \"net45\",");
        sb.AppendLine("  \"platform\": \"x64\",");
        sb.AppendLine("  \"compileTimeTypeChecks\": " + compileTimeTypes + ",");
        sb.AppendLine("  \"compileTimePropertyChecks\": " + compileTimeProperties + ",");
        sb.AppendLine("  \"compileTimeNote\": \"All typeof and property-access expressions compiled successfully against R20.1\",");
        sb.AppendLine("  \"runtimeMethodChecks\": {");
        sb.AppendLine("    \"passed\": [");
        for (int i = 0; i < passed.Count; i++)
        {
            sb.Append("      \"").Append(EscapeJson(passed[i])).Append("\"");
            if (i < passed.Count - 1) sb.Append(",");
            sb.AppendLine();
        }
        sb.AppendLine("    ],");
        sb.AppendLine("    \"failed\": [");
        for (int i = 0; i < failed.Count; i++)
        {
            sb.Append("      \"").Append(EscapeJson(failed[i])).Append("\"");
            if (i < failed.Count - 1) sb.Append(",");
            sb.AppendLine();
        }
        sb.AppendLine("    ]");
        sb.AppendLine("  },");
        sb.AppendLine("  \"summary\": {");
        sb.AppendLine("    \"totalRuntimeChecks\": " + (passed.Count + failed.Count) + ",");
        sb.AppendLine("    \"passed\": " + passed.Count + ",");
        sb.AppendLine("    \"failed\": " + failed.Count);
        sb.AppendLine("  },");
        sb.AppendLine("  \"disclaimer\": \"Compile-time checks verify types/properties exist in R20.1 assemblies. Runtime checks verify additional methods/properties via reflection. This probe does NOT start or operate AutoCAD and is NOT equivalent to AutoCAD runtime verification.\"");
        sb.AppendLine("}");

        Console.Write(sb.ToString());
    }

    private enum MemberKind { Method, Property, Any }

    private static void CheckMember(Type type, string name, MemberKind kind, List<string> passed, List<string> failed)
    {
        var flags = BindingFlags.Public | BindingFlags.Instance;
        bool found = false;
        string key;

        if (kind == MemberKind.Method || kind == MemberKind.Any)
        {
            try
            {
                var method = type.GetMethod(name, flags);
                if (method != null)
                {
                    key = type.Name + "." + name + " [method]";
                    passed.Add(key);
                    found = true;
                }
            }
            catch (AmbiguousMatchException)
            {
                // Multiple overloads exist — the method is present.
                key = type.Name + "." + name + " [method]";
                passed.Add(key);
                found = true;
            }
        }

        if (kind == MemberKind.Property || kind == MemberKind.Any)
        {
            try
            {
                var prop = type.GetProperty(name, flags);
                if (prop != null)
                {
                    key = type.Name + "." + name + " [property]";
                    passed.Add(key);
                    found = true;
                }
            }
            catch (AmbiguousMatchException)
            {
                key = type.Name + "." + name + " [property]";
                passed.Add(key);
                found = true;
            }
        }

        if (!found)
        {
            key = type.Name + "." + name + " [" + kind.ToString().ToLowerInvariant() + "]";
            failed.Add(key);
        }
    }

    private static string EscapeJson(string value)
    {
        return value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r");
    }
}
