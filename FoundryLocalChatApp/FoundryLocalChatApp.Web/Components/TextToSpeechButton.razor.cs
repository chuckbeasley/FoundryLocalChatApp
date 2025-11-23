using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.JSInterop;

namespace FoundryLocalChatApp.Web.Components;

public sealed partial class TextToSpeechButton : IDisposable
{
    [Parameter]
    public string? Text { get; set; }
    [Inject]
    public ISpeechSynthesisService SpeechSynthesis { get; set; } = null!;
    [Inject]
    public IMemoryCache Cache { get; set; } = null!;
    [Inject]
    public ILogger<TextToSpeechButton> Logger { get; set; } = default!;
    private CancellationTokenSource? _speakingMonitorCts;

    SpeechSynthesisVoice[]? _voices;
    SpeechSynthesisUtterance? _utterance;
    double _voiceSpeed = 1.0;
    bool _initialized = false;
    bool isPausing;
    bool isSpeaking;

    protected override async Task OnInitializedAsync()
    {
        _voices = await Cache.GetOrCreateAsync("voices", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24);
            var voices = await SpeechSynthesis.GetVoicesAsync();
            Console.WriteLine($"Voices fetched: {voices?.Length ?? 0}");
            return voices;
        });

        _initialized = true;
    }

    public void Dispose()
    {
        _speakingMonitorCts?.Cancel();
        _speakingMonitorCts?.Dispose();
    }

    private async void OnClick()
    {
        if (await SpeechSynthesis.Speaking)
        {
            await SpeechSynthesis.CancelAsync();
            isSpeaking = false;
            isPausing = false;
            await InvokeAsync(StateHasChanged);
        }
        else
        {
            isSpeaking = true;
            isPausing = false;
            await InvokeAsync(StateHasChanged);
            var utterance = GetOrCreateUtterance();
            utterance.Text = Text!;
            await Speak(utterance);
        }
    }

    private async Task Speak(SpeechSynthesisUtterance utterance)
    {
        _speakingMonitorCts?.Cancel();
        SpeakingMonitor();
        await SpeechSynthesis.SpeakAsync(utterance!);
    }

    private SpeechSynthesisUtterance GetOrCreateUtterance()
    {
        if (_utterance is null)
        {
            _utterance = new SpeechSynthesisUtterance
            {
                Pitch = 1.0,
                Rate = _voiceSpeed,
                Volume = 50,
                Voice = _voices is { Length: > 0 } ? _voices?[4] : null
            };
        }
        return _utterance;
    }
    private async Task OnPause()
    {
        if (await SpeechSynthesis.Speaking)
        {
            await SpeechSynthesis.PauseAsync();
            isPausing = true;
        }
    }

    private async Task OnPlay()
    {
        if (isPausing)
        {
            isPausing = false;
            await SpeechSynthesis.ResumeAsync();
        }
    }

    private void SpeakingMonitor()
    {
        _speakingMonitorCts = new CancellationTokenSource();
        var token = _speakingMonitorCts.Token;
        var firstRun = true;

        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (firstRun)
                    {
                        firstRun = false;
                        await Task.Delay(500, token); // Initial delay to allow speech to start
                    }
                    var speaking = await SpeechSynthesis.Speaking;
                    if (!speaking && isSpeaking == true)
                    {
                        isSpeaking = false;
                        isPausing = false;
                        await InvokeAsync(StateHasChanged);
                        _speakingMonitorCts?.Cancel();
                    }
                }
                catch
                {
                    // Optionally log or handle exceptions
                }
                await Task.Delay(300, token); // Poll every 300ms
            }
        }, token);
    }
}
