using System;
using ForgeMCP.Editor.Helpers;
using ForgeMCP.Editor.Tools.Graphics;
using Newtonsoft.Json.Linq;

using MCPForUnity.Editor.Resources;
using MCPForUnity.Editor.Helpers;
namespace ForgeMCP.Editor.Resources.Scene
{
    [McpForUnityResource("get_volumes")]
    public static class VolumesResource
    {
        public static object HandleCommand(JObject @params)
        {
            try
            {
                return VolumeOps.ListVolumes(@params ?? new JObject());
            }
            catch (Exception e)
            {
                McpLog.Error($"[VolumesResource] Error listing volumes: {e}");
                return new ErrorResponse($"Error listing volumes: {e.Message}");
            }
        }
    }
}
