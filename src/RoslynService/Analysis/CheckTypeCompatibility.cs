using Microsoft.CodeAnalysis;

namespace DotNetLensMcp;

public partial class RoslynService
{
    /// <summary>
    /// Checks if one type can be assigned to another.
    /// </summary>
    public async Task<object> CheckTypeCompatibilityAsync(string sourceType, string targetType)
    {
        EnsureSolutionLoaded();

        var source = await FindTypeByNameAsync(sourceType);
        if (source == null)
        {
            return CreateErrorResponse(
                ErrorCodes.TypeNotFound,
                $"Source type not found: {sourceType}",
                hint: "Use fully qualified name or check spelling",
                context: new { sourceType }
            );
        }

        var target = await FindTypeByNameAsync(targetType);
        if (target == null)
        {
            return CreateErrorResponse(
                ErrorCodes.TypeNotFound,
                $"Target type not found: {targetType}",
                hint: "Use fully qualified name or check spelling",
                context: new { targetType }
            );
        }

        var compilation = await GetProjectCompilationAsync(_solution!.Projects.First());
        if (compilation == null)
        {
            return CreateErrorResponse(
                ErrorCodes.AnalysisFailed,
                "Could not get compilation",
                context: new { sourceType, targetType }
            );
        }

        var conversion = Microsoft.CodeAnalysis.CSharp.CSharpExtensions.ClassifyConversion(compilation, source, target);

        string conversionKind;
        string reason;

        if (conversion.IsIdentity)
        {
            conversionKind = "Identity";
            reason = "Same type";
        }
        else if (conversion.IsImplicit)
        {
            if (conversion.IsReference)
            {
                conversionKind = "ImplicitReference";
                reason = $"{sourceType} inherits from or implements {targetType}";
            }
            else if (conversion.IsNumeric)
            {
                conversionKind = "ImplicitNumeric";
                reason = "Numeric widening conversion";
            }
            else if (conversion.IsBoxing)
            {
                conversionKind = "Boxing";
                reason = "Value type to object/interface";
            }
            else
            {
                conversionKind = "Implicit";
                reason = "Implicit conversion available";
            }
        }
        else if (conversion.IsExplicit)
        {
            if (conversion.IsUnboxing)
            {
                conversionKind = "Unboxing";
                reason = "Requires explicit cast (unboxing)";
            }
            else if (conversion.IsNumeric)
            {
                conversionKind = "ExplicitNumeric";
                reason = "Numeric narrowing - may lose precision";
            }
            else
            {
                conversionKind = "Explicit";
                reason = "Requires explicit cast";
            }
        }
        else
        {
            conversionKind = "None";
            reason = "No conversion exists between these types";
        }

        return CreateSuccessResponse(
            data: new
            {
                sourceType = source.ToDisplayString(),
                targetType = target.ToDisplayString(),
                compatible = conversion.Exists,
                requiresCast = conversion.IsExplicit,
                conversionKind,
                reason,
                isReferenceConversion = conversion.IsReference,
                isNumericConversion = conversion.IsNumeric,
                isBoxing = conversion.IsBoxing,
                isUnboxing = conversion.IsUnboxing
            },
            suggestedNextTools: new[]
            {
                "get_base_types to see inheritance chain",
                "get_type_members to see available members"
            }
        );
    }
}
