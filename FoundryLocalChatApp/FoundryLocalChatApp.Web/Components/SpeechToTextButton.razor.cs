using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace FoundryLocalChatApp.Web.Components;

public partial class SpeechToTextButton : IDisposable
{
    IDisposable? _recognitionSubscription;
    bool isRecording;
    [Inject]
    public ISpeechRecognitionService SpeechRecognition { get; set; } = null!;
    [Parameter]
    public EventCallback<string> OnRecognizedText { get; set; }

    private Task OnRecognized(string recognizedText) =>
        OnRecognizedText.InvokeAsync(recognizedText);

    private async Task OnRecord()
    {
        if (isRecording)
            await SpeechRecognition.CancelSpeechRecognitionAsync(true);
        _recognitionSubscription?.Dispose();
        _recognitionSubscription = await SpeechRecognition.RecognizeSpeechAsync(
            language: "en",
            onError: OnError,
            onRecognized: OnRecognized,
            onStarted: OnStarted,
            onEnded: OnEnded);
    }

    private async Task OnStopRecording() =>
        await SpeechRecognition.CancelSpeechRecognitionAsync(true);

    private Task OnEnded()
    {
        isRecording = false;
        StateHasChanged();
        return Task.CompletedTask;
    }

    private Task OnStarted()
    {
        isRecording = true;
        StateHasChanged();
        return Task.CompletedTask;
    }

    private Task OnError(SpeechRecognitionErrorEvent args)
    {
        switch (args.Error)
        {
            case "audio-capture":
            case "network":
            case "not-allowed":
            case "service-not-allowed":
            case "bad-grammar":
            case "language-not-supported":
                throw new Exception($"Speech recognition error: {args.Message}");
            case "no-speech":
            case "aborted":
                break;
        }
        ;
        StateHasChanged();
        return Task.CompletedTask;
    }

    public void Dispose() =>
        _recognitionSubscription?.Dispose();
}