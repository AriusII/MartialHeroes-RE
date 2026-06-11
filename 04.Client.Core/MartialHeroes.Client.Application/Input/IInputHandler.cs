namespace MartialHeroes.Client.Application.Input;

/// <summary>
/// One link in the input chain of responsibility. A handler inspects an event and reports whether it
/// <em>consumed</em> it. spec: Docs/RE/specs/input_ui.md §3 / §6 ("UI consumes the event first; if not
/// consumed, the world handles it").
/// </summary>
/// <remarks>
/// Handlers are registered with the <see cref="InputBus"/> in priority order: UI handler(s) first,
/// then the world handler. The bus walks them in order and stops at the first that returns
/// <see langword="true"/> — reproducing the legacy "UI is the gate" contract (spec: input_ui.md §3).
/// Implementations must be synchronous and side-effect-light (translate to a Domain call or a use-case
/// intent); they must never block the dispatch thread.
/// </remarks>
public interface IInputHandler
{
    /// <summary>
    /// Offers <paramref name="e"/> to this handler. Returns <see langword="true"/> when the handler
    /// consumed the event (the bus then stops and does not offer it to lower-priority handlers), or
    /// <see langword="false"/> to pass it on. spec: Docs/RE/specs/input_ui.md §3.
    /// </summary>
    bool TryHandle(in InputEvent e);
}