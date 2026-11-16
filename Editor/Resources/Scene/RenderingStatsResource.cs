using System;
using ForgeMCP.Editor.Helpers;
using ForgeMCP.Editor.Tools.Graphics;
using Newtonsoft.Json.Linq;

using MCPForUnity.Editor.Resources;
using MCPForUnity.Editor.Helpers;
namespace ForgeMCP.Editor.Resources.Scene
{
    [McpForUnityResource("get_rendering_stats")]
    public static class RenderingStatsResource
    {
        public static object HandleCommand(JObject @params)
        {
            try
            {
                return RenderingStatsOps.GetStats(@params ?? new JObject());
            }
            catch (Exception e)
            {
                McpLog.Error($"[RenderingStatsResource] Error: {e}");
                return new ErrorResponse($"Error getting rendering stats: {e.Message}");
            }
        }
    }
}
