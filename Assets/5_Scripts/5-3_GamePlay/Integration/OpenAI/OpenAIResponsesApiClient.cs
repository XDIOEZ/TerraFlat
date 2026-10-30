using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace FlatWorld.GamePlay.Integration.OpenAI
{
    #region OpenAI Responses API 结果

    public enum OpenAIResponsesApiError
    {
        None,
        InvalidPrompt,
        MissingApiKey,
        EnvironmentUnavailable,
        NetworkOrTimeout,
        HttpError,
        InvalidResponse,
        NonCompletedResponse,
        Cancelled
    }

    public sealed class OpenAIResponsesApiResult
    {
        public bool IsSuccess { get; }
        public bool IsCancelled => Error == OpenAIResponsesApiError.Cancelled;
        public string OutputText { get; }
        public long StatusCode { get; }
        public OpenAIResponsesApiError Error { get; }

        private OpenAIResponsesApiResult(bool isSuccess, string outputText, long statusCode,
            OpenAIResponsesApiError error)
        {
            IsSuccess = isSuccess;
            OutputText = outputText;
            StatusCode = statusCode;
            Error = error;
        }

        internal static OpenAIResponsesApiResult CreateSuccess(string outputText, long statusCode)
        {
            return new OpenAIResponsesApiResult(true, outputText, statusCode, OpenAIResponsesApiError.None);
        }

        internal static OpenAIResponsesApiResult CreateFailure(OpenAIResponsesApiError error, long statusCode = 0)
        {
            return new OpenAIResponsesApiResult(false, null, statusCode, error);
        }
    }

    #endregion

    /// <summary>从 Unity 主线程通过进程环境变量调用 Responses API，不缓存密钥。</summary>
    public static class OpenAIResponsesApiClient
    {
        #region OpenAI Responses API 请求

        private const string Endpoint = "https://api.openai.com/v1/responses";
        private const string ApiKeyEnvironmentVariable = "OPENAI_API_KEY";
        private const string ModelEnvironmentVariable = "OPENAI_MODEL";
        private const string DefaultModel = "gpt-6-luna";
        private const int RequestTimeoutSeconds = 120;

        public static async Task<OpenAIResponsesApiResult> CreateTextResponseAsync(
            string prompt,
            string model = null,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            if (string.IsNullOrWhiteSpace(prompt))
                return OpenAIResponsesApiResult.CreateFailure(OpenAIResponsesApiError.InvalidPrompt);
            if (cancellationToken.IsCancellationRequested)
                return OpenAIResponsesApiResult.CreateFailure(OpenAIResponsesApiError.Cancelled);

            string apiKey;
            string configuredModel = null;
            try
            {
                apiKey = Environment.GetEnvironmentVariable(ApiKeyEnvironmentVariable);
                if (string.IsNullOrWhiteSpace(model))
                    configuredModel = Environment.GetEnvironmentVariable(ModelEnvironmentVariable);
            }
            catch (Exception)
            {
                return OpenAIResponsesApiResult.CreateFailure(OpenAIResponsesApiError.EnvironmentUnavailable);
            }

            if (string.IsNullOrWhiteSpace(apiKey))
                return OpenAIResponsesApiResult.CreateFailure(OpenAIResponsesApiError.MissingApiKey);

            string selectedModel = string.IsNullOrWhiteSpace(model) ? configuredModel : model;
            if (string.IsNullOrWhiteSpace(selectedModel))
                selectedModel = DefaultModel;

            byte[] requestBody = Encoding.UTF8.GetBytes(JsonUtility.ToJson(new ResponsesApiRequest
            {
                model = selectedModel,
                input = prompt,
                store = false
            }));

            UnityWebRequest request = null;
            try
            {
                request = new UnityWebRequest(Endpoint, UnityWebRequest.kHttpVerbPOST)
                {
                    uploadHandler = new UploadHandlerRaw(requestBody),
                    downloadHandler = new DownloadHandlerBuffer(),
                    timeout = RequestTimeoutSeconds
                };
                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("Authorization", "Bearer " + apiKey);

                UnityWebRequestAsyncOperation operation = request.SendWebRequest();
                while (!operation.isDone)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        request.Abort();
                        return OpenAIResponsesApiResult.CreateFailure(OpenAIResponsesApiError.Cancelled);
                    }

                    await Task.Yield();
                }

                if (cancellationToken.IsCancellationRequested)
                    return OpenAIResponsesApiResult.CreateFailure(OpenAIResponsesApiError.Cancelled);

                if (request.result != UnityWebRequest.Result.Success)
                {
                    OpenAIResponsesApiError error = request.responseCode >= 400
                        ? OpenAIResponsesApiError.HttpError
                        : OpenAIResponsesApiError.NetworkOrTimeout;
                    return OpenAIResponsesApiResult.CreateFailure(error, request.responseCode);
                }

                string outputText;
                try
                {
                    ResponsesApiResponse response = JsonUtility.FromJson<ResponsesApiResponse>(request.downloadHandler.text);
                    if (response == null || response.status != "completed")
                    {
                        return OpenAIResponsesApiResult.CreateFailure(
                            OpenAIResponsesApiError.NonCompletedResponse, request.responseCode);
                    }
                    outputText = ExtractOutputText(response);
                }
                catch (Exception)
                {
                    return OpenAIResponsesApiResult.CreateFailure(
                        OpenAIResponsesApiError.InvalidResponse, request.responseCode);
                }

                if (cancellationToken.IsCancellationRequested)
                    return OpenAIResponsesApiResult.CreateFailure(OpenAIResponsesApiError.Cancelled);

                return string.IsNullOrEmpty(outputText)
                    ? OpenAIResponsesApiResult.CreateFailure(OpenAIResponsesApiError.InvalidResponse, request.responseCode)
                    : OpenAIResponsesApiResult.CreateSuccess(outputText, request.responseCode);
            }
            catch (Exception)
            {
                return OpenAIResponsesApiResult.CreateFailure(
                    OpenAIResponsesApiError.NetworkOrTimeout, request == null ? 0 : request.responseCode);
            }
            finally
            {
                if (request != null)
                {
                    if (!request.isDone)
                        request.Abort();
                    request.Dispose();
                }
            }
        }

        private static string ExtractOutputText(ResponsesApiResponse response)
        {
            if (response == null || response.output == null)
                return null;

            StringBuilder output = new StringBuilder();
            for (int itemIndex = 0; itemIndex < response.output.Length; itemIndex++)
            {
                ResponsesApiOutputItem item = response.output[itemIndex];
                if (item == null || item.content == null)
                    continue;

                for (int contentIndex = 0; contentIndex < item.content.Length; contentIndex++)
                {
                    ResponsesApiContent content = item.content[contentIndex];
                    if (content == null)
                        continue;
                    if (content.type == "output_text")
                        output.Append(content.text);
                    else if (content.type == "refusal")
                        output.Append(content.refusal);
                }
            }

            return output.Length == 0 ? null : output.ToString();
        }

        [Serializable]
        private sealed class ResponsesApiRequest
        {
            public string model;
            public string input;
            public bool store;
        }

        [Serializable]
        private sealed class ResponsesApiResponse
        {
            public string status;
            public ResponsesApiOutputItem[] output;
        }

        [Serializable]
        private sealed class ResponsesApiOutputItem
        {
            public ResponsesApiContent[] content;
        }

        [Serializable]
        private sealed class ResponsesApiContent
        {
            public string type;
            public string text;
            public string refusal;
        }

        #endregion
    }
}
