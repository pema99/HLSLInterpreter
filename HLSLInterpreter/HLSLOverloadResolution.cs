using System;
using System.Collections.Generic;
using System.Linq;
using UnityShaderParser.Common;
using UnityShaderParser.HLSL;

namespace HLSL
{
    public static class HLSLOverloadResolution
    {
        // Cast "value" to match the given type and return it.
        // Performs any implicit conversions, either promotion or demotion, needed to pass as a function parameter.
        public static HLSLValue CastForParameter(HLSLExpressionEvaluator evaluator, HLSLValue value, TypeNode typeNode)
        {
            typeNode = evaluator.ResolveType(typeNode);
            if (value is NumericValue valueNum && typeNode is NumericTypeNode typeNum)
            {
                HLSLValue reshaped = valueNum;
                if (typeNode is MatrixTypeNode mat)
                    valueNum = valueNum.BroadcastToMatrix(mat.FirstDimension, mat.SecondDimension);

                if (typeNode is VectorTypeNode vec)
                    valueNum = valueNum.BroadcastToVector(vec.Dimension);

                if (typeNode is GenericMatrixTypeNode matGen)
                {
                    int rows = ((ScalarValue)evaluator.Visit(matGen.FirstDimension)).AsInt();
                    int cols = ((ScalarValue)evaluator.Visit(matGen.SecondDimension)).AsInt();
                    valueNum = valueNum.BroadcastToMatrix(rows, cols);
                }

                if (typeNode is GenericVectorTypeNode vecGen)
                    valueNum = valueNum.BroadcastToVector(((ScalarValue)evaluator.Visit(vecGen.Dimension)).AsInt());

                return valueNum.Cast(typeNum.Kind);
            }
            return value;
        }

        public static bool TypeEquals(HLSLExpressionEvaluator evaluator, HLSLInterpreterContext context, HLSLValue from, TypeNode to, IList<ArrayRankNode> arrayRanks = null)
        {
            if (to is ScalarTypeNode scalarType &&
                from is ScalarValue scalarValue &&
                scalarType.Kind == scalarValue.Type)
                return true;
            if (to is VectorTypeNode vecType &&
                from is VectorValue vecValue &&
                vecType.Kind == vecValue.Type &&
                vecType.Dimension == vecValue.Size)
                return true;
            if (to is GenericVectorTypeNode genVecType &&
                from is VectorValue genVecValue &&
                genVecType.Kind == genVecValue.Type &&
                ((ScalarValue)evaluator.Visit(genVecType.Dimension)).AsInt() == genVecValue.Size)
                return true;
            if (to is MatrixTypeNode matType
                && from is MatrixValue matValue
                && matType.Kind == matValue.Type
                && matType.FirstDimension == matValue.Rows
                && matType.SecondDimension == matValue.Columns)
                return true;
            if (to is GenericMatrixTypeNode genMatType
                && from is MatrixValue genMatValue
                && genMatType.Kind == genMatValue.Type
                && ((ScalarValue)evaluator.Visit(genMatType.FirstDimension)).AsInt() == genMatValue.Rows
                && ((ScalarValue)evaluator.Visit(genMatType.SecondDimension)).AsInt() == genMatValue.Columns)
                return true;
            if (to is StructTypeNode strType && from is StructValue strValue)
            {
                string toName = strType.Name.GetName();
                string qualifiedToName = context.GetQualifiedStructName(toName) ?? toName;
                if (qualifiedToName == strValue.Name)
                    return true;
            }
            if (to is NamedTypeNode namedType && from is StructValue namedStrValue)
            {
                string toName = namedType.GetName();
                string qualifiedToName = context.GetQualifiedStructName(toName) ?? toName;
                if (qualifiedToName == namedStrValue.Name)
                    return true;
            }
            if (to is QualifiedNamedTypeNode qualNamedType &&
                from is StructValue qualNamedStrValue &&
               qualNamedType.GetName() == qualNamedStrValue.Name)
                return true;
            if (to is PredefinedObjectTypeNode preType &&
                from is PredefinedObjectValue preValue &&
                preType.Kind == preValue.Type &&
                preType.TemplateArguments?.Count == preValue.TemplateArguments?.Length)
            {
                for (int i = 0; i < preType.TemplateArguments?.Count; i++)
                {
                    if (preValue.TemplateArguments[i].GetPrettyPrintedCode() != preType.TemplateArguments[i].GetPrettyPrintedCode())
                        return false;
                }
                return true;
            }
            if (arrayRanks != null &&
                from is ArrayValue arrValue &&
                arrValue.Values.Length > 0 &&
                arrayRanks.Count > 0 &&
                arrayRanks[0].Dimension is LiteralExpressionNode litDim &&
                int.Parse(litDim.Lexeme) == arrValue.Values.Length)
            {
                return TypeEquals(evaluator, context, arrValue.Values[0], to, arrayRanks.Skip(1).ToList());
            }
            if (from is ReferenceValue reference)
            {
                return TypeEquals(evaluator, context, reference.Get(), to, arrayRanks);
            }

            return false;
        }

        // Can we convert a value to a type without loss of information?
        public static bool CanPromoteTo(HLSLExpressionEvaluator evaluator, HLSLValue from, TypeNode to)
        {
            if (from is NumericValue fromNum && to is NumericTypeNode toNum)
            {
                // Casting into a less informative type is not promotion
                if (HLSLTypeUtils.GetScalarRank(fromNum.Type) > HLSLTypeUtils.GetScalarRank(toNum.Kind))
                    return false;
                // Scalars broadcast
                if (fromNum is ScalarValue)
                    return true;
                // Vector extension
                if (fromNum is VectorValue fromVec && toNum is VectorTypeNode toVec)
                    return fromVec.Size <= toVec.Dimension;
                // Matrix extension
                if (fromNum is MatrixValue fromMat && toNum is MatrixTypeNode toMat)
                    return fromMat.Rows <= toMat.FirstDimension && fromMat.Columns <= toMat.SecondDimension;
                // Same but for generic
                if (fromNum is VectorValue fromVecGen && toNum is GenericVectorTypeNode toVecGen)
                    return fromVecGen.Size <= ((ScalarValue)evaluator.Visit(toVecGen.Dimension)).AsInt();
                if (fromNum is MatrixValue fromMatGen && toNum is GenericMatrixTypeNode toMatGen)
                {
                    int rows = ((ScalarValue)evaluator.Visit(toMatGen.FirstDimension)).AsInt();
                    int cols = ((ScalarValue)evaluator.Visit(toMatGen.SecondDimension)).AsInt();
                    return fromMatGen.Rows <= rows && fromMatGen.Columns <= cols;
                }
            }
            return false;
        }

        // Can we convert a value to a type with loss of information?
        public static bool CanDemoteTo(HLSLExpressionEvaluator evaluator, HLSLValue from, TypeNode to)
        {
            if (from is NumericValue fromNum && to is NumericTypeNode toNum)
            {
                // Anything can be demoted to an scalar
                if (toNum is ScalarTypeNode)
                    return true;
                // If both are the same type, compare sizes - the from type must be strictly larger
                if (fromNum is VectorValue fromVec && toNum is VectorTypeNode toVec)
                    return fromVec.Size > toVec.Dimension;
                if (fromNum is MatrixValue fromMat && toNum is MatrixTypeNode toMat)
                    return fromMat.Rows > toMat.FirstDimension && fromMat.Columns > toMat.SecondDimension;
                if (fromNum is VectorValue fromVecGen && toNum is GenericVectorTypeNode toVecGen)
                    return fromVecGen.Size > ((ScalarValue)evaluator.Visit(toVecGen.Dimension)).AsInt();
                if (fromNum is MatrixValue fromMatGen && toNum is GenericMatrixTypeNode toMatGen)
                {
                    int rows = ((ScalarValue)evaluator.Visit(toMatGen.FirstDimension)).AsInt();
                    int cols = ((ScalarValue)evaluator.Visit(toMatGen.SecondDimension)).AsInt();
                    return fromMatGen.Rows > rows && fromMatGen.Columns > cols;
                }
            }
            return false;
        }

        // Given a function and a list of parameters, evaluate how well the function matches the parameters
        public static int GetOverloadScore(HLSLExpressionEvaluator evaluator, HLSLInterpreterContext context, FunctionDefinitionNode candidate, IList<HLSLValue> parameters)
        {
            if (parameters.Count != candidate.Parameters.Count)
                return -1;

            int score = 0;
            for (int i = 0; i < parameters.Count; i++)
            {
                var from = parameters[i];
                if (from is ReferenceValue reference)
                    from = reference.Get();

                var to = evaluator.ResolveType(candidate.Parameters[i].ParamType);

                if (TypeEquals(evaluator, context, from, to, candidate.Parameters[i].Declarator.ArrayRanks))
                    score += 3; // Exact match, best case
                else if (CanPromoteTo(evaluator, from, to))
                    score += 2; // Promotion is almost as good
                else if (CanDemoteTo(evaluator, from, to))
                    score += 1; // Demotion is a last resort
                else
                    return -1;  // No viable conversion, never pick!
            }
            return score;
        }

        // Pick a function overload from a list of candidates based on the parameters. Returns null if no viable overload.
        public static FunctionDefinitionNode PickOverload(HLSLExpressionEvaluator evaluator, HLSLInterpreterContext context, IEnumerable<FunctionDefinitionNode> candidates, IList<HLSLValue> parameters)
        {
            int bestScore = -1;
            FunctionDefinitionNode selected = null;
            foreach (var candidate in candidates)
            {
                int score = GetOverloadScore(evaluator, context, candidate, parameters);
                if (score > bestScore)
                {
                    bestScore = score;
                    selected = candidate;
                }
            }
            return selected;
        }
    }
}
