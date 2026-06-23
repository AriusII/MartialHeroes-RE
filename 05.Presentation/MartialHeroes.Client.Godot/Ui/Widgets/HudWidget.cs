
using Godot;

namespace MartialHeroes.Client.Godot.Ui.Widgets;

public abstract class HudWidget
{

    private readonly List<HudWidget> _children = [];

    public int ActionId { get; set; } = -1;


    public bool IsMarkedForRemoval { get; private set; }

    public IReadOnlyList<HudWidget> Children => _children;

    public event Action<int>? ActionFired;

    public void MarkForRemoval()
    {
        IsMarkedForRemoval = true;
    }


    public void Show()
    {
        var ctrl = GetControl();
        if (ctrl is null) return;
        AlphaFade.For(ctrl).Show();
    }

    public void Hide()
    {
        var ctrl = GetControl();
        if (ctrl is null) return;
        AlphaFade.For(ctrl).Hide();
    }

    public void ShowInstant()
    {
        var ctrl = GetControl();
        if (ctrl is null) return;
        AlphaFade.For(ctrl).ShowInstant();
    }

    public void HideInstant()
    {
        var ctrl = GetControl();
        if (ctrl is null) return;
        AlphaFade.For(ctrl).HideInstant();
    }

    public void UseSlowFade()
    {
        var ctrl = GetControl();
        if (ctrl is null) return;
        AlphaFade.For(ctrl).UseSlowFade();
    }

    public void AddChild(HudWidget child)
    {
        _children.Add(child);
        var parentCtrl = GetControl();
        var childCtrl = child.GetControl();
        if (parentCtrl is not null && childCtrl is not null)
            parentCtrl.AddChild(childCtrl);
    }

    public void AddChildWithAction(HudWidget child, int actionId)
    {
        child.ActionId = actionId;
        AddChild(child);
    }

    public void RemoveMarked()
    {
        for (var i = _children.Count - 1; i >= 0; i--)
        {
            if (!_children[i].IsMarkedForRemoval) continue;
            var removed = _children[i];
            _children.RemoveAt(i);
            removed.GetControl()?.QueueFree();
        }
    }


    protected void FireAction()
    {
        if (ActionId >= 0)
            ActionFired?.Invoke(ActionId);
    }


    public abstract Control? GetControl();
}