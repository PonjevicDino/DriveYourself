using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class RadioStationHandler : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI stationNameText;
    [SerializeField] private TextMeshProUGUI stationSNameText;
    
    private List<RadioStation> radioStationList = new List<RadioStation>();
    private int lastPlayedStation = 0;
    private RadioPlayer audioPlayer;
    private bool radioOn = false;
    
    void Awake()
    {
        audioPlayer = this.GetComponent<RadioPlayer>();
        
        // AT: ORF nationwide Radios
        radioStationList.Add(new RadioStation("Hitradio OE3", "OE 3", "https://orf-live.ors-shoutcast.at/oe3-q2a"));
        radioStationList.Add(new RadioStation("OE1", "OE 1", "https://orf-live.ors-shoutcast.at/oe1-q2a"));
        radioStationList.Add(new RadioStation("Radio FM4", "FM 4", "https://orf-live.ors-shoutcast.at/fm4-q2a"));

        // AT: ORF state Radios
        radioStationList.Add(new RadioStation("ORF Radio Wien", "ORF W", "https://orf-live.ors-shoutcast.at/wie-q2a"));
        radioStationList.Add(new RadioStation("ORF Radio Vorarlberg", "ORF V", "https://orf-live.ors-shoutcast.at/vbg-q2a"));
        radioStationList.Add(new RadioStation("ORF Radio Tirol", "ORF T", "https://orf-live.ors-shoutcast.at/tir-q2a"));
        radioStationList.Add(new RadioStation("ORF Radio Steiermark", "ORF St", "https://orf-live.ors-shoutcast.at/stm-q2a"));
        radioStationList.Add(new RadioStation("ORF Radio Salzburg", "ORF Sbg", "https://orf-live.ors-shoutcast.at/sbg-q2a"));
        radioStationList.Add(new RadioStation("ORF Radio Oberösterreich", "ORF OÖ", "https://orf-live.ors-shoutcast.at/ooe-q2a"));
        radioStationList.Add(new RadioStation("ORF Radio Niederösterreich", "ORF NÖ", "https://orf-live.ors-shoutcast.at/noe-q2a"));
        radioStationList.Add(new RadioStation("ORF Radio Kärnten", "ORF K", "https://orf-live.ors-shoutcast.at/ktn-q2a"));
        radioStationList.Add(new RadioStation("ORF Radio Burgenland", "ORF B", "https://orf-live.ors-shoutcast.at/bgl-q2a"));
    }

    void OnEnable()
    {
        if (radioStationList.Count > 0 && audioPlayer != null && radioOn)
        {
            PlayCurrentStation();
        }
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.R))
        {
            radioOn = !radioOn;
            if (!radioOn)
            {
                audioPlayer.StopRadio();
                stationSNameText.text = string.Empty;
            }
            else 
            {
                PlayCurrentStation();
            }
        }

        if (Input.GetKeyDown(KeyCode.D)) NextStation();
        else if (Input.GetKeyDown(KeyCode.A)) PrevStation();

        if (Input.GetKeyDown(KeyCode.W)) audioPlayer.ChangeVolume(audioPlayer.GetVolume() + 0.025f);
        else if (Input.GetKeyDown(KeyCode.S)) audioPlayer.ChangeVolume(audioPlayer.GetVolume() - 0.025f);
    }

    private void NextStation()
    {
        lastPlayedStation = (lastPlayedStation == radioStationList.Count - 1) ? 0 : lastPlayedStation + 1;
        if (radioOn) PlayCurrentStation();
    }

    private void PrevStation()
    {
        lastPlayedStation = (lastPlayedStation == 0) ? radioStationList.Count - 1 : lastPlayedStation - 1;
        if (radioOn) PlayCurrentStation();
    }
    
    private void PlayCurrentStation()
    {
        stationNameText.text = $"{radioStationList[lastPlayedStation].GetStationName()} {lastPlayedStation + 1}/{radioStationList.Count}";
        stationSNameText.text = radioStationList[lastPlayedStation].GetShortName();
        
        audioPlayer.PlayRadioStation(radioStationList[lastPlayedStation].GetStationURL());
    }
}

public class RadioStation
{
    private string stationName;
    private string stationURL;
    private string shortName;

    public RadioStation(string stationName, string shortName, string stationURL)
    {
        this.stationName = stationName;
        this.shortName = shortName;
        this.stationURL = stationURL;
    }

    public string GetStationName() { return stationName; }
    public string GetShortName() { return shortName; }
    public string GetStationURL() { return stationURL; }
}