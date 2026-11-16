using System;
using ForgeMCP.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEditorInternal;

using MCPForUnity.Editor.Resources;
using MCPForUnity.Editor.Helpers;
namespace ForgeMCP.Editor.Resources.Project
{
    /// <summary>
    /// Provides list of all tags in the project.
    /// </summary>
    [McpForUnityResource("get_tags")]
    public static class Tags
    {
        public static object HandleCommand(JObject @params)
        {
            try
            {
                string[] tags = InternalEditorUtility.tags;
                return new SuccessResponse("Retrieved current tags.", tags);
            }
            catch (Exception e)
            {
                return new ErrorResponse($"Failed to retrieve tags: {e.Message}");
            }
        }
    }
}
