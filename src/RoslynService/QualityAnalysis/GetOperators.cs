using Microsoft.CodeAnalysis;

namespace DotNetLensMcp;

public partial class RoslynService
{
    private static readonly IReadOnlyDictionary<string, string> OperatorSymbolMap = new Dictionary<string, string>
    {
        ["op_Addition"] = "+",
        ["op_Subtraction"] = "-",
        ["op_Multiply"] = "*",
        ["op_Division"] = "/",
        ["op_Modulus"] = "%",
        ["op_ExclusiveOr"] = "^",
        ["op_BitwiseAnd"] = "&",
        ["op_BitwiseOr"] = "|",
        ["op_LeftShift"] = "<<",
        ["op_RightShift"] = ">>",
        ["op_UnsignedRightShift"] = ">>>",
        ["op_Equality"] = "==",
        ["op_Inequality"] = "!=",
        ["op_LessThan"] = "<",
        ["op_GreaterThan"] = ">",
        ["op_LessThanOrEqual"] = "<=",
        ["op_GreaterThanOrEqual"] = ">=",
        ["op_UnaryNegation"] = "unary -",
        ["op_UnaryPlus"] = "unary +",
        ["op_LogicalNot"] = "!",
        ["op_OnesComplement"] = "~",
        ["op_Increment"] = "++",
        ["op_Decrement"] = "--",
        ["op_True"] = "true",
        ["op_False"] = "false",
        ["op_Implicit"] = "implicit",
        ["op_Explicit"] = "explicit",
        ["op_CheckedAddition"] = "checked +",
        ["op_CheckedSubtraction"] = "checked -",
        ["op_CheckedMultiply"] = "checked *",
        ["op_CheckedDivision"] = "checked /",
        ["op_CheckedUnaryNegation"] = "checked unary -",
        ["op_CheckedDecrement"] = "checked --",
        ["op_CheckedIncrement"] = "checked ++",
        ["op_CheckedExplicit"] = "checked explicit",
    };

    public async Task<object> GetOperatorsAsync(string typeName)
    {
        EnsureSolutionLoaded();

        if (string.IsNullOrWhiteSpace(typeName))
            return CreateErrorResponse(ErrorCodes.InvalidParameter, "typeName is required");

        var type = await FindTypeByNameAsync(typeName);
        if (type == null)
            return CreateErrorResponse(ErrorCodes.TypeNotFound, $"Type '{typeName}' not found");

        var operators = type.GetMembers()
            .OfType<IMethodSymbol>()
            .Where(m => m.MethodKind is MethodKind.UserDefinedOperator or MethodKind.Conversion)
            .OrderBy(m => m.MethodKind == MethodKind.Conversion ? 1 : 0)
            .ThenBy(m => m.Name)
            .Select(m =>
            {
                var isConversion = m.MethodKind == MethodKind.Conversion;
                return (object)new
                {
                    name = m.Name,
                    symbol = OperatorSymbolMap.TryGetValue(m.Name, out var sym) ? sym : m.Name,
                    kind = isConversion
                        ? (m.Name == "op_Implicit" || m.Name == "op_Explicit" ? m.Name.Replace("op_", "").ToLowerInvariant() + " conversion" : "conversion")
                        : "operator",
                    returnType = m.ReturnType.ToDisplayString(),
                    parameters = m.Parameters.Select(p => new
                    {
                        name = p.Name,
                        type = p.Type.ToDisplayString()
                    }).ToList(),
                    accessibility = m.DeclaredAccessibility.ToString(),
                    isChecked = m.Name.StartsWith("op_Checked", StringComparison.Ordinal),
                    location = GetSymbolLocation(m)
                };
            })
            .ToList();

        return CreateSuccessResponse(
            data: new
            {
                typeName = type.ToDisplayString(),
                operatorCount = operators.Count,
                operators
            },
            suggestedNextTools: new[]
            {
                "get_type_members for all members including non-operator methods",
                "get_type_overview for a full picture of the type"
            },
            totalCount: operators.Count,
            returnedCount: operators.Count
        );
    }
}
