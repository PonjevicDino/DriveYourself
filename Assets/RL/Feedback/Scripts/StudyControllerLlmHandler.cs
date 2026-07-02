using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

public class StudyControllerLlmHandler : MonoBehaviour
{
    private StudyController studyController;
    
    private const string geminiApiUrl = "https://generativelanguage.googleapis.com/v1beta/models/gemini-3.1-flash-lite:generateContent?key=";
    private const string openAiApiUrl = "https://api.openai.com/v1/chat/completions";

    private const string systemPrompt = "You are an intent parser for an autonomous driving simulator. Extract the user's desired changes for 4 parameters: 'speed', 'distance to the center', 'acceleration', and 'smoothness'. Map their intent strictly to one of these exact values: 'much less', 'slightly less', 'keep', 'slightly more', 'much more'. If a parameter isn't mentioned, default to 'keep'. Additionally, deduce a 'likenessScore' as a float between 0.0 and 1.0 indicating how much the user liked the previous driving style based on their sentiment (e.g., highly critical/frustrated = 0.0-0.3, neutral/mixed = 0.4-0.6, highly positive = 0.7-1.0). If unsure, default to 0.5. If the text is completely unrelated or unintelligible, set 'isValid' to false. Reply ONLY with a JSON object matching this structure: {\"isValid\": true, \"likenessScore\": 0.5, \"speed\": \"keep\", \"dtc\": \"keep\", \"acceleration\": \"keep\", \"smoothness\": \"keep\"}";

    private void Start()
    {
        studyController = this.GetComponent<StudyController>();
    }

    public IEnumerator ProcessIntent(string userText, Action<MappedFeedback> onSuccess, Action<string> onError)
    {
        string safeUserText = userText.Replace("\"", "\\\"").Replace("\n", " ").Trim();
        
        if (studyController.ActiveLLM == StudyController.LLMProvider.Gemini)
        {
            yield return StartCoroutine(SendToGemini(safeUserText, onSuccess, onError));
        }
        else if (studyController.ActiveLLM == StudyController.LLMProvider.OpenAI)
        {
            yield return StartCoroutine(SendToOpenAI(safeUserText, onSuccess, onError));
        }
    }

    private IEnumerator SendToGemini(string userText, Action<MappedFeedback> onSuccess, Action<string> onError)
    {
        string combinedPrompt = $"{systemPrompt}\n\nUser Input: {userText}";
        string requestData = $@"{{
            ""contents"": [ {{ ""parts"": [ {{ ""text"": ""{combinedPrompt}"" }} ] }} ],
            ""generationConfig"": {{ ""temperature"": 0.0, ""responseMimeType"": ""application/json"" }}
        }}";
        
        string apiKey = PlayerPrefs.GetString("GeminiAPIKey", "");
        string requestUrl = geminiApiUrl + apiKey;
        
        using (UnityWebRequest request = new UnityWebRequest(requestUrl, "POST"))
        {
            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(requestData);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            
            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.ConnectionError || request.result == UnityWebRequest.Result.ProtocolError)
            {
                onError?.Invoke("Gemini Error: " + request.error);
            }
            else
            {
                try
                {
                    GeminiResponse responseObj = JsonUtility.FromJson<GeminiResponse>(request.downloadHandler.text);
                    string contentJson = responseObj.candidates[0].content.parts[0].text;
                    MappedFeedback mappedData = JsonUtility.FromJson<MappedFeedback>(contentJson);
                    onSuccess?.Invoke(mappedData);
                }
                catch (Exception e)
                {
                    onError?.Invoke("Failed to parse Gemini JSON: " + e.Message);
                }
            }
        }
    }

    private IEnumerator SendToOpenAI(string userText, Action<MappedFeedback> onSuccess, Action<string> onError)
    {
        string requestData = $@"{{
            ""model"": ""gpt-4o-mini"",
            ""response_format"": {{ ""type"": ""json_object"" }},
            ""temperature"": 0.0,
            ""messages"": [
                {{ ""role"": ""system"", ""content"": ""{systemPrompt}"" }},
                {{ ""role"": ""user"", ""content"": ""{userText}"" }}
            ]
        }}";
        
        string apiKey = PlayerPrefs.GetString("OpenAIAPIKey", "");
        
        using (UnityWebRequest request = new UnityWebRequest(openAiApiUrl, "POST"))
        {
            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(requestData);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Authorization", "Bearer " + apiKey);
            
            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.ConnectionError || request.result == UnityWebRequest.Result.ProtocolError)
            {
                onError?.Invoke("OpenAI Error: " + request.error + "\n" + request.downloadHandler.text);
            }
            else
            {
                try
                {
                    OpenAIResponse responseObj = JsonUtility.FromJson<OpenAIResponse>(request.downloadHandler.text);
                    string contentJson = responseObj.choices[0].message.content;
                    MappedFeedback mappedData = JsonUtility.FromJson<MappedFeedback>(contentJson);
                    onSuccess?.Invoke(mappedData);
                }
                catch (Exception e)
                {
                    onError?.Invoke("Failed to parse OpenAI JSON: " + e.Message);
                }
            }
        }
    }
}

[System.Serializable]
public class MappedFeedback
{
    public bool isValid;
    public float likenessScore; 
    public string speed;
    public string dtc;
    public string acceleration;
    public string smoothness;
}

[System.Serializable]
public class GeminiResponse { public Candidate[] candidates; }
[System.Serializable]
public class Candidate { public Content content; }
[System.Serializable]
public class Content { public Part[] parts; }
[System.Serializable]
public class Part { public string text; }

[System.Serializable]
public class OpenAIResponse { public OpenAIChoice[] choices; }
[System.Serializable]
public class OpenAIChoice { public OpenAIMessage message; }
[System.Serializable]
public class OpenAIMessage { public string content; }