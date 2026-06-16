using System.Collections;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Windows.Speech;

public class FeedbackLanguage : MonoBehaviour
{
    [Header("UI References")]
    public TextMeshProUGUI liveTranscriptionText;

    [Header("LLM API Settings")]
    public string openAIApiKey = "OPENAI_API_KEY";
    private string openAIApiUrl = "https://api.openai.com/v1/chat/completions";

    [Header("State")]
    public bool isFeedbackUnlocked = false;
    private bool isRecording = false;
    private bool isProcessing = false;
    
    private DictationRecognizer dictationRecognizer;
    private string finalTranscription = "";

    void Start()
    {
        dictationRecognizer = new DictationRecognizer();
        dictationRecognizer.DictationHypothesis += (text) =>
        {
            liveTranscriptionText.text = text + "...";
        };
        dictationRecognizer.DictationResult += (text, confidence) =>
        {
            finalTranscription += text + " ";
            liveTranscriptionText.text = finalTranscription;
        };
        dictationRecognizer.DictationError += (error, hresult) =>
        {
            Debug.LogError("Dictation error: " + error);
            ResetFeedbackState("Mic error. Try again.");
        };
    }

    void Update()
    {
        if (isFeedbackUnlocked && !isProcessing)
        {
            if (Input.GetKeyDown(KeyCode.Space))
            {
                StartRecording();
            }
            if (Input.GetKeyUp(KeyCode.Space))
            {
                StopRecordingAndProcess();
            }
        }
    }

    private void StartRecording()
    {
        isRecording = true;
        finalTranscription = "";
        liveTranscriptionText.text = "Listening...";
        dictationRecognizer.Start();
    }

    private void StopRecordingAndProcess()
    {
        if (!isRecording) return;
        
        isRecording = false;
        isProcessing = true;
        dictationRecognizer.Stop();

        if (string.IsNullOrWhiteSpace(finalTranscription))
        {
            ResetFeedbackState("I didn't catch that. Please try again.");
            return;
        }

        StartCoroutine(SendToLLM(finalTranscription));
    }

    private IEnumerator SendToLLM(string userText)
    {
        string systemPrompt = "You are an intent parser for an autonomous driving simulator. Extract the user's desired changes for 4 parameters: 'speed', 'distance to the center', 'acceleration', and 'smoothness'. Map their intent strictly to one of these exact values: 'much less', 'slightly less', 'keep', 'slightly more', 'much more'. If a parameter isn't mentioned, default to 'keep'. If the text is completely unrelated or unintelligible, set 'isValid' to false. Reply ONLY with a JSON object matching this structure: {\"isValid\": true, \"speed\": \"keep\", \"dtc\": \"keep\", \"acceleration\": \"keep\", \"smoothness\": \"keep\"}";

        string requestData = $@"{{
            ""model"": ""gpt-3.5-turbo"",
            ""messages"": [
                {{""role"": ""system"", ""content"": ""{systemPrompt}""}},
                {{""role"": ""user"", ""content"": ""{userText}""}}
            ],
            ""temperature"": 0.0,
            ""response_format"": {{ ""type"": ""json_object"" }}
        }}";
        
        UnityWebRequest request = new UnityWebRequest(openAIApiUrl, "POST");
        byte[] bodyRaw = Encoding.UTF8.GetBytes(requestData);
        request.uploadHandler = new UploadHandlerRaw(bodyRaw);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");
        request.SetRequestHeader("Authorization", "Bearer " + openAIApiKey);
        
        yield return request.SendWebRequest();

        if (request.result == UnityWebRequest.Result.ConnectionError || request.result == UnityWebRequest.Result.ProtocolError)
        {
            Debug.LogError(request.error);
            ResetFeedbackState("Network error. Try again.");
        }
        else
        {
            ParseLLMResponse(request.downloadHandler.text);
        }
    }

    private void ParseLLMResponse(string jsonResponse)
    {
        try
        {
            OpenAIResponse responseObj = JsonUtility.FromJson<OpenAIResponse>(jsonResponse);
            string contentJson = responseObj.choices[0].message.content;
            
            MappedFeedback mappedData = JsonUtility.FromJson<MappedFeedback>(contentJson);

            if (!mappedData.isValid)
            {
                ResetFeedbackState("I didn't understand that as driving feedback. Please try again.");
            }
            else
            {
                SendToBayesianOptimization(mappedData);
                
                isFeedbackUnlocked = false; 
                isProcessing = false;
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError("Failed to parse JSON: " + e.Message);
            ResetFeedbackState("Parsing error. Try again.");
        }
    }

    private void ResetFeedbackState(string message)
    {
        isProcessing = false;
    }

    private void SendToBayesianOptimization(MappedFeedback data)
    {
        Debug.Log($"Sending to BO -> Speed: {data.speed}, DtC: {data.dtc}, Accel: {data.acceleration}, Smoothness: {data.smoothness}");
        
        // TODO: Call your BO Plugin script here
        // YourBOPlugin.RegisterFeedback(data.speed, data.dtc, data.acceleration, data.smoothness);
    }

    void OnDestroy()
    {
        if (dictationRecognizer != null)
        {
            dictationRecognizer.Dispose();
        }
    }
}

[System.Serializable]
public class MappedFeedback
{
    public bool isValid;
    public string speed;
    public string dtc;
    public string acceleration;
    public string smoothness;
}

[System.Serializable]
public class OpenAIResponse
{
    public Choice[] choices;
}

[System.Serializable]
public class Choice
{
    public Message message;
}

[System.Serializable]
public class Message
{
    public string content;
}