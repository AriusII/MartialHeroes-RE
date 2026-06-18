namespace MartialHeroes.Client.Application.Assets;

/// <summary>
/// Engine-free notification for the loading-screen looping SFX. The concrete audio engine lives in
/// layer 05; Application only emits the IDA-confirmed cue id. spec: Docs/RE/specs/resource_pipeline.md §2.3.
/// </summary>
public interface ILoadingSoundSink
{
    void PlayLooping(int soundCueId);
}