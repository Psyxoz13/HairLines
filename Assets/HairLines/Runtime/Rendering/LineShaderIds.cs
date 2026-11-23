using UnityEngine;

namespace HairLines
{
    public static class LineShaderIds
    {
        public const string LineShaderName = "HairLines/Line Primitive";
        public const string ResolveShaderName = "Hidden/HairLines/LineResolve";
        public const string DepthShaderName = "Hidden/HairLines/LineDepth";
        public const string ShadowShaderName = "Hidden/HairLines/HairShadow";

        public static readonly int Positions = Shader.PropertyToID("_LinePositions");
        public static readonly int StrandInfo = Shader.PropertyToID("_LineStrandInfo");
        public static readonly int HasVertexColor = Shader.PropertyToID("_HairHasVertexColor");
        public static readonly int Colors = Shader.PropertyToID("_LineColors");
        public static readonly int Indices = Shader.PropertyToID("_LineIndices");
        public static readonly int Opacity = Shader.PropertyToID("_LineOpacity");
        public static readonly int VertexGroups = Shader.PropertyToID("_LineVertexGroups");
        public static readonly int DrawVertexOffset = Shader.PropertyToID("_LineDrawVertexOffset");
        public static readonly int GroupTint = Shader.PropertyToID("_HairGroupTint");
        public static readonly int StrandDebug = Shader.PropertyToID("_LineStrandDebug");
        public static readonly int StrandAttributes = Shader.PropertyToID("_LineStrandAttributes");
        public static readonly int HasStrandAttributes = Shader.PropertyToID("_HairHasStrandAttributes");
        public static readonly int StrandRange = Shader.PropertyToID("_HairStrandRange");
        public static readonly int HairDebugMode = Shader.PropertyToID("_HairDebugMode");
        public static readonly int HairDebugRange = Shader.PropertyToID("_HairDebugRange");

        public static readonly int PrevPositions = Shader.PropertyToID("_LinePrevPositions");
        public static readonly int PrevObjectToWorld = Shader.PropertyToID("_HairPrevObjectToWorld");
        public static readonly int HairSubpixelMotion = Shader.PropertyToID("_HairSubpixelMotion");
        public static readonly int HairMotionVectorMode = Shader.PropertyToID("_HairMotionVectorMode");
        public const string MotionVectorsKeyword = "_HAIR_MOTION_VECTORS";

        public static readonly int CopyOffsets = Shader.PropertyToID("_HairCopyOffsets");
        public static readonly int CopyParams = Shader.PropertyToID("_HairCopyParams");
        public static readonly int CopySpread = Shader.PropertyToID("_HairCopySpread");

        public static readonly int HairShadowCount = Shader.PropertyToID("_HairShadowCount");
        public static readonly int HairShadowCasters = Shader.PropertyToID("_HairShadowCasters");
        public static readonly int HairShadowSlices = Shader.PropertyToID("_HairShadowSlices");
        public static readonly int HairShadowFilterSlice = Shader.PropertyToID("_HairShadowFilterSlice");
        public static readonly int HairShadowFilterSource = Shader.PropertyToID("_HairShadowFilterSource");

        public static readonly int HairShadowDrawMatrix = Shader.PropertyToID("_HairShadowDrawMatrix");
        public static readonly int HairShadowDrawLightDir = Shader.PropertyToID("_HairShadowDrawLightDir");
        public static readonly int HairShadowDrawLayers = Shader.PropertyToID("_HairShadowDrawLayers");
        public static readonly int HairShadowDrawSlice = Shader.PropertyToID("_HairShadowDrawSlice");

        public static readonly int HairShadowParams = Shader.PropertyToID("_HairShadowParams");
        public static readonly int HairShadowDepthTex = Shader.PropertyToID("_HairShadowDepthTex");
        public static readonly int HairShadowLayerTex = Shader.PropertyToID("_HairShadowLayerTex");
        public static readonly int HairShadowOpacityThreshold = Shader.PropertyToID("_HairShadowOpacityThreshold");
        public static readonly int HairShadowBlurParams = Shader.PropertyToID("_HairShadowBlurParams");

        public static readonly int HairVolumeSplat = Shader.PropertyToID("_HairVolumeSplat");
        public static readonly int HairSplatParams = Shader.PropertyToID("_HairSplatParams");
        public static readonly int HairSplatResolve = Shader.PropertyToID("_HairSplatResolve");
        public static readonly int HairVolumeDensity = Shader.PropertyToID("_HairVolumeDensity");
        public static readonly int HairVolumeTex = Shader.PropertyToID("_HairVolumeTex");
        public static readonly int HairVolumeOrigin = Shader.PropertyToID("_HairVolumeOrigin");
        public static readonly int HairVolumeParams = Shader.PropertyToID("_HairVolumeParams");
        public static readonly int HairVolumeRange = Shader.PropertyToID("_HairVolumeRange");
        public static readonly int HairVolumeSlab = Shader.PropertyToID("_HairVolumeSlab");
        public static readonly int HairVolumeCount = Shader.PropertyToID("_HairVolumeCount");
        public static readonly int HairVolumeCasters = Shader.PropertyToID("_HairVolumeCasters");

        public static readonly int HairSkyOcclusion = Shader.PropertyToID("_HairSkyOcclusion");
        public static readonly int HairSkyParams = Shader.PropertyToID("_HairSkyParams");
        public static readonly int HairVolumeConeTexA = Shader.PropertyToID("_HairVolumeConeTexA");
        public static readonly int HairVolumeConeTexB = Shader.PropertyToID("_HairVolumeConeTexB");
        public static readonly int HairVolumeCone = Shader.PropertyToID("_HairVolumeCone");
        public static readonly int HairConeSourceTex = Shader.PropertyToID("_HairConeSourceTex");
        public static readonly int HairConeSource = Shader.PropertyToID("_HairConeSource");
        public static readonly int HairConeTarget = Shader.PropertyToID("_HairConeTarget");
        public static readonly int HairSkyVisibilityTex = Shader.PropertyToID("_HairSkyVisibilityTex");
        public static readonly int HairSkyVisibilityOut = Shader.PropertyToID("_HairSkyVisibilityOut");
        public static readonly int HairSkyWeightsPos = Shader.PropertyToID("_HairSkyWeightsPos");
        public static readonly int HairSkyWeightsNeg = Shader.PropertyToID("_HairSkyWeightsNeg");
        public static readonly int HairSkyGrid = Shader.PropertyToID("_HairSkyGrid");

        public static readonly int HairTexSize = Shader.PropertyToID("_HairTexSize");

        public static readonly int HairSubpixelSize = Shader.PropertyToID("_HairSubpixelSize");
        public static readonly int HairSubpixelDepth = Shader.PropertyToID("_HairSubpixelDepth");
        public static readonly int HairResolveDepthAware = Shader.PropertyToID("_HairResolveDepthAware");
        public static readonly int FilterTaps = Shader.PropertyToID("_FilterTaps");
        public static readonly int FilterInvSigmaSq = Shader.PropertyToID("_FilterInvSigmaSq");
        public static readonly int InvSupersample = Shader.PropertyToID("_InvSupersample");
        public static readonly int OpacityThreshold = Shader.PropertyToID("_OpacityThreshold");
        public static readonly int DepthOpacityThreshold = Shader.PropertyToID("_DepthOpacityThreshold");
        public static readonly int DepthStochasticOpacity = Shader.PropertyToID("_DepthStochasticOpacity");
        public static readonly int TipTaperLength = Shader.PropertyToID("_TipTaperLength");
        public static readonly int TipWidth = Shader.PropertyToID("_TipWidth");
        public static readonly int DepthTipTaperLength = Shader.PropertyToID("_DepthTipTaperLength");
        public static readonly int DepthTipWidth = Shader.PropertyToID("_DepthTipWidth");
        public static readonly int EarlyOpaqueDepth = Shader.PropertyToID("_HairOpaqueDepth");
        public static readonly int HairMainLightSourceAngle = Shader.PropertyToID("_HairMainLightSourceAngle");
        public static readonly int HairLightSources = Shader.PropertyToID("_HairLightSources");
        public static readonly int HairLightShadowStrengths = Shader.PropertyToID("_HairLightShadowStrengths");
        public static readonly int HairSHAr = Shader.PropertyToID("_HairSHAr");
        public static readonly int HairSHAg = Shader.PropertyToID("_HairSHAg");
        public static readonly int HairSHAb = Shader.PropertyToID("_HairSHAb");
        public static readonly int HairSHBr = Shader.PropertyToID("_HairSHBr");
        public static readonly int HairSHBg = Shader.PropertyToID("_HairSHBg");
        public static readonly int HairSHBb = Shader.PropertyToID("_HairSHBb");
        public static readonly int HairSHC = Shader.PropertyToID("_HairSHC");

        public static readonly int LodCoverageScale = Shader.PropertyToID("_HairLodCoverageScale");
        public static readonly int LodDepthScale = Shader.PropertyToID("_HairLodDepthScale");

        public static readonly int ThickDensity = Shader.PropertyToID("_HairThickDensity");
        public static readonly int ShadowFibreWeight = Shader.PropertyToID("_HairShadowFibreWeight");

        public static readonly int HairCascadeViewProj = Shader.PropertyToID("_HairCascadeViewProj");
        public static readonly int HairCascadeLight = Shader.PropertyToID("_HairCascadeLight");
        public static readonly int HairCascadeCoverage = Shader.PropertyToID("_HairCascadeCoverage");
        public static readonly int MainLightShadowmapTexture = Shader.PropertyToID("_MainLightShadowmapTexture");
    }
}
