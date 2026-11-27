namespace OpenRend.App;

internal interface ISourceAudioSession : IAsyncDisposable
{
    long CurrentFrame { get; }
    bool IsFinished { get; }
    double MasterGain { set; }
    void Start();
    Task CheckAsync();
}
