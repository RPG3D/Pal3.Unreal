// ---------------------------------------------------------------------------------------------
//  Copyright (c) 2021-2025, Jiaqi (0x7c13) Liu. All rights reserved.
//  See LICENSE file in the project root for license information.
// ---------------------------------------------------------------------------------------------

namespace Pal3.Game.Dev
{
    using System;
    using System.Collections;
    using Constants;
    using Engine.Logging;
    using Newtonsoft.Json.Linq;
    using UnityEngine.Networking;

    public static class GithubReleaseVersionFetcher
    {
        public static IEnumerator GetLatestReleaseVersionAsync(string owner, string repo, Action<string> callback)
        {
            // Construct the GitHub release API URL
            string apiUrl = $"https://api.github.com/repos/{owner}/{repo}/releases/latest";

            using UnityWebRequest unityWebRequest = UnityWebRequest.Get(apiUrl);

            // Set the user agent header
            unityWebRequest.SetRequestHeader("User-Agent", GameConstants.AppName);

            // Wait for the request to complete
            yield return unityWebRequest.SendWebRequest();

            if (unityWebRequest.result != UnityWebRequest.Result.Success)
            {
                EngineLogger.LogError($"Error fetching the latest release: {unityWebRequest.error}");
            }
            else
            {
                // Port note: in this environment the request can "succeed" with an
                // empty body (hosts/proxy interception), which would throw inside the
                // coroutine and kill it. Version check is best-effort only.
                try
                {
                    string body = unityWebRequest.downloadHandler.text;
                    if (string.IsNullOrWhiteSpace(body))
                    {
                        EngineLogger.LogWarning("Latest release check: empty response body");
                    }
                    else
                    {
                        JObject releaseInfo = JObject.Parse(body);
                        callback.Invoke(releaseInfo["tag_name"]?.ToString());
                    }
                }
                catch (Exception e)
                {
                    EngineLogger.LogWarning($"Latest release check failed: {e.Message}");
                }
            }
        }
    }
}

