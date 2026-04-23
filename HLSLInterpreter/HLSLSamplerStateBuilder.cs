using System;
using System.Collections.Generic;
using UnityShaderParser.HLSL;

namespace HLSL
{
    internal static class HLSLSamplerStateBuilder
    {
        public static SamplerStateValue Build(bool isComparison, List<StatePropertyNode> states,
            HLSLExpressionEvaluator eval = null)
        {
            var sampler = new SamplerStateValue(isComparison);
            string legacyMin = null, legacyMag = null, legacyMip = null;

            foreach (var prop in states)
            {
                if (prop.IsReference) continue;
                string name = prop.Name.GetName().ToUpperInvariant();
                string id = (prop.Value as IdentifierExpressionNode)?.GetName().ToUpperInvariant();

                switch (name)
                {
                    case "FILTER":
                        if (id != null) sampler.Filter = ParseFilterMode(id);
                        break;
                    case "ADDRESSU":
                        if (id != null) sampler.AddressU = ParseAddressMode(id);
                        break;
                    case "ADDRESSV":
                        if (id != null) sampler.AddressV = ParseAddressMode(id);
                        break;
                    case "ADDRESSW":
                        if (id != null) sampler.AddressW = ParseAddressMode(id);
                        break;
                    case "COMPARISONFUNC":
                        if (id != null) sampler.Comparison = ParseComparisonMode(id);
                        break;
                    case "MINLOD":
                        sampler.MinimumLod = ParseFloat(prop.Value, eval);
                        break;
                    case "MAXLOD":
                        sampler.MaximumLod = ParseFloat(prop.Value, eval);
                        break;
                    case "MIPLODBIAS":
                        sampler.MipLodBias = ParseFloat(prop.Value, eval);
                        break;
                    case "MAXANISOTROPY":
                        sampler.MaximumAnisotropy = (int)ParseFloat(prop.Value, eval);
                        break;
                    case "BORDERCOLOR":
                        if (eval != null && eval.Visit(prop.Value) is VectorValue vec && vec.Size >= 4)
                            sampler.BorderColor = (
                                vec[0].AsFloat(),
                                vec[1].AsFloat(),
                                vec[2].AsFloat(),
                                vec[3].AsFloat());
                        break;
                    // Legacy D3D9 per-stage filter settings
                    case "MINFILTER": legacyMin = id; break;
                    case "MAGFILTER": legacyMag = id; break;
                    case "MIPFILTER": legacyMip = id; break;
                }
            }

            if (legacyMin != null || legacyMag != null || legacyMip != null)
                sampler.Filter = CombineLegacyFilters(legacyMin, legacyMag, legacyMip);

            return sampler;
        }

        private static SamplerStateValue.FilterMode ParseFilterMode(string id) => id switch
        {
            "MIN_MAG_MIP_POINT"                          => SamplerStateValue.FilterMode.MinMagMipPoint,
            "MIN_MAG_POINT_MIP_LINEAR"                   => SamplerStateValue.FilterMode.MinMagPointMipLinear,
            "MIN_POINT_MAG_LINEAR_MIP_POINT"             => SamplerStateValue.FilterMode.MinPointMagLinearMipPoint,
            "MIN_POINT_MAG_MIP_LINEAR"                   => SamplerStateValue.FilterMode.MinPointMagMipLinear,
            "MIN_LINEAR_MAG_MIP_POINT"                   => SamplerStateValue.FilterMode.MinLinearMagMipPoint,
            "MIN_LINEAR_MAG_POINT_MIP_LINEAR"            => SamplerStateValue.FilterMode.MinLinearMagPointMipLinear,
            "MIN_MAG_LINEAR_MIP_POINT"                   => SamplerStateValue.FilterMode.MinMagLinearMipPoint,
            "ANISOTROPIC"                                => SamplerStateValue.FilterMode.Anisotropic,
            "COMPARISON_MIN_MAG_MIP_POINT"               => SamplerStateValue.FilterMode.ComparisonMinMagMipPoint,
            "COMPARISON_MIN_MAG_POINT_MIP_LINEAR"        => SamplerStateValue.FilterMode.ComparisonMinMagPointMipLinear,
            "COMPARISON_MIN_POINT_MAG_LINEAR_MIP_POINT"  => SamplerStateValue.FilterMode.ComparisonMinPointMagLinearMipPoint,
            "COMPARISON_MIN_POINT_MAG_MIP_LINEAR"        => SamplerStateValue.FilterMode.ComparisonMinPointMagMipLinear,
            "COMPARISON_MIN_LINEAR_MAG_MIP_POINT"        => SamplerStateValue.FilterMode.ComparisonMinLinearMagMipPoint,
            "COMPARISON_MIN_LINEAR_MAG_POINT_MIP_LINEAR" => SamplerStateValue.FilterMode.ComparisonMinLinearMagPointMipLinear,
            "COMPARISON_MIN_MAG_LINEAR_MIP_POINT"        => SamplerStateValue.FilterMode.ComparisonMinMagLinearMipPoint,
            "COMPARISON_MIN_MAG_MIP_LINEAR"              => SamplerStateValue.FilterMode.ComparisonMinMagMipLinear,
            "COMPARISON_ANISOTROPIC"                     => SamplerStateValue.FilterMode.ComparisonAnisotropic,
            _                                            => SamplerStateValue.FilterMode.MinMagMipLinear,
        };

        private static SamplerStateValue.TextureAddressMode ParseAddressMode(string id) => id switch
        {
            "WRAP"        => SamplerStateValue.TextureAddressMode.Wrap,
            "MIRROR"      => SamplerStateValue.TextureAddressMode.Mirror,
            "CLAMP"       => SamplerStateValue.TextureAddressMode.Clamp,
            "BORDER"      => SamplerStateValue.TextureAddressMode.Border,
            "MIRROR_ONCE" => SamplerStateValue.TextureAddressMode.MirrorOnce,
            _             => SamplerStateValue.TextureAddressMode.Wrap,
        };

        private static SamplerStateValue.ComparisonMode ParseComparisonMode(string id) => id switch
        {
            "NEVER"         => SamplerStateValue.ComparisonMode.Never,
            "LESS"          => SamplerStateValue.ComparisonMode.Less,
            "EQUAL"         => SamplerStateValue.ComparisonMode.Equal,
            "LESS_EQUAL"    => SamplerStateValue.ComparisonMode.LessEqual,
            "GREATER"       => SamplerStateValue.ComparisonMode.Greater,
            "NOT_EQUAL"     => SamplerStateValue.ComparisonMode.NotEqual,
            "GREATER_EQUAL" => SamplerStateValue.ComparisonMode.GreaterEqual,
            "ALWAYS"        => SamplerStateValue.ComparisonMode.Always,
            _               => SamplerStateValue.ComparisonMode.Always,
        };

        private static float ParseFloat(ExpressionNode expr, HLSLExpressionEvaluator eval)
        {
            if (eval.Visit(expr) is ScalarValue num)
                return num.AsFloat();
            return 0f;
        }

        private static SamplerStateValue.FilterMode CombineLegacyFilters(string min, string mag, string mip)
        {
            if (min == "ANISOTROPIC" || mag == "ANISOTROPIC")
                return SamplerStateValue.FilterMode.Anisotropic;
            bool minLin = min == "LINEAR";
            bool magLin = mag == "LINEAR";
            bool mipLin = mip == "LINEAR";
            if (minLin && magLin && mipLin) return SamplerStateValue.FilterMode.MinMagMipLinear;
            if (minLin && magLin)           return SamplerStateValue.FilterMode.MinMagLinearMipPoint;
            if (minLin && mipLin)           return SamplerStateValue.FilterMode.MinLinearMagPointMipLinear;
            if (magLin && mipLin)           return SamplerStateValue.FilterMode.MinPointMagMipLinear;
            if (minLin)                     return SamplerStateValue.FilterMode.MinLinearMagMipPoint;
            if (magLin)                     return SamplerStateValue.FilterMode.MinPointMagLinearMipPoint;
            if (mipLin)                     return SamplerStateValue.FilterMode.MinMagLinearMipPoint;
            return SamplerStateValue.FilterMode.MinMagMipPoint;
        }
    }
}
