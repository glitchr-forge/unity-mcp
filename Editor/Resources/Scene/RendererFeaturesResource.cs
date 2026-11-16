using System;
using ForgeMCP.Editor.Helpers;
using ForgeMCP.Editor.Tools.Graphics;
using Newtonsoft.Json.Linq;

using MCPForUnity.Editor.Resources;
using MCPForUnity.Editor.Helpers;
namespace ForgeMCP.Editor.Resources.Scene
{
    [McpForUnityResource("get_renderer_features")]
    public static class RendererFeaturesResource
    {
        public static object HandleCommand(JObject @params)
        {
            try
            {
                return RendererFeatureOps.ListFeatures(@params ?? new JObject());
            }
            catch (Exception e)
            {
                McpLog.Error($"[RendererFeaturesResource] Error: {e}");
                return new ErrorResponse($"Error listing renderer features: {e.Message}");
            }
        }
    }
}
