using System;
using System.Linq;
using System.Linq.Expressions;
using Dapper;

namespace NzbDrone.Core.Datastore
{
    public abstract class WhereBuilder : ExpressionVisitor
    {
        public DynamicParameters Parameters { get; protected set; }

        protected static Expression GetStaticContainsSource(MethodCallExpression call)
        {
            if (call.Method.DeclaringType == typeof(Enumerable) && call.Arguments.Count == 2)
            {
                return call.Arguments[0];
            }

            // C# 14 lowers array.Contains to a span call, with a null comparer for enums.
            if (call.Method.DeclaringType == typeof(MemoryExtensions) &&
                (call.Arguments.Count == 2 || (call.Arguments.Count == 3 && call.Arguments[2] is ConstantExpression { Value: null })) &&
                call.Arguments[0] is MethodCallExpression conversion &&
                conversion.Method.Name == "op_Implicit" &&
                conversion.Method.DeclaringType == conversion.Type &&
                conversion.Type.IsGenericType &&
                conversion.Type.GetGenericTypeDefinition() == typeof(ReadOnlySpan<>) &&
                conversion.Arguments.Count == 1 &&
                conversion.Arguments[0].Type.IsSZArray)
            {
                return conversion.Arguments[0];
            }

            throw new NotSupportedException($"Unsupported membership expression: {call.Method.DeclaringType?.Name}.{call.Method.Name}");
        }
    }
}
